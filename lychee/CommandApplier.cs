using System.Runtime.InteropServices;
using lychee.interfaces;

namespace lychee;

/// <summary>
/// Applies recorded commands to the world, serially, at a commit point.
/// Commands run in buffer order and per-buffer issue order; component hooks fire synchronously while a
/// command is applied, and any structural change a hook enqueues is drained right after that command.
/// </summary>
internal sealed class CommandApplier(App app)
{
#region Fields

    private readonly EntityPool entityPool = app.World.EntityPool;

    private readonly ArchetypeManager archetypeManager = app.World.ArchetypeManager;

    private readonly TypeRegistrar typeRegistrar = app.TypeRegistrar;

    // Structural changes enqueued from a hook. Two buffers alternate so draining one can accept the next
    // batch without touching the buffer being walked.
    private Commands hookCommands = new(app);

    private Commands drainCommands = new(app);

    // The source id -> new ref mapping for the subtree one Copy command is cloning. Reset per Copy command.
    private readonly Dictionary<int, EntityRef> cloneMap = [];

    // Reused during Remove* and Alter to hold the removed types the entity actually had. Command application
    // is single-threaded and neither method reenters, so one buffer is enough.
    private readonly List<int> removedListBuffer = [];

#endregion

#region Internal Methods

    /// <summary>
    /// Applies every command of every buffer, in buffer order and per-buffer issue order, then compacts
    /// the archetypes and reclaims removed entity ids.
    /// </summary>
    /// <param name="buffers">The command buffers to apply.</param>
    public unsafe void Apply(IReadOnlyList<Commands> buffers)
    {
        hookCommands.Queue.Clear();
        drainCommands.Queue.Clear();
        cloneMap.Clear();

        foreach (var buffer in buffers)
        {
            var queue = buffer.Queue;
            var length = queue.Length;

            if (length > 0)
            {
                fixed (byte* basePtr = queue.Buffer)
                {
                    var offset = 0;

                    while (offset < length)
                    {
                        var apply = *(delegate*<CommandApplier, byte*, void>*)(basePtr + offset);
                        var bodySize = *(int*)(basePtr + offset + 8);
                        var body = basePtr + offset + 16;

                        apply(this, body);
                        DrainHookQueue();

                        offset += CommandQueue.CommandStride(bodySize);
                    }
                }
            }

            queue.Clear();
        }

        archetypeManager.Commit(entityPool);
        entityPool.ReclaimId();
    }

    /// <summary>
    /// Invokes one component hook synchronously during apply.
    /// </summary>
    /// <param name="entity">The entity the event belongs to.</param>
    /// <param name="typeId">The component type id.</param>
    /// <param name="kind">The hook kind to fire.</param>
    /// <param name="component">A pointer to the component value the hook observes.</param>
    /// <param name="previous">A pointer to the previous value, for OnReplace, or null otherwise.</param>
    internal unsafe void InvokeHook(EntityRef entity, int typeId, ComponentHookKind kind, void* component, void* previous)
    {
        var invoker = typeRegistrar.GetComponentHook(typeId, kind);

        if (invoker == null)
        {
            return;
        }

        var context = new HookContext(entity, hookCommands, app) { Previous = previous };
        invoker(ref context, component);
    }

#endregion

#region Command Implementations

    internal void SpawnEntity(EntityRef entity)
    {
        entityPool.SetEntityInfo(entity, ArchetypeManager.EmptyArchetype, new EntityPos(0, 0));
        ArchetypeManager.EmptyArchetype.CommitAddEntity(entity);
    }

    internal void SpawnWith<T>(EntityRef entity, in T component) where T : unmanaged, IComponent
    {
        var typeId = typeRegistrar.GetTypeId<T>();
        var archetype = archetypeManager.GetAddEdge(ArchetypeManager.EmptyArchetype, typeId);
        var (chunkIdx, idx) = archetype.Reserve();

        archetype.PutComponentData(archetype.GetTypeIndex(typeId), chunkIdx, idx, in component);

        var pos = new EntityPos(chunkIdx, idx);
        entityPool.SetEntityInfo(entity, archetype, pos);
        archetype.CommitAddEntity(entity);

        InvokeAddHook(entity, archetype, pos, typeId);
    }

