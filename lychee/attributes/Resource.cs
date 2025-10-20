namespace lychee.attributes;

/// <summary>
/// Mark a parameter as a resource.
/// </summary>
/// <param name="readOnly">If true, the parameter will be read-only.</param>
[AttributeUsage(AttributeTargets.Parameter)]
public sealed class Resource(bool readOnly = false) : Attribute;