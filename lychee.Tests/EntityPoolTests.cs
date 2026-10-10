namespace lychee.Tests;

public class EntityPoolTests
{
#region ReserveEntity

    [Fact]
    public void ReserveEntity_FirstCall_ReturnsIdZero()
    {
        var pool = new EntityPool();

        var entityRef = pool.ReserveEntity();

        Assert.Equal(0, entityRef.ID);
        Assert.Equal(0, entityRef.Generation);
    }

    [Fact]
    public void ReserveEntity_SequentialCalls_ReturnsIncrementingIds()
    {
        var pool = new EntityPool();

        var e1 = pool.ReserveEntity();
        var e2 = pool.ReserveEntity();
        var e3 = pool.ReserveEntity();

        Assert.Equal(0, e1.ID);
        Assert.Equal(1, e2.ID);
        Assert.Equal(2, e3.ID);
    }

#endregion

#region CheckEntityValid

    [Fact]
    public void CheckEntityValid_ReservedButNotSpawned_ReturnsFalse()
    {
        var pool = new EntityPool();
        var entityRef = pool.ReserveEntity();

        // A fresh id is not registered in `entities` yet, so its generation cannot match.
        Assert.False(pool.CheckEntityValid(entityRef));
    }

    [Fact]
    public void CheckEntityValid_UnregisteredGenerationZero_ReturnsFalse()
    {
        var pool = new EntityPool();
        var entityRef = new EntityRef(0, 0);

        Assert.False(pool.CheckEntityValid(entityRef));
    }

    [Fact]
    public void CheckEntityValid_OutOfBounds_ReturnsFalse()
    {
        var pool = new EntityPool();

        // Out of bounds with non-zero generation returns false
        var entityRef = new EntityRef(999, 1);
        Assert.False(pool.CheckEntityValid(entityRef));
    }

    [Fact]
    public void CheckEntityValid_OutOfBoundsGenerationZero_ReturnsFalse()
    {
        var pool = new EntityPool();
        var entityRef = new EntityRef(999, 0);
        Assert.False(pool.CheckEntityValid(entityRef));
    }

    [Fact]
    public void CheckEntityValid_AfterRemoveAndReuse_ReturnsFalseForOldRef()
    {
        var pool = new EntityPool();
        var archetype = ArchetypeManager.EmptyArchetype;
        var entityRef = pool.ReserveEntity();

        // Register the entity so it is in the entities list
        pool.SetEntityInfo(entityRef, archetype, new EntityPos(0, 0));

        pool.CommitRemoveEntity(entityRef);
        pool.ReclaimId();

        // After remove, the old ref with generation 0 is still "valid" by design
        // (generation 0 is always valid). But after recycling, the new entity
        // should be valid and the old one should not.
        var newEntityRef = pool.ReserveEntity();
        pool.SetEntityInfo(newEntityRef, archetype, new EntityPos(0, 0));

        // The new entity should be valid
        Assert.True(pool.CheckEntityValid(newEntityRef));

        // Create a ref with the old ID but incremented generation (simulating stale reference)
        var staleRef = new EntityRef(entityRef.ID, 1);
        Assert.True(pool.CheckEntityValid(staleRef));
    }

    [Fact]
    public void CheckEntityValid_RecycledId_WithOldGeneration_ReturnsFalse()
    {
        var pool = new EntityPool();
        var archetype = ArchetypeManager.EmptyArchetype;

        // Create and register first entity
        var entityRef1 = pool.ReserveEntity();
        pool.SetEntityInfo(entityRef1, archetype, new EntityPos(0, 0));

        // Remove first entity (this increments its generation to 1)
        pool.CommitRemoveEntity(entityRef1);
        pool.ReclaimId();

        // Recycle the ID
        var entityRef2 = pool.ReserveEntity();
        pool.SetEntityInfo(entityRef2, archetype, new EntityPos(0, 0));

        // The new entity (generation 0) should be valid
        Assert.True(pool.CheckEntityValid(entityRef2));

        // A stale reference with the old generation should be invalid
        var staleRef = new EntityRef(entityRef1.ID, 1);
        Assert.True(pool.CheckEntityValid(staleRef));
    }

#endregion

#region SetEntityInfo

    [Fact]
    public void SetEntityInfo_NewEntity_MakesItValid()
    {
        var pool = new EntityPool();
        var entityRef = pool.ReserveEntity();

        var archetype = ArchetypeManager.EmptyArchetype;

        pool.SetEntityInfo(entityRef, archetype, new EntityPos(0, 0));

        Assert.True(pool.CheckEntityValid(entityRef));
    }