    internal void SpawnWithBundle<T>(EntityRef entity, in T bundle) where T : unmanaged, IComponentBundle
    {
        var bundleInfo = typeRegistrar.GetBundleInfo<T>();
        var archetype = archetypeManager.GetBundleAddEdge<T>(ArchetypeManager.EmptyArchetype);
        var (chunkIdx, idx) = archetype.Reserve();

        unsafe
        {
            fixed (T* bundlePtr = &bundle)
            {
                for (var i = 0; i < bundleInfo.Length; i++)
                {
                    var info = bundleInfo[i].info;
                    var ptr = archetype.Table.GetPtr(archetype.GetTypeIndex(bundleInfo[i].typeId), chunkIdx, idx);

                    NativeMemory.Copy((byte*)bundlePtr + info.Offset, ptr, (nuint)info.Size);
                }
            }
        }

        var pos = new EntityPos(chunkIdx, idx);
        entityPool.SetEntityInfo(entity, archetype, pos);
        archetype.CommitAddEntity(entity);

        InvokeAddHooksForBundle(entity, archetype, pos, bundleInfo);
    }

    /// <summary>
    /// Applies one <c>Copy</c> command: clones <paramref name="source"/> into <paramref name="newEntity"/>,
    /// recursively cloning linked subtrees when <paramref name="mode"/> is <see cref="CloneMode.Linked"/>.
    /// The tree is read at apply time, so it reflects the source's state now rather than when it was recorded.
    /// </summary>
    internal void CopyEntity(EntityRef source, EntityRef newEntity, CloneMode mode)
    {
        cloneMap.Clear();

        CloneRecursive(source, newEntity, mode);
    }

    internal void Insert<T>(EntityRef entity, in T component) where T : unmanaged, IComponent
    {
        if (!TryGetLive(entity, out var src, out var srcPos))
        {
            return;
        }

        var typeId = typeRegistrar.GetTypeId<T>();
        var dst = archetypeManager.GetAddEdge(src, typeId);

        if (dst == src)
        {
            return;
        }

        var (chunkIdx, idx) = dst.Reserve();

        dst.PutComponentData(dst.GetTypeIndex(typeId), chunkIdx, idx, in component);
        src.MoveDataTo(dst, srcPos.ChunkIdx, srcPos.Idx, chunkIdx, idx);
        src.MarkRemove(entity.ID, srcPos);

        var pos = new EntityPos(chunkIdx, idx);
        entityPool.SetEntityInfo(entity, dst, pos);
        dst.CommitAddEntity(entity);

        InvokeAddHook(entity, dst, pos, typeId);
    }

    internal void InsertBundle<T>(EntityRef entity, in T bundle) where T : unmanaged, IComponentBundle
    {
        if (!TryGetLive(entity, out var src, out var srcPos))
        {
            return;
        }

        var bundleInfo = typeRegistrar.GetBundleInfo<T>();
        var dst = archetypeManager.GetBundleAddEdge<T>(src);

        if (dst == src)
        {
            return;
        }

        var (chunkIdx, idx) = dst.Reserve();

        unsafe
        {
            fixed (T* bundlePtr = &bundle)
            {
                for (var i = 0; i < bundleInfo.Length; i++)
                {
                    var info = bundleInfo[i].info;
                    var ptr = dst.Table.GetPtr(dst.GetTypeIndex(bundleInfo[i].typeId), chunkIdx, idx);

                    NativeMemory.Copy((byte*)bundlePtr + info.Offset, ptr, (nuint)info.Size);
                }
            }
        }

        src.MoveDataTo(dst, srcPos.ChunkIdx, srcPos.Idx, chunkIdx, idx);
        src.MarkRemove(entity.ID, srcPos);

        var pos = new EntityPos(chunkIdx, idx);
        entityPool.SetEntityInfo(entity, dst, pos);
        dst.CommitAddEntity(entity);

        InvokeAddHooksForBundle(entity, dst, pos, bundleInfo);
    }

    internal void Remove<T>(EntityRef entity) where T : unmanaged, IComponent
    {
        if (!TryGetLive(entity, out var src, out var srcPos))
        {
            return;
        }

        var typeId = typeRegistrar.GetTypeId<T>();

        if (Array.IndexOf(src.TypeIdList, typeId) < 0)
        {
            return;
        }

        InvokeRemoveHook(entity, src, srcPos, typeId);
        Migrate(entity, src, srcPos, archetypeManager.GetRemoveEdge(src, typeId));
    }

