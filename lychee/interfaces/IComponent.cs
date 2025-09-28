namespace lychee.interfaces;

public interface IComponent
{
}

public interface IComponentBundle
{
    /// <summary>
    /// AG stands for auto generated. Don't modify this property manually unless you know what are you doing.
    /// </summary>
    public static abstract (nint, int)[] StructInfo { get; set; }
}
