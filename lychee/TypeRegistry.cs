using System.Runtime.InteropServices;
using lychee.exceptions;
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
    private readonly List<(Type, TypeInfo)> types = [];

    private readonly Dictionary<string, int> typenameMap = new();

    /// <summary>
    /// Register a type
    /// </summary>
    /// <param name="type"></param>
    /// <param name="alignment"></param>
    /// <returns>ID of the type</returns>
    /// <exception cref="TypeAlreadyRegisteredException">if the type already registered</exception>
    /// <exception cref="UnsupportedTypeException">if the type is not a class, value type or array type</exception>
    public int Register(Type type, int alignment = 0)
    {
        var name = type.FullName ?? type.Name;

        if (!TypeUtils.IsUnmanaged(type))
        {
            throw new UnsupportedTypeException(name);
        }

        if (typenameMap.ContainsKey(name))
        {
            throw new TypeAlreadyRegisteredException(name);
        }

        typenameMap.Add(type.Name, types.Count);
        var size = Marshal.SizeOf(type);
        if (alignment == 0)
        {
            alignment = TypeUtils.GetOrGuessAlignment(type, size);
        }

        types.Add((type, new TypeInfo(size, alignment)));

        return types.Count - 1;
    }

    public int Register<T>(int alignment = 0) where T : unmanaged
    {
        return Register(typeof(T), alignment);
    }

    public int GetOrRegister(Type type, int alignment = 0)
    {
        var name = type.FullName ?? type.Name;

        if (!TypeUtils.IsUnmanaged(type))
        {
            throw new UnsupportedTypeException(name);
        }

        if (typenameMap.TryGetValue(name, out var value))
        {
            return value;
        }

        typenameMap.Add(name, types.Count);
        var size = Marshal.SizeOf(type);
        if (alignment == 0)
        {
            alignment = TypeUtils.GetOrGuessAlignment(type, size);
        }

        types.Add((type, new TypeInfo(size, alignment)));

        return types.Count - 1;
    }

    public int GetOrRegister<T>(int alignment = 0) where T : unmanaged
    {
        return GetOrRegister(typeof(T), alignment);
    }

    /// <summary>
    /// Gets Type with given id
    /// </summary>
    /// <param name="id">Type id registered before using Register()</param>
    /// <exception cref="ArgumentOutOfRangeException"></exception>
    /// <returns></returns>
    public (Type, TypeInfo) GetTypeInfo(int id)
    {
        return types[id];
    }

    /// <summary>
    /// Gets Type with given name
    /// </summary>
    /// <param name="fullName"></param>
    /// <exception cref="ArgumentOutOfRangeException"></exception>
    /// <returns></returns>
    public (Type, TypeInfo) GetTypeInfo(string fullName)
    {
        return types[typenameMap[fullName]];
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
        if (typenameMap.TryGetValue(fullName, out var id))
        {
            return id;
        }

        return null;
    }

#region Private methods

#endregion
}