    internal void RemoveBundle<T>(EntityRef entity) where T : unmanaged, IComponentBundle
    {
        if (!TryGetLive(entity, out var src, out var srcPos))
        {
            return;
        }

        var dst = archetypeManager.GetBundleRemoveEdge<T>(src);

        if (dst == src)
        {
            return;
        }

        // OnRemove fires before the migration moves the data, while the removed values are still readable.
        foreach (var (_, typeId) in typeRegistrar.GetBundleInfo<T>())
        {
            if (Array.IndexOf(src.TypeIdList, typeId) >= 0)
            {
                InvokeRemoveHook(entity, src, srcPos, typeId);
            }
        }

        Migrate(entity, src, srcPos, dst);
    }

    internal void RemoveTuple<T>(EntityRef entity) where T : unmanaged
    {
        if (!TryGetLive(entity, out var src, out var srcPos))
        {
            return;
        }

        var dst = archetypeManager.GetTupleRemoveEdge<T>(src);

        if (dst == src)
        {
            return;
        }

        // Deduplicate: a tuple may repeat a type, and each component must fire OnRemove only once.
        var removedList = removedListBuffer;
        removedList.Clear();

        foreach (var typeId in typeRegistrar.RegisterTypesOfTuple<T>())
        {
            if (Array.IndexOf(src.TypeIdList, typeId) >= 0 && !removedList.Contains(typeId))
            {
                removedList.Add(typeId);
            }
        }

        foreach (var typeId in removedList)
        {
            InvokeRemoveHook(entity, src, srcPos, typeId);
        }

        Migrate(entity, src, srcPos, dst);
    }

    internal void Replace<T>(EntityRef entity, in T component) where T : unmanaged, IComponent
    {
        if (!TryGetLive(entity, out var archetype, out var pos))
        {
            return;
        }

        var typeId = typeRegistrar.GetTypeId<T>();

        if (!archetype.TypeIdList.Contains(typeId))
        {
            return;
        }

        var hasHook = typeRegistrar.HasAnyComponentHook(typeId);

        unsafe
        {
            var previous = default(T);

            if (hasHook)
            {
                previous = ReadComponent<T>(archetype, pos, typeId);
            }

            archetype.PutComponentData(archetype.GetTypeIndex(typeId), pos.ChunkIdx, pos.Idx, in component);

            if (hasHook)
            {
                var previousPtr = &previous;
                InvokeHook(entity, typeId, ComponentHookKind.OnReplace, GetComponentPtr(archetype, typeId, pos), previousPtr);
            }
        }
    }

    internal void Despawn(EntityRef entity)
    {
        if (!TryGetLive(entity, out var archetype, out var pos))
        {
            return;
        }

        // OnRemove fires before the entity leaves: every component is still readable from its slot.
        InvokeRemoveHooksForArchetype(entity, archetype, pos);

        archetype.MarkRemove(entity.ID, pos);
        entityPool.CommitRemoveEntity(entity);
    }

    internal void NotifyReplaced<T>(EntityRef entity, int typeId, in T previous) where T : unmanaged, IComponent
    {
        if (!TryGetLive(entity, out var archetype, out var pos) || !archetype.TypeIdList.Contains(typeId))
        {
            return;
        }

        unsafe
        {
            var previousValue = previous;
            var previousPtr = &previousValue;

            InvokeHook(entity, typeId, ComponentHookKind.OnReplace, GetComponentPtr(archetype, typeId, pos), previousPtr);
        }
    }

