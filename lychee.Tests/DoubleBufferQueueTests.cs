using lychee.collections;

namespace lychee.Tests;

public class DoubleBufferQueueTests
{
#region Enqueue

    [Fact]
    public void Enqueue_SingleItem_CanBeReadAfterExchange()
    {
        var queue = new DoubleBufferQueue<int>();

        queue.Enqueue(42);
        queue.Exchange();

        Assert.Single(queue.GetEnumerable());
        Assert.Equal(42, queue.GetEnumerable().First());
    }

    [Fact]
    public void Enqueue_MultipleItems_AllReadableAfterExchange()
    {
        var queue = new DoubleBufferQueue<int>();

        queue.Enqueue(1);
        queue.Enqueue(2);
        queue.Enqueue(3);
        queue.Exchange();

        Assert.Equal([1, 2, 3], queue.GetEnumerable().ToArray());
    }

#endregion

#region Exchange

    [Fact]
    public void Exchange_SwapsFrontAndBack()
    {
        var queue = new DoubleBufferQueue<int>();

        // Frame 1: enqueue items
        queue.Enqueue(1);
        queue.Enqueue(2);
        queue.Exchange();
        queue.ClearBack();

        // Frame 1 data is now in front
        Assert.Equal([1, 2], queue.GetEnumerable().ToArray());

        // Frame 2: enqueue new items
        queue.Enqueue(3);
        queue.Exchange();
        queue.ClearBack();

        // Frame 2 data is now in front, frame 1 data is gone
        Assert.Equal([3], queue.GetEnumerable().ToArray());
    }

    [Fact]
    public void Exchange_EmptyQueue_DoesNotThrow()
    {
        var queue = new DoubleBufferQueue<int>();

        queue.Exchange(); // should not throw

        Assert.Empty(queue.GetEnumerable());
    }

#endregion

#region ClearBack

    [Fact]
    public void ClearBack_RemovesBackBufferItems()
    {
        var queue = new DoubleBufferQueue<int>();

        queue.Enqueue(1);
        queue.Enqueue(2);
        queue.ClearBack();

        // After exchange, front should be empty since back was cleared
        queue.Exchange();

        Assert.Empty(queue.GetEnumerable());
    }

    [Fact]
    public void ClearBack_DoesNotAffectFrontBuffer()
    {
        var queue = new DoubleBufferQueue<int>();

        queue.Enqueue(1);
        queue.Exchange();
        queue.ClearBack();

        // Front still has the data
        Assert.Equal([1], queue.GetEnumerable().ToArray());
    }

#endregion

#region GetEnumerable

    [Fact]
    public void GetEnumerable_EmptyFront_ReturnsEmpty()
    {
        var queue = new DoubleBufferQueue<int>();

        Assert.Empty(queue.GetEnumerable());
    }

#endregion

#region GetFrontSpan

    [Fact]
    public void GetFrontSpan_ReturnsCorrectData()
    {
        var queue = new DoubleBufferQueue<int>();

        queue.Enqueue(10);
        queue.Enqueue(20);
        queue.Exchange();

        var span = queue.GetFrontSpan();

        Assert.Equal(2, span.Length);
        Assert.Equal(10, span[0]);
        Assert.Equal(20, span[1]);
    }

#endregion

#region DoubleBuffering Behavior

    [Fact]
    public void DoubleBuffering_WritesAndReadsAreSeparated()
    {
        var queue = new DoubleBufferQueue<string>();

        // Frame 1: write
        queue.Enqueue("frame1_a");
        queue.Enqueue("frame1_b");
        queue.Exchange();

        // Frame 1: read (front has frame1 data)
        Assert.Equal(["frame1_a", "frame1_b"], queue.GetEnumerable().ToArray());

        // Frame 2: write (back is separate from front)
        queue.Enqueue("frame2_a");

        // Frame 1 data still readable while writing frame 2
        Assert.Equal(["frame1_a", "frame1_b"], queue.GetEnumerable().ToArray());

        // Frame 2: exchange and clear
        queue.Exchange();
        queue.ClearBack();

        // Now frame 2 data is in front
        Assert.Equal(["frame2_a"], queue.GetEnumerable().ToArray());
    }

    [Fact]
    public void DoubleBuffering_MultipleExchanges_MaintainsSeparation()
    {
        var queue = new DoubleBufferQueue<int>();

        for (var frame = 0; frame < 10; frame++)
        {
            queue.Enqueue(frame);
            queue.Exchange();
            queue.ClearBack();

            Assert.Single(queue.GetEnumerable());
            Assert.Equal(frame, queue.GetEnumerable().First());
        }
    }

#endregion

#region Thread Safety

    [Fact]
    public void Enqueue_ConcurrentAdds_AllItemsCaptured()
    {
        var queue = new DoubleBufferQueue<int>();

        Parallel.For(0, 1000, i => queue.Enqueue(i));

        queue.Exchange();

        var items = queue.GetEnumerable().ToArray();
        Assert.Equal(1000, items.Length);
    }

#endregion
}
