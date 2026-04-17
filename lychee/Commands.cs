using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using lychee.collections;
using lychee.interfaces;

namespace lychee;

using TransferInfoMap = SparseMap<Dictionary<nint, EntityTransferInfo>>;

internal sealed class EntityTransferInfo(Archetype archetype, (TypeInfo info, int typeId)[] bundleInfo)
{
    public readonly Archetype Archetype = archetype;

    public readonly int[] TypeIndices = bundleInfo.Select(x => archetype.GetTypeIndex(x.typeId)).ToArray();

    public readonly (TypeInfo info, int typeId)[] BundleInfo = bundleInfo;
}

/// <summary>
/// Provides deferred entity modification operations for ECS systems.
/// Changes are buffered and applied atomically when Commit is called.
/// This ensures safe concurrent access to entity data during system execution.
/// </summary>
public sealed class Commands(App app)
{
#region Fields

    private readonly EntityPool entityPool = app.World.EntityPool;

    internal readonly ArchetypeManager ArchetypeManager = app.World.ArchetypeManager;

    internal readonly TypeRegistrar TypeRegistrar = app.TypeRegistrar;

    internal readonly TransferInfoMap ArchetypeAddingTypeMap = [];

    internal readonly TransferInfoMap ArchetypeRemovingTypeMap = [];

    private readonly SparseMap<Entity> modifiedEntityInfoMap = [];

    private readonly SparseMap<Entity> removedEntityMap = [];

    internal EntityTransferInfo? TransferDstInfo;

#endregion

#region Public Methods

    /// <summary>
    /// Creates a new entity in an uncommitted state.
    /// The entity will be fully registered when Commit is called.
    /// </summary>
    /// <returns>The newly created entity.</returns>
    public Entity CreateEntity()
    {
        var entityRef = entityPool.ReserveEntity();
        var entity = new Entity(this, ArchetypeManager.EmptyArchetype, entityRef, new());

        modifiedEntityInfoMap[entityRef.ID] = entity;

        return entity;
    }

    /// <summary>
    /// Removes an existing entity. Does nothing if the entity is already removed or in uncommitted state.
    /// </summary>
    /// <param name="entityRef">The entity to remove.</param>
    public void RemoveEntity(EntityRef entityRef)
    {
        if (modifiedEntityInfoMap.TryGetValue(entityRef.ID, out var entity))
        {
        }
        else if (!GetEntityByRef(entityRef, out entity))
        {
            return;
        }

        entity.Archetype.MarkRemove(entity.ID, entity.Pos);
        modifiedEntityInfoMap.Remove(entity.ID);
        entityPool.MarkRemoveEntity(entity.Ref);
        removedEntityMap[entity.ID] = entity;
    }

    /// <summary>
    /// Removes an existing entity.
    /// Does nothing if the entity is already removed or in uncommitted state.
    /// </summary>
    /// <param name="entity">The entity to remove.</param>
    public void RemoveEntity(in Entity entity)
    {
        var e = entity;
        if (removedEntityMap.ContainsKey(entity.ID))
        {
            return;
        }

        if (modifiedEntityInfoMap.TryGetValue(entity.ID, out var modifiedEntity))
        {
            e = modifiedEntity;
        }

        e.Archetype.MarkRemove(e.ID, e.Pos);
        modifiedEntityInfoMap.Remove(e.ID);
        entityPool.MarkRemoveEntity(e.Ref);
        removedEntityMap[e.ID] = e;
    }

    /// <summary>
    /// Gets an entity by its reference.
    /// Returns uncommitted modifications if any exist.
    /// </summary>
    /// <param name="entityRef">The entity reference to look up.</param>
    /// <param name="entity">When this method returns, contains the entity if found; otherwise, the default value.</param>
    /// <returns>True if the entity was found; false if the entity is invalid or removed.</returns>
    public bool GetEntityByRef(EntityRef entityRef, out Entity entity)
    {
        if (removedEntityMap.ContainsKey(entityRef.ID))
        {
            entity = default;
            return false;
        }

        if (modifiedEntityInfoMap.ContainsKey(entityRef.ID))
        {
            entity = modifiedEntityInfoMap[entityRef.ID];
            return true;
        }

        if (!entityPool.CheckEntityValid(entityRef))
        {
            entity = default;
            return false;
        }

        var info = entityPool.GetEntityInfo(entityRef);
        entity = new(this, info.Archetype, entityRef, info.Pos);

        return true;
    }

