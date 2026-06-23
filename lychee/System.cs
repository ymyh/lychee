using lychee.attributes;
using lychee.interfaces;

namespace lychee;

/// <summary>
/// Specifies ordering direction for set configuration.
/// </summary>
public enum Order
{
    /// <summary>
    /// The set should execute before the other set.
    /// </summary>
    Before,

    /// <summary>
    /// The set should execute after the other set.
    /// </summary>
    After,
}

public struct SystemParameterInfo(Type type, bool readOnly, bool isResource)
{
    public readonly Type Type = type;

    public readonly bool ReadOnly = readOnly;

    public readonly bool IsResource = isResource;
}

public sealed class SystemInfo(ISystem system, SystemParameterInfo[] parameters, SystemFilterInfo filterInfo, SetInfo[] setInfo)
{
    internal readonly ISystem System = system;

    internal readonly SystemParameterInfo[] Parameters = parameters;

    internal readonly SystemFilterInfo FilterInfo = filterInfo;

    internal bool Predicate = true;

    internal SetInfo[] EffectiveSets { get; set; } = setInfo;
}

/// <summary>
/// Provides configuration options for a system's execution behavior.
/// </summary>
public sealed class SystemDescriptor
{
    /// <summary>
    /// Specifies a system that this system should execute after.
    /// Use this to define execution order dependencies between systems.
    /// </summary>
    public ISystem? AddAfter { get; set; }

    /// <summary>
    /// The number of threads to use for parallel execution.
    /// This value is only used when multithreaded in <see cref="AutoImplSystemAttribute"/> is set to true; otherwise, it is ignored.
    /// Must be a positive value when used.
    /// </summary>
    public int ThreadCount { get; set; } = 0;

    /// <summary>
    /// The number of entities each thread should process in parallel execution.
    /// This value is only used when multithreaded in <see cref="AutoImplSystemAttribute"/> is set to true; otherwise, it is ignored.
    /// Must be a positive value when used.
    /// </summary>
    public int GroupSize { get; set; } = 0;

    /// <summary>
    /// The system sets this system belongs to.
    /// Systems in the same set can be ordered relative to each other via <see cref="SystemSets.ConfigureSetOrder{TS1, TS2}"/>.
    /// </summary>
    public Enum[] Sets { get; set; } = [];
}

public sealed class SystemFilterInfo
{
    public Type[] AllFilter = [];

    public Type[] AnyFilter = [];

    public Type[] NoneFilter = [];
}
