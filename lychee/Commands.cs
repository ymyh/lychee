using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using lychee.interfaces;

namespace lychee;

/// <summary>
/// Provides deferred entity modification operations for ECS systems.
/// Every structural change is only recorded here; the world is untouched until the queue is applied at a
/// commit point. This keeps entity locations stable while systems run and lets several buffers act on the
/// same entity in order instead of overwriting one another.
/// </summary>
public sealed unsafe class Commands(App app)
{
#region Fields

    private readonly EntityPool entityPool = app.World.EntityPool;

    private readonly ArchetypeManager archetypeManager = app.World.ArchetypeManager;

    internal readonly TypeRegistrar TypeRegistrar = app.TypeRegistrar;

    internal readonly CommandQueue Queue = new();

    // Reused scratch state for AlterComponents. A Commands is single-writer, so one set is enough.
    internal readonly List<int> AlterRemoveTypeIdList = [];

    internal readonly List<int> AlterAddTypeIdList = [];

    internal readonly List<int> AlterAddOffsetList = [];

    internal byte[] AlterAddBlob = new byte[128];

    internal int AlterAddBlobSize;

    internal bool AlterHasAdded;

    private byte[] alterPayloadBuffer = new byte[128];

#endregion

#region Public Methods

    /// <summary>
    /// Records creating a new entity without components.
    /// </summary>
    /// <returns>A handle to the reserved entity.</returns>
    public Entity CreateEntity()
    {
        var entityRef = entityPool.ReserveEntity();
        Queue.Enqueue(&CommandAppliers.Spawn, new SpawnCommand(entityRef));

        return new Entity(this, entityRef);
    }

    /// <summary>
    /// Records creating a new entity with a single component attached, skipping the empty archetype migration.
    /// </summary>
    /// <param name="component">The component value to attach.</param>
    /// <typeparam name="T">The component type, must be unmanaged and implement IComponent.</typeparam>
    /// <returns>A handle to the reserved entity.</returns>
    public Entity CreateEntityWithComponent<T>(in T component) where T : unmanaged, IComponent
    {
        TypeRegistrar.RegisterComponent<T>();

        var entityRef = entityPool.ReserveEntity();
        Queue.Enqueue(&CommandAppliers.SpawnWith<T>, new SpawnWithCommand<T>(entityRef, component));

        return new Entity(this, entityRef);
    }

    /// <summary>
    /// Records creating a new entity with a component bundle attached, skipping the empty archetype migration.
    /// </summary>
    /// <param name="bundle">The component bundle containing the components to attach.</param>
    /// <typeparam name="T">The component bundle type, must be unmanaged and implement IComponentBundle.</typeparam>
    /// <returns>A handle to the reserved entity.</returns>
    public Entity CreateEntityWithComponents<T>(in T bundle) where T : unmanaged, IComponentBundle
    {
        TypeRegistrar.RegisterBundle<T>();

        var entityRef = entityPool.ReserveEntity();
        Queue.Enqueue(&CommandAppliers.SpawnWithBundle<T>, new SpawnWithBundleCommand<T>(entityRef, bundle));

        return new Entity(this, entityRef);
    }

    /// <summary>
    /// Records a copy of an existing entity, taken from the source's state when the command is applied.
    /// </summary>
    /// <param name="entity">The entity to copy.</param>
    /// <param name="mode">Whether to copy the entity alone or its whole linked subtree.</param>
    /// <returns>A handle to the reserved copy entity.</returns>
    public Entity CopyEntity(in Entity entity, CloneMode mode = CloneMode.Shallow)
    {
        var newEntityRef = entityPool.ReserveEntity();
        Queue.Enqueue(&CommandAppliers.Copy, new CopyCommand(entity.Ref, newEntityRef, mode));

        return new Entity(this, newEntityRef);
    }

