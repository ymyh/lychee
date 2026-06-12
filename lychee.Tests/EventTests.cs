namespace lychee.Tests;

public class EventTests
{
#region SendEvent / GetEnumerable

    [Fact]
    public void SendEvent_SingleEvent_ReadableAfterExchange()
    {
        var ev = new Event<int>();

        ev.SendEvent(42);
        ev.ExchangeFrontBack();

        Assert.Equal([42], ev.GetEnumerable().ToArray());
    }

    [Fact]
    public void SendEvent_MultipleEvents_AllReadableAfterExchange()
    {
        var ev = new Event<int>();

        ev.SendEvent(1);
        ev.SendEvent(2);
        ev.SendEvent(3);
        ev.ExchangeFrontBack();

        Assert.Equal([1, 2, 3], ev.GetEnumerable().ToArray());
    }

    [Fact]
    public void SendEvent_BeforeExchange_NotReadable()
    {
        var ev = new Event<int>();

        ev.SendEvent(42);

        // Before exchange, front buffer is empty
        Assert.Empty(ev.GetEnumerable());
    }

#endregion

#region ExchangeFrontBack

    [Fact]
    public void ExchangeFrontBack_SwapsBuffers()
    {
        var ev = new Event<int>();

        // Frame 1: send events
        ev.SendEvent(1);
        ev.SendEvent(2);
        ev.ExchangeFrontBack();

        // Frame 1 data is in front
        Assert.Equal([1, 2], ev.GetEnumerable().ToArray());

        // Frame 2: send new events
        ev.SendEvent(3);
        ev.ExchangeFrontBack();

        // Frame 2 data is in front, frame 1 data is gone
        Assert.Equal([3], ev.GetEnumerable().ToArray());
    }

    [Fact]
    public void ExchangeFrontBack_ClearsNewBackBuffer()
    {
        var ev = new Event<int>();

        // Frame 1
        ev.SendEvent(1);
        ev.ExchangeFrontBack();

        // Frame 2: no new events
        ev.ExchangeFrontBack();

        // Front should be empty since no events were sent in frame 2
        Assert.Empty(ev.GetEnumerable());
    }

    [Fact]
    public void ExchangeFrontBack_EmptyBuffer_DoesNotThrow()
    {
        var ev = new Event<int>();

        ev.ExchangeFrontBack(); // should not throw

        Assert.Empty(ev.GetEnumerable());
    }

#endregion

#region Double Buffering Behavior

    [Fact]
    public void DoubleBuffering_EventsFromPreviousFrameOnly()
    {
        var ev = new Event<string>();

        // Frame 1: send events
        ev.SendEvent("frame1_a");
        ev.SendEvent("frame1_b");
        ev.ExchangeFrontBack();

        // Frame 1 events readable
        Assert.Equal(["frame1_a", "frame1_b"], ev.GetEnumerable().ToArray());

        // Frame 2: send new events while reading frame 1
        ev.SendEvent("frame2_a");

        // Frame 1 events still readable (not affected by frame 2 writes)
        Assert.Equal(["frame1_a", "frame1_b"], ev.GetEnumerable().ToArray());

        // Exchange: frame 2 events now readable
        ev.ExchangeFrontBack();

        Assert.Equal(["frame2_a"], ev.GetEnumerable().ToArray());
    }

    [Fact]
    public void DoubleBuffering_MultipleFrames_CorrectEvents()
    {
        var ev = new Event<int>();

        for (var frame = 0; frame < 10; frame++)
        {
            ev.SendEvent(frame);
            ev.ExchangeFrontBack();

            var events = ev.GetEnumerable().ToArray();
            Assert.Single(events);
            Assert.Equal(frame, events[0]);
        }
    }

    [Fact]
    public void DoubleBuffering_NoEventsInFrame_ReturnsEmpty()
    {
        var ev = new Event<int>();

        ev.SendEvent(1);
        ev.ExchangeFrontBack();

        // Frame 2: no events sent
        ev.ExchangeFrontBack();

        Assert.Empty(ev.GetEnumerable());
    }

#endregion

#region Struct Events

    [Fact]
    public void SendEvent_StructEvent_PreservedCorrectly()
    {
        var ev = new Event<TestEvent>();

        ev.SendEvent(new TestEvent { Id = 1, Value = 3.14f });
        ev.SendEvent(new TestEvent { Id = 2, Value = 2.72f });
        ev.ExchangeFrontBack();

        var events = ev.GetEnumerable().ToArray();
        Assert.Equal(2, events.Length);
        Assert.Equal(1, events[0].Id);
        Assert.Equal(3.14f, events[0].Value);
        Assert.Equal(2, events[1].Id);
        Assert.Equal(2.72f, events[1].Value);
    }

#endregion

    private struct TestEvent
    {
        public int Id;
        public float Value;
    }
}