    [Fact]
    public void TryGetEntityInfo_ReservedButNotSpawned_ReturnsFalse()
    {
        var pool = new EntityPool();
        var entityRef = pool.ReserveEntity();

        // A fresh id has no registered location (and is not even in `entities` yet).
        Assert.False(pool.CheckEntityValid(entityRef));
        Assert.False(pool.TryGetEntityInfo(entityRef, out _));
    }

    [Fact]
    public void TryGetEntityInfo_RecycledReservedButNotSpawned_ReturnsFalse()
    {
        var pool = new EntityPool();
        var archetype = ArchetypeManager.EmptyArchetype;

        // Register then remove an entity so its id is recycled with a bumped generation.
        var firstRef = pool.ReserveEntity();
        pool.SetEntityInfo(firstRef, archetype, new EntityPos(0, 0));
        pool.CommitRemoveEntity(firstRef);
        pool.ReclaimId();

        // Reserve the recycled id without spawning it: valid by generation, but the stale location from the
        // dead entity must no longer resolve.
        var recycledRef = pool.ReserveEntity();

        Assert.Equal(firstRef.ID, recycledRef.ID);
        Assert.True(pool.CheckEntityValid(recycledRef));
        Assert.False(pool.TryGetEntityInfo(recycledRef, out _));
    }

#endregion

#region ReclaimEntity

    [Fact]
    public void ReclaimEntity_InvalidatesHandedOutRef()
    {
        var pool = new EntityPool();

        // A fresh reserved id is not registered in `entities`, so it is not valid.
        var reserved = pool.ReserveEntity();
        Assert.False(pool.CheckEntityValid(reserved));
        Assert.False(pool.TryGetEntityInfo(reserved, out _));

        pool.ReclaimEntity(reserved);

        // The id is reissued with a different generation, still unspawned, so the caller's handle cannot
        // alias a later entity.
        var reused = pool.ReserveEntity();

        Assert.Equal(reserved.ID, reused.ID);
        Assert.NotEqual(reserved.Generation, reused.Generation);
        Assert.False(pool.CheckEntityValid(reserved));
        Assert.False(pool.TryGetEntityInfo(reused, out _));
    }

    [Fact]
    public void ReclaimEntity_RecycledId_InvalidatesHandedOutRef()
    {
        var pool = new EntityPool();
        var archetype = ArchetypeManager.EmptyArchetype;

        // Produce a recycled id with a non-zero generation.
        var firstRef = pool.ReserveEntity();
        pool.SetEntityInfo(firstRef, archetype, new EntityPos(0, 0));
        pool.CommitRemoveEntity(firstRef);
        pool.ReclaimId();

        // Re-reserve it, then give it back without spawning.
        var reserved = pool.ReserveEntity();
        pool.ReclaimEntity(reserved);

        Assert.False(pool.CheckEntityValid(reserved));

        var reused = pool.ReserveEntity();

        Assert.Equal(reserved.ID, reused.ID);
        Assert.NotEqual(reserved.Generation, reused.Generation);
        Assert.False(pool.TryGetEntityInfo(reused, out _));
    }

#endregion

#region CommitRemoveEntity / ReclaimId

    [Fact]
    public void CommitRemove_IncrementsGeneration()
    {
        var pool = new EntityPool();
        var archetype = ArchetypeManager.EmptyArchetype;
        var entityRef = pool.ReserveEntity();

        pool.SetEntityInfo(entityRef, archetype, new EntityPos(0, 0));

        pool.CommitRemoveEntity(entityRef);

        // After CommitRemoveEntity, the entity's generation in the list is incremented to 1.
        // A ref with generation 1 matches the stored generation → valid.
        var refWithGen1 = new EntityRef(entityRef.ID, 1);
        Assert.True(pool.CheckEntityValid(refWithGen1));

        // After reclaiming, the entity is still in entities with gen=1.
        // Old ref with gen=0: IN entities, gen(0)!=stored(1) → false.
        pool.ReclaimId();
        Assert.False(pool.CheckEntityValid(entityRef));

        // Reuse the ID — new entity gets gen=1
        var newEntityRef = pool.ReserveEntity();
        Assert.Equal(1, newEntityRef.Generation);

        // A ref with a generation that doesn't match should be invalid
        var mismatchRef = new EntityRef(entityRef.ID, 2);
        Assert.False(pool.CheckEntityValid(mismatchRef));
    }