    /// <summary>
    /// Parses and applies a variable-length <c>Alter</c> payload. The layout is:
    /// [entity id][generation][remove count][add count][blob offset]
    /// [remove type ids...][add type ids...][add blob offsets...][aligned value blob].
    /// </summary>
    internal unsafe void Alter(byte* body)
    {
        var entity = new EntityRef(*(int*)body, *(int*)(body + 4));
        var removeCount = *(int*)(body + 8);
        var addCount = *(int*)(body + 12);
        var blobOffset = *(int*)(body + 16);
        var removeIdsOffset = 20;
        var addIdsOffset = removeIdsOffset + removeCount * sizeof(int);
        var addOffsetsOffset = addIdsOffset + addCount * sizeof(int);

        if (!TryGetLive(entity, out var src, out var srcPos))
        {
            return;
        }

        var srcIds = src.TypeIdList;

        // Collect the removed types the entity actually has, then walk the cached edges: removes first, then
        // adds. The resulting archetype is the same as rebuilding the whole set, because archetypes are
        // canonical per type set.
        var removedList = removedListBuffer;
        removedList.Clear();

        for (var i = 0; i < removeCount; i++)
        {
            var typeId = *(int*)(body + removeIdsOffset + i * sizeof(int));

            if (Array.IndexOf(srcIds, typeId) >= 0 && !removedList.Contains(typeId))
            {
                removedList.Add(typeId);
            }
        }

        var dst = src;

        foreach (var typeId in removedList)
        {
            dst = archetypeManager.GetRemoveEdge(dst, typeId);
        }

        for (var i = 0; i < addCount; i++)
        {
            dst = archetypeManager.GetAddEdge(dst, *(int*)(body + addIdsOffset + i * sizeof(int)));
        }

        // Walking the edges collapses a same-type remove plus add (and any other no-op combination) back to
        // src; only then is the change an in-place replace.
        if (dst == src)
        {
            ApplyAlterInPlace(entity, src, srcPos, body, addCount, addIdsOffset, addOffsetsOffset, blobOffset);

            return;
        }

        // OnRemove fires before the migration moves the data, while the removed values are still readable.
        foreach (var typeId in removedList)
        {
            InvokeRemoveHook(entity, src, srcPos, typeId);
        }

        var (chunkIdx, idx) = dst.Reserve();

        src.MoveDataTo(dst, srcPos.ChunkIdx, srcPos.Idx, chunkIdx, idx);

        // Written after the move so it cannot be overwritten by copied common components.
        for (var i = 0; i < addCount; i++)
        {
            var typeId = *(int*)(body + addIdsOffset + i * sizeof(int));
            var offset = *(int*)(body + addOffsetsOffset + i * sizeof(int));
            var size = typeRegistrar.GetTypeInfo(typeId).Size;
            var dstPtr = dst.Table.GetPtr(dst.GetTypeIndex(typeId), chunkIdx, idx);

            NativeMemory.Copy(body + blobOffset + offset, dstPtr, (nuint)size);
        }

        src.MarkRemove(entity.ID, srcPos);

        var pos = new EntityPos(chunkIdx, idx);
        entityPool.SetEntityInfo(entity, dst, pos);
        dst.CommitAddEntity(entity);

        for (var i = 0; i < addCount; i++)
        {
            var typeId = *(int*)(body + addIdsOffset + i * sizeof(int));

            if (!typeRegistrar.HasAnyComponentHook(typeId))
            {
                continue;
            }

            var currentPtr = GetComponentPtr(dst, typeId, pos);
            var wasPresent = Array.IndexOf(srcIds, typeId) >= 0 && !removedList.Contains(typeId);

            if (wasPresent)
            {
                InvokeHook(entity, typeId, ComponentHookKind.OnReplace, currentPtr, GetComponentPtr(src, typeId, srcPos));
            }
            else
            {
                InvokeHook(entity, typeId, ComponentHookKind.OnAdd, currentPtr, null);
            }
        }
    }

#endregion

#region Private Methods

    private unsafe void CloneRecursive(EntityRef source, EntityRef newRef, CloneMode mode)
    {
        if (!TryGetLive(source, out var archetype, out var srcPos))
        {
            // The source was despawned before the copy applied: give the reserved id back.
            entityPool.ReclaimEntity(newRef);

            return;
        }

        cloneMap[source.ID] = newRef;

        var typeIdList = archetype.TypeIdList;
        var typeInfoList = archetype.Table.Layout.TypeInfoList;

        var (chunkIdx, idx) = archetype.Reserve();
        var pos = new EntityPos(chunkIdx, idx);

        if (archetype.Table.Layout.MaxAlignment != 0)
        {
            for (var i = 0; i < typeIdList.Length; i++)
            {
                var dstPtr = archetype.Table.GetPtr(i, chunkIdx, idx);

                if (app.World.IsTargetType(typeIdList[i]))
                {
                    // A relationship target owns native memory that must not be aliased, so the copy starts
                    // empty and the relationship hooks rebuild its contents.
                    NativeMemory.Clear(dstPtr, (nuint)typeInfoList[i].Size);

                    continue;
                }

                var srcPtr = archetype.Table.GetPtr(i, srcPos.ChunkIdx, srcPos.Idx);
                NativeMemory.Copy(srcPtr, dstPtr, (nuint)typeInfoList[i].Size);
            }
        }

        entityPool.SetEntityInfo(newRef, archetype, pos);
        archetype.CommitAddEntity(newRef);

        // Repoint copied relationship components before the add hooks run, so a linked clone points at the
        // copied parent instead of the original one.
        RemapRelationshipTargets(archetype, chunkIdx, idx, typeIdList);
        InvokeAddHooksForArchetype(newRef, archetype, pos);

        if (mode != CloneMode.Linked)
        {
            return;
        }

        CloneLinkedChildren(archetype, srcPos, typeIdList, mode);
    }

