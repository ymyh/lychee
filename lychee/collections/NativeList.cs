using System.Collections;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace lychee.collections;

public sealed class NativeList<T>() : IDisposable, IList<T>, IReadOnlyList<T> where T : unmanaged
{
    private unsafe T* data;

    private int size;

    private int capacity;

#region Public properties

    public bool IsFull => size == capacity;

    public bool IsEmpty => size == 0;

    public int Count => size;

    public bool IsReadOnly => false;

    public int Capacity
    {
        get => capacity;
        set => EnsureCapacity(value);
    }

    public T this[int id]
    {
        get
        {
            Debug.Assert((uint)id < (uint)size);

            unsafe
            {
                return data[id];
            }
        }

        set
        {
            Debug.Assert((uint)id < (uint)size);

            unsafe
            {
                data[id] = value;
            }
        }
    }

    public static implicit operator Span<T>(NativeList<T> list) => list.AsSpan();

#endregion

#region Constructors & Destructors

    public NativeList(int capacity) : this()
    {
        EnsureCapacity(capacity);
    }

    public NativeList(T[] array) : this()
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

#endregion

    public void Add(in T value)
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
            data[size++] = value;
        }
    }

    public void Add(T value)
    {
        Add(in value);
    }

    public void AddRange(IEnumerable<T> collection)
    {
        switch (collection)
        {
            case NativeList<T> nativeList:
                EnsureCapacity(size + nativeList.Count);

                unsafe
                {
                    var span = new Span<T>(data + size, nativeList.Count);
                    size += nativeList.Count;

                    nativeList.CopyTo(span);
                }

                break;
            case T[] arr:
                EnsureCapacity(size + arr.Length);

                unsafe
                {
                    var span = new Span<T>(data + size, arr.Length);
                    size += arr.Length;

                    arr.CopyTo(span);
                }

                break;
            case List<T> list:
                EnsureCapacity(size + list.Count);

                unsafe
                {
                    var span = new Span<T>(data + size, list.Count);
                    size += list.Count;

                    list.CopyTo(span);
                }

                break;

            default:
            {
                foreach (var item in collection)
                {
                    Add(item);
                }

                break;
            }
        }
    }

    public Span<T> AsSpan()
    {
        unsafe
        {
            return new(data, size);
        }
    }

    public void Clear()
    {
        size = 0;
    }

    public bool Contains(T item)
    {
        return IndexOf(item) != -1;
    }

    public void CopyTo(T[] array, int arrayIndex)
    {
        AsSpan().CopyTo(array.AsSpan(arrayIndex));
    }

    public void CopyTo(Span<T> span)
    {
        AsSpan().CopyTo(span);
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

    public delegate void ForEachRefDelegate(ref T item);

    public void ForEach(ForEachRefDelegate del)
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

    public int IndexOf(in T value, EqualityComparer<T>? comparer)
    {
        unsafe
        {
            var span = new Span<T>(data, size);
            return span.IndexOf(value, comparer);
        }
    }

    public int IndexOf(T value)
    {
        return IndexOf(in value, null);
    }

    public void Insert(int index, T value)
    {
        unsafe
        {
            if ((uint)index > (uint)size)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

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

            if (index == size)
            {
                Add(value);
                return;
            }

            NativeMemory.Copy(data + index, data + index + 1, (nuint)(sizeof(T) * (size - index - 0)));

            data[index] = value;
            size++;
        }
    }

    public bool Remove(T value)
    {
        return Remove(in value);
    }

    public bool Remove(in T value)
    {
        var index = IndexOf(value);
        if (index == -1)
        {
            return false;
        }

        RemoveAt(index);
        return true;
    }

    public void RemoveAt(int index)
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

    public void Resize(int newLength)
    {
        Resize(newLength, new());
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

    private void EnsureCapacity(int capacity)
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

        unsafe
        {
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
    }

    public void Dispose()
    {
        unsafe
        {
            if (data != null)
            {
                if (typeof(T).GetInterface(typeof(IDisposable).FullName!) != null)
                {
                    ForEach((ref x) => { (x as IDisposable)!.Dispose(); });
                }

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