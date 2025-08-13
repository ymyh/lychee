namespace lychee.attributes;

[AttributeUsage(AttributeTargets.Parameter)]
public class StringLiteral(bool dynamicInterpolated = false) : Attribute;