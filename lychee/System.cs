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

public sealed class SystemInfo(
    ISystem system,
    SystemParameterInfo[] parameters,
    SystemFilterInfo filterInfo,
    Enum[] directSets,
    ISystem? addAfter,
    int groupIndex)
{
    internal readonly ISystem System = system;

    internal readonly SystemParameterInfo[] Parameters = parameters;

    internal readonly SystemFilterInfo FilterInfo = filterInfo;

    /// <summary>
    /// The raw sets declared on the descriptor, unchanged: still the enum values, not resolved identities and
    /// without their ancestors. Resolving them is what <see cref="EffectiveSets"/> holds after a build.
    /// </summary>
    internal readonly Enum[] DirectSets = directSets;

    /// <summary>
    /// The system this one was declared to run after, or null. Kept here instead of being consumed while
    /// adding, so that reusing a descriptor for several systems cannot drop the dependency.
    /// </summary>
    internal readonly ISystem? AddAfter = addAfter;

    /// <summary>
    /// The index of the group this system was declared in through the array form of <c>AddSystems</c>,
    /// or -1 when it was added any other way. Each group runs entirely after the previous one.
    /// </summary>
    internal readonly int GroupIndex = groupIndex;

    internal bool Predicate = true;

    /// <summary>
    /// The sets this system belongs to plus all of their ancestors. Filled in every time the schedule is
    /// built, never read before that.
    /// </summary>
    internal SetInfo[] EffectiveSets = [];
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
    public ISystem? AddAfter { get; internal set; }

    /// <summary>
    /// The number of threads to use for parallel execution.
    /// This value is only used when multithreaded in <see cref="AutoImplSystemAttribute"/> is set to true; otherwise, it is ignored.
    /// Must be a positive value when used.
    /// </summary>
    public int ThreadCount { get; init; } = 0;

    /// <summary>
    /// The number of entities each thread should process in parallel execution.
    /// This value is only used when multithreaded in <see cref="AutoImplSystemAttribute"/> is set to true; otherwise, it is ignored.
    /// Must be a positive value when used.
    /// </summary>
    public int GroupSize { get; init; } = 0;

    /// <summary>
    /// The system sets this system belongs to.
    /// Systems in the same set can be ordered relative to each other via <see cref="SystemSets.ConfigureSetOrder{TS1, TS2}"/>.
    /// </summary>
    public Enum[] Sets { get; init; } = [];
}

public sealed class SystemFilterInfo
{
    public Type[] AllFilter = [];

    public Type[] AnyFilter = [];

    public Type[] NoneFilter = [];
}
