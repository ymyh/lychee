using lychee.components;
using lychee.interfaces;

namespace lychee.Tests;

internal struct TestChildBundle : IComponentBundle
{
    public TestPosition Position;

    public ChildOf ChildOf;
}

public class HierarchyTests : IDisposable
{
    private readonly App app = new();

    private readonly Commands commands;

    public HierarchyTests()
    {
        commands = new Commands(app);
    }

    public void Dispose()
    {
        app.Dispose();
    }

#region Basics

    [Fact]
    public void AddChild_Attach_ParentAndChildrenVisibleAfterCommit()
    {
        var parent = commands.CreateEntity();
        var childA = commands.CreateEntity();
        var childB = commands.CreateEntity();

        commands.AddChild(parent.Ref, childA.Ref);
        commands.AddChild(parent.Ref, childB.Ref);
        commands.Commit();

        Assert.True(commands.Hierarchy.TryGetParent(childA.Ref, out var parentOfA));
        Assert.Equal(parent.Ref, parentOfA);
        Assert.Equal(2, commands.Hierarchy.GetChildCount(parent.Ref));

        // Iteration order equals attachment order.
        var children = commands.Hierarchy.IterChildren(parent.Ref).ToList();
        Assert.Equal([childA.Ref, childB.Ref], children);
    }

    [Fact]
    public void AddComponent_ChildOf_IndexedAfterCommit()
    {
        var parent = commands.CreateEntity();
        var child = commands.CreateEntity();

        child.AddComponent(new ChildOf { Parent = parent.Ref });
        commands.Commit();

        Assert.True(commands.Hierarchy.TryGetParent(child.Ref, out var actual));
        Assert.Equal(parent.Ref, actual);
        Assert.Equal(1, commands.Hierarchy.GetChildCount(parent.Ref));
    }

    [Fact]
    public void CreateEntityWithComponent_ChildOf_IndexedAfterCommit()
    {
        var parent = commands.CreateEntity();
        var child = commands.CreateEntityWithComponent(new ChildOf { Parent = parent.Ref });
        commands.Commit();

        Assert.True(commands.Hierarchy.TryGetParent(child.Ref, out var actual));
        Assert.Equal(parent.Ref, actual);
    }

    [Fact]
    public void CreateEntityWithComponents_BundleWithChildOf_IndexedAfterCommit()
    {
        var parent = commands.CreateEntity();

        var child = commands.CreateEntityWithComponents(new TestChildBundle
        {
            Position = new() { X = 1.0f, Y = 2.0f },
            ChildOf = new() { Parent = parent.Ref },
        });
        commands.Commit();

        Assert.True(commands.Hierarchy.TryGetParent(child.Ref, out var actual));
        Assert.Equal(parent.Ref, actual);
        Assert.Equal(1.0f, child.GetComponent<TestPosition>().X);
    }

    [Fact]
    public void AddComponents_BundleWithChildOf_IndexedAfterCommit()
    {
        var parent = commands.CreateEntity();
        var child = commands.CreateEntity();
        commands.Commit();

        child.AddComponents(new TestChildBundle
        {
            Position = new() { X = 3.0f, Y = 4.0f },
            ChildOf = new() { Parent = parent.Ref },
        });
        commands.Commit();

        Assert.True(commands.Hierarchy.TryGetParent(child.Ref, out var actual));
        Assert.Equal(parent.Ref, actual);
    }

    [Fact]
    public void AddComponent_ChildOf_WhenAlreadyAttached_ThrowsArgumentException()
    {
        var parent = commands.CreateEntity();
        var child = commands.CreateEntity();

        child.AddComponent(new ChildOf { Parent = parent.Ref });
        commands.Commit();

        // Re-parenting must go through Commands.AddChild (AlterComponents), not a raw Add.
        Assert.Throws<ArgumentException>(() => child.AddComponent(new ChildOf { Parent = parent.Ref }));
    }

