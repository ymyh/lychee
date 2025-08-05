using System.Reflection;
using lychee.interfaces;

namespace lychee.utils;

public static class TypeUtils
{
    private static MethodInfo? method = typeof(TypeUtils).GetMethod("GetTupleTypes", BindingFlags.Static);

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
                var subList = (List<Type>?)method?.MakeGenericMethod(field.FieldType).Invoke(null, null);
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

    public static Type[] GetBundleTypes<T>() where T : IComponentBundle
    {
        var fields = typeof(T).GetFields(BindingFlags.Public | BindingFlags.Instance);
        return fields.Select(f => f.FieldType).ToArray();
    }

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
}
