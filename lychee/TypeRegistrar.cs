using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using lychee.interfaces;
using lychee.threading;
using lychee.utils;

namespace lychee;

[StructLayout(LayoutKind.Explicit)]
public struct TypeInfo(int size, int alignment)
{
    [FieldOffset(0)] public int Size = size;

    [FieldOffset(4)] public int Alignment = alignment;

    [FieldOffset(4)] public int Offset;
}

/// <summary>
/// A registrar for types.
/// </summary>
public sealed class TypeRegistrar
{
    private readonly ReadWriteLock<List<TypeInfo>> typeListLock = new([]);

    private readonly ConcurrentDictionary<Type, int> typeToIdDict = new();

    private readonly ConcurrentDictionary<Type, (TypeInfo info, int typeId)[]> bundleToInfoDict = new();

    private static readonly MethodInfo RegisterMethod =
        typeof(TypeRegistrar).GetMethod("Register", BindingFlags.NonPublic | BindingFlags.Instance, [typeof(int)])!;

    private static readonly MethodInfo RegisterComponentMethod =
        typeof(TypeRegistrar).GetMethod("RegisterComponent", BindingFlags.Public | BindingFlags.Instance, [typeof(int)])!;

    private static readonly MethodInfo RegisterBundleMethod =
        typeof(TypeRegistrar).GetMethod("RegisterBundle", BindingFlags.Public | BindingFlags.Instance, [])!;

    /// <summary>
    /// Register a component type.
    /// </summary>
    /// <param name="alignment">The alignment of type, default is 0, which implies auto deduction.</param>
    /// <typeparam name="T">The type to register.</typeparam>
    /// <returns>Type id</returns>
    public int RegisterComponent<T>(int alignment = 0) where T : unmanaged, IComponent
    {
        return Register<T>(alignment);
    }

    /// <summary>
    /// Register a component type.
    /// </summary>
    /// <param name="type">The type to register.</param>
    /// <param name="alignment">The alignment of type, default is 0, which implies auto deduction.</param>
    /// <returns>Type id</returns>
    public int RegisterComponent(Type type, int alignment = 0)
    {
        return (int)RegisterComponentMethod.MakeGenericMethod(type).Invoke(this, [alignment])!;
    }

    /// <summary>
    /// Register a type.
    /// </summary>
    /// <param name="type">The type to register.</param>
    /// <param name="alignment">The alignment of type, default is 0, which implies auto deduction.</param>
    /// <returns></returns>
    internal int Register(Type type, int alignment = 0)
    {
        return (int)RegisterMethod.MakeGenericMethod(type).Invoke(this, [alignment])!;
    }

    /// <summary>
    /// Register a type
    /// </summary>
    /// <param name="alignment">The alignment of type, default is 0, which implies auto deduction.</param>
    /// <typeparam name="T">The type to register.</typeparam>
    /// <returns></returns>
    internal int Register<T>(int alignment = 0)
    {
        Debug.Assert(alignment >= 0);

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

            if (size == 1 && !TypeUtils.ContainsField(type))
            {
                size = 0;
            }

            if (size != 0 && alignment == 0)
            {
                alignment = TypeUtils.GetOrGuessAlignment(type, size);
            }

            typeList.Add((new(size, alignment)));
        }

        return typeList.Count - 1;
    }

    /// <summary>
    /// Register a type bundle.
    /// </summary>
    /// <typeparam name="T">The bundle to register.</typeparam>
    /// <exception cref="ArgumentException">Thrown when bundle type has no public non-static fields</exception>
    public void RegisterBundle<T>() where T : unmanaged, IComponentBundle
    {
        var type = typeof(T);

        if (bundleToInfoDict.ContainsKey(type))
        {
            return;
        }

        var fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance);

        if (fields.Length == 0)
        {
            throw new ArgumentException("Bundle type must have at least one public non-static field", nameof(T));
        }

        bundleToInfoDict.TryAdd(type, fields.Select(f => (new TypeInfo(Marshal.SizeOf(f.FieldType), (int)Marshal.OffsetOf<T>(f.Name)),
            RegisterComponent(f.FieldType))).ToArray());
    }

    /// <summary>
    /// Register a type bundle.
    /// </summary>
    /// <param name="type"></param>
    /// <exception cref="ArgumentException">Thrown when bundle type has no public non-static fields</exception>
    public void RegisterBundle(Type type)
    {
        RegisterBundleMethod.MakeGenericMethod(type).Invoke(this, []);
    }

    /// <summary>
    /// Register all types of the given tuple.
    /// </summary>
    /// <typeparam name="T">A tuple of component types.</typeparam>
    /// <exception cref="ArgumentException">Thrown when bundle type has no public non-static fields</exception>
    /// <returns>All id of component types in the tuple.</returns>
    public int[] GetComponentTypeIds<T>() where T : unmanaged
    {
        return !TypeUtils.IsValueTuple<T>() ? throw new ArgumentException("Type parameter T must be a value tuple", nameof(T)) : TypeUtils.GetTupleTypes<T>().Select(t => RegisterComponent(t)).ToArray();
    }

    /// <summary>
    /// Gets Type with given id
    /// </summary>
    /// <param name="id">Type id registered before using Register()</param>
    /// <exception cref="ArgumentOutOfRangeException"></exception>
    /// <returns></returns>
    public TypeInfo GetTypeInfo(int id)
    {
        using var rg = typeListLock.EnterReadLock();
        return rg.Data[id];
    }

    /// <summary>
    /// Gets type info with given type
    /// </summary>
    /// <param name="type"></param>
    /// <exception cref="ArgumentOutOfRangeException"></exception>
    /// <returns></returns>
    public TypeInfo GetTypeInfo(Type type)
    {
        using var rg = typeListLock.EnterReadLock();
        return rg.Data[typeToIdDict[type]];
    }

    /// <summary>
    /// Gets bundle type info with given type.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    public (TypeInfo info, int typeId)[] GetBundleInfo<T>() where T : unmanaged, IComponentBundle
    {
        return bundleToInfoDict[typeof(T)];
    }

    /// <summary>
    /// Gets bundle type info with given type.
    /// </summary>
    /// <param name="type"></param>
    /// <returns></returns>
    public (TypeInfo info, int typeId)[] GetBundleInfo(Type type)
    {
        return bundleToInfoDict[type];
    }

    /// <summary>
    /// Gets id by type T
    /// </summary>
    /// <typeparam name="T">The target type.</typeparam>
    /// <returns>The id of the target type. Returns -1 if the type is not registered.</returns>
    public int GetTypeId<T>()
    {
        var type = typeof(T);
        return GetTypeId(type);
    }

    /// <summary>
    /// Gets id by full type
    /// </summary>
    /// <param name="type">The target type.</param>
    /// <returns>The id of the target type. Returns -1 if the type is not registered.</returns>
    public int GetTypeId(Type type)
    {
        return typeToIdDict.GetValueOrDefault(type, -1);
    }
}
