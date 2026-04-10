namespace lychee.attributes;

/// <summary>
/// Marks a system parameter as being supplied from the resource pool.
/// </summary>
/// <param name="readOnly">For class types only. If true, the parameter is treated as read-only.
/// Violating this assumption may cause race conditions. For struct types, read-only behavior
/// is determined by the parameter modifier (e.g., <c>in</c>, <c>ref</c>, <c>out</c>).</param>
[AttributeUsage(AttributeTargets.Parameter)]
public sealed class Resource(bool readOnly = false, bool requireOnExec = false) : Attribute
{
    /// <summary>
    /// Indicating whether the resource is treated as read-only.
    /// </summary>
    public readonly bool ReadOnly = readOnly;

    /// <summary>
    /// Indicates when the Resource is required. If true, it is required on each Execute call; otherwise, it is required during system initialization.
    /// Make sure the resource is added to the world before the system executes, or it will throw an exception. This is useful for resources that may be added after system initialization, such as those created by other systems.
    /// </summary>
    public readonly bool RequireOnExec = requireOnExec;
}
