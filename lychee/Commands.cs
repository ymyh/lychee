using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using lychee.collections;
using lychee.interfaces;

namespace lychee;

using TransferInfoMap = SparseMap<Dictionary<nint, EntityTransferInfo>>;

internal struct ModifiedEntityInfo(Archetype archetype, Entity entity, int chunkIdx, int idx)
{
    public readonly Archetype Archetype = archetype;

    public readonly Entity Entity = entity;

    public readonly int ChunkIdx = chunkIdx;

    public readonly int Idx = idx;
}

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
public sealed class Commands : IDisposable
{
#region Fields

    private readonly EntityPool entityPool;

    internal readonly ArchetypeManager ArchetypeManager;

    internal readonly TypeRegistrar TypeRegistrar;

    internal readonly TransferInfoMap ArchetypeAddingTypeMap = new();

    internal readonly TransferInfoMap ArchetypeRemovingTypeMap = new();

    private readonly SparseMap<ModifiedEntityInfo> modifiedEntityInfoMap = new();

    private readonly SparseMap<Entity> removedEntityMap = new();

    internal EntityTransferInfo? TransferDstInfo;

    internal Archetype SrcArchetype;

    public Archetype CurrentArchetype { get; set; } = null!;

    public Entity CurrentEntity { get; set; }

    private EntityInfo currentEntityInfo;

    private bool currentEntitySet;

    private bool srcArchetypeChanged = true;

#endregion

    internal Commands(App app)
    {
        entityPool = app.World.EntityPool;
        ArchetypeManager = app.World.ArchetypeManager;
        TypeRegistrar = app.TypeRegistrar;
        SrcArchetype = ArchetypeManager.EmptyArchetype;
    }

#region Public Methods

    /// <summary>
    /// Creates a new entity in an uncommitted state.
    /// The entity will be fully registered when Commit is called.
    /// </summary>
    /// <returns>The newly created entity.</returns>
    public Entity CreateEntity()
    {
        var entity = entityPool.ReserveEntity();
        removedEntityMap.Remove(entity.ID);

        return entity;
    }

    /// <summary>
    /// Removes an existing entity. Does nothing if the entity is already removed or in uncommitted state.
    /// </summary>
    /// <param name="entity">The entity to remove.</param>
    public void RemoveEntity(Entity entity)
    {
        if (removedEntityMap.ContainsKey(entity.ID))
        {
            return;
        }

        if (modifiedEntityInfoMap.TryGetValue(entity.ID, out var info))
        {
            info.Archetype.MarkRemove(entity.ID, info.ChunkIdx, info.Idx);
            return;
        }

        if (!entityPool.CheckEntityValid(entity))
        {
            return;
        }

        var entityInfo = entityPool.GetEntityInfo(entity);
        if (entityInfo.ArchetypeId != SrcArchetype.ID)
        {
            SrcArchetype = ArchetypeManager.GetArchetype(entityInfo.ArchetypeId);
        }

        SrcArchetype.MarkRemove(entity.ID, entityInfo.ChunkIdx, entityInfo.Idx);

        modifiedEntityInfoMap.Remove(entity.ID);
        entityPool.MarkRemoveEntity(entity);
        removedEntityMap.Add(entity.ID, entity);
    }

    /// <summary>
    /// Adds a component to an entity. The entity will be moved to a new archetype.
    /// </summary>
    /// <param name="entity">The target entity.</param>
    /// <param name="component">The component value to add.</param>
    /// <typeparam name="T">The component type, must be unmanaged and implement IComponent.</typeparam>
    /// <returns>True if the component was added; false if the entity is invalid or removed.</returns>
    public bool AddComponent<T>(Entity entity, in T component) where T : unmanaged, IComponent
    {
        if (removedEntityMap.ContainsKey(entity.ID) || entity.Generation != 0 && !entityPool.CheckEntityValid(entity))
        {
            return false;
        }

        var srcInfo = GetEntityInfo(entity);
        ChangeSrcArchetype(srcInfo.ArchetypeId);

        if (srcArchetypeChanged)
        {
            this.AddComponentTransferInfo<T>();
            srcArchetypeChanged = false;
        }

        Debug.Assert(TransferDstInfo != null);

        var (chunkIdx, idx) = TransferDstInfo.Archetype.Reserve();

        TransferDstInfo.Archetype.PutComponentData(TransferDstInfo.TypeIndices[0], chunkIdx, idx, in component);

        SrcArchetype.MoveDataTo(TransferDstInfo.Archetype, srcInfo.ChunkIdx, srcInfo.Idx, chunkIdx, idx);
        SrcArchetype.MarkRemove(entity.ID, srcInfo.ChunkIdx, srcInfo.Idx);
        modifiedEntityInfoMap.Add(entity.ID, new(TransferDstInfo.Archetype, entity, chunkIdx, idx));

        return true;
    }