    /// <summary>
    /// Adds a component to an entity. The entity will be moved to a new archetype.
    /// </summary>
    /// <param name="entity">The target entity.</param>
    /// <param name="component">The component value to add.</param>
    /// <typeparam name="T">The component type, must be unmanaged and implement IComponent.</typeparam>
    /// <returns>True if the component was added; false if the entity is invalid or removed.</returns>
    public bool AddComponent<T>(ref Entity entity, in T component) where T : unmanaged, IComponent
    {
        if (removedEntityMap.ContainsKey(entity.ID) || !entityPool.CheckEntityValid(entity.Ref))
        {
            return false;
        }

        var archetype = entity.Archetype;
        this.AddComponentTransferInfo<T>(archetype);

        Debug.Assert(TransferDstInfo != null);

        var (chunkIdx, idx) = TransferDstInfo.Archetype.Reserve();

        TransferDstInfo.Archetype.PutComponentData(TransferDstInfo.TypeIndices[0], chunkIdx, idx, in component);

        archetype.MoveDataTo(TransferDstInfo.Archetype, entity.Pos.ChunkIdx, entity.Pos.Idx, chunkIdx, idx);
        archetype.MarkRemove(entity.ID, entity.Pos);

        entity.Archetype = TransferDstInfo.Archetype;
        entity.Pos = new(chunkIdx, idx);
        modifiedEntityInfoMap[entity.ID] = entity;

        return true;
    }

    /// <summary>
    /// Adds a component with the default value to an entity.
    /// </summary>
    /// <param name="entity">The target entity.</param>
    /// <typeparam name="T">The component type, must be unmanaged and implement IComponent.</typeparam>
    /// <returns>True if the component was added; false if the entity is invalid or removed.</returns>
    public bool AddComponent<T>(ref Entity entity) where T : unmanaged, IComponent
    {
        return AddComponent(ref entity, new T());
    }

    /// <summary>
    /// Adds multiple components as a bundle to an entity.
    /// All components in the bundle will be added in a single operation.
    /// </summary>
    /// <param name="entity">The target entity.</param>
    /// <param name="bundle">The component bundle containing the components to add.</param>
    /// <typeparam name="T">The component bundle type, must be unmanaged and implement IComponentBundle.</typeparam>
    /// <returns>True if the components were added; false if the entity is invalid or removed.</returns>
    public bool AddComponents<T>(ref Entity entity, in T bundle) where T : unmanaged, IComponentBundle
    {
        if (removedEntityMap.ContainsKey(entity.ID) || !entityPool.CheckEntityValid(entity.Ref))
        {
            return false;
        }

        var archetype = entity.Archetype;
        this.AddComponentsTransferInfo<T>(archetype);

        Debug.Assert(TransferDstInfo != null);

        var (chunkIdx, idx) = TransferDstInfo.Archetype.Reserve();

        for (var i = 0; i < TransferDstInfo.TypeIndices.Length; i++)
        {
            unsafe
            {
                var bundleInfo = TransferDstInfo.BundleInfo[i].info;
                var ptr = TransferDstInfo.Archetype.Table.GetPtr(TransferDstInfo.TypeIndices[i], chunkIdx, idx);

                fixed (T* bundlePtr = &bundle)
                {
                    var componentPtr = (byte*)bundlePtr + bundleInfo.Offset;
                    NativeMemory.Copy(componentPtr, ptr, (nuint)bundleInfo.Size);
                }
            }
        }

        archetype.MoveDataTo(TransferDstInfo.Archetype, entity.Pos.ChunkIdx, entity.Pos.Idx, chunkIdx, idx);
        archetype.MarkRemove(entity.ID, entity.Pos);

        entity.Archetype = TransferDstInfo.Archetype;
        entity.Pos = new(chunkIdx, idx);
        modifiedEntityInfoMap[entity.ID] = entity;

        return true;
    }

