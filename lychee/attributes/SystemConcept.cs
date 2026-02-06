namespace lychee.attributes;

/// <summary>
/// Marks a generic type parameter as requiring the system concept constraint.
/// </summary>
/// <remarks>
/// Types conforming to this constraint must:
/// <list type="number">
///   <item>Implement the <see cref="lychee.interfaces.ISystem"/> interface</item>
///   <item>Contain a method named <c>Execute</c></item>
/// </list>
/// </remarks>
[AttributeUsage(AttributeTargets.GenericParameter)]
public sealed class SystemConcept : Attribute;
