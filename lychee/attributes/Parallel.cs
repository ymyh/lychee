namespace lychee.attributes;

[AttributeUsage(AttributeTargets.Method)]
public sealed class Parallel(int groupSize) : Attribute;