    /// <summary>
    /// Removes a component from an entity. The entity will be moved to a new archetype.
    /// </summary>
    /// <param name="entity">The target entity.</param>
    /// <typeparam name="T">The component type to remove, must be unmanaged and implement IComponent.</typeparam>
    /// <returns>True if the component was removed; false if the entity is invalid, removed, or doesn't have this component.</returns>
    public bool RemoveComponent<T>(ref Entity entity) where T : unmanaged, IComponent
    {
        if (removedEntityMap.ContainsKey(entity.ID) || !entityPool.CheckEntityValid(entity.Ref))
        {
            return false;
        }

        var archetype = entity.Archetype;
        this.RemoveComponentTransferInfo<T>(archetype);

        Debug.Assert(TransferDstInfo != null);

        if (TransferDstInfo.Archetype == entity.Archetype)
        {
            return false;
        }

        var (chunkIdx, idx) = TransferDstInfo.Archetype.Reserve();

        archetype.MoveDataTo(TransferDstInfo.Archetype, entity.Pos.ChunkIdx, entity.Pos.Idx, chunkIdx, idx);
        archetype.MarkRemove(entity.ID, entity.Pos);

        entity.Archetype = TransferDstInfo.Archetype;
        entity.Pos = new(chunkIdx, idx);
        modifiedEntityInfoMap[entity.ID] = entity;

        return true;
    }

    /// <summary>
    /// Removes all components defined in a component bundle from an entity.
    /// </summary>
    /// <param name="entity">The target entity.</param>
    /// <typeparam name="T">The component bundle type, must be unmanaged and implement IComponentBundle.</typeparam>
    /// <returns>True if the components were removed; false if the entity is invalid, removed, or doesn't have these components.</returns>
    public bool RemoveComponents<T>(ref Entity entity) where T : unmanaged, IComponentBundle
    {
        if (removedEntityMap.ContainsKey(entity.ID) || !entityPool.CheckEntityValid(entity.Ref))
        {
            return false;
        }

        var archetype = entity.Archetype;
        this.RemoveComponentsTransferInfo<T>(archetype);

        Debug.Assert(TransferDstInfo != null);

        if (TransferDstInfo.Archetype == entity.Archetype)
        {
            return false;
        }

        var (chunkIdx, idx) = TransferDstInfo.Archetype.Reserve();

        archetype.MoveDataTo(TransferDstInfo.Archetype, entity.Pos.ChunkIdx, entity.Pos.Idx, chunkIdx, idx);
        archetype.MarkRemove(entity.ID, entity.Pos);

        entity.Archetype = TransferDstInfo.Archetype;
        entity.Pos = new(chunkIdx, idx);
        modifiedEntityInfoMap[entity.ID] = entity;

        return true;
    }

    /// <summary>
    /// Removes all components defined in a tuple from an entity.
    /// </summary>
    /// <param name="entity">The target entity.</param>
    /// <typeparam name="T">The tuple type containing the component types to remove, must be unmanaged.</typeparam>
    /// <returns>True if the components were removed; false if the entity is invalid, removed, or doesn't have these components.</returns>
    public bool RemoveComponentsTuple<T>(ref Entity entity) where T : unmanaged
    {
        if (removedEntityMap.ContainsKey(entity.ID) || !entityPool.CheckEntityValid(entity.Ref))
        {
            return false;
        }

        var archetype = entity.Archetype;
        this.RemoveComponentsTupleTransferInfo<T>(archetype);

        Debug.Assert(TransferDstInfo != null);

        if (TransferDstInfo.Archetype == entity.Archetype)
        {
            return false;
        }

        var (chunkIdx, idx) = TransferDstInfo.Archetype.Reserve();

        archetype.MoveDataTo(TransferDstInfo.Archetype, entity.Pos.ChunkIdx, entity.Pos.Idx, chunkIdx, idx);
        archetype.MarkRemove(entity.ID, entity.Pos);

        entity.Archetype = TransferDstInfo.Archetype;
        entity.Pos = new(chunkIdx, idx);
        modifiedEntityInfoMap[entity.ID] = entity;

        return true;
    }

