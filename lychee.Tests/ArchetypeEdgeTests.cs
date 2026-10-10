namespace lychee.Tests;

/// <summary>
/// Covers the apply-time migration edge cache: repeating the same migration must reuse the cached edge
/// instead of creating (or scanning for) another archetype.
/// </summary>
public class ArchetypeEdgeTests : IDisposable
{
    private readonly App app = new();

    private readonly Commands commands;

    public ArchetypeEdgeTests()
    {
        commands = new Commands(app);
    }

    public void Dispose()
    {
        app.Dispose();
    }

    private ArchetypeManager ArchetypeManager => app.World.ArchetypeManager;

    private Archetype ArchetypeOf(Entity entity)
    {
        return app.World.EntityPool.GetEntityInfo(entity.Ref).Archetype;
    }

    [Fact]
    public void AddComponent_RepeatedSameEdge_CreatesArchetypeOnceAndReusesIt()
    {
        var first = commands.CreateEntity();
        var second = commands.CreateEntity();
        commands.Commit();

        first.AddComponent(new TestPosition { X = 1.0f });
        commands.Commit();
        var countAfterFirst = ArchetypeManager.Archetypes.Count;

        second.AddComponent(new TestPosition { X = 2.0f });
        commands.Commit();

        Assert.Equal(countAfterFirst, ArchetypeManager.Archetypes.Count);
        Assert.Same(ArchetypeOf(first), ArchetypeOf(second));
    }

    [Fact]
    public void RemoveComponent_RepeatedSameEdge_CreatesArchetypeOnceAndReusesIt()
    {
        var first = commands.CreateEntityWithComponents(new TestMovement());
        var second = commands.CreateEntityWithComponents(new TestMovement());
        commands.Commit();

        first.RemoveComponent<TestVelocity>();
        commands.Commit();
        var countAfterFirst = ArchetypeManager.Archetypes.Count;

        second.RemoveComponent<TestVelocity>();
        commands.Commit();

        Assert.Equal(countAfterFirst, ArchetypeManager.Archetypes.Count);
        Assert.Same(ArchetypeOf(first), ArchetypeOf(second));
    }

    [Fact]
    public void AddComponents_RepeatedSameBundleEdge_CreatesArchetypeOnceAndReusesIt()
    {
        var first = commands.CreateEntity();
        var second = commands.CreateEntity();
        commands.Commit();

        first.AddComponents(new TestMovement());
        commands.Commit();
        var countAfterFirst = ArchetypeManager.Archetypes.Count;

        second.AddComponents(new TestMovement());
        commands.Commit();

        Assert.Equal(countAfterFirst, ArchetypeManager.Archetypes.Count);
        Assert.Same(ArchetypeOf(first), ArchetypeOf(second));
    }

    [Fact]
    public void RemoveComponents_RepeatedSameBundleEdge_CreatesArchetypeOnceAndReusesIt()
    {
        var first = commands.CreateEntityWithComponents(new TestMovement());
        var second = commands.CreateEntityWithComponents(new TestMovement());
        commands.Commit();

        first.RemoveComponents<TestMovement>();
        commands.Commit();
        var countAfterFirst = ArchetypeManager.Archetypes.Count;

        second.RemoveComponents<TestMovement>();
        commands.Commit();

        Assert.Equal(countAfterFirst, ArchetypeManager.Archetypes.Count);
        Assert.Same(ArchetypeOf(first), ArchetypeOf(second));
    }

    [Fact]
    public void Alter_RepeatedSameMigration_ReusesEdgesAndReachesTheSameArchetype()
    {
        var first = commands.CreateEntityWithComponents(new TestMovement());
        var second = commands.CreateEntityWithComponents(new TestMovement());
        commands.Commit();

        first.AlterComponents((ref EntityAlterContext context) =>
        {
            context.Remove<TestVelocity>();
            context.Add(new TestHealth { Value = 1.0f });
        });
        commands.Commit();
        var countAfterFirst = ArchetypeManager.Archetypes.Count;

        second.AlterComponents((ref EntityAlterContext context) =>
        {
            context.Remove<TestVelocity>();
            context.Add(new TestHealth { Value = 2.0f });
        });
        commands.Commit();

        Assert.Equal(countAfterFirst, ArchetypeManager.Archetypes.Count);
        Assert.Same(ArchetypeOf(first), ArchetypeOf(second));
    }

    [Fact]
    public void AddComponent_AddingAnExistingType_ReturnsTheSameArchetype()
    {
        var entity = commands.CreateEntityWithComponent(new TestPosition { X = 1.0f });
        commands.Commit();

        var before = ArchetypeOf(entity);
        var countBefore = ArchetypeManager.Archetypes.Count;

        entity.AddComponent(new TestPosition { X = 2.0f });
        commands.Commit();

        Assert.Equal(countBefore, ArchetypeManager.Archetypes.Count);
        Assert.Same(before, ArchetypeOf(entity));
    }
}
