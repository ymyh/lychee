using System.Diagnostics;
using System.Runtime.InteropServices;
using lychee.collections;
using lychee.interfaces;
using lychee.utils;

namespace lychee;

public sealed class ArchetypeManager : IDisposable
{
    private readonly List<Archetype> archetypes = [];

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
        archetypes.Add(new(id, array, typeInfoList, typeRegistry));

        return id;
    }

    public int GetOrCreateArchetype<T>()
    {
        var typeList = TypeUtils.GetTupleTypes<T>();
        var typeIds = typeList.Select(x => typeRegistry.GetOrRegister(x)).ToArray();

        return GetOrCreateArchetype(typeIds);
    }

    public int GetOrCreateArchetype2<T>() where T : IComponentBundle
    {
        var type = typeof(T);
        var fields = type.GetFields();
        var typeIds = fields.Select(f => typeRegistry.GetOrRegister(f.FieldType)).ToArray();
        Array.Sort(typeIds);

        return GetOrCreateArchetype(typeIds);
    }

    public Archetype GetArchetype(int id)
    {
        Debug.Assert(id >= 0 && id < archetypes.Count);
        return archetypes[id];
    }

    public Archetype[] GetArchetypeByPredicate(Type[] allFilter, Type[] anyFilter, Type[] noneFilter, Type[] requires)
    {
        return archetypes.Where(a =>
        {
            var ret = true;

            foreach (var type in requires)
            {
                var typeId = typeRegistry.GetOrRegister(type);
                ret &= a.TypeIdList.Contains(typeId);
            }

            foreach (var type in allFilter)
            {
                var typeId = typeRegistry.GetOrRegister(type);
                ret &= a.TypeIdList.Contains(typeId);
            }

            return ret;
        }).Where(a =>
        {
            var ret = anyFilter.Length == 0;

            foreach (var type in anyFilter)
            {
                var typeId = typeRegistry.GetOrRegister(type);
                ret |= a.TypeIdList.Contains(typeId);
            }

            return ret;
        }).Where(a =>
        {
            var ret = true;

            foreach (var type in noneFilter)
            {
                var typeId = typeRegistry.GetOrRegister(type);
                ret &= !a.TypeIdList.Contains(typeId);
            }

            return ret;
        }).ToArray();
    }

    public void AddEntityInfo(EntityInfo entityInfo)
    {
        entitiesInfo.Add(entityInfo);
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

public sealed class Archetype(int id, int[] typeIdList, TypeInfo[] typeInfoList, TypeRegistry typeRegistry)
    : IDisposable
{
#region Fields

    public readonly int ID = id;

    public readonly int[] TypeIdList = typeIdList;

    private readonly Table table = new(new(typeInfoList));

    private readonly Dictionary<int, Archetype> addTypeArchetypeDict = new();

    private readonly Dictionary<int, Archetype> removeTypeArchetypeDict = new();

    private readonly Dictionary<int, int[]> dstArchetypeCommCompIndices = new();

#endregion

#region IDisposable Member

    public void Dispose()
    {
        table.Dispose();
    }

#endregion

#region Internal Methods

    internal Archetype? GetInsertCompTargetArchetype(int typeId)
    {
        addTypeArchetypeDict.TryGetValue(typeId, out var archetype);
        return archetype;
    }

    internal Archetype? GetRemoveCompDstArchetype(int typeId)
    {
        removeTypeArchetypeDict.TryGetValue(typeId, out var archetype);
        return archetype;
    }

    internal void SetInsertCompTargetArchetype(int typeId, Archetype archetype)
    {
        addTypeArchetypeDict.Add(typeId, archetype);
    }

    internal void AddRemoveCompDstArchetype(int typeId, Archetype archetype)
    {
        removeTypeArchetypeDict.Add(typeId, archetype);
    }

    internal int GetTypeIndex(int typeId)
    {
        return Array.IndexOf(TypeIdList, typeId);
    }

    internal void GetTypeIndices(IEnumerable<int> typeIdList, Span<int> output)
    {
        var i = 0;
        foreach (var typeId in typeIdList)
        {
            output[i] = Array.IndexOf(TypeIdList, typeId);
            i += 1;
        }
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

        var (chunk, idx) = table.GetChunkAndIndex(info.ArchetypeIdx);
        foreach (var index in commCompIndices)
        {
            unsafe
            {
                var src = table.GetPtr(index, ref chunk, idx);
                var dst = archetype.table.GetPtr(index, ref chunk, chunk.Size);

                NativeMemory.Copy(src, dst, (nuint)table.Layout.TypeInfoList[index].Size);
            }
        }
    }

    internal IEnumerable<nint> IterateOverComp<T>() where T : unmanaged
    {
        var typeId = typeRegistry.GetTypeId<T>()!.Value;
        var typeIdx = GetTypeIndex(typeId);

        return table.IterateOverComp(typeIdx);
    }

#endregion

#region Private Methods

#endregion
}
