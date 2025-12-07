using System.Runtime.InteropServices;
using lychee.collections;
using lychee.interfaces;
using lychee.utils;

namespace lychee;

public sealed class ArchetypeManager : IDisposable
{
    private readonly List<Archetype> archetypes = [];

    private readonly TypeRegistrar typeRegistrar;

    internal static Archetype EmptyArchetype { get; private set; } = null!;

    public delegate void ArchetypeCreatedHandler();

    /// <summary>
    /// Invoked when a new archetype is created.
    /// </summary>
    public event ArchetypeCreatedHandler? ArchetypeCreated;

    public ArchetypeManager(TypeRegistrar typeRegistrar)
    {
        this.typeRegistrar = typeRegistrar;
        GetOrCreateArchetype([]);

        EmptyArchetype = archetypes[0];
    }

#region Public methods

    public Archetype GetOrCreateArchetype(IEnumerable<int> typeIdList)
    {
        var array = typeIdList.ToArray();
        Array.Sort(array);

        lock (archetypes)
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
            archetypes.Add(new(id, array, typeInfoList));

            ArchetypeCreated?.Invoke();

            return archetypes[id];
        }
    }

    public Archetype GetOrCreateArchetype<T>()
    {
        var typeList = TypeUtils.GetTupleTypes<T>();
        var typeIds = typeList.Select(x => typeRegistrar.RegisterComponent(x)).ToArray();

        return GetOrCreateArchetype(typeIds);
    }

    public Archetype GetOrCreateArchetype2<T>() where T : IComponentBundle
    {
        var type = typeof(T);
        var fields = type.GetFields();
        var typeIds = fields.Select(f => typeRegistrar.RegisterComponent(f.FieldType)).ToArray();

        Array.Sort(typeIds);
        return GetOrCreateArchetype(typeIds);
    }

    public Archetype GetArchetype(int id)
    {
        lock (archetypes)
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

        lock (archetypes)
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

    internal void Commit()
    {
        foreach (var archetype in archetypes)
        {
            archetype.Commit();
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

public sealed class Archetype(int id, int[] typeIdList, TypeInfo[] typeInfoList) : IDisposable
{
#region Fields

    public readonly int ID = id;

    public readonly int[] TypeIdList = typeIdList;

    internal readonly Table Table = new(new(typeInfoList));

    private readonly SparseMap<int> typeIdxMap = new(typeIdList.Select((id, index) => (id, index)));

    private readonly SparseMap<int[]> dstArchetypeCommCompIndices = new();

    private readonly SparseMap<Entity> entities = [];

    private readonly Stack<(int id, int chunkIdx, int idx)> holesInTable = new();

    private bool dirty;

#endregion

#region Public Methods

    public IEnumerable<(nint ptr, int size)> IterateDataAmongChunk(int typeId)
    {
        var typeIdx = GetTypeIndex(typeId);
        return Table.IterateOfTypeAmongChunk(typeIdx);
    }

    public IEnumerable<(int chunkIdx, int chunkCount, int entityIdx)> IterateChunksAmongType(int groupSize)
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
                    yield return (chunkIdx, chunkCount, Table.CalcTotalOffset(chunkIdx, 0));
                    break;
                }

                continue;
            }

            yield return (chunkIdx, chunkCount, Table.CalcTotalOffset(chunkIdx, 0));

            chunkIdx += chunkCount;
            chunkCount = 0;
            count = 0;
        }
    }

    public (nint ptr, int size) GetChunkData(int typeId, int chunkIdx)
    {
        var typeIdx = GetTypeIndex(typeId);
        return Table.GetChunkData(typeIdx, chunkIdx);
    }

    public Span<(int, Entity)> GetEntitiesSpan()
    {
        return entities.GetDenseAsSpan();
    }

#endregion

#region Internal Methods

    internal void Commit()
    {
        if (!dirty || Table.Chunks.Count == 0)
        {
            return;
        }

        while (holesInTable.TryPop(out var hole))
        {
            var chunk = Table.Chunks[hole.chunkIdx];
            var from = chunk.Size + chunk.Reservation - 1;

            if (from > hole.idx)
            {
                FillHole(hole.chunkIdx, from, hole.idx);
            }

            if (chunk.Reservation > 0)
            {
                chunk.Reservation--;
            }
            else
            {
                chunk.Size--;
            }
        }

        Table.CommitReserved();
        dirty = false;
    }

    internal void CommitAddEntity(Entity entity)
    {
        entities[entity.ID] = entity;
    }

    internal void CommitRemoveEntity(Entity entity)
    {
        entities.Remove(entity.ID);
    }

    internal int GetTypeIndex(int typeId)
    {
        return typeIdxMap[typeId];
    }

    internal void MarkRemove(int id, int chunkIdx, int idx)
    {
        holesInTable.Push((id, chunkIdx, idx));
    }

    internal void MoveDataTo(Archetype archetype, int srcChunkIdx, int srcIdx, int dstChunkIdx, int dstIdx)
    {
        int[] commCompIndices;

        if (dstArchetypeCommCompIndices.TryGetValue(archetype.ID, out var compIndices))
        {
            commCompIndices = compIndices;
        }
        else
        {
            var commCompIds = TypeIdList.Intersect(archetype.TypeIdList).ToArray();
            commCompIndices = new int[commCompIds.Length];
            archetype.GetTypeIndices(commCompIds, commCompIndices);

            dstArchetypeCommCompIndices.Add(archetype.ID, commCompIndices);
        }

        foreach (var index in commCompIndices)
        {
            unsafe
            {
                var src = Table.GetPtr(index, srcChunkIdx, srcIdx);
                var dst = archetype.Table.GetPtr(index, dstChunkIdx, dstIdx);

                NativeMemory.Copy(src, dst, (nuint)Table.Layout.TypeInfoList[index].Size);
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

#endregion

#region IDisposable Member

    public void Dispose()
    {
        dstArchetypeCommCompIndices.Dispose();
        typeIdxMap.Dispose();

        Table.Dispose();
    }

#endregion
}
