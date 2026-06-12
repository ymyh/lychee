namespace lychee.attributes;

/// <summary>
/// Assigns a system to a named execution set for batch ordering and condition configuration.
/// Can be applied multiple times to assign a system to multiple sets.
/// </summary>
/// <param name="value">The enum value identifying the set.</param>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Enum, AllowMultiple = true)]
public sealed class InSetAttribute(Enum value) : Attribute
{
    public Enum Value { get; } = value;
}