    /// <summary>
    /// A delegate for configuring entity alterations in a single archetype migration.
    /// </summary>
    /// <param name="context">The entity alteration context builder.</param>
    public delegate void EntityAlterContextDelegate(ref EntityAlterContext context);

    /// <summary>
    /// Performs multiple component additions and removals on an entity in a single archetype migration.
    /// Remove operations must be called before Add operations within the configuration callback.
    /// </summary>
    /// <param name="entity">The target entity.</param>
    /// <param name="configure">A callback that configures the alterations using the EntityAlter builder.</param>
    /// <returns>True if any alterations were made; false if the entity is invalid or no changes were made.</returns>
    public bool AlterComponents(ref Entity entity, EntityAlterContextDelegate configure)
    {
        if (removedEntityMap.ContainsKey(entity.ID) || !entityPool.CheckEntityValid(entity.Ref))
        {
            return false;
        }

        var alter = new EntityAlterContext(entity);
        configure(ref alter);

        if (alter.Commit())
        {
            entity = alter.Entity;
            modifiedEntityInfoMap[entity.ID] = entity;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Gets a reference to a component of the current entity.
    /// Requires SetCurrentEntity to be called first.
    /// </summary>
    /// <typeparam name="T">The component type, must be unmanaged and implement IComponent.</typeparam>
    /// <returns>A reference to the component. Returns null-ref if SetCurrentEntity was not called or the entity doesn't have this component.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref T GetEntityComponent<T>(Archetype archetype, EntityPos entityPos) where T : unmanaged, IComponent
    {
        var (ptr, size) = archetype.GetChunkDataWithReservation(TypeRegistrar.GetTypeId<T>(), entityPos.ChunkIdx);

        Debug.Assert((uint)entityPos.Idx < (uint)size);

        unsafe
        {
            return ref ((T*)ptr)[entityPos.Idx];
        }
    }

    /// <summary>
    /// Checks whether an entity has a specific component.
    /// </summary>
    /// <typeparam name="T">The component type to check, must be unmanaged and implement IComponent.</typeparam>
    /// <param name="entity">The entity to check.</param>
    /// <returns>True if the entity has the component; otherwise, false.</returns>
    public bool WithComponent<T>(ref Entity entity) where T : unmanaged, IComponent
    {
        var typeId = TypeRegistrar.GetTypeId<T>();
        return entity.Archetype.TypeIdList.Any(x => x == typeId);
    }

    /// <summary>
    /// Checks whether an entity does not have a specific component.
    /// </summary>
    /// <typeparam name="T">The component type to check, must be unmanaged and implement IComponent.</typeparam>
    /// <param name="entity">The entity to check.</param>
    /// <returns>True if the entity does not have the component; otherwise, false.</returns>
    public bool WithoutComponent<T>(ref Entity entity) where T : unmanaged, IComponent
    {
        return !WithComponent<T>(ref entity);
    }

    /// <summary>
    /// Checks whether an entity reference is valid.
    /// An entity reference is valid if it points to an existing entity that has not been destroyed.
    /// </summary>
    /// <param name="entityRef">The entity reference to validate.</param>
    /// <returns>True if the entity reference is valid; otherwise, false.</returns>
    public bool CheckEntityValid(EntityRef entityRef)
    {
        return entityPool.CheckEntityValid(entityRef);
    }

#endregion

#region Internal methods

    internal Dictionary<nint, EntityTransferInfo> TrySetTransferDstInfo(TransferInfoMap map, Archetype archetype, nint ptr)
    {
        if (map.TryGetValue(archetype.ID, out var dict))
        {
            TransferDstInfo = dict.GetValueOrDefault(ptr);
        }
        else
        {
            dict = [];
            TransferDstInfo = null;
            map.AddOrUpdate(archetype.ID, dict);
        }

        return dict;
    }

    internal void Commit()
    {
        foreach (var (_, entity) in modifiedEntityInfoMap)
        {
            entityPool.CommitReservedEntity(in entity);
            entity.Archetype.CommitAddEntity(entity.Ref);
        }

        foreach (var (_, entity) in removedEntityMap)
        {
            entityPool.CommitRemoveEntity(entity.Ref);
        }

        entityPool.ReclaimId();
        ArchetypeManager.Commit(entityPool);

        removedEntityMap.Clear();
        modifiedEntityInfoMap.Clear();
    }

#endregion
}

internal static class CommandsExtensions
{
    extension(Commands self)
    {
        internal void AddComponentTransferInfo<T>(Archetype archetype) where T : unmanaged, IComponent
        {
            nint ptr;
            unsafe
            {
                ptr = (nint)(delegate* <Commands, Archetype, void>)&AddComponentTransferInfo<T>;
            }

            var dict = self.TrySetTransferDstInfo(self.ArchetypeAddingTypeMap, archetype, ptr);

            if (self.TransferDstInfo == null)
            {
                var typeId = self.TypeRegistrar.RegisterComponent<T>();
                var dstArchetype = self.ArchetypeManager.GetOrCreateArchetype(archetype.TypeIdList.Append(typeId));

                self.TransferDstInfo = new(dstArchetype, [new(new(), typeId)]);
                dict.Add(ptr, self.TransferDstInfo);
            }
        }

        internal void AddComponentsTransferInfo<T>(Archetype archetype) where T : unmanaged, IComponentBundle
        {
            nint ptr;
            unsafe
            {
                ptr = (nint)(delegate* <Commands, Archetype, void>)&AddComponentsTransferInfo<T>;
            }

            var dict = self.TrySetTransferDstInfo(self.ArchetypeAddingTypeMap, archetype, ptr);

            if (self.TransferDstInfo == null)
            {
                self.TypeRegistrar.RegisterBundle<T>();
                var bundleInfo = self.TypeRegistrar.GetBundleInfo<T>();
                var dstArchetype = self.ArchetypeManager.GetOrCreateArchetype(archetype.TypeIdList.Concat(bundleInfo.Select(x => x.typeId)));

                self.TransferDstInfo = new(dstArchetype, bundleInfo);
                dict.Add(ptr, self.TransferDstInfo);
            }
        }

        internal void RemoveComponentTransferInfo<T>(Archetype archetype) where T : unmanaged, IComponent
        {
            nint ptr;
            unsafe
            {
                ptr = (nint)(delegate* <Commands, Archetype, void>)&RemoveComponentTransferInfo<T>;
            }

            var dict = self.TrySetTransferDstInfo(self.ArchetypeRemovingTypeMap, archetype, ptr);

            if (self.TransferDstInfo == null)
            {
                var typeId = self.TypeRegistrar.RegisterComponent<T>();
                var dstArchetype = self.ArchetypeManager.GetOrCreateArchetype(archetype.TypeIdList.Where(x => x != typeId));

                self.TransferDstInfo = new(dstArchetype, []);
                dict.Add(ptr, self.TransferDstInfo);
            }
        }

        internal void RemoveComponentsTransferInfo<T>(Archetype archetype) where T : unmanaged, IComponentBundle
        {
            nint ptr;
            unsafe
            {
                ptr = (nint)(delegate* <Commands, Archetype, void>)&RemoveComponentsTransferInfo<T>;
            }

            var dict = self.TrySetTransferDstInfo(self.ArchetypeRemovingTypeMap, archetype, ptr);

            if (self.TransferDstInfo == null)
            {
                self.TypeRegistrar.RegisterBundle<T>();
                var bundleInfo = self.TypeRegistrar.GetBundleInfo<T>();
                var dstArchetype = self.ArchetypeManager.GetOrCreateArchetype(archetype.TypeIdList.Except(bundleInfo.Select(x => x.typeId)));

                self.TransferDstInfo = new(dstArchetype, []);
                dict.Add(ptr, self.TransferDstInfo);
            }
        }

        internal void RemoveComponentsTupleTransferInfo<T>(Archetype archetype) where T : unmanaged
        {
            nint ptr;
            unsafe
            {
                ptr = (nint)(delegate* <Commands, Archetype, void>)&RemoveComponentsTupleTransferInfo<T>;
            }

            var dict = self.TrySetTransferDstInfo(self.ArchetypeRemovingTypeMap, archetype, ptr);

            if (self.TransferDstInfo == null)
            {
                var typeIds = self.TypeRegistrar.RegisterTypesOfTuple<T>();
                var dstArchetype = self.ArchetypeManager.GetOrCreateArchetype(archetype.TypeIdList.Except(typeIds));

                self.TransferDstInfo = new(dstArchetype, []);
                dict.Add(ptr, self.TransferDstInfo);
            }
        }
    }
}

/// <summary>
/// A builder struct for configuring entity alterations in a single archetype migration.
/// Remove operations must be called before Add operations. Each can only be called once.
/// </summary>
public struct EntityAlterContext
{
    internal Entity Entity;

    private readonly Archetype originalArchetype;

    private bool hasAdded;

    internal EntityAlterContext(Entity entity)
    {
        Entity = entity;
        originalArchetype = entity.Archetype;
        hasAdded = false;
    }

    /// <summary>
    /// Removes a single component from the entity.
    /// Must be called before any Add operations.
    /// </summary>
    /// <typeparam name="T">The component type to remove.</typeparam>
    /// <exception cref="InvalidOperationException">Thrown if called more than once.</exception>
    public void Remove<T>() where T : unmanaged, IComponent
    {
        if (hasAdded)
        {
            throw new InvalidOperationException("Remove operation can only be called before Add operations.");
        }

        var archetype = Entity.Archetype;
        Entity.Commands.RemoveComponentTransferInfo<T>(archetype);

        Debug.Assert(Entity.Commands.TransferDstInfo != null);

        Entity.Archetype = Entity.Commands.TransferDstInfo.Archetype;
    }

    /// <summary>
    /// Removes a single component from the entity.
    /// Must be called before any Add operations.
    /// </summary>
    /// <typeparam name="T">The component type to remove.</typeparam>
    /// <exception cref="InvalidOperationException">Thrown if called more than once.</exception>
    public void RemoveBundle<T>() where T : unmanaged, IComponentBundle
    {
        if (hasAdded)
        {
            throw new InvalidOperationException("Remove operation can only be called before Add operations.");
        }

        var archetype = Entity.Archetype;
        Entity.Commands.RemoveComponentsTransferInfo<T>(archetype);

        Debug.Assert(Entity.Commands.TransferDstInfo != null);

        Entity.Archetype = Entity.Commands.TransferDstInfo.Archetype;
    }

    /// <summary>
    /// Removes all components defined in a tuple from the entity.
    /// Must be called before any Add operations.
    /// </summary>
    /// <typeparam name="T">The tuple type containing component types to remove.</typeparam>
    /// <exception cref="InvalidOperationException">Thrown if called more than once.</exception>
    public void RemoveTuple<T>() where T : unmanaged
    {
        if (hasAdded)
        {
            throw new InvalidOperationException("Remove operation can only be called before Add operations.");
        }

        var archetype = Entity.Archetype;
        Entity.Commands.RemoveComponentsTupleTransferInfo<T>(archetype);

        Debug.Assert(Entity.Commands.TransferDstInfo != null);

        Entity.Archetype = Entity.Commands.TransferDstInfo.Archetype;
    }

    /// <summary>
    /// Adds a single component to the entity.
    /// Must be called after Remove operations. Can only be called once.
    /// </summary>
    /// <typeparam name="T">The component type to add.</typeparam>
    /// <param name="component">The component value.</param>
    /// <exception cref="InvalidOperationException">Thrown if called before Remove or called more than once.</exception>
    public void Add<T>(in T component) where T : unmanaged, IComponent
    {
        if (hasAdded)
        {
            throw new InvalidOperationException("Add operation can only be called once.");
        }

        var archetype = Entity.Archetype;
        Entity.Commands.AddComponentTransferInfo<T>(archetype);

        Debug.Assert(Entity.Commands.TransferDstInfo != null);

        var dstArchetype = Entity.Commands.TransferDstInfo.Archetype;
        var (chunkIdx, idx) = dstArchetype.Reserve();

        dstArchetype.PutComponentData(Entity.Commands.TransferDstInfo.TypeIndices[0], chunkIdx, idx, in component);

        originalArchetype.MoveDataTo(dstArchetype, Entity.Pos.ChunkIdx, Entity.Pos.Idx, chunkIdx, idx);
        originalArchetype.MarkRemove(Entity.ID, Entity.Pos);

        Entity.Archetype = dstArchetype;
        Entity.Pos = new(chunkIdx, idx);

        hasAdded = true;
    }

    /// <summary>
    /// Adds multiple components as a bundle to the entity.
    /// Must be called after Remove operations. Can only be called once.
    /// </summary>
    /// <typeparam name="T">The component bundle type.</typeparam>
    /// <param name="bundle">The bundle containing component values.</param>
    /// <exception cref="InvalidOperationException">Thrown if called before Remove or called more than once.</exception>
    public void AddBundle<T>(in T bundle) where T : unmanaged, IComponentBundle
    {
        if (hasAdded)
        {
            throw new InvalidOperationException("Add operation can only be called once.");
        }

        var archetype = Entity.Archetype;
        Entity.Commands.AddComponentsTransferInfo<T>(archetype);

        Debug.Assert(Entity.Commands.TransferDstInfo != null);

        var dstArchetype = Entity.Commands.TransferDstInfo.Archetype;
        var (chunkIdx, idx) = dstArchetype.Reserve();

        for (var i = 0; i < Entity.Commands.TransferDstInfo.TypeIndices.Length; i++)
        {
            unsafe
            {
                var bundleInfo = Entity.Commands.TransferDstInfo.BundleInfo[i];
                var ptr = dstArchetype.Table.GetPtr(Entity.Commands.TransferDstInfo.TypeIndices[i], chunkIdx, idx);
                fixed (T* bundlePtr = &bundle)
                {
                    var componentPtr = (byte*)bundlePtr + bundleInfo.info.Offset;
                    NativeMemory.Copy(componentPtr, ptr, (nuint)bundleInfo.info.Size);
                }
            }
        }

        originalArchetype.MoveDataTo(dstArchetype, Entity.Pos.ChunkIdx, Entity.Pos.Idx, chunkIdx, idx);
        originalArchetype.MarkRemove(Entity.ID, Entity.Pos);

        Entity.Archetype = dstArchetype;
        Entity.Pos = new(chunkIdx, idx);

        hasAdded = true;
    }

    /// <summary>
    /// Commits the alterations by performing the actual archetype migration.
    /// Called automatically when the Alter lambda completes.
    /// </summary>
    internal bool Commit()
    {
        if (hasAdded)
        {
            return true;
        }

        var dstArchetype = Entity.Commands.TransferDstInfo?.Archetype ?? originalArchetype;

        if (dstArchetype == originalArchetype)
        {
            return false;
        }

        var (chunkIdx, idx) = dstArchetype.Reserve();
        originalArchetype.MoveDataTo(dstArchetype, Entity.Pos.ChunkIdx, Entity.Pos.Idx, chunkIdx, idx);
        originalArchetype.MarkRemove(Entity.ID, Entity.Pos);

        Entity.Archetype = dstArchetype;
        Entity.Pos = new(chunkIdx, idx);

        return true;
    }
}
