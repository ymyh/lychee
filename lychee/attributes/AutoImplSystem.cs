namespace lychee.attributes;

/// <summary>
/// Instructs the source generator to automatically implement <see cref="lychee.interfaces.ISystem"/> for the annotated class.
/// </summary>
/// <param name="groupSize">The number of entities to execute per thread group. Must be greater than 0 to take effect.</param>
/// <param name="threadCount">The number of threads to use for parallel execution. Must be greater than 0 to take effect.</param>
/// <remarks>
/// The target class must be marked as <c>partial</c>. The <c>groupSize</c> and <c>threadCount</c> parameters
/// work together to define the parallelism of system execution.
/// </remarks>
[AttributeUsage(AttributeTargets.Class)]
public sealed class AutoImplSystem(uint groupSize = 0, uint threadCount = 0) : Attribute
{
    /// <summary>
    /// Gets the number of entities to execute per thread group.
    /// </summary>
    public readonly uint GroupSize = groupSize;

    /// <summary>
    /// Gets the number of threads to use for parallel execution.
    /// </summary>
    public readonly uint ThreadCount = threadCount;
}
