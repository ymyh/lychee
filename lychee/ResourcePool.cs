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
            throw new ArgumentException("Resource already exists");
        }
    }

    public T GetResource<T>()
    {
        Debug.Assert(dataMap.ContainsKey(typeof(T)));

        return (T)dataMap[typeof(T)];
    }

    public void ReplaceResource<T>(T resource)
    {
        Debug.Assert(resource != null);

        if (dataMap.ContainsKey(typeof(T)))
        {
            dataMap[typeof(T)] = resource;
        }
        else
        {
            throw new ArgumentException("Resource not found");
        }
    }
}
