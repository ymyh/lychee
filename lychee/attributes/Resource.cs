namespace lychee.attributes;

[AttributeUsage(AttributeTargets.Parameter)]
public sealed class Resource(bool mutable = false) : Attribute;