using lychee.interfaces;

namespace lychee;

public struct SystemParameterInfo(Type type, bool readOnly, bool isResource)
{
    public readonly Type Type = type;

    public readonly bool ReadOnly = readOnly;

    public readonly bool IsResource = isResource;
}

public struct SystemInfo(ISystem system, SystemParameterInfo[] parameters, SystemDescriptor descriptor)
{
    internal readonly ISystem System = system;

    internal readonly SystemParameterInfo[] Parameters = parameters;

    internal readonly SystemDescriptor Descriptor = descriptor;
}

public sealed class SystemDescriptor
{
    internal ISystem? AddAfter;

    public Type[] AllFilter = [];

    public Type[] AnyFilter = [];

    public Type[] NoneFilter = [];

    public SystemDescriptor After(ISystem system)
    {
        AddAfter = system;
        return this;
    }

    public SystemDescriptor SetAllFilter(Type[] filterTypes)
    {
        AllFilter = filterTypes;
        return this;
    }

    public SystemDescriptor SetAnyFilter(Type[] filterTypes)
    {
        AnyFilter = filterTypes;
        return this;
    }

    public SystemDescriptor SetNoneFilter(Type[] filterTypes)
    {
        NoneFilter = filterTypes;
        return this;
    }
}
