using System.Diagnostics;

namespace lychee.collections;

public sealed class TableLayout
{
    public readonly int MaxAlignment;

    public readonly TypeInfo[] TypeInfoList;

    public TableLayout(TypeInfo[] typeInfoList)
    {
        var offset = 0;
        for (var i = 0; i < typeInfoList.Length; i++)
        {
            var info = typeInfoList[i];
            if (offset % info.Alignment != 0)
            {
                offset += info.Alignment - (offset % info.Alignment);
            }

            info.Offset = offset;
            typeInfoList[i] = info;

            offset += info.Size;
        }

        MaxAlignment = typeInfoList.Length == 0 ? 0 : typeInfoList.Max(x => x.Alignment);
        TypeInfoList = typeInfoList;
    }
}

public sealed class Table : IDisposable
{
    public readonly TableLayout Layout;

    private readonly int chunkCapacity;

    private readonly int chunkSizeBytes;

    private readonly List<TableView> chunkViews = [];

#region Constructors

    public Table(TableLayout layout, int chunkSizeBytesHint = 16384)
    {
        Layout = layout;
        chunkSizeBytes = chunkSizeBytesHint;

        var typeInfoList = layout.TypeInfoList;
        if (typeInfoList.Length > 0)
        {
            var offset = typeInfoList[^1].Offset + typeInfoList[^1].Size;
            var lastByteOffset = offset + offset % layout.MaxAlignment;

            while (lastByteOffset > chunkSizeBytes)
            {
                chunkSizeBytes *= 2;
            }

            chunkCapacity = chunkSizeBytes / lastByteOffset;
        }
    }

#endregion

#region Public methods

    public unsafe T* GetPtr<T>(int typeIdx, int chunkIdx, int indexInChunk) where T : unmanaged
    {
        var typeInfo = Layout.TypeInfoList[typeIdx];
        var ptr = (byte*)chunkViews[chunkIdx].Data;

        return (T*)(ptr + (typeInfo.Offset * chunkCapacity + typeInfo.Size * indexInChunk));
    }

    public unsafe void* GetPtr(int typeIdx, int chunkIdx, int indexInChunk)
    {
        var typeInfo = Layout.TypeInfoList[typeIdx];
        var ptr = (byte*)chunkViews[chunkIdx].Data;

        return ptr + (typeInfo.Offset * chunkCapacity + typeInfo.Size * indexInChunk);
    }

    public unsafe void* GetLastPtr(int typeIdx, int chunkIdx)
    {
        var typeInfo = Layout.TypeInfoList[typeIdx];
        var view = chunkViews[chunkIdx];
        var ptr = (byte*)view.Data;

        return ptr + (typeInfo.Offset * chunkCapacity + typeInfo.Size * view.Size);
    }

    public (int, int) GetChunkAndIndex(int idx)
    {
        Debug.Assert(idx >= 0);

        var chunkIdx = idx / chunkCapacity;

        Debug.Assert(chunkIdx < chunkViews.Count);

        var idxInChunk = idx % chunkCapacity;

        Debug.Assert(idxInChunk < chunkViews[chunkIdx].Size);

        return (chunkIdx, idxInChunk);
    }

    public int GetOrCreateLastChunk()
    {
        if (chunkViews.Count > 0)
        {
            return chunkViews.Count - 1;
        }

        var view = new TableView(chunkCapacity);
        view.Chunk.Alloc(chunkSizeBytes);

        chunkViews.Add(view);

        return chunkViews.Count - 1;
    }

    public IEnumerable<(nint, int)> IterateOfTypeAmongChunk(int typeIdx)
    {
        var typeInfo = Layout.TypeInfoList[typeIdx];

        foreach (var view in chunkViews)
        {
            nint ptr;

            unsafe
            {
                ptr = (nint)view.Data + typeInfo.Offset * chunkCapacity;
            }

            yield return (ptr, view.Size);
        }
    }

#endregion

#region IDisposable Member

    public void Dispose()
    {
        foreach (var view in chunkViews)
        {
            view.Dispose();
        }
    }

#endregion
}

public struct TableView(int capacity) : IDisposable
{
    public MemoryChunk Chunk = new();

    public int Size = 0;

    public int Reserve = 0;

    public readonly int Capacity = capacity;

    public bool isFull => Size + Reserve == Capacity;

    public unsafe void* Data => Chunk.Data;


#region IDisposable Member

    public void Dispose()
    {
        Chunk.Dispose();
    }

#endregion
}