    /// <summary>
    /// Records removing an entity.
    /// </summary>
    /// <param name="entityRef">The entity to remove.</param>
    public void RemoveEntity(EntityRef entityRef)
    {
        Queue.Enqueue(&CommandAppliers.Despawn, new DespawnCommand(entityRef));
    }

    /// <summary>
    /// Records removing an entity.
    /// </summary>
    /// <param name="entity">The entity to remove.</param>
    public void RemoveEntity(in Entity entity)
    {
        RemoveEntity(entity.Ref);
    }

    /// <summary>
    /// Records adding a component to an entity. The entity moves to a new archetype when applied.
    /// </summary>
    /// <param name="entity">The target entity.</param>
    /// <param name="component">The component value to add.</param>
    /// <typeparam name="T">The component type, must be unmanaged and implement IComponent.</typeparam>
    public void AddComponent<T>(Entity entity, in T component) where T : unmanaged, IComponent
    {
        AddComponent(entity.Ref, in component);
    }

    /// <summary>
    /// Records adding multiple components as a bundle to an entity in a single migration.
    /// </summary>
    /// <param name="entity">The target entity.</param>
    /// <param name="bundle">The component bundle containing the components to add.</param>
    /// <typeparam name="T">The component bundle type, must be unmanaged and implement IComponentBundle.</typeparam>
    public void AddComponents<T>(Entity entity, in T bundle) where T : unmanaged, IComponentBundle
    {
        TypeRegistrar.RegisterBundle<T>();
        Queue.Enqueue(&CommandAppliers.InsertBundle<T>, new InsertBundleCommand<T>(entity.Ref, bundle));
    }

    /// <summary>
    /// Records removing a component from an entity.
    /// </summary>
    /// <param name="entity">The target entity.</param>
    /// <typeparam name="T">The component type to remove, must be unmanaged and implement IComponent.</typeparam>
    public void RemoveComponent<T>(Entity entity) where T : unmanaged, IComponent
    {
        RemoveComponent<T>(entity.Ref);
    }

    /// <summary>
    /// Records removing all components defined in a bundle from an entity.
    /// </summary>
    /// <param name="entity">The target entity.</param>
    /// <typeparam name="T">The component bundle type, must be unmanaged and implement IComponentBundle.</typeparam>
    public void RemoveComponents<T>(Entity entity) where T : unmanaged, IComponentBundle
    {
        TypeRegistrar.RegisterBundle<T>();
        Queue.Enqueue(&CommandAppliers.RemoveBundle<T>, new RemoveCommand(entity.Ref));
    }

    /// <summary>
    /// Records removing all components defined in a tuple from an entity.
    /// </summary>
    /// <param name="entity">The target entity.</param>
    /// <typeparam name="T">The tuple type containing the component types to remove, must be unmanaged.</typeparam>
    public void RemoveComponentsTuple<T>(Entity entity) where T : unmanaged
    {
        TypeRegistrar.RegisterTypesOfTuple<T>();
        Queue.Enqueue(&CommandAppliers.RemoveTuple<T>, new RemoveCommand(entity.Ref));
    }

    /// <summary>
    /// Records overwriting a component the entity already has, without moving archetypes.
    /// Reads the previous value when applied so OnReplace hooks observe both the old and the new value.
    /// </summary>
    /// <param name="entity">The target entity.</param>
    /// <param name="component">The new component value.</param>
    /// <typeparam name="T">The component type, must be unmanaged and implement IComponent.</typeparam>
    public void ReplaceComponent<T>(Entity entity, in T component) where T : unmanaged, IComponent
    {
        TypeRegistrar.RegisterComponent<T>();
        Queue.Enqueue(&CommandAppliers.Replace<T>, new ReplaceCommand<T>(entity.Ref, component));
    }

    /// <summary>
    /// A delegate for configuring entity alterations in a single archetype migration.
    /// </summary>
    /// <param name="context">The entity alteration context builder.</param>
    public delegate void EntityAlterContextDelegate(ref EntityAlterContext context);

