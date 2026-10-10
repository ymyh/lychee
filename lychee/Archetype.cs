using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using lychee.collections;
using lychee.interfaces;
using lychee.utils;

namespace lychee;

/// <summary>
/// Stores all archetypes in the ECS world. Containing an empty archetype for entities without components and
/// providing methods to get or create archetypes based on component type combinations.
/// </summary>
public sealed class ArchetypeManager : IDisposable
{
#region Public Properties & Fields

    public List<Archetype> Archetypes { get; } = [];

    public delegate void ArchetypeCreatedHandler();

    /// <summary>
    /// Invoked when a new archetype is created.
    /// </summary>
    public event ArchetypeCreatedHandler? ArchetypeCreated;

#endregion

#region Private Fields

    private readonly TypeRegistrar typeRegistrar;

    private readonly int chunkSizeHint;

    // Migration edges, keyed by source archetype and the operation. They are computed and used only on the
    // single apply thread, so no lock is needed. Single-type edges pack (source id, type id) into one long;
    // bundle and tuple edges are keyed by the operation's type. Add and remove are kept apart.
    private readonly Dictionary<long, Archetype> addEdges = [];
    private readonly Dictionary<long, Archetype> removeEdges = [];
    private readonly Dictionary<(int SourceId, Type Operation), Archetype> bundleAddEdges = [];
    private readonly Dictionary<(int SourceId, Type Operation), Archetype> bundleRemoveEdges = [];

    private bool disposed;

#endregion

#region Static Members

    internal static Archetype EmptyArchetype { get; }

    static ArchetypeManager()
    {
        EmptyArchetype = new(0, [], [], null!, 0);
    }

#endregion

#region Constructors

    public ArchetypeManager(TypeRegistrar typeRegistrar, int chunkSizeHint)
    {
        this.typeRegistrar = typeRegistrar;
        this.chunkSizeHint = chunkSizeHint;
        Archetypes.Add(EmptyArchetype);
    }

#endregion

#region Public Methods

    /// <summary>
    /// Dumps all archetype definitions except empty archetype.
    /// </summary>
    /// <returns></returns>
    public ArchetypeDefinition[] DumpAllArchetypeDefinitions()
    {
        return Archetypes.Skip(1).Select(a =>
        {
            return new ArchetypeDefinition
            {
                ID = a.ID,
                TypeNames = a.Types.Select(t => t.FullName).ToArray()!,
            };
        }).ToArray();
    }

    /// <summary>
    /// Gets an existing archetype with the specified component types, or creates a new one if it doesn't exist.
    /// </summary>
    /// <param name="typeIdList">A collection of component type IDs that define the archetype.</param>
    /// <returns>The archetype matching the specified component types.</returns>
    /// <remarks>
    /// The type IDs are sorted internally to ensure consistent archetype identification. Archetypes are
    /// created only on the apply thread, so this method is not safe to call from a parallel system; use the
    /// migration edges (<see cref="GetAddEdge"/> and friends) on the hot path.
    /// </remarks>
    public Archetype GetOrCreateArchetype(IEnumerable<int> typeIdList)
    {
        var array = typeIdList.ToArray();
        Array.Sort(array);

        foreach (var archetype in Archetypes)
        {
            if (archetype.TypeIdList.SequenceEqual(array))
            {
                return archetype;
            }
        }

        var id = Archetypes.Count;
        var typeInfoList = array.Select(id => typeRegistrar.GetTypeInfo(id)).ToArray();
        Archetypes.Add(new(id, array, typeInfoList, typeRegistrar, chunkSizeHint));

        ArchetypeCreated?.Invoke();

        return Archetypes[id];
    }

