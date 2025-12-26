namespace lychee.attributes;

/// <summary>
/// Mark a system parameter is supplied from resource.
/// </summary>
/// <param name="readOnly">Only apply on class types. If true, we assume the parameter will be read-only.
/// If this assumption is violated, it may cause race conditions. <br/>
/// struct types are determined by the parameter modifier (i.e. `in`, `out`, `ref`).
/// </param>
[AttributeUsage(AttributeTargets.Parameter)]
public sealed class Resource(bool readOnly = false) : Attribute
{
    public readonly bool ReadOnly = readOnly;
}
