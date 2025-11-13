using System.Diagnostics;
using System.Runtime.InteropServices;

namespace lychee;

/// <summary>
/// Holds all resources. Each type can only add once, or update the existing one.
/// </summary>
/// <param name="typeRegistrar">The TypeRegistrar from App.</param>
public sealed class ResourcePool(TypeRegistrar typeRegistrar)
{
    private readonly Dictionary<Type, object> dataMap = new();

    /// <summary>
    /// Add a new resource to the pool.
    /// </summary>
    /// <param name="resource">The resource to add.</param>
    /// <typeparam name="T">The type of the resource.</typeparam>
    /// <returns>The resource just added.</returns>
    /// <exception cref="ArgumentException">Resource already exists.</exception>
    public T AddResource<T>(T resource)
    {
        Debug.Assert(resource != null);
        typeRegistrar.Register<T>();

        var type = typeof(T);

        if (type.IsValueType)
        {
            unsafe
            {
                var arr = GC.AllocateArray<byte>(sizeof(T), true);
                Marshal.Copy((nint)(&resource), arr, 0, arr.Length);

                if (!dataMap.TryAdd(type, arr))
                {
                    throw new ArgumentException($"Resource {typeof(T).Name} already exists");
                }
            }
        }
        else
        {
            if (!dataMap.TryAdd(type, resource))
            {
                throw new ArgumentException($"Resource {typeof(T).Name} already exists");
            }
        }

        return resource;
    }

    /// <summary>
    /// Get the resource from the pool.
    /// </summary>
    /// <typeparam name="T">The type of the resource.</typeparam>
    /// <returns>The resource.</returns>
    /// <exception cref="ArgumentException">Resource does not exist.</exception>
    public T GetResource<T>()
    {
        return (T)GetResource(typeof(T));
    }

    /// <summary>
    /// Get the resource from the pool.
    /// </summary>
    /// <param name="type">The type of the resource.</param>
    /// <returns></returns>
    /// <exception cref="ArgumentException">Resource does not exist.</exception>
    public object GetResource(Type type)
    {
        try
        {
            return dataMap[type];
        }
        catch (KeyNotFoundException)
        {
            throw new ArgumentException($"Resource {type.Name} does not exist");
        }
    }

    /// <summary>
    /// Get the resource from the pool.
    /// </summary>
    /// <typeparam name="T">The type of the resource.</typeparam>
    /// <returns>The resource.</returns>
    /// <exception cref="ArgumentException">Resource does not exist.</exception>
    public ref T GetResourceRef<T>() where T : unmanaged
    {
        try
        {
            var arr = (byte[])dataMap[typeof(T)];
            return ref MemoryMarshal.AsRef<T>(new Span<byte>(arr));
        }
        catch (KeyNotFoundException)
        {
            throw new ArgumentException($"Resource {typeof(T).Name} does not exist");
        }
    }

    /// <summary>
    /// Get the resource from the pool.
    /// </summary>
    /// <typeparam name="T">The type of the resource.</typeparam>
    /// <returns>The resource.</returns>
    /// <exception cref="ArgumentException">Resource does not exist.</exception>
    public byte[] GetResourcePtr<T>() where T : unmanaged
    {
        try
        {
            return (byte[])dataMap[typeof(T)];
        }
        catch (KeyNotFoundException)
        {
            throw new ArgumentException($"Resource {typeof(T).Name} does not exist");
        }
    }

    /// <summary>
    /// Update the resource in the pool.
    /// </summary>
    /// <param name="resource">The resource to update.</param>
    /// <typeparam name="T">The type of the resource.</typeparam>
    /// <exception cref="ArgumentException">Resource does not exist.</exception>
    public void UpdateResource<T>(T resource) where T : class
    {
        Debug.Assert(resource != null);

        if (dataMap.ContainsKey(typeof(T)))
        {
            dataMap[typeof(T)] = resource;
        }
        else
        {
            throw new ArgumentException($"Resource {typeof(T).Name} does not exist");
        }
    }
}