    /// <summary>
    /// Gets or creates an archetype using a tuple type to specify component types.
    /// </summary>
    /// <typeparam name="T">A tuple type containing the component types (e.g., (Position, Velocity)).</typeparam>
    /// <returns>The archetype matching the specified component types.</returns>
    /// <remarks>
    /// This is a convenience method that extracts types from a tuple and registers them as components.
    /// It creates archetypes, so it must not be called from a parallel system; use it from a single-threaded
    /// point (recording/setup) only, matching <see cref="GetOrCreateArchetype"/>.
    /// </remarks>
    public Archetype GetOrCreateArchetypeWithTuple<T>()
    {
        var typeList = TypeUtils.GetTupleTypes<T>();
        var typeIds = typeList.Select(x => typeRegistrar.RegisterComponent(x)).ToArray();

        return GetOrCreateArchetype(typeIds);
    }

    /// <summary>
    /// Gets or creates an archetype using a component bundle to specify component types.
    /// </summary>
    /// <typeparam name="T">A struct implementing IComponentBundle that defines the component types as fields.</typeparam>
    /// <returns>The archetype matching the component types defined in the bundle.</returns>
    /// <remarks>
    /// This method extracts field types from the bundle struct and uses them as component types. It creates
    /// archetypes, so it must not be called from a parallel system; use it from a single-threaded point
    /// (recording/setup) only, matching <see cref="GetOrCreateArchetype"/>.
    /// </remarks>
    public Archetype GetOrCreateArchetypeWithBundle<T>() where T : IComponentBundle
    {
        var type = typeof(T);
        var fields = type.GetFields();
        var typeIds = fields.Select(f => typeRegistrar.RegisterComponent(f.FieldType)).ToArray();

        Array.Sort(typeIds);
        return GetOrCreateArchetype(typeIds);
    }

    /// <summary>
    /// Get archetype by id.
    /// </summary>
    /// <param name="id">Target archetype id.</param>
    /// <returns></returns>
    public Archetype GetArchetype(int id)
    {
        return Archetypes[id];
    }

    /// <summary>
    /// Finds all archetypes that match the specified filter predicates.
    /// </summary>
    /// <param name="allFilter">Types that must ALL be present in the archetype.</param>
    /// <param name="anyFilter">Types where AT LEAST ONE must be present in the archetype.</param>
    /// <param name="noneFilter">Types that must NOT be present in the archetype.</param>
    /// <param name="typeRequires">Type IDs that are required (similar to allFilter but using IDs).</param>
    /// <param name="startIndex">Reference parameter for incremental searching; updated to the last searched index.</param>
    /// <returns>An array of archetypes that match all the specified filters.</returns>
    /// <remarks>
    /// This method is thread-safe and is typically used by query systems to find relevant archetypes.
    /// The startIndex parameter allows for incremental matching of newly created archetypes.
    /// </remarks>
    public Archetype[] MatchArchetypesByPredicate(Type[] allFilter, Type[] anyFilter, Type[] noneFilter,
        int[] typeRequires, ref int startIndex)
    {
        if (typeRequires.Length == 0)
        {
            return [];
        }

        var idx = startIndex;

        startIndex = Archetypes.Count;

        return Archetypes.Skip(idx).Where(a =>
        {
            var ret = typeRequires.Aggregate(true, (current, typeId) => current & a.TypeIdList.Contains(typeId));
            return allFilter.Select(type => typeRegistrar.RegisterComponent(type))
                .Aggregate(ret, (current, typeId) => current & a.TypeIdList.Contains(typeId));
        }).Where(a =>
        {
            var ret = anyFilter.Length == 0;

            foreach (var type in anyFilter)
            {
                var typeId = typeRegistrar.RegisterComponent(type);
                ret |= a.TypeIdList.Contains(typeId);
            }

            return ret;
        }).Where(a =>
        {
            var ret = true;

            foreach (var type in noneFilter)
            {
                var typeId = typeRegistrar.RegisterComponent(type);
                ret &= !a.TypeIdList.Contains(typeId);
            }

            return ret;
        }).ToArray();
    }

#endregion

#region Internal Methods

