using lychee.interfaces;

namespace lychee;

public sealed class EntityCommandBuffer(World world)
{
#region Fields

    internal Archetype SrcArchetype;

    internal Archetype? DstArchetype;

    internal readonly EntityPool EntityPool = world.EntityPool;

    internal readonly ArchetypeManager ArchetypeManager = world.ArchetypeManager;

    internal readonly TypeRegistry TypeRegistry = world.TypeRegistry;

    internal readonly Dictionary<nint, Archetype> Unnamed = new();

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
        SrcArchetype = archetype;

        if (DstArchetype != null)
        {
            Monitor.Exit(DstArchetype);
        }

        DstArchetype = null;
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

            self.DstArchetype.PutPartialData(entityInfo.Value, self.DstArchetypeExtraTypeId, in component);
            self.SrcArchetype.MoveDataTo(entityInfo.Value, self.DstArchetype);

            return true;
        }

        public void AddComponents<T>(Entity entity, in T component) where T : unmanaged, IComponentBundle
        {
        }
    }
}
