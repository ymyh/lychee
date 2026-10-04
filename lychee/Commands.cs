using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using lychee.collections;
using lychee.components;
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

    private readonly Hierarchy hierarchy = app.World.Hierarchy;

    // Lazily allocated: most systems never touch the hierarchy, and Commands instances
    // are created per system execution, so they should not pay for lists they never use.
    private List<HierarchyOp>? hierarchyOpList;

    private List<EntityRef>? descendantList;

    private HashSet<int>? despawnedIdSet;

    private readonly int childOfTypeId = app.TypeRegistrar.RegisterComponent<ChildOf>();

    internal EntityTransferInfo? TransferDstInfo;

#endregion

#region Public Properties

    /// <summary>
    /// Gets the world's parent-child hierarchy index. The index only reflects committed state;
    /// structural changes must go through Commands methods and are applied at commit time.
    /// </summary>
    public Hierarchy Hierarchy => hierarchy;

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
    /// Creates a new entity with a single component attached, skipping the EmptyArchetype migration.
    /// </summary>
    /// <param name="component">The component value to attach.</param>
    /// <typeparam name="T">The component type, must be unmanaged and implement IComponent.</typeparam>
    /// <returns>The newly created entity with the component.</returns>
    public Entity CreateEntityWithComponent<T>(in T component) where T : unmanaged, IComponent
    {
        this.AddComponentTransferInfo<T>(ArchetypeManager.EmptyArchetype);

        Debug.Assert(TransferDstInfo != null);

        var entityRef = entityPool.ReserveEntity();
        var (chunkIdx, idx) = TransferDstInfo.Archetype.Reserve();

        TransferDstInfo.Archetype.PutComponentData(TransferDstInfo.TypeIndices[0], chunkIdx, idx, in component);

        var entity = new Entity(this, TransferDstInfo.Archetype, entityRef, new((ushort)chunkIdx, (ushort)idx));
        modifiedEntityInfoMap[entityRef.ID] = entity;

        if (typeof(T) == typeof(ChildOf))
        {
            RecordHierarchyOp(HierarchyOpKind.AddChild, entityRef, AsChildOf(in component).Parent);
        }

        return entity;
    }

    /// <summary>
    /// Creates a new entity with a component bundle attached, skipping the EmptyArchetype migration.
    /// </summary>
    /// <param name="bundle">The component bundle containing the components to attach.</param>
    /// <typeparam name="T">The component bundle type, must be unmanaged and implement IComponentBundle.</typeparam>
    /// <returns>The newly created entity with the bundle components.</returns>
    public Entity CreateEntityWithComponents<T>(in T bundle) where T : unmanaged, IComponentBundle
    {
        this.AddComponentsTransferInfo<T>(ArchetypeManager.EmptyArchetype);

        Debug.Assert(TransferDstInfo != null);

        var entityRef = entityPool.ReserveEntity();
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

        var entity = new Entity(this, TransferDstInfo.Archetype, entityRef, new((ushort)chunkIdx, (ushort)idx));
        modifiedEntityInfoMap[entityRef.ID] = entity;

        if (TryExtractBundleChildOf(in bundle, out var childOf))
        {
            RecordHierarchyOp(HierarchyOpKind.AddChild, entityRef, childOf.Parent);
        }

        return entity;
    }

    /// <summary>
    /// Creates a copy of an existing entity with identical component data.
    /// </summary>
    /// <param name="entity">The entity to copy.</param>
    /// <returns>The newly created copy entity.</returns>
    public Entity CopyEntity(in Entity entity)
    {
        // Refresh through the pool/buffer: the passed-in struct may be stale if the entity was
        // modified through another Entity handle (e.g. Commands.AddChild) after it was obtained.
        // This also subsumes the removed/invalid check.
        if (!GetEntityByRef(entity.Ref, out var fresh))
        {
            throw new ArgumentException("Cannot copy an invalid entity");
        }

        var newEntityRef = entityPool.ReserveEntity();
        var (newChunkIdx, newIdx) = fresh.Archetype.Reserve();

        var typeIdList = fresh.Archetype.TypeIdList;
        for (var i = 0; i < typeIdList.Length; i++)
        {
            unsafe
            {
                var srcPtr = fresh.Archetype.Table.GetPtr(i, fresh.Pos.ChunkIdx, fresh.Pos.Idx);
                var dstPtr = fresh.Archetype.Table.GetPtr(i, newChunkIdx, newIdx);
                NativeMemory.Copy(
                    srcPtr,
                    dstPtr,
                    (nuint)fresh.Archetype.Table.Layout.TypeInfoList[i].Size);
            }
        }

        var newEntity = new Entity(this, fresh.Archetype, newEntityRef, new((ushort)newChunkIdx, (ushort)newIdx));
        modifiedEntityInfoMap[newEntityRef.ID] = newEntity;

        // The relationship is cloned (but not the subtree), matching Bevy's entity cloning.
        if (HasChildOf(fresh.Archetype))
        {
            var childOf = GetEntityComponent<ChildOf>(fresh.Archetype, fresh.Pos);
            RecordHierarchyOp(HierarchyOpKind.AddChild, newEntityRef, childOf.Parent);
        }

        return newEntity;
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

        RecordHierarchyOp(HierarchyOpKind.Despawn, entity.Ref);
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

        RecordHierarchyOp(HierarchyOpKind.Despawn, e.Ref);
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

        // Adding ChildOf to an entity that already has it throws above (duplicate type id
        // in the target archetype), so reaching here means a fresh attachment.
        if (typeof(T) == typeof(ChildOf))
        {
            RecordHierarchyOp(HierarchyOpKind.AddChild, entity.Ref, AsChildOf(in component).Parent);
        }

        return true;
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

        if (TryExtractBundleChildOf(in bundle, out var childOf))
        {
            RecordHierarchyOp(HierarchyOpKind.AddChild, entity.Ref, childOf.Parent);
        }

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
        var hadChildOf = typeof(T) == typeof(ChildOf) && HasChildOf(archetype);

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

        if (hadChildOf)
        {
            RecordHierarchyOp(HierarchyOpKind.RemoveChild, entity.Ref);
        }

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
        var hadChildOf = HasChildOf(archetype);

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

        if (hadChildOf && !HasChildOf(TransferDstInfo.Archetype))
        {
            RecordHierarchyOp(HierarchyOpKind.RemoveChild, entity.Ref);
        }

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
        var hadChildOf = HasChildOf(archetype);

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

        if (hadChildOf && !HasChildOf(TransferDstInfo.Archetype))
        {
            RecordHierarchyOp(HierarchyOpKind.RemoveChild, entity.Ref);
        }

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
    /// Attaches the child entity to the parent, or re-parents it if it already has a parent.
    /// The hierarchy index is updated when the commands are committed.
    /// </summary>
    /// <param name="parent">The parent entity reference.</param>
    /// <param name="child">The child entity reference.</param>
    /// <returns>True if the operation was buffered; false if the child entity is invalid or removed.</returns>
    public bool AddChild(EntityRef parent, EntityRef child)
    {
        if (!GetEntityByRef(child, out var childEntity))
        {
            return false;
        }

        return AlterComponents(ref childEntity, (ref EntityAlterContext ctx) =>
        {
            ctx.Remove<ChildOf>();
            ctx.Add(new ChildOf { Parent = parent });
        });
    }

    /// <summary>
    /// Detaches the child entity from the given parent, turning it into a root.
    /// Only takes effect if the child's current parent is the given entity.
    /// The hierarchy index is updated when the commands are committed.
    /// </summary>
    /// <param name="parent">The expected parent entity reference.</param>
    /// <param name="child">The child entity reference.</param>
    /// <returns>True if the child was detached; false otherwise.</returns>
    public bool RemoveChild(EntityRef parent, EntityRef child)
    {
        if (!GetEntityByRef(child, out var childEntity))
        {
            return false;
        }

        if (!HasChildOf(childEntity.Archetype))
        {
            return false;
        }

        ref var childOf = ref GetEntityComponent<ChildOf>(childEntity.Archetype, childEntity.Pos);

        if (childOf.Parent != parent)
        {
            return false;
        }

        return RemoveComponent<ChildOf>(ref childEntity);
    }

    /// <summary>
    /// Detaches all direct children of the parent, turning them into roots. Grandchildren keep
    /// their own parents. The detachment is applied to the hierarchy index when the commands
    /// are committed, based on the index state at that time.
    /// </summary>
    /// <param name="parent">The parent entity reference.</param>
    public void DetachAllChildren(EntityRef parent)
    {
        RecordHierarchyOp(HierarchyOpKind.DetachAllChildren, parent);
    }

    /// <summary>
    /// Gets a reference to a component of the given entity.
    /// </summary>
    /// <typeparam name="T">The component type, must be unmanaged and implement IComponent.</typeparam>
    /// <returns>A reference to the component.</returns>
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

#region Internal Methods

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
        ApplyHierarchyOps();

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
        hierarchyOpList?.Clear();
    }

    internal bool HasChildOf(Archetype archetype)
    {
        var typeIdList = archetype.TypeIdList;

        for (var i = 0; i < typeIdList.Length; i++)
        {
            if (typeIdList[i] == childOfTypeId)
            {
                return true;
            }
        }

        return false;
    }

    internal void RecordHierarchyOp(HierarchyOpKind kind, EntityRef subject, EntityRef parent = default)
    {
        hierarchyOpList ??= [];
        hierarchyOpList.Add(new(kind, subject, parent));
    }

    internal bool TryExtractBundleChildOf<T>(in T bundle, out ChildOf value) where T : unmanaged, IComponentBundle
    {
        foreach (var (info, typeId) in TypeRegistrar.GetBundleInfo<T>())
        {
            if (typeId != childOfTypeId)
            {
                continue;
            }

            unsafe
            {
                fixed (T* bundlePtr = &bundle)
                {
                    value = *(ChildOf*)((byte*)bundlePtr + info.Offset);
                }
            }

            return true;
        }

        value = default;
        return false;
    }

    internal static ChildOf AsChildOf<T>(in T value) where T : unmanaged, IComponent
    {
        return Unsafe.As<T, ChildOf>(ref Unsafe.AsRef(in value));
    }

