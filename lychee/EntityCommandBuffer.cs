using lychee.collections;
using lychee.interfaces;

namespace lychee;

internal sealed class EntityTransferInfo(Archetype archetype, int typeId, int viewIdx)
{
    public readonly Archetype Archetype = archetype;

    public readonly int TypeId = typeId;

    public readonly int ViewIdx = viewIdx;
}

public sealed class EntityCommandBuffer(World world) : IDisposable
{
#region Fields

    internal readonly EntityPool EntityPool = world.EntityPool;

    internal readonly ArchetypeManager ArchetypeManager = world.ArchetypeManager;

    internal readonly TypeRegistry TypeRegistry = world.TypeRegistry;

    internal readonly Dictionary<nint, SparseMap<EntityTransferInfo>> SrcArchetypeAddingTypeDict = new();

    internal Archetype SrcArchetype = null!;

    internal EntityTransferInfo? CurrentTransferInfo;

    internal bool ArchetypeChanged;

#endregion

#region Public Methods

    public Entity NewEntity()
    {
        var entity = EntityPool.NewEntity();
        return entity;
    }

    public bool RemoveEntity(Entity entity)
    {
        return EntityPool.RemoveEntity(entity);
    }

    public void RemoveComponent<T>(Entity entity) where T : unmanaged
    {
    }

    public void RemoveComponents<T>(Entity entity) where T : unmanaged
    {
    }

#endregion

#region Internal Methods

    internal void ChangeSrcArchetype(Archetype archetype)
    {
        if (CurrentTransferInfo != null)
        {
            Monitor.Exit(CurrentTransferInfo.Archetype);
        }

        ArchetypeChanged = true;
        SrcArchetype = archetype;
    }

#endregion

#region IDisposable Member

    public void Dispose()
    {
        foreach (var item in SrcArchetypeAddingTypeDict)
        {
            item.Value.Dispose();
        }
    }

#endregion
}

public static class EntityCommandBufferExtensions
{
    extension(EntityCommandBuffer self)
    {
        public bool AddComponent<T>(Entity entity, in T component) where T : unmanaged, IComponent
        {
            if (!self.EntityPool.GetEntityInfo(entity, out var entityInfo))
            {
                return false;
            }

            if (self.ArchetypeChanged)
            {
                nint ptr;
                unsafe
                {
                    ptr = (nint)(delegate* <EntityCommandBuffer, Entity, in T, bool>)&AddComponent<T>;
                }

                if (self.SrcArchetypeAddingTypeDict.TryGetValue(ptr, out var map))
                {
                    map.TryGetValue(self.SrcArchetype.ID, out self.CurrentTransferInfo);
                }
                else
                {
                    map = new();
                    self.SrcArchetypeAddingTypeDict.Add(ptr, map);
                }

                if (self.CurrentTransferInfo is null)
                {
                    var typeId = self.TypeRegistry.Register<T>();
                    var dstArchetype = self.SrcArchetype.GetInsertCompTargetArchetype(typeId) ??
                                       self.ArchetypeManager.GetArchetype(
                                           self.ArchetypeManager.GetOrCreateArchetype(
                                               self.SrcArchetype.TypeIdList.Append(typeId)));

                    self.CurrentTransferInfo = new(dstArchetype, dstArchetype.GetTypeIndex(typeId),
                        dstArchetype.Table.GetFirstAvailableViewIdx());
                    map.Add(self.SrcArchetype.ID, self.CurrentTransferInfo);
                }
            }

            self.CurrentTransferInfo!.Archetype.Table.ReserveOne(self.CurrentTransferInfo.ViewIdx);
            self.CurrentTransferInfo.Archetype.PutPartialData(entityInfo, self.CurrentTransferInfo.TypeId,
                in component);
            self.SrcArchetype.MoveDataTo(entityInfo, self.CurrentTransferInfo.Archetype);

            return true;
        }

        public void AddComponents<T>(Entity entity, in T component) where T : unmanaged, IComponentBundle
        {
        }
    }
}