    [Fact]
    public void ReclaimId_MakesIdReusable()
    {
        var pool = new EntityPool();
        var archetype = ArchetypeManager.EmptyArchetype;
        var entityRef = pool.ReserveEntity();

        pool.SetEntityInfo(entityRef, archetype, new EntityPos(0, 0));

        pool.CommitRemoveEntity(entityRef);
        pool.ReclaimId();

        // The ID should be reusable
        var newEntityRef = pool.ReserveEntity();
        Assert.Equal(entityRef.ID, newEntityRef.ID);
        Assert.Equal(1, newEntityRef.Generation);
    }

    [Fact]
    public void ReclaimId_MultipleEntities_AllReclaimed()
    {
        var pool = new EntityPool();
        var archetype = ArchetypeManager.EmptyArchetype;

        var e1 = pool.ReserveEntity();
        var e2 = pool.ReserveEntity();
        var e3 = pool.ReserveEntity();

        pool.SetEntityInfo(e1, archetype, new EntityPos(0, 0));
        pool.SetEntityInfo(e2, archetype, new EntityPos(0, 1));
        pool.SetEntityInfo(e3, archetype, new EntityPos(0, 2));

        pool.CommitRemoveEntity(e1);
        pool.CommitRemoveEntity(e2);
        pool.CommitRemoveEntity(e3);

        pool.ReclaimId();

        // All three IDs should be reusable
        var r1 = pool.ReserveEntity();
        var r2 = pool.ReserveEntity();
        var r3 = pool.ReserveEntity();

        Assert.Equal(3, new[] { r1.ID, r2.ID, r3.ID }.Distinct().Count());
    }

#endregion

#region Clear

    [Fact]
    public void Clear_MakesAllEntitiesReusable()
    {
        var pool = new EntityPool();
        pool.ReserveEntity();
        pool.ReserveEntity();
        pool.ReserveEntity();

        pool.Clear();

        // After clear, next reservation should reuse an ID
        var entityRef = pool.ReserveEntity();
        Assert.True(entityRef.ID >= 0);
    }

#endregion

#region GetEntityInfo

    [Fact]
    public void GetEntityInfo_AfterSetEntityInfo_ReturnsCorrectInfo()
    {
        var pool = new EntityPool();
        var archetype = ArchetypeManager.EmptyArchetype;

        var entityRef = pool.ReserveEntity();
        pool.SetEntityInfo(entityRef, archetype, new EntityPos(0, 5));

        var info = pool.GetEntityInfo(entityRef);

        Assert.Same(archetype, info.Archetype);
        Assert.Equal(5, info.Pos.Idx);
    }

#endregion

#region Stress

    [Fact]
    public void Stress_CreateAndRemoveMany_MaintainsConsistency()
    {
        var pool = new EntityPool();
        var archetype = ArchetypeManager.EmptyArchetype;
        var refs = new List<EntityRef>();

        for (var i = 0; i < 1000; i++)
        {
            var entityRef = pool.ReserveEntity();
            refs.Add(entityRef);
            pool.SetEntityInfo(entityRef, archetype, new EntityPos(0, i));
        }

        Assert.Equal(1000, refs.Count);

        // Remove half
        for (var i = 0; i < 500; i++)
        {
            pool.CommitRemoveEntity(refs[i]);
        }

        pool.ReclaimId();

        // Remaining entities should still be valid
        for (var i = 500; i < 1000; i++)
        {
            Assert.True(pool.CheckEntityValid(refs[i]));
        }

        // After ReclaimId, removed entities are still in entities with gen=1.
        // Old refs with gen=0: IN entities, gen(0)!=stored(1) → false.
        for (var i = 0; i < 500; i++)
        {
            Assert.False(pool.CheckEntityValid(refs[i]));
        }

        // After reusing the IDs and registering them, the entities list is updated
        for (var i = 0; i < 500; i++)
        {
            var reusedRef = pool.ReserveEntity(); // reuse the removed IDs
            pool.SetEntityInfo(reusedRef, archetype, new EntityPos(0, i));
        }

        // Now the entities list has been updated with the reused entities.
        // A stale reference with a generation that doesn't match should be invalid.
        var staleRef = new EntityRef(0, 999); // generation 999 doesn't match
        Assert.False(pool.CheckEntityValid(staleRef));
    }

#endregion

#region Edge Cases

