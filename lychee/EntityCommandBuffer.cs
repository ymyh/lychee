namespace lychee;

public sealed class EntityCommandBuffer
{
#region Fields

#endregion

#region Methods

    public void AddComponent<T>(Entity entity, in T component)
    {
    }

    public void AddComponents<T>(Entity entity, Func<T> func)
    {
    }

#endregion
}