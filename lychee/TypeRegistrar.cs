using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.InteropServices;
using lychee.interfaces;
using lychee.threading;
using lychee.utils;

namespace lychee;

/// <summary>
/// Stores type metadata including size, alignment, and offset information.
/// Used for efficient memory layout calculations in archetype storage.
/// </summary>
[StructLayout(LayoutKind.Explicit)]
public struct TypeInfo(int size, int alignment)
{
    /// <summary>The size of the type in bytes.</summary>
    [FieldOffset(0)] public int Size = size;

    /// <summary>The alignment requirement of the type in bytes.</summary>
    [FieldOffset(4)] public int Alignment = alignment;

    /// <summary>The offset of the type within a bundle or memory layout.</summary>
    [FieldOffset(4)] public int Offset;
}

/// <summary>
/// Centralized type registry for the ECS application.
/// Assigns unique IDs to component and resource types, tracks type metadata,
/// and manages thread-safe type registration during system initialization.
/// </summary>
public sealed class TypeRegistrar
{
    private readonly ReadWriteLock<List<TypeInfo>> typeListLock = new([]);

    private readonly ConcurrentDictionary<Type, int> typeToIdDict = new();

    private readonly ConcurrentDictionary<Type, (TypeInfo info, int typeId)[]> bundleToInfoDict = new();

    private static readonly MethodInfo RegisterMethod =
        typeof(TypeRegistrar).GetMethod("Register", BindingFlags.NonPublic | BindingFlags.Instance, [typeof(uint)])!;

    private static readonly MethodInfo RegisterComponentMethod =
        typeof(TypeRegistrar).GetMethod("RegisterComponent", BindingFlags.Public | BindingFlags.Instance, [typeof(uint)])!;

    private static readonly MethodInfo RegisterBundleMethod =
        typeof(TypeRegistrar).GetMethod("RegisterBundle", BindingFlags.Public | BindingFlags.Instance, [])!;

    /// <summary>
    /// Registers a component type and returns its unique type identifier.
    /// Subsequent registrations of the same type return the existing identifier.
    /// </summary>
    /// <param name="alignment">The alignment requirement in bytes. Default is 0 for automatic detection.</param>
    /// <typeparam name="T">The component type to register. Must be unmanaged and implement IComponent.</typeparam>
    /// <returns>The unique type identifier assigned to this component type.</returns>
    public int RegisterComponent<T>(uint alignment = 0) where T : unmanaged, IComponent
    {
        return Register<T>(alignment);
    }

    /// <summary>
    /// Registers a component type using reflection and returns its unique type identifier.
    /// Subsequent registrations of the same type return the existing identifier.
    /// </summary>
    /// <param name="type">The component type to register. Must be unmanaged and implement IComponent.</param>
    /// <param name="alignment">The alignment requirement in bytes. Default is 0 for automatic detection.</param>
    /// <returns>The unique type identifier assigned to this component type.</returns>
    public int RegisterComponent(Type type, uint alignment = 0)
    {
        return (int)RegisterComponentMethod.MakeGenericMethod(type).Invoke(this, [alignment])!;
    }

    /// <summary>
    /// Register a type.
    /// </summary>
    /// <param name="type">The type to register.</param>
    /// <param name="alignment">The alignment of type, default is 0, which means auto deduction.</param>
    /// <returns></returns>
    internal int Register(Type type, uint alignment = 0)
    {
        return (int)RegisterMethod.MakeGenericMethod(type).Invoke(this, [alignment])!;
    }

    /// <summary>
    /// Register a type
    /// </summary>
    /// <param name="alignment">The alignment of type, default is 0, which means auto deduction.</param>
    /// <typeparam name="T">The type to register.</typeparam>
    /// <returns></returns>
    internal int Register<T>(uint alignment = 0)
    {
        var type = typeof(T);

        using var wg = typeListLock.EnterWriteLock();
        var typeList = wg.Data;

        if (typeToIdDict.TryGetValue(type, out var value))
        {
            return value;
        }

        typeToIdDict.TryAdd(type, typeList.Count);
        unsafe
        {
            var size = typeof(T).IsValueType ? Marshal.SizeOf(type) : sizeof(nint);

            if (size == 1 && TypeUtils.IsEmptyStruct(type))
            {
                size = 0;
            }

            if (size != 0 && alignment == 0)
            {
                alignment = (uint)TypeUtils.GetOrGuessAlignment(type, size);
            }

            typeList.Add(new(size, (int)alignment));
        }

        return typeList.Count - 1;
    }

