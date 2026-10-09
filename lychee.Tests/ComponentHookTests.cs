using lychee.attributes;

namespace lychee.Tests;

/// <summary>
/// Overwrites its position parameter through an <c>out</c> slot, so the framework must report a replacement
/// after <c>Execute</c> returns.
/// </summary>
[AutoImplSystem]
internal sealed partial class TestPositionOutSystem
{
    private void Execute(out TestPosition position)
    {
        position = new TestPosition { X = 123.0f, Y = 456.0f };
    }
}

/// <summary>
/// Mutates its position parameter in place through a <c>ref</c> slot. This is not a replacement hook trigger.
/// </summary>
[AutoImplSystem]
internal sealed partial class TestPositionRefSystem
{
    private void Execute(ref TestPosition position)
    {
        position.X += 1.0f;
    }
}

public class ComponentHookTests : IDisposable
{
    private readonly App app = new();

    private readonly Commands commands;

    public ComponentHookTests()
    {
        commands = new Commands(app);
    }

    public void Dispose()
    {
        app.Dispose();
    }

#region OnAdd

    [Fact]
    public void Add_NewComponent_RunsOnAdd()
    {
        var added = new List<TestPosition>();
        EntityRef seenEntity = default;

        app.SetComponentHook(ComponentHookKind.OnAdd, (ref HookContext context, in TestPosition component) =>
        {
            added.Add(component);
            seenEntity = context.Entity;
        });

        var entity = commands.CreateEntity();
        entity.AddComponent(new TestPosition { X = 1.0f, Y = 2.0f });
        commands.Commit();

        Assert.Single(added);
        Assert.Equal(1.0f, added[0].X);
        Assert.Equal(2.0f, added[0].Y);
        Assert.Equal(entity.Ref, seenEntity);
    }

#endregion

#region OnRemove

    [Fact]
    public void Remove_ExistingComponent_RunsOnRemove()
    {
        var removed = new List<TestPosition>();
        app.SetComponentHook(ComponentHookKind.OnRemove, (ref HookContext context, in TestPosition component) => removed.Add(component));

        var entity = commands.CreateEntityWithComponent(new TestPosition { X = 5.0f, Y = 6.0f });
        commands.Commit();

        // CreateEntityWithComponent fired OnAdd, for which nothing is registered, so nothing was recorded yet.
        Assert.Empty(removed);

        entity.RemoveComponent<TestPosition>();
        commands.Commit();

        Assert.Single(removed);
        Assert.Equal(5.0f, removed[0].X);
        Assert.Equal(6.0f, removed[0].Y);
    }

