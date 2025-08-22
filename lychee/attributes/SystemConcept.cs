namespace lychee.attributes;

/// <summary>
/// Specifies that a type must satisfy the following requirements to conform to the SystemConcept constraint:
/// <list type="number">
///   <item>Implement the <see cref="lychee.interfaces.ISystem"/> interface</item>
///   <item>Contain a instance method named 'Execute'</item>
/// </list>
/// </summary>
[AttributeUsage(AttributeTargets.GenericParameter)]
public sealed class SystemConcept : Attribute;