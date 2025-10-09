namespace lychee;

public sealed class World : IDisposable
{
#region Fields

    public readonly SystemSchedules SystemSchedules = new();

    public readonly EntityPool EntityPool = new();

    public readonly ArchetypeManager ArchetypeManager;

#endregion

#region Constructors

    public World(TypeRegistry typeRegistry)
    {
        ArchetypeManager = new(typeRegistry, SystemSchedules);
    }

#endregion

#region Public methods

    // public void AddComponent<T>(Entity entity, T component) where T : unmanaged, IComponent
    // {
    //     AddComponent(entity, ref component);
    // }

    // public void AddComponent<T>(Entity entity, ref T component) where T : unmanaged, IComponent
    // {
    //     var info = EntityPool.GetEntityInfo(entity);
    //
    //     if (info is { } entityInfo)
    //     {
    //         var typeId = typeRegistry.GetOrRegister<T>();
    //         var srcArchetype = archetypeManager.GetArchetype(entityInfo.ArchetypeId);
    //         var dstArchetype = srcArchetype.GetInsertCompTargetArchetype(typeId);
    //
    //         if (dstArchetype == null)
    //         {
    //             var typeIdList = srcArchetype.TypeIdList.Append(typeRegistry.GetOrRegister<T>());
    //             var archetypeId = archetypeManager.GetOrCreateArchetype(typeIdList);
    //
    //             dstArchetype = archetypeManager.GetArchetype(archetypeId);
    //             srcArchetype.SetInsertCompTargetArchetype(typeId, dstArchetype);
    //         }
    //
    //         srcArchetype.MoveDataTo(entityInfo, dstArchetype);
    //     }
    // }

    // public void AddComponents<T>(Entity entity, T bundle) where T : unmanaged, IComponentBundle
    // {
    //     var info = EntityPool.GetEntityInfo(entity);
    //     var typeId = typeRegistry.GetOrRegister<T>();
    //
    //     if (info is { } bundleInfo)
    //     {
    //         var srcArchetype = archetypeManager.GetArchetype(bundleInfo.ArchetypeId);
    //         var dstArchetype = srcArchetype.GetInsertCompTargetArchetype(typeId);
    //
    //         if (dstArchetype == null)
    //         {
    //             var typeIdList = srcArchetype.TypeIdList.Concat(TypeUtils.GetBundleTypes<T>()
    //                 .Select(t => typeRegistry.GetOrRegister(t)));
    //             var archetypeId = archetypeManager.GetOrCreateArchetype(typeIdList);
    //
    //             dstArchetype = archetypeManager.GetArchetype(archetypeId);
    //             srcArchetype.SetInsertCompTargetArchetype(typeId, dstArchetype);
    //         }
    //
    //         _ = dstArchetype.ID;
    //     }
    // }

    public void Update()
    {
        SystemSchedules.Execute();
    }

#endregion

#region IDisposable Member

    public void Dispose()
    {
        ArchetypeManager.Dispose();
    }

#endregion
}
