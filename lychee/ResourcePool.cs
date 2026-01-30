using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using lychee.utils;

namespace lychee;

/// <summary>
/// Holds all resources. Each type can only add once.
/// </summary>
/// <param name="typeRegistrar">The <see cref="TypeRegistrar"/> from <see cref="App"/>.</param>
public sealed class ResourcePool(TypeRegistrar typeRegistrar)
{
    private readonly Dictionary<Type, object> dataMap = new();

    /// <summary>
    /// Add a new resource to the pool.
    /// </summary>
    /// <param name="resource">The resource to add.</param>
    /// <typeparam name="T">The type of the resource, must be class.</typeparam>
    /// <returns>The resource just added.</returns>
    /// <exception cref="ArgumentException">Resource already exists.</exception>
    public T AddResource<T>(T resource) where T : class
    {
        Debug.Assert(resource != null);
        typeRegistrar.Register<T>();

        var type = typeof(T);

        if (!dataMap.TryAdd(type, resource))
        {
            throw new ArgumentException($"Resource {typeof(T).Name} already exists");
        }

        return resource;
    }

    /// <summary>
    /// Add a new resource to the pool.
    /// </summary>
    /// <typeparam name="T">The type of the resource, must be class.</typeparam>
    /// <returns>The resource just added.</returns>
    /// <exception cref="ArgumentException">Resource already exists.</exception>
    public T AddResource<T>() where T : class, new()
    {
        return AddResource<T>(new());
    }

    /// <summary>
    /// Add a new resource to the pool.
    /// </summary>
    /// <param name="resource">The resource to add.</param>
    /// <typeparam name="T">The type of the resource, must be unmanaged.</typeparam>
    /// <exception cref="ArgumentException">Resource already exists.</exception>
    public void AddResourceStruct<T>(T resource) where T : unmanaged
    {
        typeRegistrar.Register<T>();

        unsafe
        {
            var size = (nuint)sizeof(T);
            var ptr = NativeMemory.AlignedAlloc(size, (nuint)TypeUtils.GetOrGuessAlignment(typeof(T), (int)size));
            // var arr = GC.AllocateArray<byte>(sizeof(T), true);
            NativeMemory.Copy(&resource, ptr, size);
            // Marshal.Copy((nint)(&resource), arr, 0, arr.Length);

            if (!dataMap.TryAdd(typeof(T), (nint)ptr))
            {
                throw new ArgumentException($"Resource {typeof(T).Name} already exists");
            }
        }
    }

    /// <summary>
    /// Add a new resource to the pool.
    /// </summary>
    /// <typeparam name="T">The type of the resource, must be unmanaged.</typeparam>
    /// <exception cref="ArgumentException">Resource already exists.</exception>
    public void AddResourceStruct<T>() where T : unmanaged
    {
        AddResourceStruct<T>(new());
    }

    /// <summary>
    /// Gets the resource from the pool by given type.
    /// </summary>
    /// <typeparam name="T">The type of the resource, must be class.</typeparam>
    /// <returns>The resource.</returns>
    /// <exception cref="ArgumentException">Resource does not exist.</exception>
    public T GetResource<T>() where T : class
    {
        return (T)GetResource(typeof(T));
    }

    /// <summary>
    /// Gets the resource from the pool by given type.
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
    /// Gets a resource from the pool.
    /// </summary>
    /// <typeparam name="T">The type of the resource, must be unmanaged.</typeparam>
    /// <returns>The resource.</returns>
    /// <exception cref="ArgumentException">Resource does not exist.</exception>
    public ref T GetResourceStructRef<T>() where T : unmanaged
    {
        try
        {
            unsafe
            {
                var ptr = (T*)(nint)dataMap[typeof(T)];
                return ref *ptr;
            }
        }
        catch (KeyNotFoundException)
        {
            throw new ArgumentException($"Resource {typeof(T).Name} does not exist");
        }
    }

    /// <summary>
    /// Gets a class type resource from the pool.
    /// </summary>
    /// <typeparam name="T">The type of the resource, must be class.</typeparam>
    /// <returns>The resource.</returns>
    /// <exception cref="ArgumentException">Resource does not exist.</exception>
    public ref T GetResourceClassRef<T>() where T : class
    {
        ref var value = ref CollectionsMarshal.GetValueRefOrNullRef(dataMap, typeof(T));
        if (Unsafe.IsNullRef(ref value))
        {
            throw new ArgumentException($"Resource {typeof(T).Name} does not exist");
        }

        return ref Unsafe.As<object, T>(ref value);
    }

    /// <summary>
    /// Gets a pointer of struct type resource from the pool.
    /// </summary>
    /// <typeparam name="T">The type of the resource.</typeparam>
    /// <returns>The resource pointer.</returns>
    /// <exception cref="ArgumentException">Resource does not exist.</exception>
    public unsafe T* GetResourcePtr<T>() where T : unmanaged
    {
        try
        {
            return (T*)(nint)dataMap[typeof(T)];
        }
        catch (KeyNotFoundException)
        {
            throw new ArgumentException($"Resource {typeof(T).Name} does not exist");
        }
    }
}
