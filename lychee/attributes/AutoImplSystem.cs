namespace lychee.attributes;

/// <summary>
/// Auto implements <see cref="lychee.interfaces.ISystem"/> for annotated class. <br/>
/// Target class must be partial
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class AutoImplSystem : Attribute;