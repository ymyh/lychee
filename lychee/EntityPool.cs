using System.Collections.Concurrent;
using System.Diagnostics;
using lychee.collections;

namespace lychee;

/// <summary>
/// Manages entity creation, removal, and reuse with generation tracking for safety.
/// </summary>
public sealed class EntityPool : IDisposable
{
    private int latestEntityId = -1;

    private readonly NativeList<Entity> entities = [];

    private readonly NativeList<EntityInfo> entityInfoList = [];

    private readonly Stack<int> reusableEntitiesId = [];

    private readonly ConcurrentStack<int> removedEntitiesId = [];

    /// <summary>
    /// Creates a new entity or reuses a previously removed entity ID.
    /// </summary>
    public Entity CreateEntity()
    {
        if (reusableEntitiesId.TryPop(out var id))
        {
            var info = entityInfoList[id];

            // a new entity always belongs to default archetype, thus archetypeIdx also meaningless
            info.ArchetypeId = 0;

            entityInfoList[id] = info;

            return entities[id];
        }

        id = Interlocked.Increment(ref latestEntityId);

        entities.Add(new(id, 0));
        entityInfoList.Add(new());

        return entities[^1];
    }

    /// <summary>
    /// Reserves an entity ID without initializing it. Call <see cref="CommitReservedEntity"/> to finalize.
    /// </summary>
    public Entity ReserveEntity()
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
    public void MarkRemoveEntity(Entity entity)
    {
        removedEntitiesId.Push(entity.ID);
    }

    /// <summary>
    /// Immediately removes an entity, incrementing its generation to invalidate existing references.
    /// </summary>
    /// <returns><c>true</c> if the entity was valid and removed; <c>false</c> if the entity reference was stale.</returns>
    public bool RemoveEntity(Entity entity)
    {
        var id = entity.ID;
        Debug.Assert((uint)id < (uint)entities.Count);

        if (entity.Generation != entities[id].Generation)
        {
            return false;
        }

        var span = entities.AsSpan();
        span[id].Generation++;

        removedEntitiesId.Push(id);

        return true;
    }

    /// <summary>
    /// Verifies whether an entity reference is still valid (generation matches).
    /// </summary>
    public bool CheckEntityValid(Entity entity)
    {
        if ((uint)entity.ID >= (uint)entities.Count)
        {
            return false;
        }

        return entities[entity.ID].Generation == entity.Generation;
    }

    /// <summary>
    /// Retrieves the entity's location metadata (archetype, chunk, and index).
    /// </summary>
    public EntityInfo GetEntityInfo(Entity entity)
    {
        Debug.Assert((uint)entity.ID < (uint)entityInfoList.Count);

        return entityInfoList[entity.ID];
    }

#region Internal methods

    internal void CommitRemoveEntity(Entity entity)
    {
        var id = entity.ID;
        Debug.Assert((uint)id < (uint)entities.Count);

        if (entity.Generation == entities[id].Generation)
        {
            entity.Generation++;
            entities[id] = entity;
        }
    }

    internal void Commit()
    {
        while (removedEntitiesId.TryPop(out var id))
        {
            reusableEntitiesId.Push(id);
        }
    }

    internal void CommitReservedEntity(int id, int archetypeId, int chunkIdx, int idx)
    {
        Debug.Assert(id >= 0);

        if (id < entities.Count)
        {
            // Set generation to 0 when reuse entity
            entities[id] = new(id, 0);
            entityInfoList[id] = new(archetypeId, chunkIdx, idx);
        }
        else
        {
            if (id == entities.Count)
            {
                entities.Add(new(id, 0));
                entityInfoList.Add(new(archetypeId, chunkIdx, idx));
            }
            else
            {
                entities.Resize(id + 1);
                entities.AsSpan()[id].ID = id;

                entityInfoList.Resize(id + 1);
                entityInfoList[id] = new(archetypeId, chunkIdx, idx);
            }
        }
    }

#endregion

#region IDisposable member

    public void Dispose()
    {
        entities.Dispose();
        entityInfoList.Dispose();
    }

#endregion
}
