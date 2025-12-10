namespace lychee;

/// <summary>
/// Specifies a generic parameter must be sealed.
/// </summary>
[AttributeUsage(AttributeTargets.GenericParameter)]
public sealed class SealedRequired : Attribute;