    /// <summary>
    /// Records multiple component additions and removals on an entity as a single archetype migration.
    /// Remove operations must be called before Add operations within the configuration callback.
    /// </summary>
    /// <param name="entity">The target entity.</param>
    /// <param name="configure">A callback that configures the alterations using the EntityAlter builder.</param>
    public void AlterComponents(Entity entity, EntityAlterContextDelegate configure)
    {
        AlterRemoveTypeIdList.Clear();
        AlterAddTypeIdList.Clear();
        AlterAddOffsetList.Clear();
        AlterAddBlobSize = 0;
        AlterHasAdded = false;

        var context = new EntityAlterContext(this, entity);
        configure(ref context);

        if (AlterRemoveTypeIdList.Count == 0 && AlterAddTypeIdList.Count == 0)
        {
            return;
        }

        EnqueueAlter(entity);
    }

    /// <summary>
    /// Gets an entity handle by its reference. Returns false for an unknown or removed reference, and for a
    /// reserved entity that has not been spawned yet.
    /// </summary>
    /// <param name="entityRef">The entity reference to look up.</param>
    /// <param name="entity">When this method returns, contains the entity if found; otherwise, the default value.</param>
    /// <returns>True if the entity was found; otherwise, false.</returns>
    public bool GetEntityByRef(EntityRef entityRef, out Entity entity)
    {
        if (!entityPool.CheckEntityValid(entityRef) || !entityPool.TryGetEntityInfo(entityRef, out _))
        {
            entity = default;

            return false;
        }

        entity = new(this, entityRef);

        return true;
    }

    /// <summary>
    /// Checks whether an entity reference's generation is still current for its id. A reserved id that has
    /// not been spawned is not necessarily reported as living, so use <see cref="GetEntityByRef"/> when the
    /// question is whether the entity can be resolved.
    /// </summary>
    /// <param name="entityRef">The entity reference to validate.</param>
    /// <returns>True if the entity reference's generation is current; otherwise, false.</returns>
    public bool CheckEntityValid(EntityRef entityRef)
    {
        return entityPool.CheckEntityValid(entityRef);
    }

    /// <summary>
    /// Checks whether an entity has a specific component as it exists in the world.
    /// </summary>
    /// <typeparam name="T">The component type to check, must be unmanaged and implement IComponent.</typeparam>
    /// <param name="entity">The entity to check.</param>
    /// <returns>True if the entity has the component; otherwise, false.</returns>
    public bool WithComponent<T>(Entity entity) where T : unmanaged, IComponent
    {
        return HasLiveComponent<T>(entity.Ref);
    }

    /// <summary>
    /// Checks whether an entity does not have a specific component as it exists in the world.
    /// </summary>
    /// <typeparam name="T">The component type to check, must be unmanaged and implement IComponent.</typeparam>
    /// <param name="entity">The entity to check.</param>
    /// <returns>True if the entity does not have the component; otherwise, false.</returns>
    public bool WithoutComponent<T>(Entity entity) where T : unmanaged, IComponent
    {
        return !HasLiveComponent<T>(entity.Ref);
    }

    /// <summary>
    /// Gets a reference to a component of an entity as it exists in the world.
    /// </summary>
    /// <typeparam name="T">The component type, must be unmanaged and implement IComponent.</typeparam>
    /// <param name="entity">The entity to read from.</param>
    /// <returns>A reference to the component.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the entity is not alive or lacks the component.</exception>
    public ref T GetEntityComponent<T>(Entity entity) where T : unmanaged, IComponent
    {
        return ref GetLiveComponent<T>(entity.Ref);
    }

