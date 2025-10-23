namespace lychee;

public sealed class Commander(World world)
{
    public void SendEvent<T>(T ev)
    {
        var queue = world.EventCenter.GetOrCreateQueue<T>();
        queue.Enqueue(ev);
    }
}
