using lychee.attributes;

namespace lychee.Tests;

internal struct TestPing
{
    public int Value;
}

/// <summary>
/// Everything a test wants to assert about received events, kept per App so two tests cannot see each other's run.
/// </summary>
internal sealed class EventLog
{
    public List<int> Entries { get; } = [];
}

/// <summary>
/// Sends a ping every time it runs, numbered so a test can tell which run produced the event it is looking at.
/// </summary>
[AutoImplSystem]
internal sealed partial class PingSenderSystem
{
    private int sent;

    private void Execute(EventWriter<TestPing> writer)
    {
        writer.Send(new TestPing { Value = ++sent });
    }
}

/// <summary>
/// Records the value of every ping it can still read, which is every ping of the batch: each execution gets a
/// reader of its own, so this one never misses what another System consumed.
/// </summary>
[AutoImplSystem]
internal sealed partial class PingReceiverSystem
{
    private void Execute(EventReader<TestPing> reader, [Resource] EventLog log)
    {
        while (reader.Read(out var ping))
        {
            log.Entries.Add(ping.Value);
        }
    }
}

public class EventSystemTests : IDisposable
{
    private readonly App app = new();

#region Writer / Reader Parameters

    [Fact]
    public void Execute_TwoReceiversOfTheSameType_EachReadTheWholeBatch()
    {
        app.AddEvent<TestPing>(EventPublishTiming.ScheduleEnd);
        var log = app.ResourcePool.AddResource(new EventLog());

        var sending = new DefaultSchedule(app, "Sending");
        sending.AddSystem(new PingSenderSystem());

        var receiving = new DefaultSchedule(app, "Receiving");
        receiving.AddSystems([new PingReceiverSystem()], [new PingReceiverSystem()]);

        app.AddSchedule(sending);
        app.AddSchedule(receiving);

        app.Update();

        Assert.Equal([1, 1], log.Entries);
    }

#endregion

#region UpdateEnd

    [Fact]
    public void UpdateEnd_NotReadableInTheUpdateThatSentIt()
    {
        app.AddEvent<TestPing>();
        var log = app.ResourcePool.AddResource(new EventLog());

        var sending = new DefaultSchedule(app, "Sending");
        sending.AddSystem(new PingSenderSystem());

        var receiving = new DefaultSchedule(app, "Receiving");
        receiving.AddSystem(new PingReceiverSystem());

        app.AddSchedule(sending);
        app.AddSchedule(receiving);

        app.Update();

        Assert.Empty(log.Entries);
    }

    [Fact]
    public void UpdateEnd_ReadableInTheNextUpdate()
    {
        app.AddEvent<TestPing>();
        var log = app.ResourcePool.AddResource(new EventLog());

        var schedule = new DefaultSchedule(app, "Test");
        schedule.AddSystems([new PingSenderSystem()], [new PingReceiverSystem()]);
        app.AddSchedule(schedule);

        app.Update();
        Assert.Empty(log.Entries);

        app.Update();
        Assert.Equal([1], log.Entries);

        app.Update();
        Assert.Equal([1, 2], log.Entries);
    }

#endregion

#region ScheduleEnd

    [Fact]
    public void ScheduleEnd_ReadableInALaterScheduleOfTheSameUpdate()
    {
        app.AddEvent<TestPing>(EventPublishTiming.ScheduleEnd);
        var log = app.ResourcePool.AddResource(new EventLog());

        var sending = new DefaultSchedule(app, "Sending");
        sending.AddSystem(new PingSenderSystem());

        var receiving = new DefaultSchedule(app, "Receiving");
        receiving.AddSystem(new PingReceiverSystem());

        app.AddSchedule(sending);
        app.AddSchedule(receiving);

        app.Update();

        Assert.Equal([1], log.Entries);
    }

    [Fact]
    public void ScheduleEnd_NotReadableInALaterSystemOfTheSameSchedule()
    {
        app.AddEvent<TestPing>(EventPublishTiming.ScheduleEnd);
        var log = app.ResourcePool.AddResource(new EventLog());

        var schedule = new DefaultSchedule(app, "Test");
        schedule.AddSystems([new PingSenderSystem()], [new PingReceiverSystem()]);
        app.AddSchedule(schedule);

        app.Update();

        Assert.Empty(log.Entries);
    }

#endregion

#region CommitPoint

    [Fact]
    public void CommitPoint_ReadableInALaterSystemOfTheSameSchedule()
    {
        app.AddEvent<TestPing>(EventPublishTiming.CommitPoint);
        var log = app.ResourcePool.AddResource(new EventLog());

        var schedule = new DefaultSchedule(app, "Test");
        schedule.AddSystems([new PingSenderSystem()], [new PingReceiverSystem()]);
        app.AddSchedule(schedule);

        app.Update();

        Assert.Equal([1], log.Entries);
    }

    [Fact]
    public void CommitPoint_NotDiscardedBeforeTheNextUpdateReadsIt()
    {
        app.AddEvent<TestPing>(EventPublishTiming.CommitPoint);
        var log = app.ResourcePool.AddResource(new EventLog());

        var schedule = new DefaultSchedule(app, "Test");
        schedule.AddSystems([new PingSenderSystem()], [new PingReceiverSystem()]);
        app.AddSchedule(schedule);

        app.Update();
        app.Update();

        Assert.Equal([1, 2], log.Entries);
    }

#endregion

#region IDisposable Implementation

    public void Dispose()
    {
        app.Dispose();
    }

#endregion
}
