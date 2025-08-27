using System.Collections;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace lychee.collections;

public struct TableLayout(TypeInfo[] typeInfoList)
{
    public readonly int MaxAlignment = typeInfoList.Length == 0 ? 0 : typeInfoList.Max(x => x.Alignment);

    public readonly TypeInfo[] TypeInfoList = typeInfoList;
}

public sealed class Table : IDisposable
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

#region Internal methods

    internal unsafe T* GetPtr<T>(int typeIdx, ref MemoryChunk chunk, int indexInChunk) where T : unmanaged
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

    internal IEnumerable<nint> IterateOverType(int typeIdx)
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

    internal readonly struct TableIterable(Table table, int typeIdx) : IEnumerable<nint>
    {
        private readonly TypeInfo info = table.Layout.TypeInfoList[typeIdx];

        internal struct Iterator(Table table, TypeInfo info) : IEnumerator<nint>
        {
            private nint current;

            private nint chunkEnd;

            private int chunkIdx;

            public bool MoveNext()
            {
                if (current == chunkEnd)
                {
                    if (chunkIdx < table.chunks.Count)
                    {
                        chunkIdx++;
                        var chunk = table.chunks[chunkIdx];
                        unsafe
                        {
                            current = (nint)((byte*)chunk.Data + info.Offset * table.chunkCapacity);
                            chunkEnd = current + chunk.Size * info.Size;
                        }
                    }
                    else
                    {
                        return false;
                    }
                }
                else
                {
                    current += info.Size;
                }

                return true;
            }

            public void Reset()
            {
                var chunk = table.chunks[0];
                unsafe
                {
                    current = (nint)((byte*)chunk.Data + info.Offset * table.chunkCapacity);
                    chunkEnd = current + chunk.Size * info.Size;
                }
            }

            nint IEnumerator<nint>.Current => current;

            object IEnumerator.Current => current;

            public void Dispose()
            {
            }
        }

        public IEnumerator<nint> GetEnumerator()
        {
            return new Iterator(table, info);
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }

    internal IEnumerable<nint> IterateOverType2(int typeIdx)
    {
        return new TableIterable(this, typeIdx);
    }
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
