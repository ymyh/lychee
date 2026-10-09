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
#region Private Fields

    private readonly ReadWriteLock<List<TypeInfo>> typeListLock = new([]);

    private readonly ConcurrentDictionary<Type, int> typeToIdDict = new();

    private readonly ConcurrentDictionary<int, Type> idToTypeDict = new();

    private readonly ConcurrentDictionary<Type, (TypeInfo info, int typeId)[]> bundleToInfoDict = new();

    // One row per type id, each row holding the three erased hook invokers. Rows and the outer array are
    // replaced rather than mutated, so a reader on a worker thread always sees a coherent snapshot.
    private volatile ComponentHookInvoker?[][] componentHooks = [];

    private const int ComponentHookKindCount = 3;

#endregion

#region Public Methods

    /// <summary>
    /// Registers a component type and returns its unique type identifier.
    /// Subsequent registrations of the same type return the existing identifier.
    /// </summary>
    /// <param name="alignment">The alignment requirement in bytes. Default is 0 for automatic detection.</param>
    /// <typeparam name="T">The component type to register. Must be unmanaged and implement IComponent.</typeparam>
    /// <returns>The unique type identifier assigned to this component type.</returns>
    public int RegisterComponent<T>(int size = 0, uint alignment = 0) where T : unmanaged, IComponent
    {
        return Register<T>(size, alignment);
    }

    /// <summary>
    /// Registers a component type and returns its unique type identifier.
    /// Subsequent registrations of the same type return the existing identifier.
    /// </summary>
    /// <param name="type">The component type to register. Must be unmanaged and implement IComponent.</param>
    /// <param name="alignment">The alignment requirement in bytes. Default is 0 for automatic detection.</param>
    /// <returns>The unique type identifier assigned to this component type.</returns>
    public int RegisterComponent<T>(Type type, int size = 0, uint alignment = 0) where T : unmanaged, IComponent
    {
        return Register(type, size, alignment);
    }

    /// <summary>
    /// Registers a component type using reflection and returns its unique type identifier.
    /// Subsequent registrations of the same type return the existing identifier.
    /// </summary>
    /// <param name="type">The component type to register. Must be unmanaged and implement IComponent.</param>
    /// <param name="alignment">The alignment requirement in bytes. Default is 0 for automatic detection.</param>
    /// <returns>The unique type identifier assigned to this component type.</returns>
    public int RegisterComponent(Type type, int size = 0, uint alignment = 0)
    {
        return !type.IsAssignableTo(typeof(IComponent)) ? throw new ArgumentException($"Type parameter {type.Name} must be assignable to IComponent")
            : Register(type, size, alignment);
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

        bundleToInfoDict.TryAdd(type, fields.Select(f => (new TypeInfo(GetComponentSize(f.FieldType), (int)Marshal.OffsetOf<T>(f.Name)),
            RegisterComponent(f.FieldType))).ToArray());
    }

    /// <summary>
    /// Registers a component bundle type.
    /// The bundle must be an unmanaged type with at least one instance field.
    /// Subsequent registrations of the same bundle type are ignored.
    /// </summary>
    /// <param name="type">The component bundle type to register. Must be unmanaged and implement IComponentBundle.</param>
    /// <exception cref="ArgumentException">Thrown when the bundle type has no instance fields.</exception>
    public void RegisterBundle(Type type)
    {
        if (bundleToInfoDict.ContainsKey(type))
        {
            return;
        }

        var fields = type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

        if (fields.Length == 0)
        {
            throw new ArgumentException("Bundle type must have at least one non-static field", type.Name);
        }

        bundleToInfoDict.TryAdd(type, fields.Select(f => (new TypeInfo(Marshal.SizeOf(f.FieldType), (int)Marshal.OffsetOf(type, f.Name)),
            RegisterComponent(f.FieldType))).ToArray());
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
    /// Registers an enum type and returns its unique type identifier.
    /// </summary>
    /// <typeparam name="T">The target enum type.</typeparam>
    /// <returns>The unique type identifier assigned to this enum type.</returns>
    public int RegisterEnum<T>() where T : Enum
    {
        return RegisterEnum(typeof(T));
    }

    /// <summary>
    /// Registers types by their assembly-qualified names, sorted in descending order by alignment.
    /// Types with larger alignment requirements are registered first to optimize memory layout.
    /// </summary>
    /// <param name="typeNames">A list of assembly-qualified type name strings to register.</param>
    public void RegisterTypesByName(List<string> typeNames)
    {
        foreach (var type in typeNames.Select(Type.GetType).OrderByDescending(TypeUtils.GetOrGuessAlignment))
        {
            Register(type);
        }
    }

    /// <summary>
    /// Returns the full names of all currently registered types.
    /// </summary>
    /// <returns>A list of full type name strings for all registered types.</returns>
    public List<string> DumpAllTypesName()
    {
        return typeToIdDict.Select(pair => pair.Key.FullName).ToList()!;
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

    /// <summary>
    /// Retrieves the Type object associated with a unique type identifier.
    /// </summary>
    /// <param name="typeId">The unique type identifier to look up.</param>
    /// <returns>The Type object registered with the specified identifier.</returns>
    public Type GetTypeById(int typeId)
    {
        return idToTypeDict[typeId];
    }

    /// <summary>
    /// Registers a component type and stores the hook for the given kind. The type is registered on demand,
    /// and calling this again for the same kind overwrites the previous hook.
    /// </summary>
    /// <typeparam name="T">The component type, must be unmanaged and implement IComponent.</typeparam>
    /// <param name="kind">The hook kind to register.</param>
    /// <param name="hook">The hook to invoke for this kind.</param>
    public unsafe void SetComponentHook<T>(ComponentHookKind kind, ComponentHook<T> hook) where T : unmanaged, IComponent
    {
        using var wg = typeListLock.EnterWriteLock();
        var typeId = RegisterCore(wg.Data, typeof(T));

        var row = componentHooks[typeId];
        var newRow = new ComponentHookInvoker?[ComponentHookKindCount];
        Array.Copy(row, newRow, ComponentHookKindCount);
        newRow[(int)kind] = new ComponentHookHolder<T>(hook).Invoke;
        componentHooks[typeId] = newRow;
    }

#endregion

#region Internal Methods

    /// <summary>
    /// Register a type.
    /// </summary>
    /// <param name="type">The type to register.</param>
    /// <param name="size">The size of type, default is 0, which means auto deduction.</param>
    /// <param name="alignment">The alignment of type, default is 0, which means auto deduction.</param>
    /// <returns></returns>
    internal int Register(Type type, int size = 0, uint alignment = 0)
    {
        using var wg = typeListLock.EnterWriteLock();
        return RegisterCore(wg.Data, type, size, alignment);
    }

    internal int RegisterEnum(Type type)
    {
        if (!type.IsEnum)
        {
            throw new ArgumentException("Type must be an enum", nameof(type));
        }

        using var wg = typeListLock.EnterWriteLock();
        var typeList = wg.Data;

        if (typeToIdDict.TryGetValue(type, out var value))
        {
            return value;
        }

        var typeId = typeList.Count;
        typeToIdDict.TryAdd(type, typeId);
        idToTypeDict.TryAdd(typeId, type);
        EnsureComponentHookRow(typeId);

        typeList.Add(new(Marshal.SizeOf(type.GetEnumUnderlyingType()), Marshal.SizeOf(type.GetEnumUnderlyingType())));

        return typeId;
    }

    /// <summary>
    /// Checks whether any hook kind is registered for the given type id. The hot path uses this to skip
    /// reading a previous value when nothing would consume it.
    /// </summary>
    /// <param name="typeId">The component type id to check.</param>
    /// <returns>True if at least one hook kind is registered; otherwise, false.</returns>
    internal bool HasAnyComponentHook(int typeId)
    {
        var hooks = componentHooks;

        if ((uint)typeId >= (uint)hooks.Length)
        {
            return false;
        }

        var row = hooks[typeId];

        return row != null && (row[0] != null || row[1] != null || row[2] != null);
    }

    /// <summary>
    /// Gets the erased hook invoker for a type id and kind, or null when that kind is not registered.
    /// </summary>
    /// <param name="typeId">The component type id.</param>
    /// <param name="kind">The hook kind to look up.</param>
    /// <returns>The invoker, or null when nothing is registered.</returns>
    internal ComponentHookInvoker? GetComponentHook(int typeId, ComponentHookKind kind)
    {
        var hooks = componentHooks;

        if ((uint)typeId >= (uint)hooks.Length)
        {
            return null;
        }

        return hooks[typeId]?[(int)kind];
    }

    internal int Register<T>(int size = 0, uint alignment = 0)
    {
        return Register(typeof(T), size, alignment);
    }

#endregion

#region Private Methods

    private int RegisterCore(List<TypeInfo> typeList, Type type, int size = 0, uint alignment = 0)
    {
        if (typeToIdDict.TryGetValue(type, out var value))
        {
            return value;
        }

        var typeId = typeList.Count;
        typeToIdDict.TryAdd(type, typeId);
        idToTypeDict.TryAdd(typeId, type);
        EnsureComponentHookRow(typeId);

        unsafe
        {
            if (size == 0)
            {
                if (type.IsAssignableTo(typeof(IComponent)))
                {
                    size = GetComponentSize(type);
                }
                else
                {
                    size = type.IsValueType ? Marshal.SizeOf(type) : sizeof(nint);
                    if (size == 1 && TypeUtils.IsEmptyStruct(type))
                    {
                        size = 0;
                    }
                }

                if (size != 0 && alignment == 0)
                {
                    alignment = (uint)TypeUtils.GetOrGuessAlignment(type);
                }
            }

            typeList.Add(new(size, (int)alignment));
        }

        return typeId;
    }

    private void EnsureComponentHookRow(int typeId)
    {
        var hooks = componentHooks;

        if ((uint)typeId < (uint)hooks.Length)
        {
            return;
        }

        var newHooks = new ComponentHookInvoker?[typeId + 1][];

        Array.Copy(hooks, newHooks, hooks.Length);

        for (var i = hooks.Length; i < newHooks.Length; i++)
        {
            newHooks[i] = new ComponentHookInvoker?[ComponentHookKindCount];
        }

        componentHooks = newHooks;
    }

    private int GetComponentSize(Type type)
    {
        return (Activator.CreateInstance(type) as IComponent)!.GetComponentMeta().Size;
    }

    /// <summary>
    /// Retains the typed hook and provides a non-generic entry point that unwraps the raw component pointer.
    /// </summary>
    private sealed class ComponentHookHolder<T>(ComponentHook<T> hook) where T : unmanaged, IComponent
    {
        public unsafe void Invoke(ref HookContext context, void* component)
        {
            hook(ref context, in *(T*)component);
        }
    }

#endregion
}
