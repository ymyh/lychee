using lychee.interfaces;

namespace lychee;

internal struct SystemParameterInfo(Type type, bool readOnly)
{
    public readonly Type Type = type;

    public readonly bool ReadOnly = readOnly;
}

internal struct SystemInfo(ISystem system, SystemParameterInfo[] parameters)
{
    internal readonly ISystem System = system;

    internal readonly SystemParameterInfo[] Parameters = parameters;
}

public sealed class SystemDescriptor
{
    public Type[] AllFilter = [];

    public Type[] AnyFilter = [];

    public Type[] NoneFilter = [];
}
