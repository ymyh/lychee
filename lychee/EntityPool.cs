using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace lychee;

/// <summary>
/// Holds all entities and its info.
/// </summary>
public sealed class EntityPool
{
    private int latestEntityId;

    private readonly List<Entity> entities = [];

    private readonly List<EntityInfo> entityInfoList = [];

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
            info.ArchetypeIdx = 0;

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
    /// Need to call <see cref="CommitCreateEntity"/> to make the entity available.
    /// </summary>
    /// <returns>New entity id</returns>
    public int ReserveEntity()
    {
        return reusableEntitiesId.TryPop(out var id) ? id : Interlocked.Increment(ref latestEntityId);
    }

    /// <summary>
    /// Commit a reserved entity, make it available to be used.
    /// </summary>
    /// <param name="id"></param>
    public void CommitCreateEntity(int id)
    {
        Debug.Assert(id > 0);

        if (id < entities.Count)
        {
            entities[id] = new(id, entities[id].Generation + 1);
        }
        else
        {
            if (id == entities.Count)
            {
                entities.Add(new(id, 0));
            }
            else
            {
                entities.AddRange(Enumerable.Repeat(new Entity(0, 0), id - entities.Count));
                var entity = entities[id];
                entity.ID = id;

                entities[id] = entity;
            }
        }
    }

    /// <summary>
    /// Remove entity by id
    /// </summary>
    /// <param name="entity"></param>
    public bool RemoveEntity(Entity entity)
    {
        var id = entity.ID;
        Debug.Assert(id >= 0 && id < entities.Count);

        if (entity.Generation != entities[id].Generation)
        {
            return false;
        }

        var span = CollectionsMarshal.AsSpan(entities);
        span[id].Generation++;

        reusableEntitiesId.Push(id);

        return true;
    }

    /// <summary>
    /// Mark entity to be removed, need to call <see cref="CommitRemoveEntity"/> to make the entity removed.
    /// </summary>
    /// <param name="entity"></param>
    public void MarkRemoveEntity(Entity entity)
    {
        reusableEntitiesId.Push(entity.ID);
    }

    /// <summary>
    /// Commit a marked entity to be removed.
    /// </summary>
    /// <param name="entity"></param>
    /// <returns></returns>
    public bool CommitRemoveEntity(Entity entity)
    {
        var id = entity.ID;
        Debug.Assert(id >= 0 && id < entities.Count);

        if (entity.Generation != entities[id].Generation)
        {
            return false;
        }

        entity.Generation++;
        entities[id] = entity;

        return true;
    }

    /// <summary>
    /// Get entity info by entity
    /// </summary>
    /// <param name="entity"></param>
    /// <param name="info"></param>
    /// <returns></returns>
    public bool GetEntityInfo(Entity entity, out EntityInfo info)
    {
        var e = entities[entity.ID];

        if (e.Generation == entity.Generation)
        {
            info = entityInfoList[e.ID];
            return true;
        }

        info = default;
        return false;
    }

    /// <summary>
    /// Set entity info with given entity
    /// </summary>
    /// <param name="entity"></param>
    /// <param name="info"></param>
    /// <returns></returns>
    public bool SetEntityInfo(Entity entity, EntityInfo info)
    {
        if (entities[entity.ID].Generation == entity.Generation)
        {
            entityInfoList[entity.ID] = info;
            return true;
        }

        return false;
    }
}
