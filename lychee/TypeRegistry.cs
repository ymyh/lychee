using System.Reflection;
using System.Runtime.InteropServices;
using lychee.interfaces;
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
    private readonly List<(Type, TypeInfo)> typeList = [];

    private readonly Dictionary<string, int> typenameDict = new();

    private readonly Dictionary<string, int> bundleSizeDict = new();

    private static readonly MethodInfo RegisterMethod =
        typeof(TypeRegistry).GetMethod("Register", BindingFlags.Public | BindingFlags.Instance, [typeof(int)])!;

    private static readonly MethodInfo RegisterBundleMethod =
        typeof(TypeRegistry).GetMethod("RegisterBundle", BindingFlags.Public | BindingFlags.Instance, [])!;

    public int Register<T>(int alignment = 0) where T : unmanaged, IComponent
    {
        var type = typeof(T);
        var name = type.FullName ?? type.Name;

        if (typenameDict.TryGetValue(name, out var value))
        {
            return value;
        }

        typenameDict.Add(name, typeList.Count);
        var size = Marshal.SizeOf(type);
        if (alignment == 0)
        {
            alignment = TypeUtils.GetOrGuessAlignment(type, size);
        }

        typeList.Add((type, new(size, alignment)));

        return typeList.Count - 1;
    }

    public int Register(Type type, int alignment = 0)
    {
        return (int)RegisterMethod.MakeGenericMethod(type).Invoke(this, [alignment])!;
    }

    public void RegisterBundle<T>() where T : unmanaged, IComponentBundle
    {
        var type = typeof(T);
        var fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance);

        if (fields.Length == 0)
        {
            throw new ArgumentException("Bundle type must have at least one field", nameof(T));
        }

        T.StructInfo = fields.Select(f => (new TypeInfo(Marshal.SizeOf(f.FieldType), (int)Marshal.OffsetOf<T>(f.Name)),
            Register(f.FieldType))).ToArray();

        bundleSizeDict.Add(type.FullName ?? type.Name, fields.Length);
    }

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
        return typeList[id];
    }

    /// <summary>
    /// Gets Type with given name
    /// </summary>
    /// <param name="fullName"></param>
    /// <exception cref="ArgumentOutOfRangeException"></exception>
    /// <returns></returns>
    public (Type, TypeInfo) GetTypeInfo(string fullName)
    {
        return typeList[typenameDict[fullName]];
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

    public int? GetTypeId(Type type)
    {
        return GetTypeId(type.FullName ?? type.Name);
    }

    /// <summary>
    /// Gets id by full type name
    /// </summary>
    /// <param name="fullName"></param>
    /// <returns></returns>
    public int? GetTypeId(string fullName)
    {
        if (typenameDict.TryGetValue(fullName, out var id))
        {
            return id;
        }

        return null;
    }

#region Private methods

#endregion
}