    [Fact]
    public void AddChild_Reparent_MovedToNewParentTail_OrderOfRemainingChildrenKept()
    {
        var parentA = commands.CreateEntity();
        var parentB = commands.CreateEntity();
        var child = commands.CreateEntity();
        var sibling = commands.CreateEntity();

        commands.AddChild(parentA.Ref, child.Ref);
        commands.AddChild(parentA.Ref, sibling.Ref);
        commands.Commit();

        commands.AddChild(parentB.Ref, child.Ref);
        commands.Commit();

        Assert.True(commands.Hierarchy.TryGetParent(child.Ref, out var newParent));
        Assert.Equal(parentB.Ref, newParent);

        Assert.Equal([sibling.Ref], commands.Hierarchy.IterChildren(parentA.Ref).ToList());
        Assert.Equal([child.Ref], commands.Hierarchy.IterChildren(parentB.Ref).ToList());
        Assert.Equal(1, commands.Hierarchy.GetChildCount(parentA.Ref));
    }

    [Fact]
    public void RemoveComponent_ChildOf_DetachedAfterCommit()
    {
        var parent = commands.CreateEntity();
        var child = commands.CreateEntity();

        child.AddComponent(new ChildOf { Parent = parent.Ref });
        commands.Commit();

        child.RemoveComponent<ChildOf>();
        commands.Commit();

        Assert.False(commands.Hierarchy.TryGetParent(child.Ref, out _));
        Assert.Equal(0, commands.Hierarchy.GetChildCount(parent.Ref));
        Assert.Empty(commands.Hierarchy.IterParents());
    }

    [Fact]
    public void RemoveComponents_BundleWithChildOf_DetachedAfterCommit()
    {
        var parent = commands.CreateEntity();

        var child = commands.CreateEntityWithComponents(new TestChildBundle
        {
            Position = new() { X = 1.0f, Y = 2.0f },
            ChildOf = new() { Parent = parent.Ref },
        });
        commands.Commit();

        child.RemoveComponents<TestChildBundle>();
        commands.Commit();

        Assert.False(commands.Hierarchy.TryGetParent(child.Ref, out _));
        Assert.Equal(0, commands.Hierarchy.GetChildCount(parent.Ref));
    }

    [Fact]
    public void RemoveChild_OnlyDetachesWhenParentMatches()
    {
        var parent = commands.CreateEntity();
        var other = commands.CreateEntity();
        var child = commands.CreateEntity();

        commands.AddChild(parent.Ref, child.Ref);
        commands.Commit();

        Assert.False(commands.RemoveChild(other.Ref, child.Ref));
        commands.Commit();

        Assert.True(commands.Hierarchy.TryGetParent(child.Ref, out var actual));
        Assert.Equal(parent.Ref, actual);

        Assert.True(commands.RemoveChild(parent.Ref, child.Ref));
        commands.Commit();

        Assert.False(commands.Hierarchy.TryGetParent(child.Ref, out _));
    }

    [Fact]
    public void RemoveChild_MiddleChild_RemainingOrderKept()
    {
        var parent = commands.CreateEntity();
        var childA = commands.CreateEntity();
        var childB = commands.CreateEntity();
        var childC = commands.CreateEntity();

        commands.AddChild(parent.Ref, childA.Ref);
        commands.AddChild(parent.Ref, childB.Ref);
        commands.AddChild(parent.Ref, childC.Ref);
        commands.Commit();

        commands.RemoveChild(parent.Ref, childB.Ref);
        commands.Commit();

        Assert.Equal([childA.Ref, childC.Ref], commands.Hierarchy.IterChildren(parent.Ref).ToList());
    }

    [Fact]
    public void CopyEntity_CloneAppearsUnderSameParent()
    {
        var parent = commands.CreateEntity();
        var child = commands.CreateEntity();

        commands.AddChild(parent.Ref, child.Ref);
        commands.Commit();

        var copy = child.Copy();
        commands.Commit();

        Assert.True(commands.Hierarchy.TryGetParent(copy.Ref, out var copyParent));
        Assert.Equal(parent.Ref, copyParent);
        Assert.Equal([child.Ref, copy.Ref], commands.Hierarchy.IterChildren(parent.Ref).ToList());
    }

