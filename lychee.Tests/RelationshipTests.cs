using lychee.attributes;
using lychee.components;

namespace lychee.Tests;

internal enum TestPhase
{
    Menu,
    Playing,
}

/// <summary>
/// Carries the parent a system should reparent an entity to, so the <c>out ChildOf</c> path can be tested.
/// </summary>
internal sealed class ReparentTarget
{
    public EntityRef Parent;
}

[AutoImplSystem]
internal sealed partial class ReparentSystem
{
    private static void Execute(out ChildOf child, [Resource] ReparentTarget target)
    {
        child = new ChildOf(target.Parent);
    }
}

public class RelationshipTests : IDisposable
{
    private readonly App app = new();

    private readonly Commands commands;

    public RelationshipTests()
    {
        commands = new Commands(app);
    }

    public void Dispose()
    {
        app.Dispose();
    }

    private static EntityRef[] Children(Entity parent)
    {
        return parent.WithComponent<RelationshipTarget<ChildOf>>()
            ? parent.GetComponent<RelationshipTarget<ChildOf>>().Entities.ToArray()
            : [];
    }

#region Attach

    [Fact]
    public void AddFirstChild_ParentGetsTargetContainingIt()
    {
        var parent = commands.CreateEntity();
        var child = commands.CreateEntity();
        child.AddComponent(new ChildOf(parent.Ref));
        commands.Commit();

        Assert.True(parent.WithComponent<RelationshipTarget<ChildOf>>());
        Assert.Equal(new[] { child.Ref }, Children(parent));
    }

    [Fact]
    public void AddTwoChildrenInSameBuffer_BothPresentInOrder()
    {
        var parent = commands.CreateEntity();
        var first = commands.CreateEntity();
        var second = commands.CreateEntity();
        first.AddComponent(new ChildOf(parent.Ref));
        second.AddComponent(new ChildOf(parent.Ref));
        commands.Commit();

        Assert.Equal(new[] { first.Ref, second.Ref }, Children(parent));
    }

    [Fact]
    public void AddTwoChildrenFromTwoBuffers_BothPresentInOrder()
    {
        var parent = commands.CreateEntity();
        commands.Commit();

        var buffer1 = new Commands(app);
        var buffer2 = new Commands(app);
        var first = buffer1.CreateEntity();
        var second = buffer2.CreateEntity();
        buffer1.AddComponent(first, new ChildOf(parent.Ref));
        buffer2.AddComponent(second, new ChildOf(parent.Ref));

        app.World.CommandApplier.Apply([buffer1, buffer2]);

        Assert.Equal(new[] { first.Ref, second.Ref }, Children(parent));
    }

#endregion

#region Reparent

    [Fact]
    public void ReplaceComponent_NewParent_MovesFromOldToNew()
    {
        var oldParent = commands.CreateEntity();
        var newParent = commands.CreateEntity();
        var child = commands.CreateEntity();
        child.AddComponent(new ChildOf(oldParent.Ref));
        commands.Commit();

        Assert.Equal(new[] { child.Ref }, Children(oldParent));

        child.ReplaceComponent(new ChildOf(newParent.Ref));
        commands.Commit();

        Assert.False(oldParent.WithComponent<RelationshipTarget<ChildOf>>());
        Assert.Equal(new[] { child.Ref }, Children(newParent));
    }

    [Fact]
    public void ReplaceComponent_SameParent_KeepsTheRelationship()
    {
        var parent = commands.CreateEntity();
        var child = commands.CreateEntity();
        child.AddComponent(new ChildOf(parent.Ref));
        commands.Commit();

        child.ReplaceComponent(new ChildOf(parent.Ref));
        commands.Commit();

        Assert.Equal(new[] { child.Ref }, Children(parent));
    }

    [Fact]
    public void SystemOutParameter_NewParent_MovesFromOldToNew()
    {
        var oldParent = commands.CreateEntity();
        var newParent = commands.CreateEntity();
        var child = commands.CreateEntity();
        child.AddComponent(new ChildOf(oldParent.Ref));
        commands.Commit();

        app.AddResource(new ReparentTarget { Parent = newParent.Ref });

        var schedule = new DefaultSchedule(app, "Reparent");
        schedule.AddSystem(new ReparentSystem());
        schedule.Execute();

        Assert.False(oldParent.WithComponent<RelationshipTarget<ChildOf>>());
        Assert.Equal(new[] { child.Ref }, Children(newParent));
        Assert.Equal(newParent.Ref, child.GetComponent<ChildOf>().Parent);
    }

#endregion

#region Detach

    [Fact]
    public void RemoveChildOf_LastChild_RemovesTheTarget()
    {
        var parent = commands.CreateEntity();
        var child = commands.CreateEntity();
        child.AddComponent(new ChildOf(parent.Ref));
        commands.Commit();

        child.RemoveComponent<ChildOf>();
        commands.Commit();

        Assert.False(child.WithComponent<ChildOf>());
        Assert.False(parent.WithComponent<RelationshipTarget<ChildOf>>());
    }

    [Fact]
    public void RemoveChildOf_OneOfTwo_KeepsTheOther()
    {
        var parent = commands.CreateEntity();
        var first = commands.CreateEntity();
        var second = commands.CreateEntity();
        first.AddComponent(new ChildOf(parent.Ref));
        second.AddComponent(new ChildOf(parent.Ref));
        commands.Commit();

        first.RemoveComponent<ChildOf>();
        commands.Commit();

        Assert.Equal(new[] { second.Ref }, Children(parent));
    }

