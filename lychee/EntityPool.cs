using System.Collections.Concurrent;
using System.Diagnostics;
using lychee.extensions;

namespace lychee;

/// <summary>
/// Manages entity creation, removal, and reuse with generation tracking for safety.
/// </summary>
public sealed class EntityPool
{
#region Private Fields

    private int latestEntityId = -1;

    private readonly List<EntityRef> entities = [];

    private readonly List<EntityInfo> entityInfoList = [];

    private readonly ConcurrentStack<EntityRef> reusableEntitiesId = [];

    private readonly ConcurrentQueue<EntityRef> removedEntitiesId = [];

#endregion

#region Public Methods

    /// <summary>
    /// Verifies whether the reference's generation still matches the entity stored at its id. It only checks
    /// that the id is in range and the generation is current; a recycled id that was reserved but not spawned
    /// still passes, so pair it with <see cref="TryGetEntityInfo"/> for a liveness test.
    /// </summary>
    public bool CheckEntityValid(EntityRef entityRef)
    {
        return (uint)entityRef.ID < (uint)entities.Count
            && entities[entityRef.ID].Generation == entityRef.Generation;
    }

    /// <summary>
    /// Retrieves the entity's location metadata (archetype, chunk, and index).
    /// </summary>
    public EntityInfo GetEntityInfo(EntityRef entityRef)
    {
        Debug.Assert((uint)entityRef.ID < (uint)entityInfoList.Count);

        return entityInfoList[entityRef.ID];
    }

#endregion

#region Internal Methods

    internal void Clear()
    {
        foreach (var entity in entities)
        {
            reusableEntitiesId.Push(entity);
        }

        entities.Clear();
        entityInfoList.Clear();
    }

    internal EntityRef ReserveEntity()
    {
        if (reusableEntitiesId.TryPop(out var entityRef))
        {
            return entityRef;
        }

        return new(Interlocked.Increment(ref latestEntityId), 0);
    }

    /// <summary>
    /// Finalizes a reserved entity that was never spawned and returns its id to the reuse pool. Used when a
    /// <c>Copy</c> command is skipped because its source vanished before it applied, so the copy does not
    /// leak its id.
    /// </summary>
    /// <remarks>
    /// The generation is advanced and the id is written into <c>entities</c> before the reference goes back
    /// on the stack, so the handle the caller already holds stops matching. Without the bump a recycled id
    /// would be reissued with the same generation and the caller's handle would alias the new entity.
    /// </remarks>
    /// <param name="entityRef">The reserved entity to make available again.</param>
    internal void ReclaimEntity(EntityRef entityRef)
    {
        var id = entityRef.ID;
        var reclaimed = new EntityRef(id, entityRef.Generation + 1);

        if ((uint)id >= (uint)entities.Count)
        {
            entities.Resize(id + 1, default);
        }

        entities[id] = reclaimed;

        // Keep entityInfoList in lockstep with entities, then leave the slot cleared: the reclaimed id is
        // still unspawned, so TryGetEntityInfo must report it as unregistered.
        if ((uint)id >= (uint)entityInfoList.Count)
        {
            entityInfoList.Resize(id + 1, default);
        }

        entityInfoList[id] = default;

        reusableEntitiesId.Push(reclaimed);
    }

    /// <summary>
    /// Registers a reserved entity at a concrete location, making it resolvable through
    /// <see cref="GetEntityInfo"/>. Also used to update the location after an archetype migration.
    /// </summary>
    /// <param name="entityRef">The entity to register or move.</param>
    /// <param name="archetype">The archetype the entity now lives in.</param>
    /// <param name="pos">The position of the entity inside its archetype.</param>
    internal void SetEntityInfo(EntityRef entityRef, Archetype archetype, EntityPos pos)
    {
        var id = entityRef.ID;

        if ((uint)id < (uint)entities.Count)
        {
            entities[id] = entityRef;
            entityInfoList[id] = new(archetype, pos);

            return;
        }

        entities.Resize(id + 1, default);
        entities[id] = entityRef;

        entityInfoList.Resize(id + 1, default);
        entityInfoList[id] = new(archetype, pos);
    }

    /// <summary>
    /// Tries to read the location of an entity. Returns false for an id that was reserved but never
    /// spawned, which keeps the "reserved but not yet applied" state distinguishable from a live entity.
    /// </summary>
    /// <param name="entityRef">The entity to look up.</param>
    /// <param name="info">When this method returns, the location if found; otherwise, the default value.</param>
    /// <returns>True if a location is registered; otherwise, false.</returns>
    internal bool TryGetEntityInfo(EntityRef entityRef, out EntityInfo info)
    {
        if ((uint)entityRef.ID >= (uint)entityInfoList.Count)
        {
            info = default;

            return false;
        }

        info = entityInfoList[entityRef.ID];

        // A cleared slot (default) marks an id that was registered before but has not been re-registered
        // since. Every live entity belongs to an archetype, so a null archetype means "reserved, not spawned".
        return info.Archetype is not null;
    }

    /// <summary>
    /// Finalizes a removal: bumps the stored generation so stale references turn invalid and queues the
    /// id for reuse. Called during apply, after the removal hooks have run.
    /// </summary>
    /// <param name="entityRef">The entity being removed.</param>
    internal void CommitRemoveEntity(EntityRef entityRef)
    {
        var id = entityRef.ID;

        if ((uint)id >= (uint)entities.Count)
        {
            return;
        }

        if (entityRef.Generation == entities[id].Generation)
        {
            entityRef.Generation++;
            entities[id] = entityRef;

            // Drop the dead entity's location. Until the id is reserved and re-registered through
            // SetEntityInfo, TryGetEntityInfo reports it as not spawned instead of handing back stale
            // data that may now belong to another entity. Removal hooks have already run by this point.
            entityInfoList[id] = default;

            removedEntitiesId.Enqueue(entityRef);
        }
    }

    internal void ReclaimId()
    {
        while (removedEntitiesId.TryDequeue(out var entityRef))
        {
            reusableEntitiesId.Push(entityRef);
        }
    }

    internal void UpdateEntityInfo(int archetypeId, int id, int indexInChunk)
    {
        var info = entityInfoList[id];
        info.Pos.Idx = info.Archetype.ID == archetypeId ? indexInChunk : info.Pos.Idx;
        entityInfoList[id] = info;
    }

#endregion
}
