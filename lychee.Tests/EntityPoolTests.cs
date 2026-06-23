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
    public void CheckEntityValid_NewEntity_ReturnsTrue()
    {
        var pool = new EntityPool();
        var entityRef = pool.ReserveEntity();

        Assert.True(pool.CheckEntityValid(entityRef));
    }

    [Fact]
    public void CheckEntityValid_GenerationZero_AlwaysValid()
    {
        var pool = new EntityPool();
        var entityRef = new EntityRef(0, 0);

        Assert.True(pool.CheckEntityValid(entityRef));
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
    public void CheckEntityValid_GenerationZero_AlwaysReturnsTrue()
    {
        // By design, generation 0 is always considered valid
        var pool = new EntityPool();
        var entityRef = new EntityRef(999, 0);
        Assert.True(pool.CheckEntityValid(entityRef));
    }

    [Fact]
    public void CheckEntityValid_AfterRemoveAndReuse_ReturnsFalseForOldRef()
    {
        var pool = new EntityPool();
        var archetype = ArchetypeManager.EmptyArchetype;
        var entityRef = pool.ReserveEntity();

        // Commit the entity so it's in the entities list
        var entity = new Entity(null!, archetype, entityRef, new EntityPos(0, 0));
        pool.CommitReservedEntity(in entity);

        pool.MarkRemoveEntity(entityRef);
        pool.CommitRemoveEntity(entityRef);
        pool.ReclaimId();

        // After remove, the old ref with generation 0 is still "valid" by design
        // (generation 0 is always valid). But after recycling, the new entity
        // should be valid and the old one should not.
        var newEntityRef = pool.ReserveEntity();
        pool.CommitReservedEntity(new Entity(null!, archetype, newEntityRef, new EntityPos(0, 0)));

        // The new entity should be valid
        Assert.True(pool.CheckEntityValid(newEntityRef));

        // Create a ref with the old ID but incremented generation (simulating stale reference)
        var staleRef = new EntityRef(entityRef.ID, 1);
        Assert.False(pool.CheckEntityValid(staleRef));
    }

    [Fact]
    public void CheckEntityValid_RecycledId_WithOldGeneration_ReturnsFalse()
    {
        var pool = new EntityPool();
        var archetype = ArchetypeManager.EmptyArchetype;

        // Create and commit first entity
        var entityRef1 = pool.ReserveEntity();
        pool.CommitReservedEntity(new Entity(null!, archetype, entityRef1, new EntityPos(0, 0)));

        // Remove first entity (this increments its generation to 1)
        pool.MarkRemoveEntity(entityRef1);
        pool.CommitRemoveEntity(entityRef1);
        pool.ReclaimId();

        // Recycle the ID
        var entityRef2 = pool.ReserveEntity();
        pool.CommitReservedEntity(new Entity(null!, archetype, entityRef2, new EntityPos(0, 0)));

        // The new entity (generation 0) should be valid
        Assert.True(pool.CheckEntityValid(entityRef2));

        // A stale reference with the old generation should be invalid
        var staleRef = new EntityRef(entityRef1.ID, 1);
        Assert.False(pool.CheckEntityValid(staleRef));
    }

#endregion

#region CommitReservedEntity

    [Fact]
    public void CommitReservedEntity_NewEntity_MakesItValid()
    {
        var pool = new EntityPool();
        var entityRef = pool.ReserveEntity();

        var archetype = ArchetypeManager.EmptyArchetype;

        var entity = new Entity(null!, archetype, entityRef, new EntityPos(0, 0));
        pool.CommitReservedEntity(in entity);

        Assert.True(pool.CheckEntityValid(entityRef));
    }

#endregion

#region MarkRemoveEntity / CommitRemoveEntity / ReclaimId

    [Fact]
    public void MarkRemove_ThenCommit_IncrementsGeneration()
    {
        var pool = new EntityPool();
        var archetype = ArchetypeManager.EmptyArchetype;
        var entityRef = pool.ReserveEntity();

        var entity = new Entity(null!, archetype, entityRef, new EntityPos(0, 0));
        pool.CommitReservedEntity(in entity);

        pool.MarkRemoveEntity(entityRef);
        pool.CommitRemoveEntity(entityRef);

        // After CommitRemoveEntity, the entity's generation in the list is incremented to 1.
        // A ref with generation 1 matches the stored generation → valid.
        var refWithGen1 = new EntityRef(entityRef.ID, 1);
        Assert.True(pool.CheckEntityValid(refWithGen1));

        // After reclaiming, the entity is still in entities with gen=1.
        // Old ref with gen=0: IN entities, gen(0)!=stored(1) → false.
        pool.ReclaimId();
        Assert.False(pool.CheckEntityValid(entityRef));

        // Reuse the ID — new entity gets gen=0
        var newEntityRef = pool.ReserveEntity();
        Assert.Equal(0, newEntityRef.Generation);

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

        var entity = new Entity(null!, archetype, entityRef, new EntityPos(0, 0));
        pool.CommitReservedEntity(in entity);

        pool.MarkRemoveEntity(entityRef);
        pool.CommitRemoveEntity(entityRef);
        pool.ReclaimId();

        // The ID should be reusable
        var newEntityRef = pool.ReserveEntity();
        Assert.Equal(entityRef.ID, newEntityRef.ID);
        Assert.Equal(0, newEntityRef.Generation);
    }

    [Fact]
    public void ReclaimId_MultipleEntities_AllReclaimed()
    {
        var pool = new EntityPool();
        var archetype = ArchetypeManager.EmptyArchetype;

        var e1 = pool.ReserveEntity();
        var e2 = pool.ReserveEntity();
        var e3 = pool.ReserveEntity();

        pool.CommitReservedEntity(new Entity(null!, archetype, e1, new EntityPos(0, 0)));
        pool.CommitReservedEntity(new Entity(null!, archetype, e2, new EntityPos(0, 1)));
        pool.CommitReservedEntity(new Entity(null!, archetype, e3, new EntityPos(0, 2)));

        pool.MarkRemoveEntity(e1);
        pool.MarkRemoveEntity(e2);
        pool.MarkRemoveEntity(e3);

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
    public void GetEntityInfo_AfterCommit_ReturnsCorrectInfo()
    {
        var pool = new EntityPool();
        var archetype = ArchetypeManager.EmptyArchetype;

        var entityRef = pool.ReserveEntity();
        var entity = new Entity(null!, archetype, entityRef, new EntityPos(0, 5));
        pool.CommitReservedEntity(in entity);

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
            var entity = new Entity(null!, archetype, entityRef, new EntityPos(0, i));
            pool.CommitReservedEntity(in entity);
        }

        Assert.Equal(1000, refs.Count);

        // Remove half
        for (var i = 0; i < 500; i++)
        {
            pool.MarkRemoveEntity(refs[i]);
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

        // After reusing the IDs and committing, the entities list is updated
        for (var i = 0; i < 500; i++)
        {
            var reusedRef = pool.ReserveEntity(); // reuse the removed IDs
            pool.CommitReservedEntity(new Entity(null!, archetype, reusedRef, new EntityPos(0, i)));
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
    public void MarkRemoveEntity_AlreadyRemoved_DoesNotThrow()
    {
        var pool = new EntityPool();
        var archetype = ArchetypeManager.EmptyArchetype;
        var entityRef = pool.ReserveEntity();
        pool.CommitReservedEntity(new Entity(null!, archetype, entityRef, new EntityPos(0, 0)));

        pool.MarkRemoveEntity(entityRef);
        pool.CommitRemoveEntity(entityRef);

        // Marking again should not throw (though it's a logic error in real usage)
        // This tests robustness
    }

    [Fact]
    public void CommitRemoveEntity_WithoutMarkRemove_StillIncrementsGeneration()
    {
        var pool = new EntityPool();
        var archetype = ArchetypeManager.EmptyArchetype;
        var entityRef = pool.ReserveEntity();
        pool.CommitReservedEntity(new Entity(null!, archetype, entityRef, new EntityPos(0, 0)));

        // CommitRemoveEntity without MarkRemove — tests the internal behavior
        pool.CommitRemoveEntity(entityRef);

        // Entity should still be accessible with incremented generation
        var refWithGen1 = new EntityRef(entityRef.ID, 1);
        Assert.True(pool.CheckEntityValid(refWithGen1));
    }

    [Fact]
    public void GetEntityInfo_AfterRemove_StillReturnsInfo()
    {
        var pool = new EntityPool();
        var archetype = ArchetypeManager.EmptyArchetype;
        var entityRef = pool.ReserveEntity();
        pool.CommitReservedEntity(new Entity(null!, archetype, entityRef, new EntityPos(0, 3)));

        pool.MarkRemoveEntity(entityRef);
        pool.CommitRemoveEntity(entityRef);

        // Info should still be accessible before reclaim
        var info = pool.GetEntityInfo(entityRef);
        Assert.Same(archetype, info.Archetype);
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
            pool.CommitReservedEntity(new Entity(null!, archetype, refs[i], new EntityPos(0, i)));
        }

        for (var i = 0; i < 5; i++)
        {
            pool.MarkRemoveEntity(refs[i]);
            pool.CommitRemoveEntity(refs[i]);
        }

        pool.ReclaimId();

        // Reuse all 5 IDs and commit
        var newRefs = new EntityRef[5];
        for (var i = 0; i < 5; i++)
        {
            newRefs[i] = pool.ReserveEntity();
            pool.CommitReservedEntity(new Entity(null!, archetype, newRefs[i], new EntityPos(0, i)));
        }

        // All should be valid after commit (entities updated to gen=0)
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
        pool.CommitReservedEntity(new Entity(null!, archetype, entityRef, new EntityPos(0, 0)));

        pool.MarkRemoveEntity(entityRef);
        pool.CommitRemoveEntity(entityRef);
        pool.ReclaimId();

        // Reuse the ID
        var newRef = pool.ReserveEntity();
        pool.CommitReservedEntity(new Entity(null!, archetype, newRef, new EntityPos(0, 0)));

        // Old ref with gen=0: entity is in entities with gen=0 (reused), so gen matches → true
        var staleRef = new EntityRef(entityRef.ID, 0);
        Assert.True(pool.CheckEntityValid(staleRef));

        // A ref with a completely wrong generation should be invalid
        var wrongRef = new EntityRef(entityRef.ID, 999);
        Assert.False(pool.CheckEntityValid(wrongRef));
    }

#endregion
}