    [Fact]
    public void DetachAllChildren_ChildrenBecomeRoots_GrandchildrenKept()
    {
        var parent = commands.CreateEntity();
        var childA = commands.CreateEntity();
        var childB = commands.CreateEntity();
        var grandChild = commands.CreateEntity();

        commands.AddChild(parent.Ref, childA.Ref);
        commands.AddChild(parent.Ref, childB.Ref);
        commands.AddChild(childA.Ref, grandChild.Ref);
        commands.Commit();

        commands.DetachAllChildren(parent.Ref);
        commands.Commit();

        Assert.False(commands.Hierarchy.TryGetParent(childA.Ref, out _));
        Assert.False(commands.Hierarchy.TryGetParent(childB.Ref, out _));
        Assert.Equal(0, commands.Hierarchy.GetChildCount(parent.Ref));

        // Grandchild keeps its own parent.
        Assert.True(commands.Hierarchy.TryGetParent(grandChild.Ref, out var grandParent));
        Assert.Equal(childA.Ref, grandParent);
    }

    [Fact]
    public void ForEachChild_VisitsAllChildrenInOrder()
    {
        var parent = commands.CreateEntity();
        var childA = commands.CreateEntity();
        var childB = commands.CreateEntity();

        commands.AddChild(parent.Ref, childA.Ref);
        commands.AddChild(parent.Ref, childB.Ref);
        commands.Commit();

        var visitedList = new List<EntityRef>();
        commands.Hierarchy.ForEachChild(parent.Ref, visitedList.Add);

        Assert.Equal([childA.Ref, childB.Ref], visitedList);
    }

#endregion

#region Cascade

    [Fact]
    public void Despawn_Parent_CascadeDeletesWholeSubtree()
    {
        var root = commands.CreateEntity();
        var child = commands.CreateEntity();
        var grandChild = commands.CreateEntity();

        commands.AddChild(root.Ref, child.Ref);
        commands.AddChild(child.Ref, grandChild.Ref);
        commands.Commit();

        root.Despawn();
        commands.Commit();

        Assert.False(commands.CheckEntityValid(child.Ref));
        Assert.False(commands.CheckEntityValid(grandChild.Ref));
        Assert.Empty(commands.Hierarchy.IterParents());
    }

    [Fact]
    public void Despawn_AfterDetachAllChildren_SubtreeSurvives()
    {
        var parent = commands.CreateEntity();
        var child = commands.CreateEntity();
        var grandChild = commands.CreateEntity();

        commands.AddChild(parent.Ref, child.Ref);
        commands.AddChild(child.Ref, grandChild.Ref);
        commands.Commit();

        parent.DetachAllChildren();
        parent.Despawn();
        commands.Commit();

        Assert.True(commands.CheckEntityValid(child.Ref));
        Assert.True(commands.CheckEntityValid(grandChild.Ref));
        Assert.False(commands.Hierarchy.TryGetParent(child.Ref, out _));

        Assert.True(commands.Hierarchy.TryGetParent(grandChild.Ref, out var grandParent));
        Assert.Equal(child.Ref, grandParent);
    }

    [Fact]
    public void SameBuffer_AddChildThenDespawnParent_ChildCascadeDeleted()
    {
        var parent = commands.CreateEntity();
        var child = commands.CreateEntity();

        commands.AddChild(parent.Ref, child.Ref);
        commands.RemoveEntity(parent.Ref);
        commands.Commit();

        Assert.Empty(commands.Hierarchy.IterParents());

        // Entities created and despawned within one buffer were never committed, so the pool
        // still reports the stale ref as valid (documented quirk) until the ID is recycled.
        var recycled = commands.CreateEntity();
        commands.Commit();

        Assert.Equal(child.Ref.ID, recycled.Ref.ID);
        Assert.False(commands.CheckEntityValid(child.Ref));
    }

    [Fact]
    public void SameBuffer_DespawnParentThenAddChild_ChildSurvives()
    {
        var parent = commands.CreateEntity();
        var child = commands.CreateEntity();
        commands.Commit();

        commands.RemoveEntity(parent.Ref);
        commands.AddChild(parent.Ref, child.Ref);
        commands.Commit();

        Assert.True(commands.CheckEntityValid(child.Ref));
        Assert.False(commands.Hierarchy.TryGetParent(child.Ref, out _));
        Assert.Empty(commands.Hierarchy.IterParents());
    }

#endregion

#region Cross-Buffer Validation

