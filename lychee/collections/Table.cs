using System.Diagnostics;
using System.Runtime.InteropServices;

namespace lychee.collections;

public struct TableLayout
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

    private readonly List<MemoryChunk> chunks = [];

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

    public unsafe T* GetPtr<T>(int typeIdx, ref MemoryChunk chunk, int indexInChunk) where T : unmanaged
    {
        var typeInfo = Layout.TypeInfoList[typeIdx];
        var ptr = (byte*)chunk.Data;

        return (T*)(ptr + (typeInfo.Offset * chunkCapacity + typeInfo.Size * indexInChunk));
    }

    public unsafe void* GetPtr(int typeIdx, ref MemoryChunk chunk, int indexInChunk)
    {
        var typeInfo = Layout.TypeInfoList[typeIdx];
        var ptr = (byte*)chunk.Data;

        return ptr + (typeInfo.Offset * chunkCapacity + typeInfo.Size * indexInChunk);
    }

    public (MemoryChunk, int) GetChunkAndIndex(int idx)
    {
        Debug.Assert(idx >= 0);

        var chunkIdx = idx / chunkCapacity;

        Debug.Assert(chunkIdx < chunks.Count);

        var idxInChunk = idx % chunkCapacity;

        Debug.Assert(idxInChunk < chunks[chunkIdx].Size);

        return (chunks[chunkIdx], idxInChunk);
    }

    public MemoryChunk GetOrCreateLastChunk()
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

    public IEnumerable<nint> IterateOverComp(int typeIdx)
    {
        var typeInfo = Layout.TypeInfoList[typeIdx];

        foreach (var chunk in chunks)
        {
            nint ptr;

            unsafe
            {
                ptr = (nint)((byte*)chunk.Data + typeInfo.Offset * chunkCapacity);
            }

            for (var i = 0; i < chunk.Size; i++)
            {
                yield return ptr;
                ptr += typeInfo.Size;
            }
        }
    }

    public Span<MemoryChunk> GetChunkSpan()
    {
        return CollectionsMarshal.AsSpan(chunks);
    }

#endregion

#region IDisposable Member

    public void Dispose()
    {
        foreach (var memoryChunk in chunks)
        {
            memoryChunk.Dispose();
        }
    }

#endregion
}

public struct MemoryChunk(int capacity) : IDisposable
{
    public unsafe void* Data { get; private set; } = null;

    public int Size = 0;

    public readonly int Capacity = capacity;

    public bool isFull => Size == Capacity;

#region Public Methods

    public void Alloc(int sizeBytes)
    {
        unsafe
        {
            Debug.Assert(Data == null);
            Data = NativeMemory.AlignedAlloc((nuint)sizeBytes, 64);
        }
    }

#endregion

#region IDisposable Member

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

#endregion
}
