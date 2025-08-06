using System.Diagnostics;
using System.Runtime.InteropServices;

namespace lychee.collections;

public struct TableLayout
{
    public int MaxAlignment;
    
    public TypeInfo[] TypeInfoList;
}

public sealed class Table(TableLayout layout, int chunkSizeBytes)
{
    private List<MemoryChunk> chunks = [];

    private TableLayout layout = layout;

    private int chunkSizeBytes = chunkSizeBytes;

    private int chunkCapacity = ComputeChunkSize(chunkSizeBytes, layout);

    public Table(TableLayout layout) : this(layout, 16384)
    {
        
    }

    private static int ComputeChunkSize(int chunkSizeBytes, TableLayout layout)
    {
        var typeInfoList = layout.TypeInfoList;
        var offset = typeInfoList[^1].Offset + typeInfoList[^1].Size;
        
        return chunkSizeBytes / (offset + offset % layout.MaxAlignment);
    }

    internal unsafe T* GetData<T>(int typeIdx, ref MemoryChunk chunk, int indexInChunk)
    {
        var typeInfo = layout.TypeInfoList[typeIdx];
        var ptr = (byte*)chunk.Data;
        
        return (T*)(ptr + (typeInfo.Offset * chunkCapacity + typeInfo.Size * indexInChunk));
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
}

public struct MemoryChunk(int capacity) : IDisposable
{
    public unsafe void* Data { get; private set; } = null;

    public int Size { get; set; } = 0;

    public int Capacity { get; } = capacity;

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
