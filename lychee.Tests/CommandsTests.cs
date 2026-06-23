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

    [Fact]
    public void MultipleEntities_AddAndRemoveComponents_IndependentAfterCommit()
    {
        var e1 = commands.CreateEntity();
        var e2 = commands.CreateEntity();
        e1.AddComponent(new TestPosition { X = 1.0f, Y = 2.0f });
        e1.AddComponent(new TestVelocity { DX = 3.0f, DY = 4.0f });
        e2.AddComponent(new TestPosition { X = 10.0f, Y = 20.0f });
        var e1Ref = e1.Ref;
        var e2Ref = e2.Ref;
        commands.Commit();

        // Re-fetch entities after commit to get valid positions
        commands.GetEntityByRef(e1Ref, out e1);
        commands.GetEntityByRef(e2Ref, out e2);
        e1.RemoveComponent<TestVelocity>();
        e2.AddComponent(new TestVelocity { DX = 30.0f, DY = 40.0f });
        commands.Commit();

        // Re-fetch again after commit with archetype migration
        commands.GetEntityByRef(e1Ref, out e1);
        commands.GetEntityByRef(e2Ref, out e2);

        // e1: has Position only
        Assert.True(e1.WithComponent<TestPosition>());
        Assert.False(e1.WithComponent<TestVelocity>());
        Assert.Equal(1.0f, e1.GetComponent<TestPosition>().X);

        // e2: has both Position and Velocity
        Assert.True(e2.WithComponent<TestPosition>());
        Assert.True(e2.WithComponent<TestVelocity>());
        Assert.Equal(10.0f, e2.GetComponent<TestPosition>().X);
        Assert.Equal(30.0f, e2.GetComponent<TestVelocity>().DX);
    }

#endregion