    /// <summary>
    /// Notifies OnReplace hooks for a component a system overwrote through an <c>out</c> parameter.
    /// Called by generated code after <c>Execute</c> returns; do not call it manually.
    /// The current value is read from the world when the command is applied; the passed one is ignored.
    /// </summary>
    /// <typeparam name="T">The component type, must be unmanaged and implement IComponent.</typeparam>
    /// <param name="typeId">The registered type id of <typeparamref name="T"/>.</param>
    /// <param name="entity">The entity whose component was overwritten.</param>
    /// <param name="previous">The value the component had before <c>Execute</c>.</param>
    /// <param name="current">The value the component has after <c>Execute</c>; ignored at apply time.</param>
    public void InvokeReplaceHooks<T>(int typeId, EntityRef entity, in T previous, in T current) where T : unmanaged, IComponent
    {
        if (!TypeRegistrar.HasAnyComponentHook(typeId))
        {
            return;
        }

        Queue.Enqueue(&CommandAppliers.NotifyReplaced<T>, new NotifyReplacedCommand<T>(entity, typeId, previous));
    }

    /// <summary>
    /// Applies this buffer's recorded commands to the world immediately.
    /// </summary>
    public void Commit()
    {
        app.World.CommandApplier.Apply([this]);
    }

#endregion

#region Internal Methods

    internal void AddComponent<T>(EntityRef entityRef, in T component) where T : unmanaged, IComponent
    {
        TypeRegistrar.RegisterComponent<T>();
        Queue.Enqueue(&CommandAppliers.Insert<T>, new InsertCommand<T>(entityRef, component));
    }

    internal void RemoveComponent<T>(EntityRef entityRef) where T : unmanaged, IComponent
    {
        TypeRegistrar.RegisterComponent<T>();
        Queue.Enqueue(&CommandAppliers.Remove<T>, new RemoveCommand(entityRef));
    }

    internal bool IsAlive(EntityRef entity)
    {
        return entityPool.CheckEntityValid(entity) && entityPool.TryGetEntityInfo(entity, out _);
    }

    internal bool HasLiveComponent<T>(EntityRef entity) where T : unmanaged, IComponent
    {
        var typeId = TypeRegistrar.GetTypeId<T>();

        return typeId >= 0
            && entityPool.CheckEntityValid(entity)
            && entityPool.TryGetEntityInfo(entity, out var info)
            && info.Archetype.TypeIdList.Contains(typeId);
    }

    internal bool TryGetLiveComponent<T>(EntityRef entity, out T component) where T : unmanaged, IComponent
    {
        component = default;

        if (!TryResolveComponent(entity, TypeRegistrar.GetTypeId<T>(), out var archetype, out var pos))
        {
            return false;
        }

        component = GetComponentRef<T>(archetype, pos, TypeRegistrar.GetTypeId<T>());

        return true;
    }

    internal ref T GetLiveComponent<T>(EntityRef entity) where T : unmanaged, IComponent
    {
        var typeId = TypeRegistrar.GetTypeId<T>();

        if (!TryResolveComponent(entity, typeId, out var archetype, out var pos))
        {
            throw new InvalidOperationException($"Entity {entity.ID} does not resolve to a live component of type {typeof(T).Name}.");
        }

        return ref GetComponentRef<T>(archetype, pos, typeId);
    }

    internal unsafe void WriteAlterAddComponent(int typeId, void* source, int size)
    {
        var offset = (AlterAddBlobSize + 15) & ~15;

        EnsureAlterBlob(offset + size);

        fixed (byte* blobPtr = AlterAddBlob)
        {
            NativeMemory.Copy(source, blobPtr + offset, (nuint)size);
        }

        AlterAddTypeIdList.Add(typeId);
        AlterAddOffsetList.Add(offset);
        AlterAddBlobSize = offset + size;
    }

#endregion

#region Private Methods