#endregion

#region Private Methods

    private void ApplyHierarchyOps()
    {
        if (hierarchyOpList == null)
        {
            return;
        }

        // Deaths are tracked in op application order, NOT via removedEntityMap: the map is
        // populated at call time and would wrongly reject an AddChild that was recorded
        // before the parent's Despawn within the same buffer.
        despawnedIdSet ??= [];
        despawnedIdSet.Clear();

        foreach (var op in hierarchyOpList)
        {
            switch (op.Kind)
            {
                case HierarchyOpKind.AddChild:
                    if (op.Parent.ID == op.Subject.ID)
                    {
                        // Self-cycle rejected.
                        break;
                    }

                    // Deaths from earlier ops of this buffer.
                    if (despawnedIdSet.Contains(op.Parent.ID) || despawnedIdSet.Contains(op.Subject.ID))
                    {
                        break;
                    }

                    // Deaths committed by earlier buffers of this commit round are only
                    // visible to the pool (generations were bumped). Entities created in
                    // this buffer pass the pool check.
                    if (!entityPool.CheckEntityValid(op.Parent) || !entityPool.CheckEntityValid(op.Subject))
                    {
                        break;
                    }

                    hierarchy.AddChild(op.Parent, op.Subject);
                    break;

                case HierarchyOpKind.RemoveChild:
                    hierarchy.RemoveChild(op.Subject);
                    break;

                case HierarchyOpKind.DetachAllChildren:
                    hierarchy.DetachAllChildren(op.Subject);
                    break;

                case HierarchyOpKind.Despawn:
                    ResolveDespawn(op.Subject, despawnedIdSet);
                    break;
            }
        }
    }

    private void ResolveDespawn(EntityRef rootRef, HashSet<int> despawnedIdSet)
    {
        descendantList ??= [];
        descendantList.Clear();
        hierarchy.CollectDescendants(rootRef, descendantList);

        foreach (var entity in descendantList)
        {
            despawnedIdSet.Add(entity.ID);

            if (entity.ID == rootRef.ID)
            {
                hierarchy.RemoveFromIndex(entity);
            }
            else
            {
                // Skip entities already dead in this buffer or cross-buffer. The latter
                // cannot actually be collected (their nodes are gone); this is defensive.
                if (removedEntityMap.ContainsKey(entity.ID) || !entityPool.CheckEntityValid(entity))
                {
                    continue;
                }

                MarkRemoved(entity);
                hierarchy.RemoveNodeOnly(entity);
            }
        }
    }

    // Same bookkeeping as RemoveEntity.
    private void MarkRemoved(EntityRef entityRef)
    {
        if (!GetEntityByRef(entityRef, out var entity))
        {
            return;
        }

        entity.Archetype.MarkRemove(entity.ID, entity.Pos);
        modifiedEntityInfoMap.Remove(entity.ID);
        entityPool.MarkRemoveEntity(entity.Ref);
        removedEntityMap[entity.ID] = entity;
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

    private readonly bool originalHasChildOf;

    private bool hasAdded;

    private bool childOfRemoved;

    internal EntityAlterContext(Entity entity)
    {
        Entity = entity;
        originalArchetype = entity.Archetype;
        originalHasChildOf = entity.Commands.HasChildOf(entity.Archetype);
        hasAdded = false;
        childOfRemoved = false;
    }

    // Records a RemoveChild op once the alterations actually strip ChildOf off the entity.
    private void RecordRemoveChildOfIfNeeded()
    {
        if (!childOfRemoved && originalHasChildOf && !Entity.Commands.HasChildOf(Entity.Archetype))
        {
            Entity.Commands.RecordHierarchyOp(HierarchyOpKind.RemoveChild, Entity.Ref);
            childOfRemoved = true;
        }
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

        RecordRemoveChildOfIfNeeded();
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

        RecordRemoveChildOfIfNeeded();
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

        RecordRemoveChildOfIfNeeded();
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

        if (dstArchetype == originalArchetype)
        {
            // Remove and Add of the same component type — archetype doesn't change.
            // Just update the component data in place, no migration needed.
            dstArchetype.PutComponentData(Entity.Commands.TransferDstInfo.TypeIndices[0], Entity.Pos.ChunkIdx, Entity.Pos.Idx, in component);
            Entity.Archetype = dstArchetype;
        }
        else
        {
            var (chunkIdx, idx) = dstArchetype.Reserve();

            dstArchetype.PutComponentData(Entity.Commands.TransferDstInfo.TypeIndices[0], chunkIdx, idx, in component);

            originalArchetype.MoveDataTo(dstArchetype, Entity.Pos.ChunkIdx, Entity.Pos.Idx, chunkIdx, idx);
            originalArchetype.MarkRemove(Entity.ID, Entity.Pos);

            Entity.Archetype = dstArchetype;
            Entity.Pos = new(chunkIdx, idx);
        }

        hasAdded = true;

        if (typeof(T) == typeof(ChildOf))
        {
            // Replacing an existing ChildOf without an explicit Remove means detaching from
            // the old parent first.
            if (!childOfRemoved && originalHasChildOf)
            {
                Entity.Commands.RecordHierarchyOp(HierarchyOpKind.RemoveChild, Entity.Ref);
            }

            Entity.Commands.RecordHierarchyOp(HierarchyOpKind.AddChild, Entity.Ref, Commands.AsChildOf(in component).Parent);
        }
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

        if (dstArchetype == originalArchetype)
        {
            // Remove and Add of the same bundle type — archetype doesn't change.
            // Just update the component data in place, no migration needed.
            for (var i = 0; i < Entity.Commands.TransferDstInfo.TypeIndices.Length; i++)
            {
                unsafe
                {
                    var bundleInfo = Entity.Commands.TransferDstInfo.BundleInfo[i];
                    var ptr = dstArchetype.Table.GetPtr(Entity.Commands.TransferDstInfo.TypeIndices[i], Entity.Pos.ChunkIdx, Entity.Pos.Idx);
                    fixed (T* bundlePtr = &bundle)
                    {
                        var componentPtr = (byte*)bundlePtr + bundleInfo.info.Offset;
                        NativeMemory.Copy(componentPtr, ptr, (nuint)bundleInfo.info.Size);
                    }
                }
            }
            Entity.Archetype = dstArchetype;
        }
        else
        {
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
        }

        hasAdded = true;

        if (Entity.Commands.TryExtractBundleChildOf(in bundle, out var childOf))
        {
            // Replacing an existing ChildOf without an explicit Remove means detaching from
            // the old parent first.
            if (!childOfRemoved && originalHasChildOf)
            {
                Entity.Commands.RecordHierarchyOp(HierarchyOpKind.RemoveChild, Entity.Ref);
            }

            Entity.Commands.RecordHierarchyOp(HierarchyOpKind.AddChild, Entity.Ref, childOf.Parent);
        }
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