    [Fact]
    public void DespawnChild_DetachesFromParent()
    {
        var parent = commands.CreateEntity();
        var child = commands.CreateEntity();
        child.AddComponent(new ChildOf(parent.Ref));
        commands.Commit();

        child.Despawn();
        commands.Commit();

        Assert.False(commands.CheckEntityValid(child.Ref));
        Assert.False(parent.WithComponent<RelationshipTarget<ChildOf>>());
    }

#endregion

#region Cascade despawn

    [Fact]
    public void DespawnParent_CascadesTheSubtree()
    {
        var parent = commands.CreateEntity();
        var child = commands.CreateEntity();
        var grandchild = commands.CreateEntity();
        child.AddComponent(new ChildOf(parent.Ref));
        grandchild.AddComponent(new ChildOf(child.Ref));
        commands.Commit();

        parent.Despawn();
        commands.Commit();

        Assert.False(commands.CheckEntityValid(parent.Ref));
        Assert.False(commands.CheckEntityValid(child.Ref));
        Assert.False(commands.CheckEntityValid(grandchild.Ref));
    }

    [Fact]
    public void RemoveChildOfBeforeDespawningParent_ChildSurvives()
    {
        var parent = commands.CreateEntity();
        var child = commands.CreateEntity();
        child.AddComponent(new ChildOf(parent.Ref));
        commands.Commit();

        child.RemoveComponent<ChildOf>();
        parent.Despawn();
        commands.Commit();

        Assert.True(commands.CheckEntityValid(child.Ref));
        Assert.False(commands.CheckEntityValid(parent.Ref));
    }

    [Fact]
    public void EntityWithBothChildOfAndChildren_DespawnCascadesBothWays()
    {
        // The middle entity is a child and a parent at once, so both of its relationship components fire
        // OnRemove; either order must despawn the whole subtree.
        var grandparent = commands.CreateEntity();
        var parent = commands.CreateEntity();
        var child = commands.CreateEntity();
        parent.AddComponent(new ChildOf(grandparent.Ref));
        child.AddComponent(new ChildOf(parent.Ref));
        commands.Commit();

        grandparent.Despawn();
        commands.Commit();

        Assert.False(commands.CheckEntityValid(grandparent.Ref));
        Assert.False(commands.CheckEntityValid(parent.Ref));
        Assert.False(commands.CheckEntityValid(child.Ref));
    }

    [Fact]
    public void EarlierBufferDespawnsParent_LaterBufferAddsChild_ChildOfIsRemoved()
    {
        var parent = commands.CreateEntity();
        var child = commands.CreateEntity();
        commands.Commit();

        var buffer1 = new Commands(app);
        var buffer2 = new Commands(app);
        buffer1.RemoveEntity(parent.Ref);
        buffer2.AddComponent(child, new ChildOf(parent.Ref));

        app.World.CommandApplier.Apply([buffer1, buffer2]);

        Assert.False(commands.CheckEntityValid(parent.Ref));
        Assert.False(child.WithComponent<ChildOf>());
    }

#endregion

#region Invalid relationships

    [Fact]
    public void SelfReference_RelationshipIsRemoved()
    {
        var entity = commands.CreateEntity();
        entity.AddComponent(new ChildOf(entity.Ref));
        commands.Commit();

        Assert.False(entity.WithComponent<ChildOf>());
        Assert.False(entity.WithComponent<RelationshipTarget<ChildOf>>());
    }

    [Fact]
    public void DanglingTarget_RelationshipIsRemoved()
    {
        var entity = commands.CreateEntity();
        var dangling = new EntityRef(9999, 0);
        entity.AddComponent(new ChildOf(dangling));
        commands.Commit();

        Assert.False(entity.WithComponent<ChildOf>());
    }

    [Fact]
    public void Cycle_SecondEdgeIsRemoved()
    {
        var a = commands.CreateEntity();
        var b = commands.CreateEntity();
        a.AddComponent(new ChildOf(b.Ref));
        commands.Commit();

        Assert.True(a.WithComponent<ChildOf>());

        b.AddComponent(new ChildOf(a.Ref));
        commands.Commit();

        Assert.True(a.WithComponent<ChildOf>());
        Assert.False(b.WithComponent<ChildOf>());
        Assert.Equal(new[] { a.Ref }, Children(b));
    }

#endregion

#region Target disposal

    [Fact]
    public void Target_OnRemove_DisposesInPlace_ForLaterHooks()
    {
        var parent = commands.CreateEntity();
        var child = commands.CreateEntity();
        child.AddComponent(new ChildOf(parent.Ref));
        commands.Commit();

        var countSeenLater = -1;

        // TestPosition is registered after the relationship pair, so its OnRemove runs after the target's.
        app.SetComponentHook(ComponentHookKind.OnRemove, (ref HookContext context, in TestPosition _) =>
        {
            countSeenLater = context.Get<RelationshipTarget<ChildOf>>(context.Entity).RelatedCount;
        });

        parent.AddComponent(new TestPosition { X = 1.0f, Y = 2.0f });
        commands.Commit();

        parent.Despawn();
        commands.Commit();

        // The target's hook disposed the list in place, so the later hook reads an empty collection rather
        // than the dangling pointer a boxed Dispose would have left behind.
        Assert.Equal(0, countSeenLater);
    }

#endregion

#region State change

    [Fact]
    public void StateChange_DespawnsParentAndSubtree()
    {
        var state = app.AddState(TestPhase.Menu);

        var parent = commands.CreateEntityWithComponent(new StateScoped<TestPhase> { Value = TestPhase.Menu });
        var child = commands.CreateEntity();
        child.AddComponent(new ChildOf(parent.Ref));
        commands.Commit();

        state.Set(TestPhase.Playing);
        app.Update();

        Assert.False(commands.CheckEntityValid(parent.Ref));
        Assert.False(commands.CheckEntityValid(child.Ref));
    }

#endregion
}
