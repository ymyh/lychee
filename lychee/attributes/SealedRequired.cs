namespace lychee;

/// <summary>
/// Marks a generic type parameter as requiring a sealed type.
/// </summary>
[AttributeUsage(AttributeTargets.GenericParameter)]
public sealed class SealedRequired : Attribute;