#region Stress

    [Fact]
    public void Stress_CreateManyEntities_AllDataCorrect()
    {
        var entities = new Entity[1000];
        for (var i = 0; i < 1000; i++)
        {
            entities[i] = commands.CreateEntity();
            entities[i].AddComponent(new TestPosition { X = i, Y = i * 2 });
        }
        commands.Commit();

        for (var i = 0; i < 1000; i++)
        {
            ref var pos = ref entities[i].GetComponent<TestPosition>();
            Assert.Equal(i, pos.X);
            Assert.Equal(i * 2.0f, pos.Y);
        }
    }

    [Fact]
    public void Stress_ManyEntities_AddMultipleComponents_AllRetrievable()
    {
        var entities = new Entity[500];
        for (var i = 0; i < 500; i++)
        {
            entities[i] = commands.CreateEntity();
            entities[i].AddComponent(new TestPosition { X = i, Y = 0 });
            entities[i].AddComponent(new TestVelocity { DX = i * 0.1f, DY = 0 });
            entities[i].AddComponent(new TestHealth { Value = 100 - i });
        }
        commands.Commit();

        for (var i = 0; i < 500; i++)
        {
            Assert.Equal(i, entities[i].GetComponent<TestPosition>().X);
            Assert.Equal(i * 0.1f, entities[i].GetComponent<TestVelocity>().DX);
            Assert.Equal(100 - i, entities[i].GetComponent<TestHealth>().Value);
        }
    }

    [Fact]
    public void Stress_ManyEntities_RemoveHalf_OtherUnaffected()
    {
        var entities = new Entity[1000];
        for (var i = 0; i < 1000; i++)
        {
            entities[i] = commands.CreateEntity();
            entities[i].AddComponent(new TestPosition { X = i, Y = 0 });
        }
        commands.Commit();

        // Remove even-indexed entities
        for (var i = 0; i < 1000; i += 2)
        {
            entities[i].Despawn();
        }
        commands.Commit();

        // Odd-indexed entities should still be valid
        for (var i = 1; i < 1000; i += 2)
        {
            var ref_i = entities[i].Ref;
            Assert.True(commands.GetEntityByRef(ref_i, out var e));
            Assert.Equal(i, e.GetComponent<TestPosition>().X);
        }
    }

    [Fact]
    public void Stress_ManyEntities_AddComponent_ArchetypeMigration()
    {
        var entities = new Entity[500];
        for (var i = 0; i < 500; i++)
        {
            entities[i] = commands.CreateEntity();
            entities[i].AddComponent(new TestPosition { X = i, Y = 0 });
        }
        commands.Commit();

        // Add Velocity to all entities → triggers archetype migration
        for (var i = 0; i < 500; i++)
        {
            entities[i].AddComponent(new TestVelocity { DX = 1.0f, DY = 2.0f });
        }
        commands.Commit();

        for (var i = 0; i < 500; i++)
        {
            var ref_i = entities[i].Ref;
            Assert.True(commands.GetEntityByRef(ref_i, out var e));
            Assert.True(e.WithComponent<TestPosition>());
            Assert.True(e.WithComponent<TestVelocity>());
            Assert.Equal(i, e.GetComponent<TestPosition>().X);
            Assert.Equal(1.0f, e.GetComponent<TestVelocity>().DX);
        }
    }

    [Fact]
    public void Stress_ManyEntities_RemoveComponent_ArchetypeMigration()
    {
        var entities = new Entity[500];
        for (var i = 0; i < 500; i++)
        {
            entities[i] = commands.CreateEntity();
            entities[i].AddComponent(new TestPosition { X = i, Y = 0 });
            entities[i].AddComponent(new TestVelocity { DX = 1.0f, DY = 2.0f });
        }
        commands.Commit();

        // Remove Velocity from all entities → triggers archetype migration
        for (var i = 0; i < 500; i++)
        {
            entities[i].RemoveComponent<TestVelocity>();
        }
        commands.Commit();

        for (var i = 0; i < 500; i++)
        {
            var ref_i = entities[i].Ref;
            Assert.True(commands.GetEntityByRef(ref_i, out var e));
            Assert.True(e.WithComponent<TestPosition>());
            Assert.False(e.WithComponent<TestVelocity>());
            Assert.Equal(i, e.GetComponent<TestPosition>().X);
        }
    }

    [Fact]
    public void Stress_ManyEntities_InterleavedAddRemove()
    {
        // Create entities with Position only
        var entities = new Entity[400];
        for (var i = 0; i < 400; i++)
        {
            entities[i] = commands.CreateEntity();
            entities[i].AddComponent(new TestPosition { X = i, Y = 0 });
        }
        commands.Commit();

        // Add Velocity to half, remove Position from the other half
        for (var i = 0; i < 400; i++)
        {
            if (i % 2 == 0)
            {
                entities[i].AddComponent(new TestVelocity { DX = i, DY = 0 });
            }
            else
            {
                entities[i].RemoveComponent<TestPosition>();
                entities[i].AddComponent(new TestHealth { Value = i });
            }
        }
        commands.Commit();

        for (var i = 0; i < 400; i++)
        {
            var ref_i = entities[i].Ref;
            Assert.True(commands.GetEntityByRef(ref_i, out var e));
            if (i % 2 == 0)
            {
                Assert.True(e.WithComponent<TestPosition>());
                Assert.True(e.WithComponent<TestVelocity>());
                Assert.Equal(i, e.GetComponent<TestPosition>().X);
            }
            else
            {
                Assert.False(e.WithComponent<TestPosition>());
                Assert.True(e.WithComponent<TestHealth>());
                Assert.Equal(i, e.GetComponent<TestHealth>().Value);
            }
        }
    }

    [Fact]
    public void Stress_ManyEntities_CopyAll_DataMatches()
    {
        var originals = new Entity[300];
        for (var i = 0; i < 300; i++)
        {
            originals[i] = commands.CreateEntity();
            originals[i].AddComponent(new TestPosition { X = i, Y = i * 3 });
            originals[i].AddComponent(new TestHealth { Value = i * 10 });
        }
        commands.Commit();

        var copies = new Entity[300];
        for (var i = 0; i < 300; i++)
        {
            copies[i] = originals[i].Copy();
        }
        commands.Commit();

        for (var i = 0; i < 300; i++)
        {
            Assert.Equal(i, copies[i].GetComponent<TestPosition>().X);
            Assert.Equal(i * 3.0f, copies[i].GetComponent<TestPosition>().Y);
            Assert.Equal(i * 10.0f, copies[i].GetComponent<TestHealth>().Value);
        }
    }

    [Fact]
    public void Stress_MultipleCommits_AddAndRemove()
    {
        var entities = new Entity[200];
        for (var i = 0; i < 200; i++)
        {
            entities[i] = commands.CreateEntity();
            entities[i].AddComponent(new TestPosition { X = i, Y = 0 });
        }
        commands.Commit();

        // Batch 1: add Velocity to first 100
        for (var i = 0; i < 100; i++)
        {
            entities[i].AddComponent(new TestVelocity { DX = 1.0f, DY = 0 });
        }
        commands.Commit();

        // Batch 2: remove Velocity from first 50, add Health to last 100
        for (var i = 0; i < 50; i++)
        {
            entities[i].RemoveComponent<TestVelocity>();
        }
        for (var i = 100; i < 200; i++)
        {
            entities[i].AddComponent(new TestHealth { Value = i });
        }
        commands.Commit();

        // Verify final state
        for (var i = 0; i < 50; i++)
        {
            var ref_i = entities[i].Ref;
            Assert.True(commands.GetEntityByRef(ref_i, out var e));
            Assert.True(e.WithComponent<TestPosition>());
            Assert.False(e.WithComponent<TestVelocity>());
        }
        for (var i = 50; i < 100; i++)
        {
            var ref_i = entities[i].Ref;
            Assert.True(commands.GetEntityByRef(ref_i, out var e));
            Assert.True(e.WithComponent<TestPosition>());
            Assert.True(e.WithComponent<TestVelocity>());
        }
        for (var i = 100; i < 200; i++)
        {
            var ref_i = entities[i].Ref;
            Assert.True(commands.GetEntityByRef(ref_i, out var e));
            Assert.True(e.WithComponent<TestPosition>());
            Assert.True(e.WithComponent<TestHealth>());
            Assert.False(e.WithComponent<TestVelocity>());
        }
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

#region Edge Cases

    [Fact]
    public void CreateEntity_EmptyEntity_HasNoComponents()
    {
        var entity = commands.CreateEntity();
        commands.Commit();

        Assert.False(entity.WithComponent<TestPosition>());
        Assert.False(entity.WithComponent<TestVelocity>());
        Assert.False(entity.WithComponent<TestHealth>());
    }

    [Fact]
    public void Commit_NoOperations_DoesNotThrow()
    {
        commands.Commit(); // empty commit

        // Should not throw
    }

    [Fact]
    public void MultipleCommits_NoOperationsBetween_DoesNotThrow()
    {
        commands.Commit();
        commands.Commit();
        commands.Commit();

        // Should not throw
    }

    [Fact]
    public void GetEntityByRef_InvalidRef_ReturnsFalse()
    {
        var invalidRef = new EntityRef(999, 999);

        var found = commands.GetEntityByRef(invalidRef, out var entity);

        Assert.False(found);
    }

    [Fact]
    public void Despawn_ThenGetEntityByRef_ReturnsFalse()
    {
        var entity = commands.CreateEntity();
        entity.AddComponent(new TestPosition { X = 1.0f, Y = 2.0f });
        var entityRef = entity.Ref;
        commands.Commit();

        commands.GetEntityByRef(entityRef, out var e);
        e.Despawn();
        commands.Commit();

        Assert.False(commands.GetEntityByRef(entityRef, out _));
    }

    [Fact]
    public void AlterComponents_AddOnly_NoRemove()
    {
        var entity = commands.CreateEntity();
        entity.AddComponent(new TestPosition { X = 1.0f, Y = 2.0f });
        commands.Commit();

        entity.AlterComponents((ref EntityAlterContext ctx) =>
        {
            ctx.Add(new TestHealth { Value = 50.0f });
        });
        commands.Commit();

        Assert.True(entity.WithComponent<TestPosition>());
        Assert.True(entity.WithComponent<TestHealth>());
        Assert.Equal(50.0f, entity.GetComponent<TestHealth>().Value);
    }

    [Fact]
    public void AlterComponents_RemoveThenAddSameType()
    {
        var entity = commands.CreateEntity();
        entity.AddComponent(new TestPosition { X = 1.0f, Y = 2.0f });
        commands.Commit();

        entity.AlterComponents((ref EntityAlterContext ctx) =>
        {
            ctx.Remove<TestPosition>();
            ctx.Add(new TestPosition { X = 99.0f, Y = 88.0f });
        });
        commands.Commit();

        Assert.True(entity.WithComponent<TestPosition>());
        Assert.Equal(99.0f, entity.GetComponent<TestPosition>().X);
        Assert.Equal(88.0f, entity.GetComponent<TestPosition>().Y);
    }

    [Fact]
    public void AddComponent_AfterCommit_WorksInNextCommit()
    {
        var entity = commands.CreateEntity();
        entity.AddComponent(new TestPosition { X = 1.0f, Y = 2.0f });
        commands.Commit();

        entity.AddComponent(new TestVelocity { DX = 3.0f, DY = 4.0f });
        commands.Commit();

        Assert.True(entity.WithComponent<TestPosition>());
        Assert.True(entity.WithComponent<TestVelocity>());
        Assert.Equal(1.0f, entity.GetComponent<TestPosition>().X);
        Assert.Equal(3.0f, entity.GetComponent<TestVelocity>().DX);
    }

    [Fact]
    public void CreateEntityWithComponent_ThenAddMore_Works()
    {
        var entity = commands.CreateEntityWithComponent(new TestPosition { X = 1.0f, Y = 2.0f });
        entity.AddComponent(new TestVelocity { DX = 3.0f, DY = 4.0f });
        commands.Commit();

        Assert.True(entity.WithComponent<TestPosition>());
        Assert.True(entity.WithComponent<TestVelocity>());
    }

    [Fact]
    public void CopyEntity_EmptyEntity_CopiesCorrectly()
    {
        var original = commands.CreateEntity();
        commands.Commit();

        var copy = original.Copy();
        commands.Commit();

        Assert.False(copy.WithComponent<TestPosition>());
    }

    [Fact]
    public void RemoveComponent_ThenAddBack_SameComponent()
    {
        var entity = commands.CreateEntity();
        entity.AddComponent(new TestPosition { X = 1.0f, Y = 2.0f });
        commands.Commit();

        entity.RemoveComponent<TestPosition>();
        entity.AddComponent(new TestPosition { X = 99.0f, Y = 88.0f });
        commands.Commit();

        Assert.True(entity.WithComponent<TestPosition>());
        Assert.Equal(99.0f, entity.GetComponent<TestPosition>().X);
    }

    [Fact]
    public void MultipleEntities_SameArchetype_IndependentData()
    {
        var entities = new Entity[10];
        for (var i = 0; i < 10; i++)
        {
            entities[i] = commands.CreateEntity();
            entities[i].AddComponent(new TestPosition { X = i, Y = i * 10 });
        }
        commands.Commit();

        for (var i = 0; i < 10; i++)
        {
            Assert.Equal(i, entities[i].GetComponent<TestPosition>().X);
            Assert.Equal(i * 10.0f, entities[i].GetComponent<TestPosition>().Y);
        }
    }

    [Fact]
    public void CheckEntityValid_ValidEntity_ReturnsTrue()
    {
        var entity = commands.CreateEntity();
        var entityRef = entity.Ref;
        commands.Commit();

        Assert.True(commands.CheckEntityValid(entityRef));
    }

    [Fact]
    public void CheckEntityValid_InvalidRef_ReturnsFalse()
    {
        var invalidRef = new EntityRef(999, 999);

        Assert.False(commands.CheckEntityValid(invalidRef));
    }

#endregion
}
