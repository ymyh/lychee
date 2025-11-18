using System.Collections.Concurrent;
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

            if (info.Alignment == 0)
            {
                continue;
            }

            if (offset % info.Alignment != 0)
            {
                offset += info.Alignment - (offset % info.Alignment);
            }

            MaxAlignment = Math.Max(MaxAlignment, info.Alignment);

            info.Offset = offset;
            typeInfoList[i] = info;

            offset += info.Size;
        }

        TypeInfoList = typeInfoList;
    }
}

public sealed class Table : IDisposable
{
    public readonly TableLayout Layout;

    internal readonly List<TableMemoryChunk> Chunks = [];

    internal readonly int ChunkCapacity;

    private readonly int chunkSizeBytes;

    private int lastAvailableViewIndex;

    private volatile bool isInitialized;

#region Constructors

    public Table(TableLayout layout, int chunkSizeBytesHint = 16384)
    {
        Layout = layout;

        if (layout.MaxAlignment != 0)
        {
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

                ChunkCapacity = chunkSizeBytes / lastByteOffset;
            }
        }
    }

#endregion

#region Public methods

    public (int chunkIdx, int idx) Reserve()
    {
        if (!isInitialized)
        {
            lock (this)
            {
                if (!isInitialized)
                {
                    CreateChunk();
                }

                isInitialized = true;
            }
        }

        var idx = Chunks[lastAvailableViewIndex].Reserve();

        while (idx == -1)
        {
            GetFirstAvailableViewIdx();
            idx = Chunks[lastAvailableViewIndex].Reserve();
        }

        return (lastAvailableViewIndex, idx);
    }

    public void CommitReserved()
    {
        foreach (var chunk in Chunks)
        {
            chunk.CommitReserved();
        }
    }

    public unsafe void* GetPtr(int typeIdx, int chunkIdx, int indexInChunk)
    {
        var typeInfo = Layout.TypeInfoList[typeIdx];
        var ptr = (byte*)Chunks[chunkIdx].Data;

        return ptr + (typeInfo.Offset * ChunkCapacity + typeInfo.Size * indexInChunk);
    }

    public IEnumerable<(nint ptr, int size)> IterateOfTypeAmongChunk(int typeIdx)
    {
        var typeInfo = Layout.TypeInfoList[typeIdx];

        foreach (var chunk in Chunks)
        {
            nint ptr;

            unsafe
            {
                ptr = (nint)chunk.Data + typeInfo.Offset * ChunkCapacity;
            }

            yield return (ptr, chunk.Size);
        }
    }

    public (nint ptr, int size) GetChunkData(int typeIdx, int chunkIdx)
    {
        var typeInfo = Layout.TypeInfoList[typeIdx];

        unsafe
        {
            var ptr = (nint)Chunks[chunkIdx].Data + typeInfo.Offset * ChunkCapacity;
            return (ptr, Chunks[chunkIdx].Size);
        }
    }

#endregion

#region Internal methods

    internal (int, int) GetChunkAndIndex(int idx)
    {
        var chunkIdx = 0;

        while (idx >= Chunks[chunkIdx].Size)
        {
            idx -= Chunks[chunkIdx].Size;
            chunkIdx++;
        }

        return (chunkIdx, idx);
    }

    internal int CalcTotalOffset(int chunkIdx, int idx)
    {
        Debug.Assert((uint)chunkIdx < (uint)Chunks.Count);

        var result = 0;

        for (var i = 0; i < chunkIdx; i++)
        {
            var chunk = Chunks[i];
            result += chunk.Size;
        }

        Debug.Assert(idx < Chunks[chunkIdx].Size);

        return result + idx;
    }

#endregion

#region Private methods

    private void CreateChunk()
    {
        if (chunkSizeBytes > 0)
        {
            var chunk = new TableMemoryChunk(ChunkCapacity);
            chunk.Chunk.Alloc(chunkSizeBytes);

            Chunks.Add(chunk);
        }
    }

    private int GetFirstAvailableViewIdx()
    {
        lock (this)
        {
            if (!Chunks[lastAvailableViewIndex].IsFull)
            {
                return lastAvailableViewIndex;
            }

            for (var i = 0; i < Chunks.Count; i++)
            {
                if (!Chunks[i].IsFull)
                {
                    lastAvailableViewIndex = i;
                    return lastAvailableViewIndex;
                }
            }

            CreateChunk();
            lastAvailableViewIndex = Chunks.Count - 1;

            return lastAvailableViewIndex;
        }
    }

#endregion

#region IDisposable Member

    public void Dispose()
    {
        foreach (var chunk in Chunks)
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

    internal volatile int Reservation;

    private ConcurrentStack<int> holeIndices = new();

    public bool IsFull => Size + Reservation == Capacity;

    public unsafe void* Data => Chunk.Data;

#region Public methods

    /// <summary>
    /// Try to reserve one extra slot in chunk.
    /// </summary>
    /// <returns>Whether succeed</returns>
    public int Reserve()
    {
        var newVal = Interlocked.Increment(ref Reservation);

        if (Size + newVal > Capacity)
        {
            Interlocked.Decrement(ref Reservation);
            return -1;
        }

        return Size + newVal - 1;
    }

    public void CommitReserved()
    {
        Size += Reservation;
        Reservation = 0;
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
