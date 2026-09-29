namespace lychee.Tests;

public class EventTests
{
#region Send / Read

    [Fact]
    public void Send_SingleEvent_ReadableAfterExchange()
    {
        var ev = new Event<int>();
        var writer = new EventWriter<int>(ev);

        writer.Send(42);
        ev.ExchangeFrontBack();

        Assert.Equal([42], Drained(ev));
    }

    [Fact]
    public void Send_MultipleEvents_ReadableInSendOrder()
    {
        var ev = new Event<int>();
        var writer = new EventWriter<int>(ev);

        writer.Send(1);
        writer.Send(2);
        writer.Send(3);
        ev.ExchangeFrontBack();

        Assert.Equal([1, 2, 3], Drained(ev));
    }

    [Fact]
    public void Read_BeforeExchange_NoEvents()
    {
        var ev = new Event<int>();
        var writer = new EventWriter<int>(ev);

        writer.Send(42);

        Assert.Empty(Drained(ev));
    }

#endregion

#region ExchangeFrontBack

    [Fact]
    public void ExchangeFrontBack_SwapsBuffers()
    {
        var ev = new Event<int>();
        var writer = new EventWriter<int>(ev);

        writer.Send(1);
        writer.Send(2);
        ev.ExchangeFrontBack();

        Assert.Equal([1, 2], Drained(ev));

        writer.Send(3);
        ev.ExchangeFrontBack();

        Assert.Equal([3], Drained(ev));
    }

    [Fact]
    public void ExchangeFrontBack_ClearsNewBackBuffer()
    {
        var ev = new Event<int>();
        var writer = new EventWriter<int>(ev);

        writer.Send(1);
        ev.ExchangeFrontBack();

        ev.ExchangeFrontBack();

        Assert.Empty(Drained(ev));
    }

    [Fact]
    public void ExchangeFrontBack_EmptyBuffer_DoesNotThrow()
    {
        var ev = new Event<int>();
        var writer = new EventWriter<int>(ev);

        ev.ExchangeFrontBack();

        Assert.Empty(Drained(ev));
    }

#endregion

#region Double Buffering Behavior

    [Fact]
    public void DoubleBuffering_SendingWhileReading_DoesNotAffectTheReadBatch()
    {
        var ev = new Event<string>();
        var writer = new EventWriter<string>(ev);

        writer.Send("batch1_a");
        writer.Send("batch1_b");
        ev.ExchangeFrontBack();

        var reader = new EventReader<string>(ev);
        Assert.Equal("batch1_a", ReadOne(reader));

        writer.Send("batch2_a");

        Assert.Equal("batch1_b", ReadOne(reader));

        ev.ExchangeFrontBack();

        Assert.Equal(["batch2_a"], Drained(ev));
    }

    [Fact]
    public void DoubleBuffering_MultipleBatches_CorrectEvents()
    {
        var ev = new Event<int>();
        var writer = new EventWriter<int>(ev);

        for (var batch = 0; batch < 10; batch++)
        {
            writer.Send(batch);
            ev.ExchangeFrontBack();

            Assert.Equal([batch], Drained(ev));
        }
    }

    [Fact]
    public void DoubleBuffering_NoEventsInBatch_ReturnsEmpty()
    {
        var ev = new Event<int>();
        var writer = new EventWriter<int>(ev);

        writer.Send(1);
        ev.ExchangeFrontBack();

        ev.ExchangeFrontBack();

        Assert.Empty(Drained(ev));
    }

#endregion

#region Reader Cursor

    [Fact]
    public void Read_ExhaustedReader_ReturnsFalseForever()
    {
        var ev = new Event<int>();
        var writer = new EventWriter<int>(ev);

        writer.Send(1);
        ev.ExchangeFrontBack();

        var reader = new EventReader<int>(ev);
        Assert.True(reader.Read(out var first));
        Assert.Equal(1, first);
        Assert.False(reader.Read(out _));
        Assert.False(reader.Read(out _));
    }

    [Fact]
    public void Read_SecondReader_ReadsTheWholeBatchIndependently()
    {
        var ev = new Event<int>();
        var writer = new EventWriter<int>(ev);

        writer.Send(1);
        writer.Send(2);
        ev.ExchangeFrontBack();

        var first = new EventReader<int>(ev);
        Assert.True(first.Read(out _));

        var second = new EventReader<int>(ev);
        Assert.Equal([1, 2], Drained(second));
    }

    [Fact]
    public void Read_CursorIsNotRewound_AfterANewBatchIsPublished()
    {
        var ev = new Event<int>();
        var writer = new EventWriter<int>(ev);

        writer.Send(1);
        ev.ExchangeFrontBack();

        var reader = new EventReader<int>(ev);
        Assert.True(reader.Read(out _));

        writer.Send(2);
        ev.ExchangeFrontBack();

        // The cursor points past the end of the batch it started on, so it does not pick up the new one.
        Assert.False(reader.Read(out _));
    }

#endregion

#region Struct Events

    [Fact]
    public void Send_StructEvent_PreservedCorrectly()
    {
        var ev = new Event<TestEvent>();
        var writer = new EventWriter<TestEvent>(ev);

        writer.Send(new TestEvent { Id = 1, Value = 3.14f });
        writer.Send(new TestEvent { Id = 2, Value = 2.72f });
        ev.ExchangeFrontBack();

        var events = Drained(ev);
        Assert.Equal(2, events.Count);
        Assert.Equal(1, events[0].Id);
        Assert.Equal(3.14f, events[0].Value);
        Assert.Equal(2, events[1].Id);
        Assert.Equal(2.72f, events[1].Value);
    }

#endregion

#region Private Static Methods

    private static T ReadOne<T>(EventReader<T> reader)
    {
        Assert.True(reader.Read(out var value));

        return value;
    }

    private static List<T> Drained<T>(EventReader<T> reader)
    {
        var events = new List<T>();

        while (reader.Read(out var value))
        {
            events.Add(value);
        }

        return events;
    }

    private static List<T> Drained<T>(Event<T> ev)
    {
        return Drained(new EventReader<T>(ev));
    }

#endregion

    private struct TestEvent
    {
        public int Id;
        public float Value;
    }
}
