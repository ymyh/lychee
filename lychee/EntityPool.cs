using System.Collections.Concurrent;
using System.Diagnostics;
using lychee.extensions;

namespace lychee;

/// <summary>
/// Manages entity creation, removal, and reuse with generation tracking for safety.
/// </summary>
public sealed class EntityPool
{
    private int latestEntityId = -1;

    private readonly List<EntityRef> entities = [];

    private readonly List<EntityInfo> entityInfoList = [];

    private readonly Stack<EntityRef> reusableEntitiesId = [];

    private readonly ConcurrentStack<EntityRef> removedEntitiesId = [];

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

    internal void MarkRemoveEntity(EntityRef entityRef)
    {
        removedEntitiesId.Push(entityRef);
    }

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

    internal void ReclaimId()
    {
        while (removedEntitiesId.TryPop(out var id))
        {
            reusableEntitiesId.Push(id);
        }
    }

    internal void CommitReservedEntity(in Entity entity)
    {
        Debug.Assert(entity.ID >= 0);

        if (entity.ID < entities.Count)
        {
            // Set generation to 0 when reuse entity
            entities[entity.ID] = entity.Ref;
            entityInfoList[entity.ID] = new(entity.Archetype, new(entity.Pos.ChunkIdx, entity.Pos.Idx));
        }
        else
        {
            if (entity.ID == entities.Count)
            {
                entities.Add(entity.Ref);
                entityInfoList.Add(new(entity.Archetype, new(entity.Pos.ChunkIdx, entity.Pos.Idx)));
            }
            else
            {
                entities.Resize(entity.ID + 1, default);
                entities[entity.ID] = entity.Ref;

                entityInfoList.Resize(entity.ID + 1, default);
                entityInfoList[entity.ID] = new(entity.Archetype, new(entity.Pos.ChunkIdx, entity.Pos.Idx));
            }
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
