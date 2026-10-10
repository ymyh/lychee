using System.Collections.Concurrent;
using lychee.attributes;

namespace lychee.Tests;

/// <summary>
/// Collects the ids reserved by a multi-threaded system, so a test can prove concurrent
/// <c>CreateEntity</c> calls never hand out the same id twice.
/// </summary>
internal sealed class SpawnRecorder
{
    public ConcurrentBag<int> Ids { get; } = [];
}

[AutoImplSystem(multiThreaded: true)]
internal sealed partial class ParallelSpawnSystem
{
    private static void Execute(in TestPosition position, Commands commands, [Resource] SpawnRecorder recorder)
    {
        recorder.Ids.Add(commands.CreateEntity().Ref.ID);
    }
}

public class CommandQueueTests
{
#region Buffers

    [Fact]
    public void TwoBuffers_AddDifferentComponents_BothApplied()
    {
        using var app = new App();
        var buffer1 = new Commands(app);
        var buffer2 = new Commands(app);

        var entity = buffer1.CreateEntity();
        buffer1.AddComponent(entity, new TestPosition { X = 1.0f, Y = 2.0f });
        buffer2.AddComponent(entity, new TestVelocity { DX = 3.0f, DY = 4.0f });

        app.World.CommandApplier.Apply([buffer1, buffer2]);

        Assert.True(entity.WithComponent<TestPosition>());
        Assert.True(entity.WithComponent<TestVelocity>());
        Assert.Equal(1.0f, entity.GetComponent<TestPosition>().X);
        Assert.Equal(3.0f, entity.GetComponent<TestVelocity>().DX);
    }

    [Fact]
    public void EarlierBufferDespawns_LaterBufferCommandIsSkipped()
    {
        using var app = new App();
        var buffer1 = new Commands(app);
        var buffer2 = new Commands(app);

        var entity = buffer1.CreateEntity();
        buffer1.Commit();

        buffer1.RemoveEntity(entity.Ref);
        buffer2.AddComponent(entity, new TestPosition { X = 1.0f, Y = 2.0f });

        app.World.CommandApplier.Apply([buffer1, buffer2]);

        Assert.False(entity.WithComponent<TestPosition>());
        Assert.False(app.World.EntityPool.CheckEntityValid(entity.Ref));
    }

#endregion

#region Hook queue

    [Fact]
    public void HookCommand_AppliesBeforeTheNextRecordedCommand()
    {
        using var app = new App();
        var commands = new Commands(app);

        app.SetComponentHook(ComponentHookKind.OnAdd, (ref HookContext context, in TestHealth health) =>
        {
            // Enqueued from the hook, this add must land before the next command of the issuing buffer.
            context.Commands.AddComponent(new Entity(context.Commands, context.Entity), new TestMarker { Value = 5 });
        });

        var entity = commands.CreateEntity();
        entity.AddComponent(new TestHealth { Value = 1.0f });
        entity.RemoveComponent<TestMarker>();
        commands.Commit();

        // The hook added a marker, then the buffer's own remove took it back off: the hook ran first.
        Assert.False(entity.WithComponent<TestMarker>());
    }

    [Fact]
    public void OnRemove_TryGet_SeesOtherComponentsOfTheDespawnedEntity()
    {
        using var app = new App();
        var commands = new Commands(app);
        var seenHealth = new List<float>();

        app.SetComponentHook(ComponentHookKind.OnRemove, (ref HookContext context, in TestVelocity velocity) =>
        {
            if (context.TryGet<TestHealth>(out var health))
            {
                seenHealth.Add(health.Value);
            }
        });

        var entity = commands.CreateEntity();
        entity.AddComponent(new TestVelocity { DX = 1.0f, DY = 2.0f });
        entity.AddComponent(new TestHealth { Value = 42.0f });
        commands.Commit();

        entity.Despawn();
        commands.Commit();

        Assert.Equal([42.0f], seenHealth);
    }

#endregion

#region Multithreading

