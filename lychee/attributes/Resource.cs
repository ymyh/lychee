namespace lychee.attributes;

/// <summary>
/// Mark a parameter as a resource.
/// </summary>
/// <param name="readOnly">Only apply on managed types. If true, we assume the parameter will be read-only.
/// If this assumption is violated, it may cause multi-threading synchronization issues. <br/>
/// Unmanaged types are determined by the parameter modifier (i.e. `in`, `out`, `ref`).
/// </param>
[AttributeUsage(AttributeTargets.Parameter)]
public sealed class Resource(bool readOnly = false) : Attribute;