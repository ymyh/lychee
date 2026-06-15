using lychee.attributes;

namespace lychee.Tests;

public class CommandsTests : IDisposable
{
    private readonly App app = new();
    private readonly Commands commands;

    public CommandsTests()
    {
        commands = new Commands(app);
    }

    public void Dispose()
    {
        app.Dispose();
    }

#region AddComponent

    [Fact]
    public void AddComponent_SingleComponent_CanBeRetrievedAfterCommit()
    {
        var entity = commands.CreateEntity();
        entity.AddComponent(new TestPosition { X = 1.0f, Y = 2.0f });
        commands.Commit();

        ref var pos = ref entity.GetComponent<TestPosition>();
        Assert.Equal(1.0f, pos.X);
        Assert.Equal(2.0f, pos.Y);
    }

    [Fact]
    public void AddComponent_MultipleComponents_AllRetrievableAfterCommit()
    {
        var entity = commands.CreateEntity();
        entity.AddComponent(new TestPosition { X = 10.0f, Y = 20.0f });
        entity.AddComponent(new TestVelocity { DX = 1.0f, DY = 0.5f });
        commands.Commit();

        ref var pos = ref entity.GetComponent<TestPosition>();
        ref var vel = ref entity.GetComponent<TestVelocity>();

        Assert.Equal(10.0f, pos.X);
        Assert.Equal(20.0f, pos.Y);
        Assert.Equal(1.0f, vel.DX);
        Assert.Equal(0.5f, vel.DY);
    }

    [Fact]
    public void AddComponent_ViaEntity_AddWorksAfterCommit()
    {
        var entity = commands.CreateEntity();
        entity.AddComponent(new TestHealth { Value = 100.0f });
        commands.Commit();

        ref var health = ref entity.GetComponent<TestHealth>();
        Assert.Equal(100.0f, health.Value);
    }

    [Fact]
    public void AddComponent_ModifyViaRef_ChangesArePreservedAfterCommit()
    {
        var entity = commands.CreateEntity();
        entity.AddComponent(new TestPosition { X = 5.0f, Y = 5.0f });
        commands.Commit();

        ref var pos = ref entity.GetComponent<TestPosition>();
        pos.X = 99.0f;
        pos.Y = 88.0f;

        ref var pos2 = ref entity.GetComponent<TestPosition>();
        Assert.Equal(99.0f, pos2.X);
        Assert.Equal(88.0f, pos2.Y);
    }

#endregion

#region RemoveComponent

    [Fact]
    public void RemoveComponent_ExistingComponent_RemovedAfterCommit()
    {
        var entity = commands.CreateEntity();
        entity.AddComponent(new TestPosition { X = 1.0f, Y = 2.0f });
        entity.AddComponent(new TestVelocity { DX = 3.0f, DY = 4.0f });
        commands.Commit();

        entity.RemoveComponent<TestVelocity>();
        commands.Commit();

        Assert.False(entity.WithComponent<TestVelocity>());
        Assert.True(entity.WithComponent<TestPosition>());
    }

    [Fact]
    public void RemoveComponent_NonExistentComponent_ReturnsFalse()
    {
        var entity = commands.CreateEntity();
        entity.AddComponent(new TestPosition { X = 1.0f, Y = 2.0f });
        commands.Commit();

        // RemoveComponent on an entity without that component is a no-op
        entity.RemoveComponent<TestVelocity>();
        commands.Commit();

        Assert.True(entity.WithComponent<TestPosition>());
    }

#endregion

#region WithComponent / WithoutComponent

    [Fact]
    public void WithComponent_HasComponent_ReturnsTrue()
    {
        var entity = commands.CreateEntity();
        entity.AddComponent(new TestPosition { X = 1.0f, Y = 2.0f });
        commands.Commit();

        Assert.True(entity.WithComponent<TestPosition>());
    }

    [Fact]
    public void WithComponent_DoesNotHaveComponent_ReturnsFalse()
    {
        var entity = commands.CreateEntity();
        entity.AddComponent(new TestPosition { X = 1.0f, Y = 2.0f });
        commands.Commit();

        Assert.False(entity.WithComponent<TestVelocity>());
    }

