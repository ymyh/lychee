namespace lychee;

public sealed class EntityCommandBuffer
{
#region Fields

    internal Archetype SrcArchetype;

    internal Archetype? DstArchetype;

    internal EntityPool EntityPool;

    internal readonly ArchetypeManager ArchetypeManager;

    internal readonly TypeRegistry TypeRegistry;

    internal Dictionary<nint, Archetype> Unnamed = new();

#endregion

#region Public Methods

    public Entity SpawnEntity()
    {
        var entity = EntityPool.NewEntity();
        return entity;
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
    }

#endregion
}

public static class EntityCommandBufferExtensions
{
    extension(EntityCommandBuffer self)
    {
        public void AddComponent<T>(Entity entity, in T component) where T : unmanaged
        {
            nint ptr;

            if (self.DstArchetype == null)
            {
                unsafe
                {
                    delegate* <EntityCommandBuffer, Entity, in T, void> fptr = &AddComponent<T>;
                    ptr = (nint)fptr;
                }

                if (self.Unnamed.TryGetValue(ptr, out var value))
                {
                    self.DstArchetype = value;
                }
                else
                {
                    var typeId = self.TypeRegistry.GetOrRegister<T>();
                    self.DstArchetype = self.SrcArchetype.GetInsertCompTargetArchetype(typeId) ??
                                        self.ArchetypeManager.GetArchetype(
                                            self.ArchetypeManager.GetOrCreateArchetype(
                                                self.SrcArchetype.TypeIdList.Append(typeId)));
                }
            }

            var entityInfo = self.EntityPool.GetEntityInfo(entity);

            if (entityInfo is { } info)
            {
            }
        }

        public void AddComponents<T>(Entity entity, in T component) where T : unmanaged
        {
        }
    }
}