    /// <summary>
    /// Adds a component with the default value to an entity.
    /// </summary>
    /// <param name="entity">The target entity.</param>
    /// <typeparam name="T">The component type, must be unmanaged and implement IComponent.</typeparam>
    /// <returns>True if the component was added; false if the entity is invalid or removed.</returns>
    public bool AddComponent<T>(Entity entity) where T : unmanaged, IComponent
    {
        return AddComponent(entity, new T());
    }

    /// <summary>
    /// Adds multiple components as a bundle to an entity.
    /// All components in the bundle will be added in a single operation.
    /// </summary>
    /// <param name="entity">The target entity.</param>
    /// <param name="bundle">The component bundle containing the components to add.</param>
    /// <typeparam name="T">The component bundle type, must be unmanaged and implement IComponentBundle.</typeparam>
    /// <returns>True if the components were added; false if the entity is invalid or removed.</returns>
    public bool AddComponents<T>(Entity entity, in T bundle) where T : unmanaged, IComponentBundle
    {
        if (removedEntityMap.ContainsKey(entity.ID) || entity.Generation != 0 && !entityPool.CheckEntityValid(entity))
        {
            return false;
        }

        var srcInfo = GetEntityInfo(entity);
        ChangeSrcArchetype(srcInfo.ArchetypeId);

        if (srcArchetypeChanged)
        {
            this.AddComponentsTransferInfo<T>();
            srcArchetypeChanged = false;
        }

        Debug.Assert(TransferDstInfo != null);

        var (chunkIdx, idx) = TransferDstInfo!.Archetype.Reserve();

        for (var i = 0; i < TransferDstInfo!.TypeIndices.Length; i++)
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

        SrcArchetype.MoveDataTo(TransferDstInfo.Archetype, srcInfo.ChunkIdx, srcInfo.Idx, chunkIdx, idx);
        SrcArchetype.MarkRemove(entity.ID, srcInfo.ChunkIdx, srcInfo.Idx);
        modifiedEntityInfoMap.Add(entity.ID, new(TransferDstInfo.Archetype, entity, chunkIdx, idx));

        return true;
    }

    /// <summary>
    /// Removes a component from an entity. The entity will be moved to a new archetype.
    /// </summary>
    /// <param name="entity">The target entity.</param>
    /// <typeparam name="T">The component type to remove, must be unmanaged and implement IComponent.</typeparam>
    /// <returns>True if the component was removed; false if the entity is invalid, removed, or doesn't have this component.</returns>
    public bool RemoveComponent<T>(Entity entity) where T : unmanaged, IComponent
    {
        if (removedEntityMap.ContainsKey(entity.ID) || entity.Generation != 0 && !entityPool.CheckEntityValid(entity))
        {
            return false;
        }

        var srcInfo = GetEntityInfo(entity);
        ChangeSrcArchetype(srcInfo.ArchetypeId);

        if (srcArchetypeChanged)
        {
            this.RemoveComponentTransferInfo<T>();
            srcArchetypeChanged = false;
        }

        Debug.Assert(TransferDstInfo != null);

        var (chunkIdx, idx) = TransferDstInfo.Archetype.Reserve();

        SrcArchetype.MoveDataTo(TransferDstInfo.Archetype, srcInfo.ChunkIdx, srcInfo.Idx, chunkIdx, idx);
        SrcArchetype.MarkRemove(entity.ID, srcInfo.ChunkIdx, srcInfo.Idx);
        modifiedEntityInfoMap.Add(entity.ID, new(TransferDstInfo.Archetype, entity, chunkIdx, idx));

        return true;
    }

