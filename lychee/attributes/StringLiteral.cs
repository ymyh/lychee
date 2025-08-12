namespace lychee.attributes;

[AttributeUsage(AttributeTargets.Parameter)]
public class StringLiteral(bool dynamicInterpolated) : Attribute
{
    public bool DynamicInterpolated = dynamicInterpolated;
}
