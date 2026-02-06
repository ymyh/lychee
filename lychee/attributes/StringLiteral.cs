namespace lychee.attributes;

/// <summary>
/// Specifies that a string parameter must be a compile-time string literal.
/// </summary>
/// <param name="dynamicInterpolated">If true, dynamically interpolated strings are also accepted.</param>
[AttributeUsage(AttributeTargets.Parameter)]
public sealed class StringLiteral(bool dynamicInterpolated = false) : Attribute;