    private unsafe void RemapRelationshipTargets(Archetype archetype, int chunkIdx, int idx, int[] typeIdList)
    {
        for (var i = 0; i < typeIdList.Length; i++)
        {
            var typeId = typeIdList[i];

            if (!app.World.Relationships.TryGet(typeId, out var info) || info.RelationshipTypeId != typeId)
            {
                continue;
            }

            var ptr = archetype.Table.GetPtr(i, chunkIdx, idx);
            var target = info.GetTarget(ptr);

            if (cloneMap.TryGetValue(target.ID, out var mapped))
            {
                info.RemapTarget(ptr, mapped);
            }
        }
    }

    private unsafe void CloneLinkedChildren(Archetype archetype, EntityPos srcPos, int[] typeIdList, CloneMode mode)
    {
        for (var i = 0; i < typeIdList.Length; i++)
        {
            if (!app.World.TryGetTarget(typeIdList[i], out var info) || !info.LinkedSpawn)
            {
                continue;
            }

            var ptr = archetype.Table.GetPtr(i, srcPos.ChunkIdx, srcPos.Idx);
            var count = info.RelatedCount(ptr);

            if (count == 0)
            {
                continue;
            }

            // Snapshot: cloning a child attaches it to the copied parent, which mutates that parent's target
            // list while we are still walking the source's one.
            var children = new EntityRef[count];

            for (var c = 0; c < count; c++)
            {
                children[c] = info.GetRelated(ptr, c);
            }

            foreach (var child in children)
            {
                var childCopy = entityPool.ReserveEntity();

                CloneRecursive(child, childCopy, mode);
            }
        }
    }

    private unsafe void ApplyAlterInPlace(EntityRef entity, Archetype archetype, EntityPos pos, byte* body, int addCount, int addIdsOffset, int addOffsetsOffset, int blobOffset)
    {
        for (var i = 0; i < addCount; i++)
        {
            var typeId = *(int*)(body + addIdsOffset + i * sizeof(int));

            if (!archetype.TypeIdList.Contains(typeId))
            {
                continue;
            }

            var hasHook = typeRegistrar.HasAnyComponentHook(typeId);
            var offset = *(int*)(body + addOffsetsOffset + i * sizeof(int));
            var size = typeRegistrar.GetTypeInfo(typeId).Size;
            var dstPtr = GetComponentPtr(archetype, typeId, pos);

            var previous = hasHook ? ReadComponentBytes(typeId, dstPtr) : null;

            NativeMemory.Copy(body + blobOffset + offset, dstPtr, (nuint)size);

            if (hasHook)
            {
                fixed (byte* previousPtr = previous)
                {
                    InvokeHook(entity, typeId, ComponentHookKind.OnReplace, dstPtr, previousPtr);
                }
            }
        }
    }

    private void Migrate(EntityRef entity, Archetype src, EntityPos srcPos, Archetype dst)
    {
        var (chunkIdx, idx) = dst.Reserve();

        src.MoveDataTo(dst, srcPos.ChunkIdx, srcPos.Idx, chunkIdx, idx);
        src.MarkRemove(entity.ID, srcPos);

        var pos = new EntityPos(chunkIdx, idx);
        entityPool.SetEntityInfo(entity, dst, pos);
        dst.CommitAddEntity(entity);
    }

    private void InvokeAddHook(EntityRef entity, Archetype archetype, EntityPos pos, int typeId)
    {
        if (!typeRegistrar.HasAnyComponentHook(typeId))
        {
            return;
        }

        unsafe
        {
            InvokeHook(entity, typeId, ComponentHookKind.OnAdd, GetComponentPtr(archetype, typeId, pos), null);
        }
    }

