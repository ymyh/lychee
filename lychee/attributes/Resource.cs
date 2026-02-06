namespace lychee.attributes;

/// <summary>
/// Marks a system parameter as being supplied from the resource pool.
/// </summary>
/// <param name="readOnly">For class types only. If true, the parameter is treated as read-only.
/// Violating this assumption may cause race conditions. For struct types, read-only behavior
/// is determined by the parameter modifier (e.g., <c>in</c>, <c>ref</c>, <c>out</c>).</param>
[AttributeUsage(AttributeTargets.Parameter)]
public sealed class Resource(bool readOnly = false) : Attribute
{
    /// <summary>
    /// Gets a value indicating whether the resource is treated as read-only.
    /// </summary>
    public readonly bool ReadOnly = readOnly;
}