    private void EnqueueAlter(Entity entity)
    {
        unsafe
        {
            fixed (byte* blobPtr = AlterAddBlob)
            {
                var removeCount = AlterRemoveTypeIdList.Count;
                var addCount = AlterAddTypeIdList.Count;
                var removeIdsOffset = 20;
                var addIdsOffset = removeIdsOffset + removeCount * sizeof(int);
                var addOffsetsOffset = addIdsOffset + addCount * sizeof(int);
                var blobOffset = (addOffsetsOffset + addCount * sizeof(int) + 15) & ~15;
                var payloadSize = blobOffset + AlterAddBlobSize;

                EnsureAlterPayload(payloadSize);

                fixed (byte* payloadPtr = alterPayloadBuffer)
                {
                    var refValue = entity.Ref;
                    *(int*)payloadPtr = refValue.ID;
                    *(int*)(payloadPtr + 4) = refValue.Generation;
                    *(int*)(payloadPtr + 8) = removeCount;
                    *(int*)(payloadPtr + 12) = addCount;
                    *(int*)(payloadPtr + 16) = blobOffset;

                    for (var i = 0; i < removeCount; i++)
                    {
                        *(int*)(payloadPtr + removeIdsOffset + i * sizeof(int)) = AlterRemoveTypeIdList[i];
                    }

                    for (var i = 0; i < addCount; i++)
                    {
                        *(int*)(payloadPtr + addIdsOffset + i * sizeof(int)) = AlterAddTypeIdList[i];
                        *(int*)(payloadPtr + addOffsetsOffset + i * sizeof(int)) = AlterAddOffsetList[i];
                    }

                    if (AlterAddBlobSize > 0)
                    {
                        NativeMemory.Copy(blobPtr, payloadPtr + blobOffset, (nuint)AlterAddBlobSize);
                    }

                    Queue.EnqueueRaw(&CommandAppliers.Alter, payloadPtr, payloadSize);
                }
            }
        }
    }

    private void EnsureAlterBlob(int required)
    {
        if (AlterAddBlob.Length >= required)
        {
            return;
        }

        var capacity = AlterAddBlob.Length;

        while (capacity < required)
        {
            capacity *= 2;
        }

        var newBlob = new byte[capacity];
        Array.Copy(AlterAddBlob, newBlob, AlterAddBlobSize);
        AlterAddBlob = newBlob;
    }

    private void EnsureAlterPayload(int required)
    {
        if (alterPayloadBuffer.Length >= required)
        {
            return;
        }

        var capacity = alterPayloadBuffer.Length;

        while (capacity < required)
        {
            capacity *= 2;
        }

        alterPayloadBuffer = new byte[capacity];
    }

    private bool TryResolveComponent(EntityRef entity, int typeId, out Archetype archetype, out EntityPos pos)
    {
        archetype = null!;
        pos = default;

        if (typeId < 0
            || !entityPool.CheckEntityValid(entity)
            || !entityPool.TryGetEntityInfo(entity, out var info)
            || !info.Archetype.TypeIdList.Contains(typeId))
        {
            return false;
        }

        archetype = info.Archetype;
        pos = info.Pos;

        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ref T GetComponentRef<T>(Archetype archetype, EntityPos pos, int typeId) where T : unmanaged, IComponent
    {
        var (ptr, size) = archetype.GetChunkDataWithReservation(typeId, pos.ChunkIdx);

        System.Diagnostics.Debug.Assert((uint)pos.Idx < (uint)size);

        unsafe
        {
            return ref ((T*)ptr)[pos.Idx];
        }
    }

#endregion
}

/// <summary>
/// A builder struct for configuring entity alterations in a single archetype migration.
/// Remove operations must be called before Add operations. Each Add can only be issued once.
/// </summary>
public struct EntityAlterContext
{
#region Fields

    internal Entity Entity;

    private readonly Commands commands;

#endregion

#region Constructors

    internal EntityAlterContext(Commands commands, Entity entity)
    {
        this.commands = commands;
        Entity = entity;
    }

#endregion

#region Public Methods

