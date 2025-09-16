namespace lychee.interfaces;

public interface IComponent
{
}

public interface IComponentBundle
{
    /// <summary>
    /// AG stands for auto generated. Don't implement this method manually unless you know what are you doing. <br/>
    /// Copy data to pointer with given index.
    /// </summary>
    /// <param name="index">Type index</param>
    /// <param name="ptr">Target pointer.</param>
    public unsafe void SetDataAG(int index, void* ptr);

    /// <summary>
    /// AG stands for auto generated. Don't modify this property manually unless you know what are you doing.
    /// </summary>
    public static abstract int[] TypeIdAG { get; set; }
}