using lychee.interfaces;

namespace lychee;

public struct SystemParameterInfo(Type type, bool readOnly, bool isResource)
{
    public readonly Type Type = type;

    public readonly bool ReadOnly = readOnly;

    public readonly bool IsResource = isResource;
}

public struct SystemInfo(ISystem system, SystemParameterInfo[] parameters, SystemFilterInfo filterInfo)
{
    internal readonly ISystem System = system;

    internal readonly SystemParameterInfo[] Parameters = parameters;

    internal readonly SystemFilterInfo FilterInfo = filterInfo;
}

public sealed class SystemDescriptor
{
    public ISystem? AddAfter { get; set; }

    public int ThreadCount { get; set; } = 0;

    public int GroupSize { get; set; } = 0;
}

public sealed class SystemFilterInfo
{
    public Type[] AllFilter = [];

    public Type[] AnyFilter = [];

    public Type[] NoneFilter = [];
}
