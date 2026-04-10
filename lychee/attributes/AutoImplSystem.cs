namespace lychee.attributes;

/// <summary>
/// Instructs the source generator to automatically implement <see cref="lychee.interfaces.ISystem"/> for the annotated class.
/// </summary>
/// <remarks>
/// The target class must be marked as <c>partial</c> and contains a method named <c>Execute</c>. The <c>multiThreaded</c> parameter
/// indicates whether the generated code is multi-thread style. <br/>
/// All parameters of the <c>Execute</c> may not named with prefix "_" in order to avoid name conflict with generated variables.
/// </remarks>
[AttributeUsage(AttributeTargets.Class)]
public sealed class AutoImplSystem(bool multiThreaded = false) : Attribute
{
    public readonly bool MultiThreaded = multiThreaded;
}
