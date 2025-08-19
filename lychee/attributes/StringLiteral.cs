namespace lychee.attributes;

[AttributeUsage(AttributeTargets.Parameter)]
public sealed class StringLiteral(bool dynamicInterpolated = false) : Attribute;