    /// <summary>
    /// Removes all components defined in a component bundle from an entity.
    /// </summary>
    /// <param name="entity">The target entity.</param>
    /// <typeparam name="T">The component bundle type, must be unmanaged and implement IComponentBundle.</typeparam>
    /// <returns>True if the components were removed; false if the entity is invalid, removed, or doesn't have these components.</returns>
    public bool RemoveComponents<T>(Entity entity) where T : unmanaged, IComponentBundle
    {
        if (removedEntityMap.ContainsKey(entity.ID) || entity.Generation != 0 && !entityPool.CheckEntityValid(entity))
        {
            return false;
        }

        var srcInfo = GetEntityInfo(entity);
        ChangeSrcArchetype(srcInfo.ArchetypeId);

        if (srcArchetypeChanged)
        {
            this.RemoveComponentsTransferInfo<T>();
            srcArchetypeChanged = false;
        }

        Debug.Assert(TransferDstInfo != null);

        var (chunkIdx, idx) = TransferDstInfo.Archetype.Reserve();

        SrcArchetype.MoveDataTo(TransferDstInfo.Archetype, srcInfo.ChunkIdx, srcInfo.Idx, chunkIdx, idx);
        SrcArchetype.MarkRemove(entity.ID, srcInfo.ChunkIdx, srcInfo.Idx);
        modifiedEntityInfoMap.Add(entity.ID, new(TransferDstInfo.Archetype, entity, chunkIdx, idx));

        return true;
    }

    /// <summary>
    /// Removes all components defined in a tuple from an entity.
    /// </summary>
    /// <param name="entity">The target entity.</param>
    /// <typeparam name="T">The tuple type containing the component types to remove, must be unmanaged.</typeparam>
    /// <returns>True if the components were removed; false if the entity is invalid, removed, or doesn't have these components.</returns>
    public bool RemoveComponentsTuple<T>(Entity entity) where T : unmanaged
    {
        if (removedEntityMap.ContainsKey(entity.ID) || entity.Generation != 0 && !entityPool.CheckEntityValid(entity))
        {
            return false;
        }

        var srcInfo = GetEntityInfo(entity);
        ChangeSrcArchetype(srcInfo.ArchetypeId);

        if (srcArchetypeChanged)
        {
            this.RemoveComponentsTupleTransferInfo<T>();
            srcArchetypeChanged = false;
        }

        Debug.Assert(TransferDstInfo != null);

        var (chunkIdx, idx) = TransferDstInfo.Archetype.Reserve();

        SrcArchetype.MoveDataTo(TransferDstInfo.Archetype, srcInfo.ChunkIdx, srcInfo.Idx, chunkIdx, idx);
        SrcArchetype.MarkRemove(entity.ID, srcInfo.ChunkIdx, srcInfo.Idx);
        modifiedEntityInfoMap.Add(entity.ID, new(TransferDstInfo.Archetype, entity, chunkIdx, idx));

        return true;
    }

    /// <summary>
    /// Sets the current entity context for component access via GetCurrentEntityComponent.
    /// This enables efficient component access without repeated entity lookups.
    /// </summary>
    /// <param name="entity">The entity to set as current.</param>
    /// <param name="isCurrentArchetype">Whether to update CurrentArchetype. Set to false if you're manually managing archetypes.</param>
    /// <exception cref="ArgumentException">Thrown when the entity is invalid or in an uncommitted state.</exception>
    public void SetCurrentEntity(Entity entity, bool isCurrentArchetype = true)
    {
        if (modifiedEntityInfoMap.ContainsKey(entity.ID) || !entityPool.CheckEntityValid(entity))
        {
            throw new ArgumentException($"Entity {entity.ID} is invalid or in uncommitted state");
        }

        currentEntityInfo = entityPool.GetEntityInfo(entity);

        if (!isCurrentArchetype || currentEntityInfo.ArchetypeId != CurrentArchetype.ID)
        {
            CurrentArchetype = ArchetypeManager.GetArchetype(currentEntityInfo.ArchetypeId);
        }

        currentEntitySet = true;
    }

    /// <summary>
    /// Gets a reference to a component of the current entity.
    /// Requires SetCurrentEntity to be called first.
    /// </summary>
    /// <typeparam name="T">The component type, must be unmanaged and implement IComponent.</typeparam>
    /// <returns>A reference to the component. Returns null-ref if SetCurrentEntity was not called or the entity doesn't have this component.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref T GetCurrentEntityComponent<T>() where T : unmanaged, IComponent
    {
        if (!currentEntitySet)
        {
            return ref Unsafe.NullRef<T>();
        }

        var (ptr, size) = CurrentArchetype.GetChunkData(TypeRegistrar.GetTypeId<T>(), currentEntityInfo.ChunkIdx);

        Debug.Assert((uint)currentEntityInfo.Idx < (uint)size);

        unsafe
        {
            return ref *((T*)ptr + currentEntityInfo.Idx);
        }
    }

#endregion

#region Internal methods