    private void InvokeAddHooksForBundle(EntityRef entity, Archetype archetype, EntityPos pos, (TypeInfo info, int typeId)[] bundleInfo)
    {
        for (var i = 0; i < bundleInfo.Length; i++)
        {
            InvokeAddHook(entity, archetype, pos, bundleInfo[i].typeId);
        }
    }

    private void InvokeAddHooksForArchetype(EntityRef entity, Archetype archetype, EntityPos pos)
    {
        var typeIdList = archetype.TypeIdList;

        for (var i = 0; i < typeIdList.Length; i++)
        {
            InvokeAddHook(entity, archetype, pos, typeIdList[i]);
        }
    }

    private void InvokeRemoveHooksForArchetype(EntityRef entity, Archetype archetype, EntityPos pos)
    {
        var typeIdList = archetype.TypeIdList;

        for (var i = 0; i < typeIdList.Length; i++)
        {
            InvokeRemoveHook(entity, archetype, pos, typeIdList[i]);
        }
    }

    private void InvokeRemoveHook(EntityRef entity, Archetype archetype, EntityPos pos, int typeId)
    {
        if (!typeRegistrar.HasAnyComponentHook(typeId))
        {
            return;
        }

        unsafe
        {
            InvokeHook(entity, typeId, ComponentHookKind.OnRemove, GetComponentPtr(archetype, typeId, pos), null);
        }
    }

    private T ReadComponent<T>(Archetype archetype, EntityPos pos, int typeId) where T : unmanaged
    {
        unsafe
        {
            return *(T*)GetComponentPtr(archetype, typeId, pos);
        }
    }

    private unsafe byte[] ReadComponentBytes(int typeId, void* ptr)
    {
        var size = typeRegistrar.GetTypeInfo(typeId).Size;
        var buffer = new byte[size];

        fixed (byte* bufferPtr = buffer)
        {
            NativeMemory.Copy(ptr, bufferPtr, (nuint)size);
        }

        return buffer;
    }

    private bool TryGetLive(EntityRef entity, out Archetype archetype, out EntityPos pos)
    {
        archetype = null!;
        pos = default;

        if (!entityPool.CheckEntityValid(entity) || !entityPool.TryGetEntityInfo(entity, out var info))
        {
            return false;
        }

        archetype = info.Archetype;
        pos = info.Pos;

        return true;
    }

    private unsafe void* GetComponentPtr(Archetype archetype, int typeId, EntityPos pos)
    {
        if (archetype.Table.Layout.MaxAlignment == 0)
        {
            return null;
        }

        var size = typeRegistrar.GetTypeInfo(typeId).Size;
        var (basePtr, _) = archetype.GetChunkDataWithReservation(typeId, pos.ChunkIdx);

        return (void*)(basePtr + size * pos.Idx);
    }

    private unsafe void DrainHookQueue()
    {
        while (!hookCommands.Queue.IsEmpty)
        {
            (hookCommands, drainCommands) = (drainCommands, hookCommands);

            var queue = drainCommands.Queue;
            var length = queue.Length;

            if (length > 0)
            {
                fixed (byte* basePtr = queue.Buffer)
                {
                    var offset = 0;

                    while (offset < length)
                    {
                        var apply = *(delegate*<CommandApplier, byte*, void>*)(basePtr + offset);
                        var bodySize = *(int*)(basePtr + offset + 8);
                        var body = basePtr + offset + 16;

                        apply(this, body);

                        offset += CommandQueue.CommandStride(bodySize);
                    }
                }
            }

            queue.Clear();
        }
    }

#endregion
}

/// <summary>
/// The unmanaged bodies of every recorded command. Each body is written into a <see cref="CommandQueue"/>
/// followed by its apply function, which reads the body back through the matching pointer type.
/// </summary>
internal static class CommandAppliers
{
    public static unsafe void Spawn(CommandApplier applier, byte* body)
    {
        var command = *(SpawnCommand*)body;
        applier.SpawnEntity(command.Entity);
    }

    public static unsafe void SpawnWith<T>(CommandApplier applier, byte* body) where T : unmanaged, IComponent
    {
        var command = *(SpawnWithCommand<T>*)body;
        applier.SpawnWith(command.Entity, in command.Component);
    }