    /// <summary>
    /// Registers all component types contained within a component bundle.
    /// The bundle must be an unmanaged type with at least one instance field.
    /// Subsequent registrations of the same bundle type are ignored.
    /// </summary>
    /// <typeparam name="T">The component bundle type. Must be unmanaged and implement IComponentBundle.</typeparam>
    /// <exception cref="ArgumentException">Thrown when the bundle type has no instance fields.</exception>
    public void RegisterBundle<T>() where T : unmanaged, IComponentBundle
    {
        var type = typeof(T);

        if (bundleToInfoDict.ContainsKey(type))
        {
            return;
        }

        var fields = type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

        if (fields.Length == 0)
        {
            throw new ArgumentException("Bundle type must have at least one non-static field", nameof(T));
        }

        bundleToInfoDict.TryAdd(type, fields.Select(f => (new TypeInfo(Marshal.SizeOf(f.FieldType), (int)Marshal.OffsetOf<T>(f.Name)),
            RegisterComponent(f.FieldType))).ToArray());
    }

    /// <summary>
    /// Registers a component bundle type using reflection.
    /// The bundle must be an unmanaged type with at least one instance field.
    /// Subsequent registrations of the same bundle type are ignored.
    /// </summary>
    /// <param name="type">The component bundle type to register. Must be unmanaged and implement IComponentBundle.</param>
    /// <exception cref="ArgumentException">Thrown when the bundle type has no instance fields.</exception>
    public void RegisterBundle(Type type)
    {
        RegisterBundleMethod.MakeGenericMethod(type).Invoke(this, []);
    }

    /// <summary>
    /// Registers all types contained within a value tuple.
    /// Nested tuples are recursively expanded to register all contained types.
    /// </summary>
    /// <typeparam name="T">A value tuple type. Must be unmanaged.</typeparam>
    /// <exception cref="ArgumentException">Thrown when type T is not a value tuple.</exception>
    /// <returns>An array of type identifiers corresponding to each type in the tuple.</returns>
    public int[] RegisterTypesOfTuple<T>() where T : unmanaged
    {
        return !TypeUtils.IsValueTuple<T>()
            ? throw new ArgumentException("Type parameter T must be a value tuple", nameof(T))
            : TypeUtils.GetTupleTypes<T>().Select(t => Register(t)).ToArray();
    }

    /// <summary>
    /// Retrieves type metadata by its unique identifier.
    /// </summary>
    /// <param name="typeId">The unique type identifier of a registered type.</param>
    /// <returns>Type information including size, alignment, and offset for the specified type.</returns>
    public TypeInfo GetTypeInfo(int typeId)
    {
        using var rg = typeListLock.EnterReadLock();
        return rg.Data[typeId];
    }

    /// <summary>
    /// Retrieves type metadata by its Type object.
    /// </summary>
    /// <param name="type">The Type object of a registered type.</param>
    /// <returns>Type information including size, alignment, and offset for the specified type.</returns>
    public TypeInfo GetTypeInfo(Type type)
    {
        using var rg = typeListLock.EnterReadLock();
        return rg.Data[typeToIdDict[type]];
    }

    /// <summary>
    /// Retrieves bundle metadata including all component types contained within the bundle.
    /// </summary>
    /// <typeparam name="T">The component bundle type. Must be unmanaged and implement IComponentBundle.</typeparam>
    /// <returns>An array of tuples containing type information and type IDs for each component in the bundle.</returns>
    public (TypeInfo info, int typeId)[] GetBundleInfo<T>() where T : unmanaged, IComponentBundle
    {
        return bundleToInfoDict[typeof(T)];
    }

    /// <summary>
    /// Retrieves the unique type identifier for a type specified as a generic parameter.
    /// </summary>
    /// <typeparam name="T">The type to look up.</typeparam>
    /// <returns>The unique type identifier, or -1 if the type has not been registered.</returns>
    public int GetTypeId<T>()
    {
        var type = typeof(T);
        return GetTypeId(type);
    }

    /// <summary>
    /// Retrieves the unique type identifier for a type specified as a Type object.
    /// </summary>
    /// <param name="type">The Type object to look up.</param>
    /// <returns>The unique type identifier, or -1 if the type has not been registered.</returns>
    public int GetTypeId(Type type)
    {
        return typeToIdDict.GetValueOrDefault(type, -1);
    }
}
