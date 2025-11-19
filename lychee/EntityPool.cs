using System.Collections.Concurrent;
using System.Diagnostics;
using lychee.collections;

namespace lychee;

/// <summary>
/// Holds all entities and its info.
/// </summary>
public sealed class EntityPool : IDisposable
{
    private int latestEntityId = -1;

    private readonly NativeList<Entity> entities = [];

    private readonly NativeList<EntityInfo> entityInfoList = [];

    private readonly ConcurrentStack<int> reusableEntitiesId = new();

    /// <summary>
    /// Create a new Entity
    /// </summary>
    /// <returns>The entity just created</returns>
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
    /// Reserve an entity and return its id.<br/>
    /// Need to call <see cref="CommitReservedEntity"/> to make the entity available.
    /// </summary>
    /// <returns>New entity id</returns>
    public Entity ReserveEntity()
    {
        if (reusableEntitiesId.TryPop(out var id))
        {
            return new(id, 0);
        }

        return new(Interlocked.Increment(ref latestEntityId), 0);
    }

    /// <summary>
    /// Commit a reserved entity, make it available to be used.
    /// </summary>
    /// <param name="id"></param>
    /// <param name="archetypeId"></param>
    /// <param name="chunkIdx"></param>
    /// <param name="idx"></param>
    public Entity CommitReservedEntity(int id, int archetypeId, int chunkIdx, int idx)
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

        return entities[id];
    }

    public bool CheckEntityValid(Entity entity)
    {
        if ((uint)entity.ID >= (uint)entities.Count)
        {
            return false;
        }

        return entities[entity.ID].Generation == entity.Generation;
    }

    /// <summary>
    /// Remove entity by id
    /// </summary>
    /// <param name="entity"></param>
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

        reusableEntitiesId.Push(id);

        return true;
    }

    /// <summary>
    /// Mark entity to be removed, need to call <see cref="CommitRemoveEntity"/> to make the entity removed.
    /// </summary>
    /// <param name="entity">The entity to mark removed.</param>
    public void MarkRemoveEntity(Entity entity)
    {
        reusableEntitiesId.Push(entity.ID);
    }

    /// <summary>
    /// Commit a marked entity to be removed.
    /// </summary>
    /// <param name="entity">The entity to commit removed.</param>
    /// <returns></returns>
    public bool CommitRemoveEntity(Entity entity)
    {
        var id = entity.ID;
        Debug.Assert((uint)id < (uint)entities.Count);

        if (entity.Generation != entities[id].Generation)
        {
            return false;
        }

        entity.Generation++;
        entities[id] = entity;

        return true;
    }

    /// <summary>
    /// Get entity info by entity.
    /// </summary>
    /// <param name="entity">The target entity.</param>
    /// <returns>The entity info of the target entity.</returns>
    public EntityInfo GetEntityInfo(Entity entity)
    {
        Debug.Assert((uint)entity.ID < (uint)entityInfoList.Count);

        return entityInfoList[entity.ID];
    }

#region IDisposable member

    public void Dispose()
    {
        entities.Dispose();
        entityInfoList.Dispose();
    }

#endregion
}