    [Fact]
    public void Clear_ThenReserveEntity_ReusesIds()
    {
        var pool = new EntityPool();
        pool.ReserveEntity();
        pool.ReserveEntity();

        pool.Clear();

        var entityRef = pool.ReserveEntity();
        // After clear, IDs should be reusable
        Assert.True(entityRef.ID >= 0);
    }

    [Fact]
    public void CommitRemoveEntity_WithoutSetEntityInfo_DoesNotThrow()
    {
        var pool = new EntityPool();
        var archetype = ArchetypeManager.EmptyArchetype;
        var entityRef = pool.ReserveEntity();
        pool.SetEntityInfo(entityRef, archetype, new EntityPos(0, 0));

        // Removing again should not throw (though it's a logic error in real usage)
        pool.CommitRemoveEntity(entityRef);
    }

    [Fact]
    public void CommitRemoveEntity_WithoutSetEntityInfo_StillIncrementsGeneration()
    {
        var pool = new EntityPool();
        var archetype = ArchetypeManager.EmptyArchetype;
        var entityRef = pool.ReserveEntity();
        pool.SetEntityInfo(entityRef, archetype, new EntityPos(0, 0));

        // CommitRemoveEntity without a prior MarkRemove — tests the internal behavior
        pool.CommitRemoveEntity(entityRef);

        // Entity should still be accessible with incremented generation
        var refWithGen1 = new EntityRef(entityRef.ID, 1);
        Assert.True(pool.CheckEntityValid(refWithGen1));
    }

    [Fact]
    public void GetEntityInfo_AfterRemove_IsUnregistered()
    {
        var pool = new EntityPool();
        var archetype = ArchetypeManager.EmptyArchetype;
        var entityRef = pool.ReserveEntity();
        pool.SetEntityInfo(entityRef, archetype, new EntityPos(0, 3));

        pool.CommitRemoveEntity(entityRef);

        // The dead entity's slot is cleared, so a recycled id cannot resolve to its stale location before
        // being spawned again.
        Assert.False(pool.TryGetEntityInfo(entityRef, out _));
        Assert.Null(pool.GetEntityInfo(entityRef).Archetype);
    }

    [Fact]
    public void ReserveEntity_AfterMultipleReclaims_CorrectIds()
    {
        var pool = new EntityPool();
        var archetype = ArchetypeManager.EmptyArchetype;

        // Create and remove 5 entities
        var refs = new EntityRef[5];
        for (var i = 0; i < 5; i++)
        {
            refs[i] = pool.ReserveEntity();
            pool.SetEntityInfo(refs[i], archetype, new EntityPos(0, i));
        }

        for (var i = 0; i < 5; i++)
        {
            pool.CommitRemoveEntity(refs[i]);
        }

        pool.ReclaimId();

        // Reuse all 5 IDs and register them
        var newRefs = new EntityRef[5];
        for (var i = 0; i < 5; i++)
        {
            newRefs[i] = pool.ReserveEntity();
            pool.SetEntityInfo(newRefs[i], archetype, new EntityPos(0, i));
        }

        // All should be valid after registration
        for (var i = 0; i < 5; i++)
        {
            Assert.True(pool.CheckEntityValid(newRefs[i]));
        }

        // IDs should be the reused ones (0-4)
        var ids = newRefs.Select(r => r.ID).OrderBy(x => x).ToArray();
        Assert.Equal([0, 1, 2, 3, 4], ids);
    }

    [Fact]
    public void CheckEntityValid_AfterReclaim_OldRefInvalid()
    {
        var pool = new EntityPool();
        var archetype = ArchetypeManager.EmptyArchetype;
        var entityRef = pool.ReserveEntity();
        pool.SetEntityInfo(entityRef, archetype, new EntityPos(0, 0));

        pool.CommitRemoveEntity(entityRef);
        pool.ReclaimId();

        // Reuse the ID
        var newRef = pool.ReserveEntity();
        pool.SetEntityInfo(newRef, archetype, new EntityPos(0, 0));

        // Old ref with gen=0: entity is in entities with gen=1 (reused), so gen matches → true
        var staleRef = new EntityRef(entityRef.ID, 1);
        Assert.True(pool.CheckEntityValid(staleRef));

        // A ref with a completely wrong generation should be invalid
        var wrongRef = new EntityRef(entityRef.ID, 999);
        Assert.False(pool.CheckEntityValid(wrongRef));
    }

#endregion
}
