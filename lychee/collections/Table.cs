using System.Diagnostics;
using System.Runtime.InteropServices;

namespace lychee.collections;

public class Table()
{
    private List<MemoryChunk> chunks = [];

    private TypeInfo[] typeInfoList = [];

    private int chunkSize = 16384;

    public Table(int chunkSize) : this()
    {
        this.chunkSize = chunkSize;
    }

}

public struct MemoryChunk(int capacity) : IDisposable
{
    private unsafe void* data = null;

    public int Size { get; set; } = 0;

    private int capacity = capacity;

    public bool isFull => Size == capacity;

    internal void Alloc()
    {
        unsafe
        {
            Debug.Assert(data == null);
            data = NativeMemory.AlignedAlloc(16384, 64);
        }
    }

    public void Dispose()
    {
        unsafe
        {
            if (data != null)
            {
                NativeMemory.AlignedFree(data);
                data = null;
            }
        }
    }
}
