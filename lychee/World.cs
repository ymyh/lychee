using lychee.interfaces;
using lychee.utils;

namespace lychee;

public sealed class World(TypeRegistry typeRegistry)
{
#region Fields

    private TypeRegistry TypeRegistry { get; } = typeRegistry;

    private readonly EntityPool entityPool = new();

    private readonly ArchetypeManager archetypeManager = new(typeRegistry);

    public ISystemExecutor SystemExecutor { get; set; } = new SystemExecutor();

#endregion

#region Methods

    public int GetOrCreateArchetype(IEnumerable<int> typeIdList)
    {
        return archetypeManager.GetOrCreateArchetype(typeIdList);
    }

    public int GetOrCreateArchetype<T>()
    {
        return archetypeManager.GetOrCreateArchetype<T>();
    }

    public Entity NewEntity()
    {
        return entityPool.NewEntity();
    }

    /// <summary>
    /// Remove entity by id
    /// </summary>
    /// <param name="id"></param>
    /// <exception cref="ArgumentOutOfRangeException">if id is invalid</exception>
    public void RemoveEntity(int id)
    {
        entityPool.RemoveEntity(id);
    }

    public void AddComponent<T>(Entity entity, T component) where T : IComponent
    {
        AddComponent(entity, ref component);
    }

    public void AddComponent<T>(Entity entity, ref T component) where T : IComponent
    {
        var info = entityPool.GetEntityInfo(entity);

        if (info is { } entityInfo)
        {
            var typeId = typeRegistry.GetOrRegister<T>();
            var srcArchetype = archetypeManager.GetArchetype(entityInfo.ArchetypeId);
            var dstArchetype = srcArchetype.GetInsertCompTargetArchetype(typeId);

            if (dstArchetype == null)
            {
                var typeIdList = srcArchetype.TypeIdList.Append(typeRegistry.GetOrRegister<T>());
                var archetypeId = archetypeManager.GetOrCreateArchetype(typeIdList);

                dstArchetype = archetypeManager.GetArchetype(archetypeId);
                srcArchetype.SetInsertCompTargetArchetype(typeId, dstArchetype);
            }

            srcArchetype.MoveDataTo(entityInfo, dstArchetype);
        }
    }

    public void AddComponents<T>(Entity entity, T bundle) where T : IComponentBundle
    {
        var info = entityPool.GetEntityInfo(entity);
        var typeId = typeRegistry.GetOrRegister<T>();

        if (info is { } bundleInfo)
        {
            var srcArchetype = archetypeManager.GetArchetype(bundleInfo.ArchetypeId);
            var dstArchetype = srcArchetype.GetInsertCompTargetArchetype(typeId);

            if (dstArchetype == null)
            {
                var typeIdList =
                    srcArchetype.TypeIdList.Concat(TypeUtils.GetBundleTypes<T>()
                        .Select(t => TypeRegistry.GetOrRegister(t)));
                var archetypeId = archetypeManager.GetOrCreateArchetype(typeIdList);
                dstArchetype = archetypeManager.GetArchetype(archetypeId);
                srcArchetype.SetInsertCompTargetArchetype(typeId, dstArchetype);
            }

            _ = dstArchetype.ID;
        }
    }

    public void Update()
    {
        SystemExecutor.Execute();
    }

#endregion
}
