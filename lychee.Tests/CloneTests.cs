using lychee.components;

namespace lychee.Tests;

public class CloneTests : IDisposable
{
    private readonly App app = new();

    private readonly Commands commands;

    public CloneTests()
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

    private Entity Resolve(EntityRef entityRef)
    {
        Assert.True(commands.GetEntityByRef(entityRef, out var entity));

        return entity;
    }

#region Shallow

    [Fact]
    public void Shallow_CopyOfParent_HasNoChildrenAndDoesNotAliasTheSource()
    {
        var parent = commands.CreateEntity();
        var child = commands.CreateEntity();
        child.AddComponent(new ChildOf(parent.Ref));
        commands.Commit();

        var copy = parent.Copy();
        commands.Commit();

        // The copied target component is present but empty, and the source keeps its own child.
        Assert.Empty(Children(copy));
        Assert.Equal(new[] { child.Ref }, Children(parent));

        // The copy owns a distinct list, so destroying it must leave the source intact.
        copy.Despawn();
        commands.Commit();

        Assert.True(commands.CheckEntityValid(child.Ref));
        Assert.Equal(new[] { child.Ref }, Children(parent));
    }

    [Fact]
    public void Shallow_CopyOfChild_BecomesASibling()
    {
        var parent = commands.CreateEntity();
        var child = commands.CreateEntity();
        child.AddComponent(new ChildOf(parent.Ref));
        commands.Commit();

        var sibling = child.Copy();
        commands.Commit();

        Assert.Equal(parent.Ref, sibling.GetComponent<ChildOf>().Parent);
        Assert.Equal(new[] { child.Ref, sibling.Ref }, Children(parent));
    }

#endregion

#region Linked

    [Fact]
    public void Linked_CopiesSubtreeRebuildsTargetsAndKeepsOrder()
    {
        var root = commands.CreateEntity();
        var first = commands.CreateEntity();
        var second = commands.CreateEntity();
        var grandchild = commands.CreateEntity();
        first.AddComponent(new ChildOf(root.Ref));
        second.AddComponent(new ChildOf(root.Ref));
        grandchild.AddComponent(new ChildOf(first.Ref));
        commands.Commit();

        var copy = commands.CopyEntity(root, CloneMode.Linked);
        commands.Commit();

        var copiedChildren = Children(copy);
        Assert.Equal(2, copiedChildren.Length);

        var copiedFirst = Resolve(copiedChildren[0]);
        var copiedSecond = Resolve(copiedChildren[1]);
        Assert.Equal(copy.Ref, copiedFirst.GetComponent<ChildOf>().Parent);
        Assert.Equal(copy.Ref, copiedSecond.GetComponent<ChildOf>().Parent);

        var copiedGrandchildren = Children(copiedFirst);
        Assert.Single(copiedGrandchildren);
        Assert.Equal(copiedFirst.Ref, Resolve(copiedGrandchildren[0]).GetComponent<ChildOf>().Parent);
        Assert.Empty(Children(copiedSecond));

        // The source tree is untouched.
        Assert.Equal(new[] { first.Ref, second.Ref }, Children(root));
        Assert.Equal(new[] { grandchild.Ref }, Children(first));
    }

    [Fact]
    public void Linked_CopySurvivesDespawningTheSourceSubtree()
    {
        var root = commands.CreateEntity();
        var child = commands.CreateEntity();
        var grandchild = commands.CreateEntity();
        child.AddComponent(new ChildOf(root.Ref));
        grandchild.AddComponent(new ChildOf(child.Ref));
        commands.Commit();

        var copy = commands.CopyEntity(root, CloneMode.Linked);
        commands.Commit();

        root.Despawn();
        commands.Commit();

        Assert.False(commands.CheckEntityValid(root.Ref));
        Assert.False(commands.CheckEntityValid(child.Ref));
        Assert.False(commands.CheckEntityValid(grandchild.Ref));

        // If the copy aliased the source's collections, this would be a use-after-free.
        var copiedChildren = Children(copy);
        Assert.Single(copiedChildren);

        var copiedChild = Resolve(copiedChildren[0]);
        Assert.Single(Children(copiedChild));
    }

    [Fact]
    public void Linked_Leaf_CopiesJustItself()
    {
        var leaf = commands.CreateEntity();
        leaf.AddComponent(new TestPosition { X = 5.0f, Y = 6.0f });
        commands.Commit();

        var copy = leaf.Copy(CloneMode.Linked);
        commands.Commit();

        Assert.Equal(5.0f, copy.GetComponent<TestPosition>().X);
        Assert.Equal(6.0f, copy.GetComponent<TestPosition>().Y);
        Assert.False(copy.WithComponent<RelationshipTarget<ChildOf>>());
    }

    [Fact]
    public void Linked_CopyOfChildWithChildren_IsASiblingThatKeepsItsOwnSubtree()
    {
        var grandparent = commands.CreateEntity();
        var parent = commands.CreateEntity();
        var child = commands.CreateEntity();
        parent.AddComponent(new ChildOf(grandparent.Ref));
        child.AddComponent(new ChildOf(parent.Ref));
        commands.Commit();

        var copy = commands.CopyEntity(parent, CloneMode.Linked);
        commands.Commit();

        // The copy hangs under the original grandparent, next to the source parent.
        Assert.Equal(grandparent.Ref, copy.GetComponent<ChildOf>().Parent);
        Assert.Equal(new[] { parent.Ref, copy.Ref }, Children(grandparent));

        // It carries its own child, repointed at it.
        var copiedChildren = Children(copy);
        Assert.Single(copiedChildren);
        Assert.Equal(copy.Ref, Resolve(copiedChildren[0]).GetComponent<ChildOf>().Parent);

        // The source keeps its child.
        Assert.Equal(new[] { child.Ref }, Children(parent));
    }

#endregion

#region Missing source

    [Fact]
    public void Copy_WhenSourceIsGone_DoesNotSpawnTheCopy()
    {
        var source = commands.CreateEntity();
        commands.Commit();

        var buffer1 = new Commands(app);
        var buffer2 = new Commands(app);
        buffer1.RemoveEntity(source.Ref);
        var copy = buffer2.CopyEntity(source);

        app.World.CommandApplier.Apply([buffer1, buffer2]);

        Assert.False(commands.GetEntityByRef(copy.Ref, out _));
    }

    [Fact]
    public void Copy_WhenSourceIsGone_HandleDoesNotAliasAReusedId()
    {
        var source = commands.CreateEntity();
        commands.Commit();

        var buffer1 = new Commands(app);
        var buffer2 = new Commands(app);
        buffer1.RemoveEntity(source.Ref);
        var copy = buffer2.CopyEntity(source);

        app.World.CommandApplier.Apply([buffer1, buffer2]);

        // The skipped copy's id and the removed source's id are both reusable now. Reserving them must not
        // let a later entity inherit the handle the caller was handed.
        var first = commands.CreateEntity();
        var second = commands.CreateEntity();
        commands.Commit();

        Assert.Contains(copy.ID, new[] { first.ID, second.ID });
        Assert.False(commands.CheckEntityValid(copy.Ref));
        Assert.False(commands.GetEntityByRef(copy.Ref, out _));
        Assert.True(commands.GetEntityByRef(first.Ref, out _));
        Assert.True(commands.GetEntityByRef(second.Ref, out _));
    }

#endregion
}
