namespace lychee.collections;

public sealed class DoubleBufferQueue<T>
{
    private List<T> front = [];

    private List<T> back = [];

    public void Enqueue(T item)
    {
        lock (back)
        {
            back.Add(item);
        }
    }

    public IEnumerable<T> GetEnumerable()
    {
        return front;
    }

    public void Switch()
    {
        (front, back) = (back, front);
        back.Clear();
    }
}
