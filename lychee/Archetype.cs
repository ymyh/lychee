using System.Diagnostics;
using System.Runtime.InteropServices;
using lychee.collections;
using lychee.interfaces;
using lychee.utils;

namespace lychee;

public sealed class ArchetypeManager
{
    private readonly TypeRegistry typeRegistry;

    private readonly List<Archetype> archetypes = [];

    private readonly List<EntityInfo> entitiesInfo = [];

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
        archetypes.Add(new Archetype(id, array, typeInfoList));

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

    public EntityInfo GetEntityInfo(Entity entity)
    {
        Debug.Assert(entity.ID >= 0 && entity.ID < entitiesInfo.Count);
        return entitiesInfo[entity.ID];
    }

    public void SetEntityInfo(Entity entity, EntityInfo entityInfo)
    {
        Debug.Assert(entity.ID >= 0 && entity.ID < entitiesInfo.Count);
        entitiesInfo[entity.ID] =  entityInfo;
    }
}

public sealed class Archetype
{
#region Fields

    public int ID { get; }

    public int[] TypeIdList { get; }

    private readonly Table table;

    private readonly Dictionary<int, Archetype> addTypeArchetypeDict = new();

    private readonly Dictionary<int, Archetype> removeTypeArchetypeDict = new();

    private readonly Dictionary<int, int[]> dstArchetypeCommCompIndices = new();

#endregion

#region Constructors

    public Archetype(int id, int[] typeIdList, TypeInfo[] typeInfoList)
    {
        ID = id;
        TypeIdList = typeIdList;

        var offset = 0;
        for (var i = 0; i < typeInfoList.Length; i++)
        {
            var info = typeInfoList[i];
            if (offset % info.Alignment != 0)
            {
                offset += info.Alignment - (offset % info.Alignment);
            }

            info.Offset = offset;
            typeInfoList[i] = info;

            offset += info.Size;
        }

        var layout = new TableLayout
        {
            MaxAlignment = typeInfoList.Max(x => x.Alignment),
            TypeInfoList = typeInfoList,
        };
        table = new Table(layout);
    }

#endregion

#region Public Methods



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

                NativeMemory.Copy(src, dst, (nuint) table.Layout.TypeInfoList[index].Size);
            }
        }
    }

    internal unsafe void* GetEntityCompData(int index, Entity entity)
    {
        return null;
    }

#endregion

#region Private Methods



#endregion
}
