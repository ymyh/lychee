namespace lychee;

public sealed class EntityCommandBuffer
{
#region Fields

#endregion

#region Methods

    public void AddComponent<T>(Entity entity, in T component) where T : unmanaged
    {
    }

    public void AddComponents<T>(Entity entity, Func<T> func) where T : unmanaged
    {
    }

    public void RemoveComponent<T>(Entity entity) where T : unmanaged
    {
    }

    public void RemoveComponents<T>(Entity entity, Func<T> func) where T : unmanaged
    {
    }

#endregion
}
