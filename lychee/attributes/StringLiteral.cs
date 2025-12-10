namespace lychee.attributes;

/// <summary>
/// Specifies that a string parameter must be a string literal.
/// <param name="dynamicInterpolated">
/// Accept dynamic interpolated string if true.
/// </param>
/// </summary>
[AttributeUsage(AttributeTargets.Parameter)]
public sealed class StringLiteral(bool dynamicInterpolated = false) : Attribute;
