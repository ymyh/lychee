namespace lychee.attributes;

/// <summary>
/// Marks a struct as an ECS component. The source generator will automatically implement
/// <see cref="lychee.interfaces.IComponent"/> and its <c>GetComponentMeta()</c> method for the annotated type.
/// </summary>
/// <remarks>
/// The target struct must be marked as <c>partial</c>.
/// </remarks>
[AttributeUsage(AttributeTargets.Struct)]
public sealed class ComponentAttribute : Attribute;
