using System.Reflection;

namespace lychee.utils;

/// <summary>
/// Provides utility methods for type introspection and reflection operations.
/// </summary>
public static class TypeUtils
{
    private static readonly MethodInfo GetTupleTypesMethod =
        typeof(TypeUtils).GetMethod("GetTupleTypes", BindingFlags.Static | BindingFlags.Public)!;

    private static readonly MethodInfo TestUnmanagedMethod =
        typeof(TypeUtils).GetMethod("TestUnmanaged", BindingFlags.Static | BindingFlags.NonPublic)!;

    private static readonly Type[] TupleTypes =
    [
        typeof(ValueTuple<>),
        typeof(ValueTuple<,>),
        typeof(ValueTuple<,,>),
        typeof(ValueTuple<,,,>),
        typeof(ValueTuple<,,,,>),
        typeof(ValueTuple<,,,,,>),
        typeof(ValueTuple<,,,,,,>),
        typeof(ValueTuple<,,,,,,,>),
    ];

    /// <summary>
    /// Extracts all generic type arguments from a value tuple type.
    /// </summary>
    /// <typeparam name="T">The value tuple type to extract types from.</typeparam>
    /// <returns>A list containing all type arguments in the tuple, including nested ones.</returns>
    /// <exception cref="ArgumentException">Thrown when type T is not a value tuple type.</exception>
    public static List<Type> GetTupleTypes<T>()
    {
        var type = typeof(T);

        if (!IsValueTuple(type))
        {
            throw new ArgumentException($"Generic Argument {type.Name} is not a ValueTuple");
        }

        var fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance);
        List<Type> list = [];

        foreach (var field in fields)
        {
            if (field.Name == "Rest")
            {
                var subList = (List<Type>)GetTupleTypesMethod.MakeGenericMethod(field.FieldType).Invoke(null, null)!;
                list.AddRange(subList);

                continue;
            }

            list.Add(field.FieldType);
        }

        return list;
    }

    /// <summary>
    /// Determines whether the specified type is a value tuple type.
    /// </summary>
    /// <typeparam name="T">The type to check.</typeparam>
    /// <returns>true if T is a value tuple type; otherwise, false.</returns>
    public static bool IsValueTuple<T>()
    {
        return IsValueTuple(typeof(T));
    }

    /// <summary>
    /// Determines whether the specified unmanaged struct type contains no fields or properties.
    /// </summary>
    /// <typeparam name="T">The unmanaged struct type to check.</typeparam>
    /// <returns>true if the struct has no fields or properties; otherwise, false.</returns>
    public static bool IsEmptyStruct<T>() where T : unmanaged
    {
        return IsEmptyStruct(typeof(T));
    }

    /// <summary>
    /// Determines whether the specified struct type contains no fields or properties.
    /// </summary>
    /// <param name="type">The struct type to check.</param>
    /// <returns>true if the struct has no fields or properties; otherwise, false.</returns>
    public static bool IsEmptyStruct(Type type)
    {
        return type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).Length == 0 &&
               type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).Length == 0;
    }

    /// <summary>
    /// Determines whether the specified type is a value tuple type.
    /// </summary>
    /// <param name="t">The type to check.</param>
    /// <returns>true if the type is a value tuple; otherwise, false.</returns>
    public static bool IsValueTuple(Type t)
    {
        return t.IsGenericType && TupleTypes.Any(x => x == t.GetGenericTypeDefinition());
    }

    /// <summary>
    /// Gets the memory alignment for a type, either from its StructLayoutAttribute or by estimation.
    /// </summary>
    /// <param name="type">The type to get alignment for.</param>
    /// <param name="size">The size in bytes of the type.</param>
    /// <returns>The alignment in bytes. Returns pointer size for reference types, StructLayoutAttribute.Pack if specified,
    /// or an estimated value based on type size (max 64 bytes).</returns>
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
    /// Determines whether the specified type is an unmanaged type at runtime.
    /// </summary>
    /// <param name="type">The type to check.</param>
    /// <returns>true if the type is unmanaged; otherwise, false.</returns>
    /// <remarks>
    /// This method uses reflection to test the unmanaged constraint at runtime,
    /// which is not directly available through the Type API.
    /// </remarks>
    public static bool IsUnmanaged(Type type)
    {
        try
        {
            TestUnmanagedMethod.MakeGenericMethod(type).Invoke(null, null);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static void TestUnmanaged<T>() where T : unmanaged
    {
    }
}