    [Fact]
    public void WithoutComponent_HasComponent_ReturnsFalse()
    {
        var entity = commands.CreateEntity();
        entity.AddComponent(new TestPosition { X = 1.0f, Y = 2.0f });
        commands.Commit();

        Assert.False(entity.WithoutComponent<TestPosition>());
    }

    [Fact]
    public void WithoutComponent_DoesNotHaveComponent_ReturnsTrue()
    {
        var entity = commands.CreateEntity();
        entity.AddComponent(new TestPosition { X = 1.0f, Y = 2.0f });
        commands.Commit();

        Assert.True(entity.WithoutComponent<TestVelocity>());
    }

#endregion

#region AlterComponents

    [Fact]
    public void AlterComponents_RemoveAndAdd_SingleMigration()
    {
        var entity = commands.CreateEntity();
        entity.AddComponent(new TestPosition { X = 1.0f, Y = 2.0f });
        entity.AddComponent(new TestVelocity { DX = 3.0f, DY = 4.0f });
        commands.Commit();

        entity.AlterComponents((ref EntityAlterContext ctx) =>
        {
            ctx.Remove<TestVelocity>();
            ctx.Add(new TestHealth { Value = 50.0f });
        });
        commands.Commit();

        Assert.True(entity.WithComponent<TestPosition>());
        Assert.False(entity.WithComponent<TestVelocity>());
        Assert.True(entity.WithComponent<TestHealth>());
        Assert.Equal(50.0f, entity.GetComponent<TestHealth>().Value);
        // Original component data should be preserved
        Assert.Equal(1.0f, entity.GetComponent<TestPosition>().X);
        Assert.Equal(2.0f, entity.GetComponent<TestPosition>().Y);
    }

    [Fact]
    public void AlterComponents_RemoveOnly_MovesToSmallerArchetype()
    {
        var entity = commands.CreateEntity();
        entity.AddComponent(new TestPosition { X = 1.0f, Y = 2.0f });
        entity.AddComponent(new TestVelocity { DX = 3.0f, DY = 4.0f });
        commands.Commit();

        entity.AlterComponents((ref EntityAlterContext ctx) =>
        {
            ctx.Remove<TestVelocity>();
        });
        commands.Commit();

        Assert.True(entity.WithComponent<TestPosition>());
        Assert.False(entity.WithComponent<TestVelocity>());
        Assert.Equal(1.0f, entity.GetComponent<TestPosition>().X);
    }

#endregion

#region CreateEntityWithComponent

    [Fact]
    public void CreateEntityWithComponent_EntityHasComponentAfterCommit()
    {
        var entity = commands.CreateEntityWithComponent(new TestPosition { X = 7.0f, Y = 8.0f });
        commands.Commit();

        Assert.True(entity.WithComponent<TestPosition>());
        Assert.Equal(7.0f, entity.GetComponent<TestPosition>().X);
        Assert.Equal(8.0f, entity.GetComponent<TestPosition>().Y);
    }

    [Fact]
    public void CreateEntityWithComponents_Bundle_AllComponentsPresentAfterCommit()
    {
        var entity = commands.CreateEntityWithComponents(new TestMovement
        {
            Position = new TestPosition { X = 1.0f, Y = 2.0f },
            Velocity = new TestVelocity { DX = 3.0f, DY = 4.0f }
        });
        commands.Commit();

        Assert.True(entity.WithComponent<TestPosition>());
        Assert.True(entity.WithComponent<TestVelocity>());
        Assert.Equal(1.0f, entity.GetComponent<TestPosition>().X);
        Assert.Equal(3.0f, entity.GetComponent<TestVelocity>().DX);
    }

#endregion

#region Multiple Entities

    [Fact]
    public void MultipleEntities_IndependentComponentData()
    {
        var e1 = commands.CreateEntity();
        var e2 = commands.CreateEntity();
        e1.AddComponent(new TestPosition { X = 1.0f, Y = 2.0f });
        e2.AddComponent(new TestPosition { X = 10.0f, Y = 20.0f });
        commands.Commit();

        Assert.Equal(1.0f, e1.GetComponent<TestPosition>().X);
        Assert.Equal(10.0f, e2.GetComponent<TestPosition>().X);
    }

