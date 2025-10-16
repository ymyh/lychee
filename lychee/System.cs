using lychee.interfaces;

namespace lychee;

internal struct SystemParameterInfo(Type type, bool readOnly)
{
    public readonly Type Type = type;

    public readonly bool ReadOnly = readOnly;
}

internal struct SystemInfo(ISystem system, SystemParameterInfo[] parameters, SystemDescriptor descriptor)
{
    internal readonly ISystem System = system;

    internal readonly SystemParameterInfo[] Parameters = parameters;

    internal readonly SystemDescriptor Descriptor = descriptor;
}

public sealed class SystemDescriptor
{
    public ISystem? AddAfter;

    public Type[] AllFilter = [];

    public Type[] AnyFilter = [];

    public Type[] NoneFilter = [];
}