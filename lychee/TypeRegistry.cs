using System.Collections.Concurrent;
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

public sealed class TypeRegistry
{
    private readonly ReadWriteLock<List<(Type, TypeInfo)>> typeListLock = new([]);

    private readonly ConcurrentDictionary<string, int> typenameToIdDict = new();

    private readonly ConcurrentDictionary<string, (TypeInfo info, int typeId)[]> bundleToInfoDict = new();

    private static readonly MethodInfo RegisterMethod =
        typeof(TypeRegistry).GetMethod("Register", BindingFlags.NonPublic | BindingFlags.Instance, [typeof(int)])!;

    private static readonly MethodInfo RegisterComponentMethod =
        typeof(TypeRegistry).GetMethod("RegisterComponent", BindingFlags.Public | BindingFlags.Instance, [typeof(int)])!;

    private static readonly MethodInfo RegisterBundleMethod =
        typeof(TypeRegistry).GetMethod("RegisterBundle", BindingFlags.Public | BindingFlags.Instance, [])!;

    /// <summary>
    /// Register a component type.
    /// </summary>
    /// <param name="alignment"></param>
    /// <typeparam name="T"></typeparam>
    /// <returns>Type id</returns>
    public int RegisterComponent<T>(int alignment = 0) where T : unmanaged, IComponent
    {
        return Register<T>(alignment);
    }

    /// <summary>
    /// Register a component type.
    /// </summary>
    /// <param name="type"></param>
    /// <param name="alignment"></param>
    /// <returns>Type id</returns>
    public int RegisterComponent(Type type, int alignment = 0)
    {
        return (int)RegisterComponentMethod.MakeGenericMethod(type).Invoke(this, [alignment])!;
    }

    /// <summary>
    /// Register a type.
    /// </summary>
    /// <param name="type"></param>
    /// <param name="alignment"></param>
    /// <returns></returns>
    internal int Register(Type type, int alignment = 0)
    {
        return (int)RegisterMethod.MakeGenericMethod(type).Invoke(this, [alignment])!;
    }

    /// <summary>
    /// Register a type
    /// </summary>
    /// <param name="alignment"></param>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    internal int Register<T>(int alignment = 0)
    {
        var type = typeof(T);

        using var wg = typeListLock.EnterWriteLock();
        var typeList = wg.Data;

        if (typenameToIdDict.TryGetValue(type.FullName!, out var value))
        {
            return value;
        }

        typenameToIdDict.TryAdd(type.FullName!, typeList.Count);
        unsafe
        {
            var size = typeof(T).IsValueType ? Marshal.SizeOf(type) : sizeof(nint);
            if (alignment == 0)
            {
                alignment = TypeUtils.GetOrGuessAlignment(type, size);
            }

            typeList.Add((type, new(size, alignment)));
        }

        return typeList.Count - 1;
    }

    /// <summary>
    /// Register a bundle type.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <exception cref="ArgumentException">Thrown when bundle type has no public non-static fields</exception>
    public void RegisterBundle<T>() where T : unmanaged, IComponentBundle
    {
        var type = typeof(T);

        if (bundleToInfoDict.ContainsKey(type.FullName!))
        {
            return;
        }

        var fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance);

        if (fields.Length == 0)
        {
            throw new ArgumentException("Bundle type must have at least one public non-static field", nameof(T));
        }

        bundleToInfoDict.TryAdd(type.FullName!, fields.Select(f => (new TypeInfo(Marshal.SizeOf(f.FieldType), (int)Marshal.OffsetOf<T>(f.Name)),
            RegisterComponent(f.FieldType))).ToArray());
    }

    /// <summary>
    /// Register a bundle type.
    /// </summary>
    /// <param name="type"></param>
    /// <exception cref="ArgumentException">Thrown when bundle type has no public non-static fields</exception>
    public void RegisterBundle(Type type)
    {
        RegisterBundleMethod.MakeGenericMethod(type).Invoke(this, []);
    }

    /// <summary>
    /// Gets Type with given id
    /// </summary>
    /// <param name="id">Type id registered before using Register()</param>
    /// <exception cref="ArgumentOutOfRangeException"></exception>
    /// <returns></returns>
    public (Type, TypeInfo) GetTypeInfo(int id)
    {
        using var rg = typeListLock.EnterReadLock();
        return rg.Data[id];
    }

    /// <summary>
    /// Gets Type with given name
    /// </summary>
    /// <param name="fullName"></param>
    /// <exception cref="ArgumentOutOfRangeException"></exception>
    /// <returns></returns>
    public (Type, TypeInfo) GetTypeInfo(string fullName)
    {
        using var rg = typeListLock.EnterReadLock();
        return rg.Data[typenameToIdDict[fullName]];
    }

    /// <summary>
    /// Gets bundle type info with given type.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    public (TypeInfo info, int typeId)[] GetBundleInfo<T>() where T : unmanaged, IComponentBundle
    {
        return bundleToInfoDict[typeof(T).FullName!];
    }

    /// <summary>
    /// Gets bundle type info with given full type name.
    /// </summary>
    /// <param name="fullname"></param>
    /// <returns></returns>
    public (TypeInfo info, int typeId)[] GetBundleInfo(string fullname)
    {
        return bundleToInfoDict[fullname];
    }

    /// <summary>
    /// Gets id by type T
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    public int? GetTypeId<T>()
    {
        var type = typeof(T);
        return GetTypeId(type);
    }

    /// <summary>
    /// Gets id by type.
    /// </summary>
    /// <param name="type"></param>
    /// <returns></returns>
    public int? GetTypeId(Type type)
    {
        return GetTypeId(type.FullName!);
    }

    /// <summary>
    /// Gets id by full type name
    /// </summary>
    /// <param name="fullName"></param>
    /// <returns></returns>
    public int? GetTypeId(string fullName)
    {
        if (typenameToIdDict.TryGetValue(fullName, out var id))
        {
            return id;
        }

        return null;
    }
}