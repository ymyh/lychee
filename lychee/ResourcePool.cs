using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using lychee.utils;

namespace lychee;

/// <summary>
/// Manages global resources for dependency injection within the ECS application.
/// Each resource type can only be added once. Resources can be reference types (stored as objects)
/// or unmanaged types (stored in aligned native memory for performance).
/// </summary>
/// <param name="typeRegistrar">The type registrar from the App for tracking component and resource types.</param>
public sealed class ResourcePool(TypeRegistrar typeRegistrar) : IDisposable
{
    private readonly Dictionary<Type, object> dataMap = new();

    /// <summary>
    /// Adds a reference-type resource to the pool.
    /// Each resource type can only be added once.
    /// </summary>
    /// <param name="resource">The resource instance to add.</param>
    /// <typeparam name="T">The resource type, must be a reference type.</typeparam>
    /// <returns>The resource instance that was added.</returns>
    /// <exception cref="ArgumentException">Thrown when a resource of this type already exists.</exception>
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
    /// Creates and adds a new reference-type resource with the default constructor.
    /// Each resource type can only be added once.
    /// </summary>
    /// <typeparam name="T">The resource type, must be a reference type with a default constructor.</typeparam>
    /// <returns>The newly created resource instance.</returns>
    /// <exception cref="ArgumentException">Thrown when a resource of this type already exists.</exception>
    public T AddResource<T>() where T : class, new()
    {
        return AddResource<T>(new());
    }

    /// <summary>
    /// Adds an unmanaged resource to the pool.
    /// The resource is copied into aligned native memory for optimal performance.
    /// Each resource type can only be added once.
    /// </summary>
    /// <param name="resource">The resource value to copy into native memory.</param>
    /// <typeparam name="T">The resource type, must be unmanaged.</typeparam>
    /// <exception cref="ArgumentException">Thrown when a resource of this type already exists.</exception>
    public void AddResourceStruct<T>(T resource) where T : unmanaged
    {
        typeRegistrar.Register<T>();

        unsafe
        {
            var size = (nuint)sizeof(T);
            var ptr = NativeMemory.AlignedAlloc(size, (nuint)TypeUtils.GetOrGuessAlignment(typeof(T), (int)size));
            NativeMemory.Copy(&resource, ptr, size);

            if (!dataMap.TryAdd(typeof(T), (nint)ptr))
            {
                throw new ArgumentException($"Resource {typeof(T).Name} already exists");
            }
        }
    }

    /// <summary>
    /// Creates and adds a new unmanaged resource with the default value.
    /// Each resource type can only be added once.
    /// </summary>
    /// <typeparam name="T">The resource type, must be unmanaged.</typeparam>
    /// <exception cref="ArgumentException">Thrown when a resource of this type already exists.</exception>
    public void AddResourceStruct<T>() where T : unmanaged
    {
        AddResourceStruct<T>(new());
    }

    /// <summary>
    /// Retrieves a reference-type resource from the pool by type.
    /// </summary>
    /// <typeparam name="T">The resource type, must be a reference type.</typeparam>
    /// <returns>The resource instance.</returns>
    /// <exception cref="ArgumentException">Thrown when no resource of this type exists.</exception>
    public T GetResource<T>() where T : class
    {
        return (T)GetResource(typeof(T));
    }

    /// <summary>
    /// Retrieves a resource from the pool by type.
    /// </summary>
    /// <param name="type">The type of the resource to retrieve.</param>
    /// <returns>The resource instance.</returns>
    /// <exception cref="ArgumentException">Thrown when no resource of this type exists.</exception>
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
    /// Gets a mutable reference to an unmanaged resource stored in native memory.
    /// </summary>
    /// <typeparam name="T">The resource type, must be unmanaged.</typeparam>
    /// <returns>A reference to the resource in native memory.</returns>
    /// <exception cref="ArgumentException">Thrown when no resource of this type exists.</exception>
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
    /// Gets a mutable reference to a reference-type resource in the pool.
    /// This provides direct access to the stored object without copying.
    /// </summary>
    /// <typeparam name="T">The resource type, must be a reference type.</typeparam>
    /// <returns>A reference to the resource.</returns>
    /// <exception cref="ArgumentException">Thrown when no resource of this type exists.</exception>
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
    /// Gets a pointer to an unmanaged resource stored in native memory.
    /// Useful for unsafe code scenarios requiring direct pointer access.
    /// </summary>
    /// <typeparam name="T">The resource type, must be unmanaged.</typeparam>
    /// <returns>A pointer to the resource in native memory.</returns>
    /// <exception cref="ArgumentException">Thrown when no resource of this type exists.</exception>
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

    /// <summary>
    /// Checks whether a resource of the specified type exists in the pool.
    /// </summary>
    /// <typeparam name="T">The resource type to check.</typeparam>
    /// <returns>True if the resource exists; otherwise, false.</returns>
    public bool HasResource<T>()
    {
        return dataMap.ContainsKey(typeof(T));
    }

#region IDisposable Members

    public void Dispose()
    {
        foreach (var (type, value) in dataMap)
        {
            if (TypeUtils.IsUnmanaged(type))
            {
                unsafe
                {
                    NativeMemory.AlignedFree((void*)(nint)value);
                }
            }
        }

        dataMap.Clear();
    }

#endregion
}