    internal void Commit()
    {
        currentEntitySet = false;

        foreach (var (id, info) in modifiedEntityInfoMap)
        {
            entityPool.CommitReservedEntity(id, info.Archetype.ID, info.ChunkIdx, info.Idx);
            var archetype = ArchetypeManager.GetArchetypeUnsafe(info.Archetype.ID);
            archetype.CommitAddEntity(info.Entity);
        }

        foreach (var (_, entity) in removedEntityMap)
        {
            entityPool.CommitRemoveEntity(entity);
            var archetype = ArchetypeManager.GetArchetypeUnsafe(entityPool.GetEntityInfo(entity).ArchetypeId);
            archetype.CommitRemoveEntity(entity);
        }

        entityPool.Commit();
        ArchetypeManager.Commit();

        removedEntityMap.Clear();
        modifiedEntityInfoMap.Clear();
    }

#endregion

#region Private methods

    private (int srcChunkIdx, int srcIdx) GetPositionOfEntity(Entity entity)
    {
        var srcChunkIdx = 0;
        var srcIdx = 0;
        var oldSrcArchetype = SrcArchetype;

        if (modifiedEntityInfoMap.TryGetValue(entity.ID, out var info))
        {
            SrcArchetype = info.Archetype;
            srcChunkIdx = info.ChunkIdx;
            srcIdx = info.Idx;
        }
        else
        {
            if (entityPool.CheckEntityValid(entity))
            {
                var entityInfo = entityPool.GetEntityInfo(entity);
                SrcArchetype = ArchetypeManager.GetArchetype(entityInfo.ArchetypeId);

                srcChunkIdx = entityInfo.ChunkIdx;
                srcIdx = entityInfo.Idx;
            }
            else
            {
                SrcArchetype = ArchetypeManager.EmptyArchetype;
            }
        }

        if (SrcArchetype != oldSrcArchetype)
        {
            srcArchetypeChanged = true;
        }

        return (srcChunkIdx, srcIdx);
    }

    private void ChangeSrcArchetype(int archetypeId)
    {
        var oldSrcArchetype = SrcArchetype;
        SrcArchetype = ArchetypeManager.GetArchetype(archetypeId);

        if (oldSrcArchetype != SrcArchetype)
        {
            srcArchetypeChanged = true;
        }
    }

    private EntityInfo GetEntityInfo(Entity entity)
    {
        if (modifiedEntityInfoMap.TryGetValue(entity.ID, out var info))
        {
            return new(info.Archetype.ID, info.ChunkIdx, info.Idx);
        }

        if (entityPool.CheckEntityValid(entity))
        {
            var entityInfo = entityPool.GetEntityInfo(entity);
            return entityInfo;
        }

        return new(0, 0, 0);
    }

#endregion

#region IDisposable Member

    public void Dispose()
    {
        ArchetypeAddingTypeMap.Dispose();
        ArchetypeRemovingTypeMap.Dispose();
        modifiedEntityInfoMap.Dispose();
        removedEntityMap.Dispose();
    }

#endregion
}

public static class EntityCommandBufferExtensions
{
    extension(Commands self)
    {
        internal void AddComponentTransferInfo<T>() where T : unmanaged, IComponent
        {
            nint ptr;
            unsafe
            {
                ptr = (nint)(delegate* <Commands, void>)&AddComponentTransferInfo<T>;
            }

            if (self.ArchetypeAddingTypeMap.TryGetValue(self.SrcArchetype.ID, out var dict))
            {
                self.TransferDstInfo = dict.GetValueOrDefault(ptr);
            }
            else
            {
                self.TransferDstInfo = null;
                dict = new();
                self.ArchetypeAddingTypeMap.Add(self.SrcArchetype.ID, dict);
            }

            if (self.TransferDstInfo == null)
            {
                var typeId = self.TypeRegistrar.RegisterComponent<T>();
                var dstArchetype = self.ArchetypeManager.GetOrCreateArchetype(self.SrcArchetype.TypeIdList.Append(typeId));

                self.TransferDstInfo = new(dstArchetype, [new(new(), typeId)]);
                dict.Add(ptr, self.TransferDstInfo);
            }
        }