    [Fact]
    public void CrossBuffer_AddChildRecordedBeforeParentDeath_RejectedAtCommit()
    {
        var parent = commands.CreateEntity();
        var child = commands.CreateEntity();
        commands.Commit();

        var commandsA = new Commands(app);
        var commandsB = new Commands(app);

        // Recorded while the parent is still alive...
        commandsB.AddChild(parent.Ref, child.Ref);
        commandsA.RemoveEntity(parent.Ref);

        // ...but the parent's death commits first, so the op must be rejected.
        commandsA.Commit();
        commandsB.Commit();

        Assert.True(commands.CheckEntityValid(child.Ref));
        Assert.False(commands.Hierarchy.TryGetParent(child.Ref, out _));
        Assert.Empty(commands.Hierarchy.IterParents());
    }

    [Fact]
    public void CrossBuffer_AddChildCommittedBeforeDespawn_CascadeTakesChild()
    {
        var parent = commands.CreateEntity();
        var child = commands.CreateEntity();
        commands.Commit();

        var commandsA = new Commands(app);
        var commandsB = new Commands(app);

        commandsB.AddChild(parent.Ref, child.Ref);
        commandsA.RemoveEntity(parent.Ref);

        // The attachment commits while the parent is alive; the despawn then cascades.
        commandsB.Commit();
        commandsA.Commit();

        Assert.False(commands.CheckEntityValid(child.Ref));
        Assert.Empty(commands.Hierarchy.IterParents());
    }

#endregion

#region Traversal & Integration

    [Fact]
    public void IterDescendants_DepthFirstPreOrder()
    {
        var root = commands.CreateEntity();
        var childA = commands.CreateEntity();
        var grandChild = commands.CreateEntity();
        var childB = commands.CreateEntity();

        commands.AddChild(root.Ref, childA.Ref);
        commands.AddChild(childA.Ref, grandChild.Ref);
        commands.AddChild(root.Ref, childB.Ref);
        commands.Commit();

        var descendants = commands.Hierarchy.IterDescendants(root.Ref).ToList();
        Assert.Equal([childA.Ref, grandChild.Ref, childB.Ref], descendants);
    }

    [Fact]
    public void IterAncestors_And_RootAncestor()
    {
        var root = commands.CreateEntity();
        var child = commands.CreateEntity();
        var grandChild = commands.CreateEntity();

        commands.AddChild(root.Ref, child.Ref);
        commands.AddChild(child.Ref, grandChild.Ref);
        commands.Commit();

        var ancestors = commands.Hierarchy.IterAncestors(grandChild.Ref).ToList();
        Assert.Equal([child.Ref, root.Ref], ancestors);

        Assert.Equal(root.Ref, commands.Hierarchy.RootAncestor(grandChild.Ref));
        Assert.Equal(root.Ref, commands.Hierarchy.RootAncestor(root.Ref));
    }

    [Fact]
    public void MultipleCommits_IndexStaysConsistent()
    {
        var parent = commands.CreateEntity();
        var childA = commands.CreateEntity();
        commands.AddChild(parent.Ref, childA.Ref);
        commands.Commit();

        var childB = commands.CreateEntity();
        commands.AddChild(parent.Ref, childB.Ref);
        commands.Commit();

        Assert.Equal(2, commands.Hierarchy.GetChildCount(parent.Ref));
        Assert.Equal([childA.Ref, childB.Ref], commands.Hierarchy.IterChildren(parent.Ref).ToList());
    }

    [Fact]
    public void RemoveAllEntities_ClearsHierarchy()
    {
        var parent = commands.CreateEntity();
        var child = commands.CreateEntity();

        commands.AddChild(parent.Ref, child.Ref);
        commands.Commit();

        app.World.RemoveAllEntities();

        Assert.Empty(commands.Hierarchy.IterParents());
        Assert.Equal(0, commands.Hierarchy.GetChildCount(parent.Ref));
    }

#endregion
}
