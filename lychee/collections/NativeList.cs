using System.Collections;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace lychee.collections;

public sealed class NativeList<T> : IDisposable, IEnumerable<T> where T : unmanaged
{
    private unsafe T* data;

    private int size;

    private int capacity;

    public bool IsFull => size == capacity;

    public bool IsEmpty => size == 0;

    public int Count => size;

    public int Capacity
    {
        get => capacity;
        set => EnsureCapacity(value);
    }

    public ref T this[int id]
    {
        get
        {
            Debug.Assert((uint)id < (uint)size);

            unsafe
            {
                return ref data[id];
            }
        }
    }

    public NativeList()
    {
    }

    public NativeList(int capacity)
    {
        EnsureCapacity(capacity);
    }

    public NativeList(T[] array)
    {
        EnsureCapacity(array.Length);
        size = array.Length;

        unsafe
        {
            var span = new Span<T>(data, array.Length);
            array.CopyTo(span);
        }
    }

    ~NativeList()
    {
        Dispose();
    }

    public void Add(in T item)
    {
        if (IsFull)
        {
            if (capacity != 0)
            {
                EnsureCapacity(capacity * 2);
            }
            else
            {
                EnsureCapacity(16);
            }
        }

        unsafe
        {
            data[size++] = item;
        }
    }

    public void Clear()
    {
        size = 0;
    }

    public void Fill(in T item)
    {
        unsafe
        {
            var span = new Span<T>(data, size);
            span.Fill(item);
        }
    }

    public void Fill(int begin, int end, in T item)
    {
        if ((uint)begin > (uint)end || (uint)end > (uint)size)
        {
            throw new ArgumentOutOfRangeException(nameof(begin) + "/" + nameof(end), "Invalid range specified");
        }

        unsafe
        {
            var span = new Span<T>(data, size);
            span.Slice(begin, end - begin).Fill(item);
        }
    }

    public delegate void ForEachDelegate(ref T item);

    public void ForEach(ForEachDelegate del)
    {
        unsafe
        {
            for (var i = 0; i < size; i++)
            {
                del(ref data[i]);
            }
        }
    }

    public void ForEach(Action<T> act)
    {
        unsafe
        {
            for (var i = 0; i < size; i++)
            {
                act(data[i]);
            }
        }
    }

    public void Remove(int index)
    {
        if (index < 0 || index >= size)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        if (index == size - 1)
        {
            RemoveLast();
            return;
        }

        unsafe
        {
            for (var i = index; i < size - 1; i++)
            {
                data[i] = data[i + 1];
            }
        }

        size--;
    }

    public void RemoveLast()
    {
        if (size == 0)
        {
            throw new InvalidOperationException("List is empty");
        }

        size--;
    }

    public void Resize(int newLength, in T item)
    {
        if (newLength < size)
        {
            size = newLength;
            return;
        }

        EnsureCapacity(newLength);

        unsafe
        {
            var span = new Span<T>(data, newLength);
            span.Slice(size, newLength - size).Fill(item);
        }

        size = newLength;
    }

    public void ShirkToFit()
    {
        if (capacity > size)
        {
            unsafe
            {
                if (size != 0)
                {
                    var ptr = NativeMemory.Alloc((nuint)(size * sizeof(T)));
                    NativeMemory.Copy(data, ptr, (nuint)(size * sizeof(T)));
                    NativeMemory.Free(data);

                    data = (T*)ptr;
                }
                else
                {
                    NativeMemory.Free(data);
                    data = null;
                    capacity = 0;
                }
            }
        }
    }

    private unsafe void EnsureCapacity(int capacity)
    {
        if (capacity < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), "Capacity must be non-negative");
        }

        if (capacity < this.capacity)
        {
            return;
        }

        this.capacity = Math.Max(this.capacity * 2, capacity);

        if (data != null)
        {
            var ptr = NativeMemory.Alloc((nuint)(this.capacity * sizeof(T)));
            NativeMemory.Copy(data, ptr, (nuint)(size * sizeof(T)));
            NativeMemory.Free(data);

            data = (T*)ptr;
        }
        else
        {
            data = (T*)NativeMemory.Alloc((nuint)(this.capacity * sizeof(T)));
        }
    }

    public void Dispose()
    {
        unsafe
        {
            if (data != null)
            {
                NativeMemory.Free(data);
                data = null;
                size = 0;
                capacity = 0;
            }
        }
    }

    public IEnumerator<T> GetEnumerator()
    {
        return new Enumerator(this);
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return ((IEnumerable)this).GetEnumerator();
    }

    private struct Enumerator(NativeList<T> list) : IEnumerator<T>
    {
        private int index = -1;

        public T Current => list[index];

        object IEnumerator.Current => Current;

        public bool MoveNext()
        {
            index++;
            return index < list.size;
        }

        public void Reset()
        {
            index = -1;
        }

        public void Dispose()
        {
        }
    }
}
