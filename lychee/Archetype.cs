using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using lychee.collections;
using lychee.interfaces;
using lychee.utils;

namespace lychee;

public sealed class ArchetypeManager : IDisposable
{
    private readonly List<Archetype> archetypes = [];

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
        archetypes.Add(EmptyArchetype);
    }

#region Public methods

    public Archetype GetOrCreateArchetype(IEnumerable<int> typeIdList)
    {
        var array = typeIdList.ToArray();
        Array.Sort(array);

        lock (archetypeLock)
        {
            foreach (var archetype in archetypes)
            {
                if (archetype.TypeIdList.SequenceEqual(array))
                {
                    return archetype;
                }
            }

            var id = archetypes.Count;
            var typeInfoList = array.Select(id => typeRegistrar.GetTypeInfo(id)).ToArray();
            archetypes.Add(new(id, array, typeInfoList, typeRegistrar));

            ArchetypeCreated?.Invoke();

            return archetypes[id];
        }
    }

    public Archetype GetOrCreateArchetypeWithTuple<T>()
    {
        var typeList = TypeUtils.GetTupleTypes<T>();
        var typeIds = typeList.Select(x => typeRegistrar.RegisterComponent(x)).ToArray();

        return GetOrCreateArchetype(typeIds);
    }

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
            return archetypes[id];
        }
    }

    public Archetype[] MatchArchetypesByPredicate(Type[] allFilter, Type[] anyFilter, Type[] noneFilter,
        int[] typeRequires)
    {
        if (typeRequires.Length == 0)
        {
            return [];
        }

        lock (archetypeLock)
        {
            return archetypes.Where(a =>
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
        foreach (var archetype in archetypes)
        {
            archetype.Commit(entityPool);
        }
    }

    internal Archetype GetArchetypeUnsafe(int id)
    {
        return archetypes[id];
    }

#endregion

#region IDisposable Member

    public void Dispose()
    {
        foreach (var archetype in archetypes)
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

    public int ID { get; } = id;

    public int[] TypeIdList { get; } = typeIdList.Distinct().Count() != typeIdList.Length ? throw new ArgumentException("Duplicate type id in archetype.") : typeIdList;

    public Type[] Types => typeIdList.Select(typeRegistrar.GetTypeById).ToArray();

    public bool IsCoherent => Table.TotalCount == entities.Count;

#endregion

#endregion

#region Public Methods

    public IEnumerable<(nint ptr, int size)> IterateDataAmongChunk(int typeId)
    {
        var typeIdx = GetTypeIndex(typeId);
        return Table.IterateOfTypeAmongChunk(typeIdx);
    }

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

    internal (nint ptr, int size) GetChunkDataWithReservation(int typeId, int chunkIdx)
    {
        var typeIdx = GetTypeIndex(typeId);
        return Table.GetChunkDataWithReservation(typeIdx, chunkIdx);
    }

    public Span<(int, EntityRef)> GetEntitiesSpan()
    {
        return entities.GetDenseAsSpan();
    }

#endregion

#region Internal Methods

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
        ShirkTable();
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

    private void GetTypeIndices(IEnumerable<int> typeIdList, Span<int> output)
    {
        var i = 0;
        foreach (var typeId in typeIdList)
        {
            output[i] = typeIdxMap[typeId];
            i++;
        }
    }

    private void ShirkTable()
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
