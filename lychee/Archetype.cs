using System.Diagnostics;
using System.Runtime.InteropServices;
using lychee.collections;
using lychee.interfaces;
using lychee.utils;

namespace lychee;

public sealed class ArchetypeManager : IDisposable
{
    private readonly List<Archetype> archetypes = [];

    private readonly List<Entity> entities = [];

    private readonly List<EntityInfo> entitiesInfo = [];

    private readonly TypeRegistry typeRegistry;

    public ArchetypeManager(TypeRegistry typeRegistry)
    {
        this.typeRegistry = typeRegistry;
        GetOrCreateArchetype([]);
    }

    public int GetOrCreateArchetype(IEnumerable<int> typeIdList)
    {
        var array = typeIdList.ToArray();
        Array.Sort(array);

        foreach (var archetype in archetypes.Where(archetype => archetype.TypeIdList.SequenceEqual(array)))
        {
            return archetype.ID;
        }

        var id = archetypes.Count;
        var typeInfoList = array.Select(id => typeRegistry.GetTypeInfo(id).Item2).ToArray();
        archetypes.Add(new(id, array, typeInfoList));

        return id;
    }

    public int GetOrCreateArchetype<T>()
    {
        var typeList = TypeUtils.GetTupleTypes<T>();
        var typeIds = typeList.Select(x => typeRegistry.Register(x)).ToArray();

        return GetOrCreateArchetype(typeIds);
    }

    public int GetOrCreateArchetype2<T>() where T : IComponentBundle
    {
        var type = typeof(T);
        var fields = type.GetFields();
        var typeIds = fields.Select(f => typeRegistry.Register(f.FieldType)).ToArray();
        Array.Sort(typeIds);

        return GetOrCreateArchetype(typeIds);
    }

    public Archetype GetArchetype(int id)
    {
        Debug.Assert(id >= 0 && id < archetypes.Count);
        return archetypes[id];
    }

    public Archetype[] MatchArchetypesByPredicate(Type[] allFilter, Type[] anyFilter, Type[] noneFilter,
        int[] requires)
    {
        return archetypes.Where(a =>
        {
            var ret = requires.Aggregate(true, (current, typeId) => current & a.TypeIdList.Contains(typeId));
            return allFilter.Select(type => typeRegistry.Register(type))
                .Aggregate(ret, (current, typeId) => current & a.TypeIdList.Contains(typeId));
        }).Where(a =>
        {
            var ret = anyFilter.Length == 0;

            foreach (var type in anyFilter)
            {
                var typeId = typeRegistry.Register(type);
                ret |= a.TypeIdList.Contains(typeId);
            }

            return ret;
        }).Where(a =>
        {
            var ret = true;

            foreach (var type in noneFilter)
            {
                var typeId = typeRegistry.Register(type);
                ret &= !a.TypeIdList.Contains(typeId);
            }

            return ret;
        }).ToArray();
    }

    public void AddEntity(Entity entity)
    {
        entities.Add(entity);
        entitiesInfo.Add(new(0, 0));
    }

    public EntityInfo GetEntityInfo(Entity entity)
    {
        Debug.Assert(entity.ID >= 0 && entity.ID < entitiesInfo.Count);
        return entitiesInfo[entity.ID];
    }

    public void SetEntityInfo(Entity entity, EntityInfo entityInfo)
    {
        Debug.Assert(entity.ID >= 0 && entity.ID < entitiesInfo.Count);
        entitiesInfo[entity.ID] = entityInfo;
    }

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

public sealed class Archetype(int id, int[] typeIdList, TypeInfo[] typeInfoList)
    : IDisposable
{
#region Fields

    public readonly int ID = id;

    public readonly int[] TypeIdList = typeIdList;

    private readonly SparseMap<int> typeIdxMap = new(typeIdList.Select((id, index) => (id, index)));

    internal readonly Table Table = new(new(typeInfoList));

    private readonly SparseMap<Archetype> addTypeArchetypeMap = new();

    private readonly SparseMap<Archetype> removeTypeArchetypeMap = new();

    private readonly SparseMap<int[]> dstArchetypeCommCompIndices = new();

#endregion

#region Public Methods

    public IEnumerable<(nint ptr, int size)> IterateTypeAmongChunk(int typeId)
    {
        var typeIdx = GetTypeIndex(typeId);
        return Table.IterateOfTypeAmongChunk(typeIdx);
    }

#endregion

#region Internal Methods

    internal Archetype? GetInsertCompTargetArchetype(int typeId)
    {
        addTypeArchetypeMap.TryGetValue(typeId, out var archetype);
        return archetype;
    }

    internal Archetype? GetRemoveCompDstArchetype(int typeId)
    {
        removeTypeArchetypeMap.TryGetValue(typeId, out var archetype);
        return archetype;
    }

    internal void SetInsertCompTargetArchetype(int typeId, Archetype archetype)
    {
        addTypeArchetypeMap.Add(typeId, archetype);
    }

    internal void AddRemoveCompDstArchetype(int typeId, Archetype archetype)
    {
        removeTypeArchetypeMap.Add(typeId, archetype);
    }

    internal void MoveDataTo(EntityInfo info, Archetype archetype)
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

        var (chunkIdx, idx) = Table.GetChunkAndIndex(info.ArchetypeIdx);
        foreach (var index in commCompIndices)
        {
            unsafe
            {
                var src = Table.GetPtr(index, chunkIdx, idx);
                var dst = archetype.Table.GetLastPtr(index, chunkIdx);

                NativeMemory.Copy(src, dst, (nuint)Table.Layout.TypeInfoList[index].Size);
            }
        }
    }

    internal void PutPartialData<T>(EntityInfo info, int typeIdx, in T data) where T : unmanaged
    {
        unsafe
        {
            var dstPtr = Table.GetLastPtr(typeIdx, info.ArchetypeIdx);
            fixed (T* srcPtr = &data)
            {
                NativeMemory.Copy(srcPtr, dstPtr, (nuint)Table.Layout.TypeInfoList[typeIdx].Size);
            }
        }
    }

    internal int GetTypeIndex(int typeId)
    {
        return typeIdxMap[typeId];
    }

#endregion

#region Private methods

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
        addTypeArchetypeMap.Dispose();
        removeTypeArchetypeMap.Dispose();
        dstArchetypeCommCompIndices.Dispose();
        typeIdxMap.Dispose();

        Table.Dispose();
    }

#endregion
}
