using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace lychee;

/// <summary>
/// Manages entity creation, removal, and reuse with generation tracking for safety.
/// </summary>
public sealed class EntityPool
{
    private int latestEntityId = -1;

    private readonly List<EntityRef> entities = [];

    private readonly List<EntityInfo> entityInfoList = [];

    private readonly Stack<int> reusableEntitiesId = [];

    private readonly ConcurrentStack<int> removedEntitiesId = [];

    /// <summary>
    /// Reserves an entity ID without initializing it. Call <see cref="CommitReservedEntity"/> to finalize.
    /// </summary>
    public EntityRef ReserveEntity()
    {
        if (reusableEntitiesId.TryPop(out var id))
        {
            return new(id, 0);
        }

        return new(Interlocked.Increment(ref latestEntityId), 0);
    }

    /// <summary>
    /// Marks an entity for removal. The actual removal happens on commit.
    /// </summary>
    public void MarkRemoveEntity(EntityRef entityRef)
    {
        removedEntitiesId.Push(entityRef.ID);
    }

    /// <summary>
    /// Immediately removes an entity, incrementing its generation to invalidate existing references.
    /// </summary>
    /// <returns><c>true</c> if the entity was valid and removed; <c>false</c> if the entity reference was stale.</returns>
    public bool RemoveEntity(EntityRef entityRef)
    {
        var id = entityRef.ID;
        Debug.Assert((uint)id < (uint)entities.Count);

        if (entityRef.Generation != entities[id].Generation)
        {
            return false;
        }

        var span = CollectionsMarshal.AsSpan(entities);
        span[id].Generation++;

        removedEntitiesId.Push(id);

        return true;
    }

    /// <summary>
    /// Verifies whether an entity reference is still valid (generation matches).
    /// </summary>
    public bool CheckEntityValid(EntityRef entityRef)
    {
        if (entityRef.Generation == 0)
        {
            return true;
        }

        if ((uint)entityRef.ID >= (uint)entities.Count)
        {
            return false;
        }

        return entities[entityRef.ID].Generation == entityRef.Generation;
    }

    /// <summary>
    /// Retrieves the entity's location metadata (archetype, chunk, and index).
    /// </summary>
    public EntityInfo GetEntityInfo(EntityRef entityRef)
    {
        Debug.Assert((uint)entityRef.ID < (uint)entityInfoList.Count);

        return entityInfoList[entityRef.ID];
    }

#region Internal methods

    internal void CommitRemoveEntity(EntityRef entityRef)
    {
        var id = entityRef.ID;
        Debug.Assert((uint)id < (uint)entities.Count);

        if (entityRef.Generation == entities[id].Generation)
        {
            entityRef.Generation++;
            entities[id] = entityRef;
        }
    }

    internal void Commit()
    {
        while (removedEntitiesId.TryPop(out var id))
        {
            reusableEntitiesId.Push(id);
        }
    }

    internal void CommitReservedEntity(int id, Archetype archetype, int chunkIdx, int idx)
    {
        Debug.Assert(id >= 0);

        if (id < entities.Count)
        {
            // Set generation to 0 when reuse entity
            entities[id] = new(id, 0);
            entityInfoList[id] = new(archetype, new(chunkIdx, idx));
        }
        else
        {
            if (id == entities.Count)
            {
                entities.Add(new(id, 0));
                entityInfoList.Add(new(archetype, new(chunkIdx, idx)));
            }
            else
            {
                CollectionsMarshal.SetCount(entities, id + 1);
                // entities.Resize(id + 1);
                entities[id] = new(id, 0);

                CollectionsMarshal.SetCount(entityInfoList, id + 1);

                // entityInfoList.Resize(id + 1);
                entityInfoList[id] = new(archetype, new(chunkIdx, idx));
            }
        }
    }

#endregion
}