    [Fact]
    public void Despawn_WithOtherComponents_TryGetSeesThemInsideRemoveHook()
    {
        var seenHealth = new List<float>();

        app.SetComponentHook(ComponentHookKind.OnRemove, (ref HookContext context, in TestVelocity component) =>
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

    [Fact]
    public void Despawn_ViaEntityRef_WithOtherComponents_TryGetSeesThemInsideRemoveHook()
    {
        var seenHealth = new List<float>();

        app.SetComponentHook(ComponentHookKind.OnRemove, (ref HookContext context, in TestVelocity component) =>
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

        // Exercises the EntityRef overload of RemoveEntity, which resolves the entity by reference.
        commands.RemoveEntity(entity.Ref);
        commands.Commit();

        Assert.Equal([42.0f], seenHealth);
    }

#endregion

#region ReplaceComponent

    [Fact]
    public void ReplaceComponent_ExistingComponent_RunsOnReplaceWithOldAndNew()
    {
        var previousValues = new List<float>();
        var currentValues = new List<float>();

        app.SetComponentHook(ComponentHookKind.OnReplace, (ref HookContext context, in TestHealth component) =>
        {
            if (context.TryGetPrevious<TestHealth>(out var previous))
            {
                previousValues.Add(previous.Value);
            }

            currentValues.Add(component.Value);
        });

        var entity = commands.CreateEntityWithComponent(new TestHealth { Value = 10.0f });
        commands.Commit();

        entity.ReplaceComponent(new TestHealth { Value = 99.0f });
        commands.Commit();

        Assert.Equal([10.0f], previousValues);
        Assert.Equal([99.0f], currentValues);
        Assert.Equal(99.0f, entity.GetComponent<TestHealth>().Value);
    }

    [Fact]
    public void ReplaceComponent_MissingComponent_ReturnsFalseAndDoesNotRun()
    {
        var ran = false;
        app.SetComponentHook(ComponentHookKind.OnReplace, (ref HookContext context, in TestHealth component) => ran = true);

        var entity = commands.CreateEntity();
        entity.AddComponent(new TestPosition { X = 1.0f, Y = 2.0f });
        commands.Commit();

        var replaced = commands.ReplaceComponent(ref entity, new TestHealth { Value = 1.0f });
        commands.Commit();

        Assert.False(replaced);
        Assert.False(ran);
    }

#endregion

#region System out parameter

    [Fact]
    public void SystemOutParameter_AfterExecute_RunsOnReplace()
    {
        var previousValues = new List<float>();
        var currentValues = new List<float>();

        app.SetComponentHook(ComponentHookKind.OnReplace, (ref HookContext context, in TestPosition component) =>
        {
            if (context.TryGetPrevious<TestPosition>(out var previous))
            {
                previousValues.Add(previous.X);
            }

            currentValues.Add(component.X);
        });

        var entity = commands.CreateEntityWithComponent(new TestPosition { X = 1.0f, Y = 2.0f });
        commands.Commit();

        var schedule = new DefaultSchedule(app, "Replace");
        schedule.AddSystem(new TestPositionOutSystem());
        schedule.Execute();

        Assert.Equal([1.0f], previousValues);
        Assert.Equal([123.0f], currentValues);

        Assert.True(commands.GetEntityByRef(entity.Ref, out var updated));
        Assert.Equal(123.0f, updated.GetComponent<TestPosition>().X);
    }

    [Fact]
    public void SystemRefParameter_DoesNotRunOnReplace()
    {
        var ran = false;
        app.SetComponentHook(ComponentHookKind.OnReplace, (ref HookContext context, in TestPosition component) => ran = true);

        var entity = commands.CreateEntityWithComponent(new TestPosition { X = 1.0f, Y = 2.0f });
        commands.Commit();

        var schedule = new DefaultSchedule(app, "Ref");
        schedule.AddSystem(new TestPositionRefSystem());
        schedule.Execute();

        Assert.False(ran);
        Assert.True(commands.GetEntityByRef(entity.Ref, out var updated));
        Assert.Equal(2.0f, updated.GetComponent<TestPosition>().X);
    }

#endregion

#region AlterComponents

    [Fact]
    public void Alter_RemoveAndAddSameType_RunsOnReplace()
    {
        var previousValues = new List<float>();
        var currentValues = new List<float>();

        app.SetComponentHook(ComponentHookKind.OnReplace, (ref HookContext context, in TestPosition component) =>
        {
            if (context.TryGetPrevious<TestPosition>(out var previous))
            {
                previousValues.Add(previous.X);
            }

            currentValues.Add(component.X);
        });

        var entity = commands.CreateEntityWithComponent(new TestPosition { X = 1.0f, Y = 2.0f });
        commands.Commit();

        entity.AlterComponents((ref EntityAlterContext context) =>
        {
            context.Remove<TestPosition>();
            context.Add(new TestPosition { X = 9.0f, Y = 8.0f });
        });
        commands.Commit();

        Assert.Equal([1.0f], previousValues);
        Assert.Equal([9.0f], currentValues);
        Assert.Equal(9.0f, entity.GetComponent<TestPosition>().X);
    }

#endregion

#region Migration keeps hooks quiet

    [Fact]
    public void Move_KeepingComponent_DoesNotRunItsHooks()
    {
        var events = new List<string>();

        app.SetComponentHook(ComponentHookKind.OnAdd, (ref HookContext context, in TestPosition component) => events.Add("add"));
        app.SetComponentHook(ComponentHookKind.OnReplace, (ref HookContext context, in TestPosition component) => events.Add("replace"));
        app.SetComponentHook(ComponentHookKind.OnRemove, (ref HookContext context, in TestPosition component) => events.Add("remove"));

        var entity = commands.CreateEntityWithComponent(new TestPosition { X = 1.0f, Y = 2.0f });
        commands.Commit();

        events.Clear();

        // Moving the entity to a larger archetype keeps TestPosition but must not report it.
        entity.AddComponent(new TestVelocity { DX = 1.0f, DY = 2.0f });
        commands.Commit();

        Assert.Empty(events);
    }

#endregion

#region Hook side effects

    [Fact]
    public void Hook_StructuralChange_RunsBeforeOuterCallReturns()
    {
        var nestedRan = false;

        app.SetComponentHook(ComponentHookKind.OnAdd, (ref HookContext context, in TestHealth component) =>
        {
            if (context.Commands.GetEntityByRef(context.Entity, out var entity))
            {
                entity.AddComponent(new TestVelocity { DX = 7.0f, DY = 8.0f });
            }
        });

        app.SetComponentHook(ComponentHookKind.OnAdd, (ref HookContext context, in TestVelocity component) => nestedRan = true);

        var entity = commands.CreateEntity();
        entity.AddComponent(new TestHealth { Value = 1.0f });

        // The nested add must have run synchronously, before the outer AddComponent returned.
        Assert.True(nestedRan);
        Assert.True(commands.GetEntityByRef(entity.Ref, out var updated));
        Assert.True(updated.WithComponent<TestVelocity>());
        Assert.Equal(7.0f, updated.GetComponent<TestVelocity>().DX);
    }

    [Fact]
    public void Hook_DeepRecursion_RunsWithoutLimit()
    {
        var seen = new List<float>();

        app.SetComponentHook(ComponentHookKind.OnAdd, (ref HookContext context, in TestHealth component) =>
        {
            seen.Add(component.Value);

            if (component.Value < 50.0f && context.Commands.GetEntityByRef(context.Entity, out var entity))
            {
                entity.RemoveComponent<TestHealth>();
                entity.AddComponent(new TestHealth { Value = component.Value + 1.0f });
            }
        });

        var entity = commands.CreateEntity();
        entity.AddComponent(new TestHealth { Value = 1.0f });

        // 50 levels is above the old 32 cap; reaching this assert proves the cap is gone and the run did not throw.
        Assert.Equal(50, seen.Count);
        Assert.Equal(50.0f, seen[^1]);
    }

#endregion

#region Ordering and plain behaviour

    [Fact]
    public void SameEntity_SeveralOperations_RunsInIssueOrder()
    {
        var order = new List<string>();

        app.SetComponentHook(ComponentHookKind.OnAdd, (ref HookContext context, in TestHealth component) => order.Add("add"));
        app.SetComponentHook(ComponentHookKind.OnReplace, (ref HookContext context, in TestHealth component) => order.Add("replace"));
        app.SetComponentHook(ComponentHookKind.OnRemove, (ref HookContext context, in TestHealth component) => order.Add("remove"));

        var entity = commands.CreateEntityWithComponent(new TestHealth { Value = 1.0f });
        commands.Commit();

        entity.ReplaceComponent(new TestHealth { Value = 2.0f });
        commands.Commit();

        entity.RemoveComponent<TestHealth>();
        commands.Commit();

        Assert.Equal(["add", "replace", "remove"], order);
    }

    [Fact]
    public void Commit_TypeWithoutHooks_MatchesCurrentBehavior()
    {
        var entity = commands.CreateEntity();
        entity.AddComponent(new TestPosition { X = 1.0f, Y = 2.0f });
        commands.Commit();

        Assert.True(entity.WithComponent<TestPosition>());

        entity.RemoveComponent<TestPosition>();
        commands.Commit();

        Assert.False(entity.WithComponent<TestPosition>());
    }

#endregion
}
