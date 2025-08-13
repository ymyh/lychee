namespace lychee.interfaces;

public interface ISystem
{
    public IComponent[] AllFilter { get; }

    public IComponent[] AnyFilter { get; }

    public IComponent[] NoneFilter { get; }

    public void Execute();
}