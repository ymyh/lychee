using System.Runtime.InteropServices;
using lychee.collections;
using lychee.interfaces;

namespace lychee;

internal sealed class EntityTransferInfo(Archetype archetype, int[] typeIndices, int viewIdx)
{
    public readonly Archetype Archetype = archetype;

    public readonly int[] TypeIndices = typeIndices;

    public int ViewIdx = viewIdx;
}

public sealed class EntityCommander(World world) : IDisposable
{
#region Fields

    internal readonly EntityPool EntityPool = world.EntityPool;

    internal readonly ArchetypeManager ArchetypeManager = world.ArchetypeManager;

    internal readonly TypeRegistry TypeRegistry = world.TypeRegistry;

    internal readonly Dictionary<nint, SparseMap<EntityTransferInfo>> SrcArchetypeAddingTypeDict = new();

    internal Archetype SrcArchetype = null!;

    internal EntityTransferInfo? TransferInfo;

    internal bool ArchetypeChanged;

#endregion

#region Public Methods

    public Entity NewEntity()
    {
        var entity = EntityPool.NewEntity();
        return entity;
    }

    public bool RemoveEntity(Entity entity)
    {
        return EntityPool.RemoveEntity(entity);
    }

    public void RemoveComponent<T>(Entity entity) where T : unmanaged
    {
    }

    public void RemoveComponents<T>(Entity entity) where T : unmanaged
    {
    }

#endregion

#region Internal Methods

    internal void ChangeSrcArchetype(Archetype archetype)
    {
        if (TransferInfo != null)
        {
            Monitor.Exit(TransferInfo.Archetype);
        }

        ArchetypeChanged = true;
        SrcArchetype = archetype;
    }

#endregion

#region IDisposable Member

    public void Dispose()
    {
        foreach (var item in SrcArchetypeAddingTypeDict)
        {
            item.Value.Dispose();
        }
    }

#endregion
}

public static class EntityCommandBufferExtensions
{
    extension(EntityCommander self)
    {
        public bool AddComponent<T>(Entity entity, in T component) where T : unmanaged, IComponent
        {
            if (!self.EntityPool.GetEntityInfo(entity, out var entityInfo))
            {
                return false;
            }

            if (self.ArchetypeChanged)
            {
                nint ptr;
                unsafe
                {
                    ptr = (nint)(delegate* <EntityCommander, Entity, in T, bool>)&AddComponent<T>;
                }

                if (self.SrcArchetypeAddingTypeDict.TryGetValue(ptr, out var map))
                {
                    map.TryGetValue(self.SrcArchetype.ID, out self.TransferInfo);
                }
                else
                {
                    map = new();
                    self.SrcArchetypeAddingTypeDict.Add(ptr, map);
                }

                if (self.TransferInfo is null)
                {
                    var typeId = self.TypeRegistry.Register<T>();
                    var dstArchetype = self.SrcArchetype.GetInsertCompTargetArchetype(typeId) ??
                                       self.ArchetypeManager.GetArchetype(
                                           self.ArchetypeManager.GetOrCreateArchetype(
                                               self.SrcArchetype.TypeIdList.Append(typeId)));

                    self.TransferInfo = new(dstArchetype, [dstArchetype.GetTypeIndex(typeId)],
                        dstArchetype.Table.GetFirstAvailableViewIdx());
                    map.Add(self.SrcArchetype.ID, self.TransferInfo);
                }

                Monitor.Enter(self.TransferInfo!.Archetype);
            }

            self.TransferInfo!.Archetype.Table.ReserveOne(self.TransferInfo.ViewIdx);
            self.TransferInfo.Archetype.PutPartialData(entityInfo, self.TransferInfo.TypeIndices[0],
                in component);
            self.SrcArchetype.MoveDataTo(entityInfo, self.TransferInfo.Archetype);

            return true;
        }

        public bool AddComponents<T>(Entity entity, in T bundle) where T : unmanaged, IComponentBundle
        {
            if (!self.EntityPool.GetEntityInfo(entity, out var entityInfo))
            {
                return false;
            }

            if (self.ArchetypeChanged)
            {
                nint ptr;
                unsafe
                {
                    ptr = (nint)(delegate* <EntityCommander, Entity, in T, bool>)&AddComponents<T>;
                }

                if (self.SrcArchetypeAddingTypeDict.TryGetValue(ptr, out var map))
                {
                    map.TryGetValue(self.SrcArchetype.ID, out self.TransferInfo);
                }
                else
                {
                    map = new();
                    self.SrcArchetypeAddingTypeDict.Add(ptr, map);
                }

                if (self.TransferInfo is null)
                {
                    if (T.StructInfo == null)
                    {
                        self.TypeRegistry.RegisterBundle<T>();
                    }

                    Archetype dstArchetype = null!;
                    var srcArchetype = self.SrcArchetype;

                    foreach (var (offset, typeId) in T.StructInfo!)
                    {
                        dstArchetype = srcArchetype.GetInsertCompTargetArchetype(typeId) ??
                                       self.ArchetypeManager.GetArchetype(
                                           self.ArchetypeManager.GetOrCreateArchetype(
                                               srcArchetype.TypeIdList.Append(typeId)));

                        srcArchetype = dstArchetype;
                    }

                    self.TransferInfo = new(dstArchetype,
                        T.StructInfo.Select(x => dstArchetype.GetTypeIndex(x.typeId)).ToArray(),
                        dstArchetype.Table.GetFirstAvailableViewIdx());
                    map.Add(self.SrcArchetype.ID, self.TransferInfo);
                }

                Monitor.Enter(self.TransferInfo!.Archetype);
            }

            if (!self.TransferInfo!.Archetype.Table.ReserveOne(self.TransferInfo.ViewIdx))
            {
                self.TransferInfo.ViewIdx = self.TransferInfo.Archetype.Table.GetFirstAvailableViewIdx();
                self.TransferInfo.Archetype.Table.ReserveOne(self.TransferInfo.ViewIdx);
            }

            for (var i = 0; i < self.TransferInfo.TypeIndices.Length; i++)
            {
                unsafe
                {
                    var info = T.StructInfo![i].info;
                    var ptr = self.TransferInfo.Archetype.Table.GetLastPtr(self.TransferInfo.TypeIndices[i],
                        self.TransferInfo.ViewIdx);

                    fixed (T* bundlePtr = &bundle)
                    {
                        var componentPtr = (byte*)bundlePtr + info.Offset;
                        NativeMemory.Copy(componentPtr, ptr, (nuint)info.Size);
                    }
                }
            }

            self.SrcArchetype.MoveDataTo(entityInfo, self.TransferInfo.Archetype);

            return true;
        }
    }
}
