using lychee.interfaces;

namespace lychee;

public struct SystemParameterInfo(Type type, bool readOnly, bool isResource)
{
    public readonly Type Type = type;

    public readonly bool ReadOnly = readOnly;

    public readonly bool IsResource = isResource;
}

public struct SystemInfo(ISystem system, SystemParameterInfo[] parameters, SystemFilterInfo filterInfo)
{
    internal readonly ISystem System = system;

    internal readonly SystemParameterInfo[] Parameters = parameters;

    internal readonly SystemFilterInfo FilterInfo = filterInfo;
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
    /// This value is only used when multithreaded in <see cref="attributes.AutoImplSystem"/> is set to true; otherwise, it is ignored.
    /// Must be a positive value when used.
    /// </summary>
    public int ThreadCount { get; set; } = 0;

    /// <summary>
    /// The number of entities each thread should process in parallel execution.
    /// This value is only used when multithreaded in <see cref="attributes.AutoImplSystem"/> is set to true; otherwise, it is ignored.
    /// Must be a positive value when used.
    /// </summary>
    public int GroupSize { get; set; } = 0;
}

public sealed class SystemFilterInfo
{
    public Type[] AllFilter = [];

    public Type[] AnyFilter = [];

    public Type[] NoneFilter = [];
}