        internal void AddComponentsTransferInfo<T>() where T : unmanaged, IComponentBundle
        {
            nint ptr;
            unsafe
            {
                ptr = (nint)(delegate* <Commands, void>)&AddComponentsTransferInfo<T>;
            }

            if (self.ArchetypeAddingTypeMap.TryGetValue(self.SrcArchetype.ID, out var dict))
            {
                self.TransferDstInfo = dict.GetValueOrDefault(ptr);
            }
            else
            {
                self.TransferDstInfo = null;
                dict = new();
                self.ArchetypeAddingTypeMap.Add(self.SrcArchetype.ID, dict);
            }

            if (self.TransferDstInfo == null)
            {
                self.TypeRegistrar.RegisterBundle<T>();
                var bundleInfo = self.TypeRegistrar.GetBundleInfo<T>();
                var dstArchetype = self.ArchetypeManager.GetOrCreateArchetype(self.SrcArchetype.TypeIdList.Concat(bundleInfo.Select(x => x.typeId)));

                self.TransferDstInfo = new(dstArchetype, bundleInfo);
                dict.Add(ptr, self.TransferDstInfo);
            }
        }

        internal void RemoveComponentTransferInfo<T>() where T : unmanaged, IComponent
        {
            nint ptr;
            unsafe
            {
                ptr = (nint)(delegate* <Commands, void>)&RemoveComponentTransferInfo<T>;
            }

            if (self.ArchetypeRemovingTypeMap.TryGetValue(self.SrcArchetype.ID, out var dict))
            {
                self.TransferDstInfo = dict.GetValueOrDefault(ptr);
            }
            else
            {
                dict = new();
                self.TransferDstInfo = null;
                self.ArchetypeRemovingTypeMap.Add(self.SrcArchetype.ID, dict);
            }

            if (self.TransferDstInfo == null)
            {
                var typeId = self.TypeRegistrar.RegisterComponent<T>();
                var dstArchetype = self.ArchetypeManager.GetOrCreateArchetype(self.SrcArchetype.TypeIdList.Where(x => x != typeId));

                self.TransferDstInfo = new(dstArchetype, []);
                dict.Add(ptr, self.TransferDstInfo);
            }
        }

        internal void RemoveComponentsTransferInfo<T>() where T : unmanaged, IComponentBundle
        {
            nint ptr;
            unsafe
            {
                ptr = (nint)(delegate* <Commands, void>)&RemoveComponentsTransferInfo<T>;
            }

            if (self.ArchetypeRemovingTypeMap.TryGetValue(self.SrcArchetype.ID, out var dict))
            {
                self.TransferDstInfo = dict.GetValueOrDefault(ptr);
            }
            else
            {
                self.TransferDstInfo = null;
                dict = new();
                self.ArchetypeRemovingTypeMap.Add(self.SrcArchetype.ID, dict);
            }

            if (self.TransferDstInfo == null)
            {
                self.TypeRegistrar.RegisterBundle<T>();
                var bundleInfo = self.TypeRegistrar.GetBundleInfo<T>();
                var dstArchetype = self.ArchetypeManager.GetOrCreateArchetype(self.SrcArchetype.TypeIdList.Except(bundleInfo.Select(x => x.typeId)));

                self.TransferDstInfo = new(dstArchetype, []);
                dict.Add(ptr, self.TransferDstInfo);
            }
        }

        internal void RemoveComponentsTupleTransferInfo<T>() where T : unmanaged
        {
            nint ptr;
            unsafe
            {
                ptr = (nint)(delegate* <Commands, void>)&RemoveComponentsTupleTransferInfo<T>;
            }

            if (self.ArchetypeRemovingTypeMap.TryGetValue(self.SrcArchetype.ID, out var dict))
            {
                self.TransferDstInfo = dict.GetValueOrDefault(ptr);
            }
            else
            {
                self.TransferDstInfo = null;
                dict = new();
                self.ArchetypeRemovingTypeMap.Add(self.SrcArchetype.ID, dict);
            }

            if (self.TransferDstInfo == null)
            {
                var typeIds = self.TypeRegistrar.RegisterTypesOfTuple<T>();
                var dstArchetype = self.ArchetypeManager.GetOrCreateArchetype(self.SrcArchetype.TypeIdList.Except(typeIds));

                self.TransferDstInfo = new(dstArchetype, []);
                dict.Add(ptr, self.TransferDstInfo);
            }
        }
    }
}