    /// <summary>
    /// Records removing a single component from the entity.
    /// Must be called before any Add operation.
    /// </summary>
    /// <typeparam name="T">The component type to remove.</typeparam>
    /// <exception cref="InvalidOperationException">Thrown when an Add was already issued.</exception>
    public readonly void Remove<T>() where T : unmanaged, IComponent
    {
        if (commands.AlterHasAdded)
        {
            throw new InvalidOperationException("Remove operation can only be called before Add operations.");
        }

        commands.AlterRemoveTypeIdList.Add(commands.TypeRegistrar.RegisterComponent<T>());
    }

    /// <summary>
    /// Records removing all components defined in a bundle from the entity.
    /// Must be called before any Add operation.
    /// </summary>
    /// <typeparam name="T">The component bundle type whose components to remove.</typeparam>
    /// <exception cref="InvalidOperationException">Thrown when an Add was already issued.</exception>
    public readonly void RemoveBundle<T>() where T : unmanaged, IComponentBundle
    {
        if (commands.AlterHasAdded)
        {
            throw new InvalidOperationException("Remove operation can only be called before Add operations.");
        }

        commands.TypeRegistrar.RegisterBundle<T>();

        foreach (var (_, typeId) in commands.TypeRegistrar.GetBundleInfo<T>())
        {
            commands.AlterRemoveTypeIdList.Add(typeId);
        }
    }

    /// <summary>
    /// Records removing all components defined in a tuple from the entity.
    /// Must be called before any Add operation.
    /// </summary>
    /// <typeparam name="T">The tuple type containing component types to remove.</typeparam>
    /// <exception cref="InvalidOperationException">Thrown when an Add was already issued.</exception>
    public readonly void RemoveTuple<T>() where T : unmanaged
    {
        if (commands.AlterHasAdded)
        {
            throw new InvalidOperationException("Remove operation can only be called before Add operations.");
        }

        foreach (var typeId in commands.TypeRegistrar.RegisterTypesOfTuple<T>())
        {
            commands.AlterRemoveTypeIdList.Add(typeId);
        }
    }

    /// <summary>
    /// Records adding a single component to the entity.
    /// Can only be called once, and after any Remove operation.
    /// </summary>
    /// <typeparam name="T">The component type to add.</typeparam>
    /// <param name="component">The component value.</param>
    /// <exception cref="InvalidOperationException">Thrown when an Add was already issued.</exception>
    public void Add<T>(in T component) where T : unmanaged, IComponent
    {
        if (commands.AlterHasAdded)
        {
            throw new InvalidOperationException("Add operation can only be called once.");
        }

        var typeId = commands.TypeRegistrar.RegisterComponent<T>();

        unsafe
        {
            fixed (T* componentPtr = &component)
            {
                commands.WriteAlterAddComponent(typeId, componentPtr, sizeof(T));
            }
        }

        commands.AlterHasAdded = true;
    }

    /// <summary>
    /// Records adding multiple components as a bundle to the entity in a single migration.
    /// Can only be called once, and after any Remove operation.
    /// </summary>
    /// <typeparam name="T">The component bundle type.</typeparam>
    /// <param name="bundle">The bundle containing component values.</param>
    /// <exception cref="InvalidOperationException">Thrown when an Add was already issued.</exception>
    public void AddBundle<T>(in T bundle) where T : unmanaged, IComponentBundle
    {
        if (commands.AlterHasAdded)
        {
            throw new InvalidOperationException("Add operation can only be called once.");
        }

        commands.TypeRegistrar.RegisterBundle<T>();
        var bundleInfo = commands.TypeRegistrar.GetBundleInfo<T>();

        unsafe
        {
            fixed (T* bundlePtr = &bundle)
            {
                for (var i = 0; i < bundleInfo.Length; i++)
                {
                    var info = bundleInfo[i].info;
                    commands.WriteAlterAddComponent(bundleInfo[i].typeId, (byte*)bundlePtr + info.Offset, info.Size);
                }
            }
        }

        commands.AlterHasAdded = true;
    }

#endregion
}
