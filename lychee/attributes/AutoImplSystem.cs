namespace lychee.attributes;

/// <summary>
/// Auto implements <see cref="lychee.interfaces.ISystem"/> for annotated class. <br/>
/// Target class must be partial. <br/>
/// The parameter groupSize and threadCount must be greater than 0 to take effect.
/// Together they define the parallelism of system execution.
/// <param name="groupSize">Size of entities to execute in same thread</param>
/// <param name="threadCount">Number of thread to execute system</param>
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class AutoImplSystem(uint groupSize = 0, uint threadCount = 0) : Attribute
{
    public uint GroupSize = groupSize;

    public uint ThreadCount = threadCount;
}