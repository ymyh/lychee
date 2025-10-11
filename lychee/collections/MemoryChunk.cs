using System.Runtime.InteropServices;

namespace lychee.collections;

/// <summary>
/// Represents a native memory chunk.
/// </summary>
public struct MemoryChunk() : IDisposable
{
    public unsafe void* Data { get; private set; } = null;

    public int Size { get; private set; } = 0;

#region Public Methods

    public void Alloc(int sizeBytes)
    {
        unsafe
        {
            if (Data != null)
            {
                throw new InvalidOperationException("MemoryChunk already allocated");
            }

            Data = NativeMemory.AlignedAlloc((nuint)sizeBytes, 64);
            Size = sizeBytes;
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
                Size = 0;
            }
        }
    }

#endregion
}