    [Fact]
    public void MultiThreadedSystem_ConcurrentCreateEntity_IdsAreUnique()
    {
        using var app = new App();
        var recorder = new SpawnRecorder();
        app.AddResource(recorder);

        var commands = new Commands(app);
        const int count = 200;

        for (var i = 0; i < count; i++)
        {
            commands.CreateEntityWithComponent(new TestPosition { X = i });
        }

        commands.Commit();

        var schedule = new DefaultSchedule(app, "Parallel", BasicSchedule.ExecutionModeEnum.MultiThread);
        schedule.AddSystem(new ParallelSpawnSystem(), new SystemDescriptor { ThreadCount = 4, GroupSize = 16 });
        schedule.Execute();

        Assert.Equal(count, recorder.Ids.Count);
        Assert.Equal(count, recorder.Ids.Distinct().Count());
    }

#endregion

#region Hook registration

    [Fact]
    public void SetComponentHook_SameTypeAndKindTwice_Throws()
    {
        using var app = new App();

        app.SetComponentHook(ComponentHookKind.OnAdd, (ref HookContext context, in TestMarker marker) => { });

        Assert.Throws<InvalidOperationException>(() =>
            app.SetComponentHook(ComponentHookKind.OnAdd, (ref HookContext context, in TestMarker marker) => { }));
    }

#endregion

#region Alter buffer reuse and capacity

    [Fact]
    public void Alter_MultipleRemovesAndAdd_AppliesAndTheBufferIsReusable()
    {
        using var app = new App();
        var commands = new Commands(app);

        var entity = commands.CreateEntity();
        entity.AddComponent(new TestPosition { X = 1.0f, Y = 2.0f });
        entity.AddComponent(new TestVelocity { DX = 3.0f, DY = 4.0f });
        entity.AddComponent(new TestHealth { Value = 5.0f });
        commands.Commit();

        entity.AlterComponents((ref EntityAlterContext context) =>
        {
            context.RemoveTuple<(TestVelocity, TestHealth)>();
            context.Add(new TestMarker { Value = 7 });
        });
        commands.Commit();

        Assert.True(entity.WithComponent<TestPosition>());
        Assert.False(entity.WithComponent<TestVelocity>());
        Assert.False(entity.WithComponent<TestHealth>());
        Assert.True(entity.WithComponent<TestMarker>());
        Assert.Equal(7, entity.GetComponent<TestMarker>().Value);
        Assert.Equal(1.0f, entity.GetComponent<TestPosition>().X);

        // The queue and its scratch state survive a commit: a second Alter on a fresh entity works.
        var second = commands.CreateEntity();
        second.AddComponent(new TestHealth { Value = 9.0f });
        commands.Commit();

        second.AlterComponents((ref EntityAlterContext context) =>
        {
            context.Remove<TestHealth>();
            context.Add(new TestMarker { Value = 11 });
        });
        commands.Commit();

        Assert.False(second.WithComponent<TestHealth>());
        Assert.True(second.WithComponent<TestMarker>());
        Assert.Equal(11, second.GetComponent<TestMarker>().Value);
    }

    [Fact]
    public void Commit_ClearsTheQueueButKeepsTheCapacity()
    {
        using var app = new App();
        var commands = new Commands(app);

        for (var i = 0; i < 1000; i++)
        {
            commands.CreateEntity();
        }

        var capacity = commands.Queue.Buffer.Length;
        Assert.True(capacity > 0);

        commands.Commit();

        Assert.True(commands.Queue.IsEmpty);
        Assert.Equal(capacity, commands.Queue.Buffer.Length);

        // Enqueuing after a commit must not reallocate below the high-water mark.
        commands.CreateEntity();
        Assert.Equal(capacity, commands.Queue.Buffer.Length);
        commands.Commit();
    }

#endregion
}
