namespace lychee;

public sealed class EntityCommandBuffer
{
#region Fields

    private ArchetypeManager archetypeManager;

    private TypeRegistry typeRegistry;

#endregion

#region Methods

    public void AddComponent<T>(Entity entity, in T component) where T : unmanaged
    {
        var info = archetypeManager.GetEntityInfo(entity);
        var src = archetypeManager.GetArchetype(info.ArchetypeId);
        var typeId = typeRegistry.GetOrRegister(typeof(T));
        var dst = src.GetInsertCompTargetArchetype(typeId) ?? archetypeManager.GetArchetype(archetypeManager.GetOrCreateArchetype(src.TypeIdList.Append(typeId)));
    }

    public void AddComponents<T>(Entity entity, Func<T> func) where T : unmanaged
    {

    }

    public void RemoveComponent<T>(Entity entity) where T : unmanaged
    {
    }

    public void RemoveComponents<T>(Entity entity) where T : unmanaged
    {
    }

#endregion
}
