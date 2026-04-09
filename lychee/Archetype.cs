using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using lychee.collections;
using lychee.extensions;
using lychee.interfaces;
using lychee.utils;

namespace lychee;

public sealed class ArchetypeManager : IDisposable
{
    public List<Archetype> Archetypes { get; } = [];

    private readonly TypeRegistrar typeRegistrar;

    private readonly Lock archetypeLock = new();

    public delegate void ArchetypeCreatedHandler();

    internal static Archetype EmptyArchetype { get; }

    /// <summary>
    /// Invoked when a new archetype is created.
    /// </summary>
    public event ArchetypeCreatedHandler? ArchetypeCreated;

    static ArchetypeManager()
    {
        EmptyArchetype = new(0, [], [], null!);
    }

    public ArchetypeManager(TypeRegistrar typeRegistrar)
    {
        this.typeRegistrar = typeRegistrar;
        Archetypes.Add(EmptyArchetype);
    }

#region Public methods

    public void ClearData()
    {
        foreach (var archetype in Archetypes)
        {
            archetype.Clear();
        }
    }

    /// <summary>
    /// Gets an existing archetype with the specified component types, or creates a new one if it doesn't exist.
    /// </summary>
    /// <param name="typeIdList">A collection of component type IDs that define the archetype.</param>
    /// <returns>The archetype matching the specified component types.</returns>
    /// <remarks>
    /// The type IDs are sorted internally to ensure consistent archetype identification.
    /// This method is thread-safe.
    /// </remarks>
    public Archetype GetOrCreateArchetype(IEnumerable<int> typeIdList)
    {
        var array = typeIdList.ToArray();
        Array.Sort(array);

        lock (archetypeLock)
        {
            foreach (var archetype in Archetypes)
            {
                if (archetype.TypeIdList.SequenceEqual(array))
                {
                    return archetype;
                }
            }

            var id = Archetypes.Count;
            var typeInfoList = array.Select(id => typeRegistrar.GetTypeInfo(id)).ToArray();
            Archetypes.Add(new(id, array, typeInfoList, typeRegistrar));

            ArchetypeCreated?.Invoke();

            return Archetypes[id];
        }
    }

    /// <summary>
    /// Gets or creates an archetype using a tuple type to specify component types.
    /// </summary>
    /// <typeparam name="T">A tuple type containing the component types (e.g., (Position, Velocity)).</typeparam>
    /// <returns>The archetype matching the specified component types.</returns>
    /// <remarks>
    /// This is a convenience method that extracts types from a tuple and registers them as components.
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
    /// This method extracts field types from the bundle struct and uses them as component types.
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
    /// Get archetype by id, this method is multi-thread safe.
    /// </summary>
    /// <param name="id">Target archetype id.</param>
    /// <returns></returns>
    public Archetype GetArchetype(int id)
    {
        lock (archetypeLock)
        {
            return Archetypes[id];
        }
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

        lock (archetypeLock)
        {
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
    }

#endregion

#region Internal methods

    internal void Commit(EntityPool entityPool)
    {
        foreach (var archetype in Archetypes)
        {
            archetype.Commit(entityPool);
        }
    }

    internal Archetype GetArchetypeUnsafe(int id)
    {
        return Archetypes[id];
    }

#endregion

#region IDisposable Member

    /// <summary>
    /// Releases all resources used by the ArchetypeManager and all managed archetypes.
    /// </summary>
    public void Dispose()
    {
        foreach (var archetype in Archetypes)
        {
            archetype.Dispose();
        }
    }

#endregion
}

public sealed class Archetype(int id, int[] typeIdList, TypeInfo[] typeInfoList, TypeRegistrar typeRegistrar) : IDisposable
{
#region Fields

    internal readonly Table Table = new(new(typeInfoList));

    private readonly SparseMap<int> typeIdxMap = new(typeIdList.Select((id, index) => (id, index)));

    private readonly SparseMap<(int[] src, int[] dst)> dstArchetypeCommCompIndices = new();

    private readonly SparseMap<EntityRef> entities = [];

    private readonly ConcurrentStack<(int id, ushort chunkIdx, ushort idx)> holesInTable = new();

    private bool dirty;

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

    /// <summary>
    /// Indicates whether the archetype's table and entity count are synchronized.
    /// </summary>
    /// <remarks>
    /// Returns true when the total count in the table equals the number of tracked entities.
    /// An archetype may be incoherent temporarily during structural changes.
    /// </remarks>
    public bool IsCoherent => Table.TotalCount == entities.Count;

#endregion

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

        Debug.Assert(IsCoherent);
    }

    internal void CommitAddEntity(EntityRef entityRef)
    {
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
            return (hole.chunkIdx, hole.idx);
        }

        dirty = true;
        return Table.Reserve();
    }

#endregion

#region Private methods

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

#region IDisposable Member

    public void Dispose()
    {
        Table.Dispose();
    }

#endregion
}
