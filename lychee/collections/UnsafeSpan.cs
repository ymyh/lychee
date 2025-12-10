namespace lychee.collections;

/// <summary>
/// Represents a span of unmanaged memory and can be a struct member.
/// </summary>
/// <param name="ptr">The pointer to the first element of the span.</param>
/// <param name="size">The number of elements in the span.</param>
/// <typeparam name="T">The type of the elements in the span.</typeparam>
public unsafe struct UnsafeSpan<T>(T* ptr, int size) where T : unmanaged
{
    private T* ptr = ptr;

    private int size = size;

    public T* Data => ptr;

    public int Count => size;

    public Span<T> Span => new(ptr, size);
}
