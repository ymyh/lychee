namespace lychee.attributes;

[AttributeUsage(AttributeTargets.Parameter)]
public sealed class ResReadOnly : Attribute;

[AttributeUsage(AttributeTargets.Parameter)]
public sealed class ResMut : Attribute;