    public static unsafe void SpawnWithBundle<T>(CommandApplier applier, byte* body) where T : unmanaged, IComponentBundle
    {
        var command = *(SpawnWithBundleCommand<T>*)body;
        applier.SpawnWithBundle(command.Entity, in command.Bundle);
    }

    public static unsafe void Copy(CommandApplier applier, byte* body)
    {
        var command = *(CopyCommand*)body;
        applier.CopyEntity(command.Source, command.NewEntity, command.Mode);
    }

    public static unsafe void Insert<T>(CommandApplier applier, byte* body) where T : unmanaged, IComponent
    {
        var command = *(InsertCommand<T>*)body;
        applier.Insert(command.Entity, in command.Component);
    }

    public static unsafe void InsertBundle<T>(CommandApplier applier, byte* body) where T : unmanaged, IComponentBundle
    {
        var command = *(InsertBundleCommand<T>*)body;
        applier.InsertBundle(command.Entity, in command.Bundle);
    }

    public static unsafe void Remove<T>(CommandApplier applier, byte* body) where T : unmanaged, IComponent
    {
        var command = *(RemoveCommand*)body;
        applier.Remove<T>(command.Entity);
    }

    public static unsafe void RemoveBundle<T>(CommandApplier applier, byte* body) where T : unmanaged, IComponentBundle
    {
        var command = *(RemoveCommand*)body;
        applier.RemoveBundle<T>(command.Entity);
    }

    public static unsafe void RemoveTuple<T>(CommandApplier applier, byte* body) where T : unmanaged
    {
        var command = *(RemoveCommand*)body;
        applier.RemoveTuple<T>(command.Entity);
    }

    public static unsafe void Replace<T>(CommandApplier applier, byte* body) where T : unmanaged, IComponent
    {
        var command = *(ReplaceCommand<T>*)body;
        applier.Replace(command.Entity, in command.Component);
    }

    public static unsafe void Despawn(CommandApplier applier, byte* body)
    {
        var command = *(DespawnCommand*)body;
        applier.Despawn(command.Entity);
    }

    public static unsafe void NotifyReplaced<T>(CommandApplier applier, byte* body) where T : unmanaged, IComponent
    {
        var command = *(NotifyReplacedCommand<T>*)body;
        applier.NotifyReplaced(command.Entity, command.TypeId, in command.Previous);
    }

    public static unsafe void Alter(CommandApplier applier, byte* body)
    {
        applier.Alter(body);
    }
}

internal readonly struct SpawnCommand(EntityRef entity)
{
    public readonly EntityRef Entity = entity;
}

internal readonly struct SpawnWithCommand<T>(EntityRef entity, T component) where T : unmanaged, IComponent
{
    public readonly EntityRef Entity = entity;

    public readonly T Component = component;
}

internal readonly struct SpawnWithBundleCommand<T>(EntityRef entity, T bundle) where T : unmanaged, IComponentBundle
{
    public readonly EntityRef Entity = entity;

    public readonly T Bundle = bundle;
}

internal readonly struct CopyCommand(EntityRef source, EntityRef newEntity, CloneMode mode)
{
    public readonly EntityRef Source = source;

    public readonly EntityRef NewEntity = newEntity;

    public readonly CloneMode Mode = mode;
}

internal readonly struct InsertCommand<T>(EntityRef entity, T component) where T : unmanaged, IComponent
{
    public readonly EntityRef Entity = entity;

    public readonly T Component = component;
}

internal readonly struct InsertBundleCommand<T>(EntityRef entity, T bundle) where T : unmanaged, IComponentBundle
{
    public readonly EntityRef Entity = entity;

    public readonly T Bundle = bundle;
}

internal readonly struct RemoveCommand(EntityRef entity)
{
    public readonly EntityRef Entity = entity;
}

internal readonly struct ReplaceCommand<T>(EntityRef entity, T component) where T : unmanaged, IComponent
{
    public readonly EntityRef Entity = entity;

    public readonly T Component = component;
}

internal readonly struct DespawnCommand(EntityRef entity)
{
    public readonly EntityRef Entity = entity;
}

internal readonly struct NotifyReplacedCommand<T>(EntityRef entity, int typeId, T previous) where T : unmanaged, IComponent
{
    public readonly EntityRef Entity = entity;

    public readonly int TypeId = typeId;

    public readonly T Previous = previous;
}