    [Fact]
    public void MultipleEntities_DifferentComponentSets()
    {
        var e1 = commands.CreateEntity();
        var e2 = commands.CreateEntity();
        e1.AddComponent(new TestPosition { X = 1.0f, Y = 2.0f });
        e2.AddComponent(new TestVelocity { DX = 5.0f, DY = 6.0f });
        commands.Commit();

        Assert.True(e1.WithComponent<TestPosition>());
        Assert.False(e1.WithComponent<TestVelocity>());
        Assert.True(e2.WithComponent<TestVelocity>());
        Assert.False(e2.WithComponent<TestPosition>());
    }

    [Fact]
    public void MultipleEntities_RemoveOne_OtherStillValid()
    {
        var e1 = commands.CreateEntity();
        var e2 = commands.CreateEntity();
        e1.AddComponent(new TestPosition { X = 1.0f, Y = 2.0f });
        e2.AddComponent(new TestPosition { X = 10.0f, Y = 20.0f });
        var e2Ref = e2.Ref;
        commands.Commit();

        // Despawn e1 and commit — archetype compaction may shift e2's position,
        // so we must re-fetch e2 via GetEntityByRef to get the updated Pos.
        e1.Despawn();
        commands.Commit();

        Assert.True(commands.GetEntityByRef(e2Ref, out var e2Updated));
        Assert.Equal(10.0f, e2Updated.GetComponent<TestPosition>().X);
    }

    [Fact]
    public void MultipleEntities_AddComponentToSecondEntity_PreservesFirst()
    {
        var e1 = commands.CreateEntity();
        var e2 = commands.CreateEntity();
        e1.AddComponent(new TestPosition { X = 1.0f, Y = 2.0f });
        e2.AddComponent(new TestPosition { X = 10.0f, Y = 20.0f });
        commands.Commit();

        // Add a component to e2 only — no removal involved
        e2.AddComponent(new TestVelocity { DX = 30.0f, DY = 40.0f });
        commands.Commit();

        // e1 should be unaffected
        Assert.True(e1.WithComponent<TestPosition>());
        Assert.False(e1.WithComponent<TestVelocity>());
        Assert.Equal(1.0f, e1.GetComponent<TestPosition>().X);

        // e2 should have both components
        Assert.True(e2.WithComponent<TestPosition>());
        Assert.True(e2.WithComponent<TestVelocity>());
        Assert.Equal(10.0f, e2.GetComponent<TestPosition>().X);
        Assert.Equal(30.0f, e2.GetComponent<TestVelocity>().DX);
    }

#endregion

#region CopyEntity

    [Fact]
    public void CopyEntity_HasSameComponentData()
    {
        var original = commands.CreateEntity();
        original.AddComponent(new TestPosition { X = 42.0f, Y = 99.0f });
        original.AddComponent(new TestHealth { Value = 50.0f });
        commands.Commit();

        var copy = original.Copy();
        commands.Commit();

        Assert.True(copy.WithComponent<TestPosition>());
        Assert.True(copy.WithComponent<TestHealth>());
        Assert.Equal(42.0f, copy.GetComponent<TestPosition>().X);
        Assert.Equal(99.0f, copy.GetComponent<TestPosition>().Y);
        Assert.Equal(50.0f, copy.GetComponent<TestHealth>().Value);
    }

    [Fact]
    public void CopyEntity_IndependentFromOriginal()
    {
        var original = commands.CreateEntity();
        original.AddComponent(new TestPosition { X = 1.0f, Y = 2.0f });
        commands.Commit();

        var copy = original.Copy();
        commands.Commit();

        // Modify copy, original should be unaffected
        copy.GetComponent<TestPosition>().X = 999.0f;

        Assert.Equal(1.0f, original.GetComponent<TestPosition>().X);
        Assert.Equal(999.0f, copy.GetComponent<TestPosition>().X);
    }

#endregion
}
