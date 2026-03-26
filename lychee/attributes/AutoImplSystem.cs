namespace lychee.attributes;

/// <summary>
/// Instructs the source generator to automatically implement <see cref="lychee.interfaces.ISystem"/> for the annotated class.
/// </summary>
/// <remarks>
/// The target class must be marked as <c>partial</c>. The <c>groupSize</c> and <c>threadCount</c> parameters
/// work together to define the parallelism of system execution.
/// </remarks>
[AttributeUsage(AttributeTargets.Class)]
public sealed class AutoImplSystem(bool multiThreaded = false) : Attribute
{
    public readonly bool MultiThreaded = multiThreaded;
}
