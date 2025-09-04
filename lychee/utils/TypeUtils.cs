using System.Reflection;
using lychee.interfaces;

namespace lychee.utils;

public static class TypeUtils
{
    private static readonly MethodInfo? GetTupleTypesMethod =
        typeof(TypeUtils).GetMethod("GetTupleTypes", BindingFlags.Static);

    /// <summary>
    /// Recursively gets tuple type as list.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    /// <exception cref="ArgumentException"></exception>
    public static List<Type> GetTupleTypes<T>()
    {
        var type = typeof(T);
        var fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance);
        List<Type> list = [];

        var i = 1;
        foreach (var field in fields)
        {
            if (field.Name == "Rest")
            {
                var subList = (List<Type>?)GetTupleTypesMethod?.MakeGenericMethod(field.FieldType).Invoke(null, null);
                list.AddRange(subList);

                continue;
            }

            if (field.Name != $"Item{i}")
            {
                throw new ArgumentException($"Generic Argument {type.Name} is not a ValueTuple");
            }

            list.Add(field.FieldType);
            i += 1;
        }

        return list;
    }

    /// <summary>
    /// Get all public fields types of a <see cref="IComponentBundle"/>
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    public static Type[] GetBundleTypes<T>() where T : IComponentBundle
    {
        var fields = typeof(T).GetFields(BindingFlags.Public | BindingFlags.Instance);
        return fields.Select(f => f.FieldType).ToArray();
    }

    /// <summary>
    /// Get or guess the alignment of a type <br/>
    /// Simply return the value of StructLayoutAttribute.Pack if present, <br/>
    /// or try to guess the alignment based on size of type.
    /// </summary>
    /// <param name="type"></param>
    /// <param name="size"></param>
    /// <returns></returns>
    public static int GetOrGuessAlignment(Type type, int size)
    {
        unsafe
        {
            if (!type.IsValueType)
            {
                return sizeof(nint);
            }

            var alignment = type.StructLayoutAttribute?.Pack ?? 0;
            return alignment == 0 ? Math.Min((size % 32 == 0 ? 32 : (size % 16 == 0 ? 16 : 8)), 64) : alignment;
        }
    }

    /// <summary>
    /// Returns whether a specified type is unmanaged
    /// </summary>
    /// <param name="type"></param>
    /// <returns></returns>
    public static bool IsUnmanaged(Type type)
    {
        if (type.IsPrimitive || type.IsPointer || type.IsEnum)
        {
            return true;
        }

        if (!type.IsValueType)
        {
            return false;
        }

        foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
        {
            if (!IsUnmanaged(field.FieldType))
            {
                return false;
            }
        }

        return true;
    }
}