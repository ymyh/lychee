using lychee.interfaces;

namespace lychee;

internal sealed class SystemParameterInfo(Type type, bool readOnly)
{
    public readonly Type Type = type;

    public readonly bool ReadOnly = readOnly;
}

internal struct SystemInfo(ISystem system, SystemParameterInfo[] parameters)
{
    internal readonly ISystem System = system;

    internal readonly SystemParameterInfo[] Parameters = parameters;
}