using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;

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

    private readonly List<TableMemoryChunk> chunks = [];

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
        lock (this)
        {
            if (chunks.Count == 0)
            {
                CreateNewChunk();
                return 0;
            }

            if (!chunks[lastAvailableViewIndex].IsFull)
            {
                return lastAvailableViewIndex;
            }

            for (var i = 0; i < chunks.Count; i++)
            {
                if (!chunks[i].IsFull)
                {
                    lastAvailableViewIndex = i;
                    return lastAvailableViewIndex;
                }
            }

            CreateNewChunk();
            lastAvailableViewIndex = chunks.Count - 1;

            return lastAvailableViewIndex;
        }
    }

    public (int chunkIdx, int idx) Reserve()
    {
        var idx = chunks[lastAvailableViewIndex].Reserve();

        while (idx != -1)
        {
            GetFirstAvailableViewIdx();
            idx = chunks[lastAvailableViewIndex].Reserve();
        }

        return (lastAvailableViewIndex, idx);
    }

    public unsafe void* GetPtr(int typeIdx, int chunkIdx, int indexInChunk)
    {
        var typeInfo = Layout.TypeInfoList[typeIdx];
        var ptr = (byte*)chunks[chunkIdx].Data;

        return ptr + (typeInfo.Offset * chunkCapacity + typeInfo.Size * indexInChunk);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe void* GetLastPtr(int typeIdx, int chunkIdx)
    {
        var typeInfo = Layout.TypeInfoList[typeIdx];
        var chunk = chunks[chunkIdx];
        var ptr = (byte*)chunk.Data;

        return ptr + (typeInfo.Offset * chunkCapacity + typeInfo.Size * chunk.Size);
    }

    public (int, int) GetChunkAndIndex(int idx)
    {
        var chunkIdx = 0;

        while (idx >= chunks[chunkIdx].Size)
        {
            idx -= chunks[chunkIdx].Size;
            chunkIdx++;
        }

        return (chunkIdx, idx);
    }

    public IEnumerable<(nint ptr, int size)> IterateOfTypeAmongChunk(int typeIdx)
    {
        var typeInfo = Layout.TypeInfoList[typeIdx];

        foreach (var chunk in chunks)
        {
            nint ptr;

            unsafe
            {
                ptr = (nint)chunk.Data + typeInfo.Offset * chunkCapacity;
            }

            yield return (ptr, chunk.Size);
        }
    }

#endregion

#region Private methods

    private void CreateNewChunk()
    {
        var chunk = new TableMemoryChunk(chunkCapacity);
        chunk.Chunk.Alloc(chunkSizeBytes);

        chunks.Add(chunk);
    }

#endregion

#region IDisposable Member

    public void Dispose()
    {
        foreach (var chunk in chunks)
        {
            chunk.Dispose();
        }
    }

#endregion
}

public sealed class TableMemoryChunk(int capacity) : IDisposable
{
    internal MemoryChunk Chunk = new();

    internal int Size;

    internal readonly int Capacity = capacity;

    private volatile int reserve;

    private ConcurrentStack<int> holeIndices = new();

    public bool IsFull => Size + reserve == Capacity;

    public unsafe void* Data => Chunk.Data;

#region Public methods

    /// <summary>
    /// Try to reserve one extra slot in chunk.
    /// </summary>
    /// <returns>Whether succeed</returns>
    public int Reserve()
    {
        var newVal = Interlocked.Increment(ref reserve);

        if (Size + newVal > Capacity)
        {
            Interlocked.Decrement(ref reserve);
            return -1;
        }

        return newVal;
    }

    public void CommitReserved()
    {
        Size += reserve;
        reserve = 0;
    }

    public void MarkRemove(int idx)
    {
        Debug.Assert(idx >= 0 && idx < Size);
        holeIndices.Push(idx);
    }

    public void CommitRemove()
    {
        holeIndices.Clear();
    }

#endregion

#region IDisposable Member

    public void Dispose()
    {
        Chunk.Dispose();
    }

#endregion
}
