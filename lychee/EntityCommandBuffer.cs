namespace lychee;

public sealed class EntityCommandBuffer
{
#region Fields

    internal Archetype SrcArchetype;

    internal readonly ArchetypeManager ArchetypeManager;

    internal readonly TypeRegistry TypeRegistry;

    internal Dictionary<nint, Archetype> Unnamed = new();

#endregion

#region Public Methods

    public void AddComponent<T>(EntityCommandBuffer buffer, Entity entity, in T component) where T : unmanaged
    {
        var info = buffer.ArchetypeManager.GetEntityInfo(entity);
        var typeId = buffer.TypeRegistry.GetOrRegister<T>();
        var dst = buffer.SrcArchetype.GetInsertCompTargetArchetype(typeId) ??
                  buffer.ArchetypeManager.GetArchetype(
                      buffer.ArchetypeManager.GetOrCreateArchetype(buffer.SrcArchetype.TypeIdList.Append(typeId)));

        Monitor.Enter(dst);
    }

    public void AddComponents<T>(Entity entity, Func<T> func) where T : unmanaged
    {
        func.Method.MethodHandle.GetFunctionPointer();
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
    extension(EntityCommandBuffer buffer)
    {
        public void AddComponent<T>(Entity entity, in T component) where T : unmanaged
        {
            var info = buffer.ArchetypeManager.GetEntityInfo(entity);
            var typeId = buffer.TypeRegistry.GetOrRegister<T>();
            var dst = buffer.SrcArchetype.GetInsertCompTargetArchetype(typeId) ??
                      buffer.ArchetypeManager.GetArchetype(
                          buffer.ArchetypeManager.GetOrCreateArchetype(buffer.SrcArchetype.TypeIdList.Append(typeId)));

            unsafe
            {
                delegate* <EntityCommandBuffer, Entity, in T, void> ptr = &AddComponent<T>;
                buffer.Unnamed.Add((nint)ptr, dst);
            }

            Monitor.Enter(dst);
        }
    }
}
