using System.Diagnostics;

namespace lychee;

public sealed class ResourcePool
{
    private readonly Dictionary<Type, object> dataMap = new();

    public void AddResource<T>(T resource)
    {
        Debug.Assert(resource != null);
        if (!dataMap.TryAdd(typeof(T), resource))
        {
            throw new Exception("Resource already exists");
        }
    }

    public T GetResource<T>()
    {
        return (T)dataMap[typeof(T)];
    }

    public void ReplaceResource<T>(T resource)
    {
        Debug.Assert(resource != null);
        if (dataMap.TryGetValue(typeof(T), out var oldResource))
        {
            dataMap[typeof(T)] = resource;
        }
        else
        {
            throw new Exception("Resource not found");
        }
    }
}
