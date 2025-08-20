namespace lychee.interfaces;

/// <summary>
/// Interface for system
/// </summary>
public interface ISystem
{
    public IComponent[] AllFilter { get; }

    public IComponent[] AnyFilter { get; }

    public IComponent[] NoneFilter { get; }

    /// <summary>
    /// AG stands for auto generated. Don't implement this method manually unless you know what are you doing
    /// </summary>
    public void ExecuteAG();
}
