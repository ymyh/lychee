namespace lychee.interfaces;

public interface IComponentBundle
{
    public unsafe void SetDataWithPtr(int typeId, void* ptr);

    public static abstract Type[] Types { get; set; }
}
