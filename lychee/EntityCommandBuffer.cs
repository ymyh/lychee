using lychee.collections;
using lychee.interfaces;

namespace lychee;

internal sealed class EntityMovingInfo(Archetype srcArchetype)
{
    private readonly Archetype srcArchetype = srcArchetype;

    public readonly SparseMap<Archetype> DstArchetypeList = new();

    public int ViewIdx;
}

public sealed class EntityCommandBuffer(World world) : IDisposable
{
#region Fields

    internal Archetype SrcArchetype;

    internal Archetype? DstArchetype;

    internal readonly EntityPool EntityPool = world.EntityPool;

    internal readonly ArchetypeManager ArchetypeManager = world.ArchetypeManager;

    internal readonly TypeRegistry TypeRegistry = world.TypeRegistry;

    internal readonly Dictionary<nint, Archetype> Unnamed = new();

    internal readonly SparseMap<EntityMovingInfo> Unnamed2 = new();

    internal EntityMovingInfo? CurrentTransferInfo;

    internal int DstArchetypeExtraTypeId;

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
            Monitor.Exit(CurrentTransferInfo.DstArchetypeList);
        }

        Unnamed2.TryGetValue(archetype.ID, out CurrentTransferInfo);
        SrcArchetype = archetype;
    }

#endregion

#region IDisposable Member

    public void Dispose()
    {
        Unnamed2.Dispose();
    }

#endregion
}

public static class EntityCommandBufferExtensions
{
    extension(EntityCommandBuffer self)
    {
        public bool AddComponent<T>(Entity entity, in T component) where T : unmanaged, IComponent
        {
            var entityInfo = self.EntityPool.GetEntityInfo(entity);
            if (entityInfo is null)
            {
                return false;
            }

            EntityMovingInfo entityMovingInfo;

            if (self.CurrentTransferInfo == null)
            {
                var typeId = self.TypeRegistry.Register<T>();
                var dstArchetype = self.SrcArchetype.GetInsertCompTargetArchetype(typeId) ??
                                    self.ArchetypeManager.GetArchetype(
                                        self.ArchetypeManager.GetOrCreateArchetype(
                                            self.SrcArchetype.TypeIdList.Append(typeId)));

                entityMovingInfo = new(self.SrcArchetype);

                self.Unnamed2.Add(self.SrcArchetype.ID, entityMovingInfo);
            }

            if (self.DstArchetype == null)
            {
                nint ptr;
                unsafe
                {
                    delegate* <EntityCommandBuffer, Entity, in T, bool> fptr = &AddComponent<T>;
                    ptr = (nint)fptr;
                }

                if (self.Unnamed.TryGetValue(ptr, out var value))
                {
                    self.DstArchetype = value;
                }
                else
                {
                    var typeId = self.TypeRegistry.Register<T>();
                    self.DstArchetype = self.SrcArchetype.GetInsertCompTargetArchetype(typeId) ??
                                        self.ArchetypeManager.GetArchetype(
                                            self.ArchetypeManager.GetOrCreateArchetype(
                                                self.SrcArchetype.TypeIdList.Append(typeId)));
                }

                self.DstArchetypeExtraTypeId = self.DstArchetype.GetTypeIndex(self.TypeRegistry.GetTypeId<T>()!.Value);
                Monitor.Enter(self.DstArchetype);
            }

            var dstViewIdx = self.DstArchetype.Table.GetFirstAvailableViewIdx();
            self.DstArchetype.Table.ReserveOne(dstViewIdx);

            self.DstArchetype.PutPartialData(entityInfo.Value, self.DstArchetypeExtraTypeId, in component);
            self.SrcArchetype.MoveDataTo(entityInfo.Value, self.DstArchetype);

            return true;
        }

        public void AddComponents<T>(Entity entity, in T component) where T : unmanaged, IComponentBundle
        {
        }
    }
}
