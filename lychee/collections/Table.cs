using System.Diagnostics;
using System.Runtime.InteropServices;

namespace lychee.collections;

public struct TableLayout(TypeInfo[] typeInfoList)
{
    public readonly int MaxAlignment = typeInfoList.Max(x => x.Alignment);

    public readonly TypeInfo[] TypeInfoList = typeInfoList;
}

public sealed class Table
{
    private readonly int chunkCapacity;

    private readonly List<MemoryChunk> chunks = [];

    private readonly int chunkSizeBytes;

    public readonly TableLayout Layout;

#region Constructors

    public Table(TableLayout layout, int chunkSizeBytesHint = 16384)
    {
        Layout = layout;
        chunkSizeBytes = chunkSizeBytesHint;

        var typeInfoList = layout.TypeInfoList;
        var offset = typeInfoList[^1].Offset + typeInfoList[^1].Size;
        var lastByteOffset = offset + offset % layout.MaxAlignment;

        while (lastByteOffset > chunkSizeBytes)
        {
            chunkSizeBytes *= 2;
        }

        chunkCapacity = chunkSizeBytes / lastByteOffset;
    }

#endregion

#region Internal methods

    internal unsafe T* GetPtr<T>(int typeIdx, ref MemoryChunk chunk, int indexInChunk)
    {
        var typeInfo = Layout.TypeInfoList[typeIdx];
        var ptr = (byte*)chunk.Data;

        return (T*)(ptr + (typeInfo.Offset * chunkCapacity + typeInfo.Size * indexInChunk));
    }

    internal unsafe void* GetPtr(int typeIdx, ref MemoryChunk chunk, int indexInChunk)
    {
        var typeInfo = Layout.TypeInfoList[typeIdx];
        var ptr = (byte*)chunk.Data;

        return ptr + (typeInfo.Offset * chunkCapacity + typeInfo.Size * indexInChunk);
    }

    internal (MemoryChunk, int) GetChunkAndIndex(int idx)
    {
        Debug.Assert(idx >= 0);

        var chunkIdx = idx / chunkCapacity;

        Debug.Assert(chunkIdx < chunks.Count);

        var idxInChunk = idx % chunkCapacity;

        Debug.Assert(idxInChunk < chunks[chunkIdx].Size);

        return (chunks[chunkIdx], idxInChunk);
    }

    internal MemoryChunk GetOrCreateLastChunk()
    {
        if (chunks.Count > 0)
        {
            return chunks[^1];
        }

        var chunk = new MemoryChunk();
        chunk.Alloc(chunkSizeBytes);

        chunks.Add(chunk);

        return chunk;
    }

#endregion
}

public struct MemoryChunk(int capacity) : IDisposable
{
    public unsafe void* Data { get; private set; } = null;

    public int Size = 0;

    public readonly int Capacity = capacity;

    public bool isFull => Size == Capacity;

    internal void Alloc(int sizeBytes)
    {
        unsafe
        {
            Debug.Assert(Data == null);
            Data = NativeMemory.AlignedAlloc((nuint)sizeBytes, 64);
        }
    }

    public void Dispose()
    {
        unsafe
        {
            if (Data != null)
            {
                NativeMemory.AlignedFree(Data);
                Data = null;
            }
        }
    }
}