    /// <summary>
    /// Gets the archetype reached by adding <paramref name="typeId"/> to <paramref name="src"/>, caching the
    /// edge. Returns <paramref name="src"/> when it already has the type. Apply-thread only.
    /// </summary>
    internal Archetype GetAddEdge(Archetype src, int typeId)
    {
        if (Array.BinarySearch(src.TypeIdList, typeId) >= 0)
        {
            return src;
        }

        var key = EdgeKey(src, typeId);

        if (addEdges.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var srcIds = src.TypeIdList;
        var ids = new int[srcIds.Length + 1];
        var i = 0;
        var inserted = false;

        foreach (var id in srcIds)
        {
            if (!inserted && typeId < id)
            {
                ids[i++] = typeId;
                inserted = true;
            }

            ids[i++] = id;
        }

        if (!inserted)
        {
            ids[i] = typeId;
        }

        var dst = GetOrCreateArchetype(ids);
        addEdges[key] = dst;

        return dst;
    }

    /// <summary>
    /// Gets the archetype reached by removing <paramref name="typeId"/> from <paramref name="src"/>, caching
    /// the edge. Returns <paramref name="src"/> when it does not have the type. Apply-thread only.
    /// </summary>
    internal Archetype GetRemoveEdge(Archetype src, int typeId)
    {
        var index = Array.BinarySearch(src.TypeIdList, typeId);

        if (index < 0)
        {
            return src;
        }

        var key = EdgeKey(src, typeId);

        if (removeEdges.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var srcIds = src.TypeIdList;
        var ids = new int[srcIds.Length - 1];
        Array.Copy(srcIds, 0, ids, 0, index);
        Array.Copy(srcIds, index + 1, ids, index, ids.Length - index);

        var dst = GetOrCreateArchetype(ids);
        removeEdges[key] = dst;

        return dst;
    }

    /// <summary>
    /// Gets the archetype reached by adding every component of bundle <typeparamref name="T"/> to
    /// <paramref name="src"/>, caching the edge. Apply-thread only.
    /// </summary>
    internal Archetype GetBundleAddEdge<T>(Archetype src) where T : unmanaged, IComponentBundle
    {
        var key = (src.ID, typeof(T));

        if (bundleAddEdges.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var set = new SortedSet<int>(src.TypeIdList);

        foreach (var (_, typeId) in typeRegistrar.GetBundleInfo<T>())
        {
            set.Add(typeId);
        }

        var dst = GetOrCreateArchetype(set);
        bundleAddEdges[key] = dst;

        return dst;
    }

    /// <summary>
    /// Gets the archetype reached by removing every component of bundle <typeparamref name="T"/> from
    /// <paramref name="src"/>, caching the edge. Apply-thread only.
    /// </summary>
    internal Archetype GetBundleRemoveEdge<T>(Archetype src) where T : unmanaged, IComponentBundle
    {
        var key = (src.ID, typeof(T));

        if (bundleRemoveEdges.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var set = new SortedSet<int>(src.TypeIdList);

        foreach (var (_, typeId) in typeRegistrar.GetBundleInfo<T>())
        {
            set.Remove(typeId);
        }

        var dst = GetOrCreateArchetype(set);
        bundleRemoveEdges[key] = dst;

        return dst;
    }

    /// <summary>
    /// Gets the archetype reached by removing every type of tuple <typeparamref name="T"/> from
    /// <paramref name="src"/>, caching the edge. Apply-thread only.
    /// </summary>
    internal Archetype GetTupleRemoveEdge<T>(Archetype src) where T : unmanaged
    {
        var key = (src.ID, typeof(T));

        if (bundleRemoveEdges.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var set = new SortedSet<int>(src.TypeIdList);

        foreach (var typeId in typeRegistrar.RegisterTypesOfTuple<T>())
        {
            set.Remove(typeId);
        }

        var dst = GetOrCreateArchetype(set);
        bundleRemoveEdges[key] = dst;

        return dst;
    }

    private static long EdgeKey(Archetype src, int typeId)
    {
        return ((long)src.ID << 32) | (uint)typeId;
    }

    internal void Commit(EntityPool entityPool)
    {
        foreach (var archetype in Archetypes)
        {
            archetype.Commit(entityPool);
        }
    }

    internal void ClearData()
    {
        foreach (var archetype in Archetypes)
        {
            archetype.Clear();
        }
    }

#endregion

#region IDisposable Implementation

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;

        foreach (var archetype in Archetypes)
        {
            archetype.Dispose();
        }
    }

#endregion
}

public sealed class Archetype(int id, int[] typeIdList, TypeInfo[] typeInfoList, TypeRegistrar typeRegistrar, int chunkSizeHint) : IDisposable
{
#region Private Fields

    internal readonly Table Table = new(new(typeInfoList), chunkSizeHint);

    private readonly SparseMap<int> typeIdxMap = new(typeIdList.Select((id, index) => (id, index)));

    private readonly SparseMap<(int[] src, int[] dst)> dstArchetypeCommCompIndices = [];

    private readonly SparseMap<EntityRef> entities = [];

    private readonly ConcurrentStack<(int id, ushort chunkIdx, ushort idx)> holesInTable = [];

    private bool dirty;

    private bool disposed;

#endregion

#region Public Properties

    /// <summary>
    /// The unique identifier of this archetype.
    /// </summary>
    public int ID { get; } = id;

    /// <summary>
    /// The sorted array of component type IDs that define this archetype.
    /// </summary>
    public int[] TypeIdList { get; } = typeIdList.Distinct().Count() != typeIdList.Length ? throw new ArgumentException("Duplicate type id in archetype.") : typeIdList;

    /// <summary>
    /// Gets the array of component types in this archetype.
    /// </summary>
    public Type[] Types => typeIdList.Select(typeRegistrar.GetTypeById).ToArray();

    public int EntityCount => Table.TotalElementCount;

    /// <summary>
    /// Indicates whether the archetype's table and entity count are synchronized.
    /// </summary>
    /// <remarks>
    /// Returns true when the total count in the table equals the number of tracked entities.
    /// An archetype may be incoherent temporarily during structural changes.
    /// </remarks>
    public bool IsCoherent => EntityCount == entities.Count;

#endregion

#region Public Methods

    /// <summary>
    /// Iterates over the raw memory data of a specific component type across all chunks.
    /// </summary>
    /// <param name="typeId">The component type ID to iterate.</param>
    /// <returns>An enumerable of tuples containing the memory pointer and element count for each chunk.</returns>
    /// <remarks>
    /// This method is useful for efficient bulk processing of component data.
    /// The returned pointers point to unmanaged memory; use with caution.
    /// </remarks>
    public IEnumerable<(nint ptr, int size)> IterateDataAmongChunk(int typeId)
    {
        var typeIdx = GetTypeIndex(typeId);
        return Table.IterateOfTypeAmongChunk(typeIdx);
    }

    public IEnumerable<UnsafeSpan<T>> IterateDataAmongChunk<T>(int typeId) where T : unmanaged
    {
        var typeIdx = GetTypeIndex(typeId);

        foreach (var (ptr, size) in Table.IterateOfTypeAmongChunk(typeIdx))
        {
            UnsafeSpan<T> span;
            unsafe
            {
                span = new((T*)ptr, size);
            }

            yield return span;
        }
    }

    /// <summary>
    /// Iterates over chunks in groups based on a target group size.
    /// </summary>
    /// <param name="groupSize">The minimum number of entities to include in each group.</param>
    /// <returns>An enumerable of tuples containing the starting chunk index and the number of chunks in each group.</returns>
    /// <remarks>
    /// This method is useful for parallel processing or batching operations across chunks.
    /// Groups may span multiple chunks to reach the target size.
    /// </remarks>
    /// <exception cref="ArgumentException">Thrown when groupSize is less than 1.</exception>
    public IEnumerable<(int chunkIdx, int chunkCount)> IterateChunksAmongType(int groupSize)
    {
        if (groupSize < 1)
        {
            throw new ArgumentException("groupSize must be greater than 0");
        }

        var chunkIdx = 0;
        var chunkCount = 0;
        var count = 0;

        while (chunkIdx < Table.Chunks.Count)
        {
            count += Table.Chunks[chunkIdx + chunkCount].Size;
            chunkCount++;

            if (count < groupSize)
            {
                if (chunkIdx == Table.Chunks.Count - 1)
                {
                    yield return (chunkIdx, chunkCount);
                    break;
                }

                continue;
            }

            yield return (chunkIdx, chunkCount);

            chunkIdx += chunkCount;
            chunkCount = 0;
            count = 0;
        }
    }

    /// <summary>
    /// Gets the raw memory pointer and element count for a specific component type in a specific chunk.
    /// </summary>
    /// <param name="typeId">The component type ID.</param>
    /// <param name="chunkIdx">The index of the chunk.</param>
    /// <returns>A tuple containing the memory pointer and the number of elements in the chunk.</returns>
    public (nint ptr, int size) GetChunkData(int typeId, int chunkIdx)
    {
        var typeIdx = GetTypeIndex(typeId);
        return Table.GetChunkData(typeIdx, chunkIdx);
    }

    public Span<T> GetChunkData<T>(int typeId, int chunkIdx) where T : unmanaged
    {
        var typeIdx = GetTypeIndex(typeId);
        return Table.GetChunkData<T>(typeIdx, chunkIdx);
    }

    /// <summary>
    /// Gets a span of all entity references stored in this archetype.
    /// </summary>
    /// <returns>A span containing tuples of entity IDs and their corresponding EntityRef structs.</returns>
    public Span<(int, EntityRef)> GetEntitiesSpan()
    {
        return entities.GetDenseAsSpan();
    }

#endregion

#region Internal Methods

    internal void Clear()
    {
        entities.Clear();
        Table.Clear();
    }

    internal (nint ptr, int size) GetChunkDataWithReservation(int typeId, int chunkIdx)
    {
        var typeIdx = GetTypeIndex(typeId);
        return Table.GetChunkDataWithReservation(typeIdx, chunkIdx);
    }

    internal void Commit(EntityPool entityPool)
    {
        if (!dirty || Table.Layout.MaxAlignment == 0)
        {
            return;
        }

        while (holesInTable.TryPop(out var hole))
        {
            var chunk = Table.Chunks[hole.chunkIdx];
            var from = chunk.Size + chunk.Reservation - 1;

            if (entities.ContainsKey(hole.id))
            {
                hole.idx = (ushort)entities.GetIndex(hole.id);
            }

            if (from > hole.idx)
            {
                FillHole(hole.chunkIdx, from, hole.idx);
                entityPool.UpdateEntityInfo(ID, entities.GetDenseAsSpan()[^1].Item2.ID, hole.idx);
            }

            if (chunk.Reservation > 0)
            {
                chunk.Reservation--;
            }
            else
            {
                chunk.Size--;
            }

            entities.Remove(hole.id);
        }

        Table.CommitReserved();
        ShrinkTable();
        dirty = false;

        // EntityCount can exceed entities.Count here: multiple command buffers commit
        // sequentially per round, and this merge may include slots reserved by later buffers
        // whose CommitAddEntity has not run yet. The transient gap is unobservable because
        // buffers commit back-to-back. The reverse direction (entity without a slot) is
        // never legitimate.
        Debug.Assert(EntityCount >= entities.Count);
    }

    internal void CommitAddEntity(EntityRef entityRef)
    {
        // The empty archetype has no table and no component types, so its entity map is never read: skipping
        // the write keeps a process-wide shared instance out of the picture during concurrent worlds.
        if (Table.Layout.MaxAlignment == 0)
        {
            return;
        }

        entities[entityRef.ID] = entityRef;
    }

    internal int GetTypeIndex(int typeId)
    {
        return typeIdxMap[typeId];
    }

    internal void MarkRemove(int entityId, EntityPos entityPos)
    {
        if (Table.Layout.MaxAlignment == 0)
        {
            return;
        }

        dirty = true;
        holesInTable.Push((entityId, (ushort)entityPos.ChunkIdx, (ushort)entityPos.Idx));

        Debug.Assert(ID == 0 || (ID != 0 && holesInTable.Distinct().Count() == holesInTable.Count));
    }

    internal void MoveDataTo(Archetype archetype, int srcChunkIdx, int srcIdx, int dstChunkIdx, int dstIdx)
    {
        int[] srcCommCompIndices;
        int[] dstCommCompIndices;

        if (dstArchetypeCommCompIndices.TryGetValue(archetype.ID, out var compIndices))
        {
            (srcCommCompIndices, dstCommCompIndices) = compIndices;
        }
        else
        {
            var commCompIds = TypeIdList.Intersect(archetype.TypeIdList).ToArray();
            srcCommCompIndices = new int[commCompIds.Length];
            dstCommCompIndices = new int[commCompIds.Length];
            GetTypeIndices(commCompIds, srcCommCompIndices);
            archetype.GetTypeIndices(commCompIds, dstCommCompIndices);

            dstArchetypeCommCompIndices.AddOrUpdate(archetype.ID, (srcCommCompIndices, dstCommCompIndices));
        }

        for (var i = 0; i < srcCommCompIndices.Length; i++)
        {
            unsafe
            {
                var src = Table.GetPtr(srcCommCompIndices[i], srcChunkIdx, srcIdx);
                var dst = archetype.Table.GetPtr(dstCommCompIndices[i], dstChunkIdx, dstIdx);

                NativeMemory.Copy(src, dst, (nuint)Table.Layout.TypeInfoList[srcCommCompIndices[i]].Size);
            }
        }

        dirty = true;
    }

    internal void PutComponentData<T>(int typeIdx, int chunkIdx, int idx, in T data) where T : unmanaged
    {
        unsafe
        {
            var dstPtr = Table.GetPtr(typeIdx, chunkIdx, idx);
            fixed (T* srcPtr = &data)
            {
                NativeMemory.Copy(srcPtr, dstPtr, (nuint)Table.Layout.TypeInfoList[typeIdx].Size);
            }
        }
    }

    internal (int chunkIdx, int idx) Reserve()
    {
        if (Table.Layout.MaxAlignment == 0)
        {
            return (0, 0);
        }

        if (holesInTable.TryPop(out var hole))
        {
            entities.Remove(hole.id);
            return (hole.chunkIdx, hole.idx);
        }

        dirty = true;
        return Table.Reserve();
    }

#endregion

#region Private Methods

    private void FillHole(int chunkIdx, int from, int to)
    {
        for (var i = 0; i < Table.Layout.TypeInfoList.Length; i++)
        {
            unsafe
            {
                var srcPtr = Table.GetPtr(i, chunkIdx, from);
                var dstPtr = Table.GetPtr(i, chunkIdx, to);

                NativeMemory.Copy(srcPtr, dstPtr, (nuint)Table.Layout.TypeInfoList[i].Size);
            }
        }
    }

    private void GetTypeIndices(IEnumerable<int> typeIds, Span<int> output)
    {
        var i = 0;
        foreach (var typeId in typeIds)
        {
            output[i] = typeIdxMap[typeId];
            i++;
        }
    }

    private void ShrinkTable()
    {
        for (var i = 0; i < Table.Chunks.Count - 1; i++)
        {
            var current = Table.Chunks[i];
            var next = Table.Chunks[i + 1];

            if (current.Size + next.Size <= current.Capacity)
            {
                foreach (var typeInfo in Table.Layout.TypeInfoList)
                {
                    unsafe
                    {
                        var src = (nint)next.Data;
                        src += (typeInfo.Offset * next.Capacity);

                        var dst = (nint)current.Data;
                        dst += (typeInfo.Offset * current.Capacity + typeInfo.Size * current.Size);

                        NativeMemory.Copy((void*)src, (void*)dst, (nuint)(typeInfo.Size * next.Size));
                    }
                }

                current.Size += next.Size;
                Table.Chunks.RemoveAt(i + 1);
            }
        }
    }

#endregion

#region IDisposable Implementation

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;

        Table.Dispose();
    }

#endregion
}

/// <summary>
/// Contains id and all type names of archetype.
/// </summary>
public sealed class ArchetypeDefinition
{
    /// <summary>
    /// Archetype id.
    /// </summary>
    public int ID { get; init; }

    /// <summary>
    /// Full name of types.
    /// </summary>
    public string[] TypeNames { get; init; }
}
