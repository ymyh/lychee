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

    private int lastAvailableViewIndex;

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

    public int GetFirstAvailableViewIdx()
    {
        if (!chunkViews[lastAvailableViewIndex].IsFull)
        {
            return lastAvailableViewIndex;
        }

        for (var i = 0; i < chunkViews.Count; i++)
        {
            if (!chunkViews[i].IsFull)
            {
                lastAvailableViewIndex = i;
                return lastAvailableViewIndex;
            }
        }

        CreateNewChunk();
        lastAvailableViewIndex = chunkViews.Count - 1;

        return lastAvailableViewIndex;
    }

    public bool ReserveOne(int viewIdx)
    {
        return chunkViews[viewIdx].ReserveOne();
    }

    public void PutData<T>(TableView view, in T data) where T : unmanaged
    {
    }

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
        var chunkIdx = 0;

        while (idx >= chunkViews[chunkIdx].Size)
        {
            idx -= chunkViews[chunkIdx].Size;
            chunkIdx++;
        }

        return (chunkIdx, idx);
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

#region Private methods

    private void CreateNewChunk()
    {
        var view = new TableView(chunkCapacity);
        view.Chunk.Alloc(chunkSizeBytes);

        chunkViews.Add(view);
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

public sealed class TableView(int capacity) : IDisposable
{
    public MemoryChunk Chunk = new();

    public int Size = 0;

    public readonly int Capacity = capacity;

    private int reserve = 0;

    public bool IsFull => Size + reserve == Capacity;

    public unsafe void* Data => Chunk.Data;


#region Public methods

    /// <summary>
    /// Try to reserve one extra slot in chunk.
    /// </summary>
    /// <returns>Whether succeed</returns>
    public bool ReserveOne()
    {
        if (IsFull)
        {
            return false;
        }

        reserve++;
        return true;
    }

#endregion

#region IDisposable Member

    public void Dispose()
    {
        Chunk.Dispose();
    }

#endregion
}
