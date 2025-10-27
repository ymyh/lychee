using System.Collections;
using System.Diagnostics;
using System.Runtime.InteropServices;
using lychee.utils;

namespace lychee.collections;

/// <summary>
/// Like <see cref="List&lt;T&gt;"/>, but manually alloc and free the memory and holds only unmanaged type. <br/>
/// Probably have better performance than <see cref="List&lt;T&gt;"/>.
/// </summary>
/// <typeparam name="T">The type of elements in the list.</typeparam>
public sealed class NativeList<T>() : IDisposable, IList<T>, IReadOnlyList<T> where T : unmanaged
{
    private unsafe T* data;

    private int size;

    private int capacity;

    private static readonly unsafe nuint Alignment = (nuint)TypeUtils.GetOrGuessAlignment(typeof(T), sizeof(T));

#region Public properties

    public bool IsFull => size == capacity;

    public bool IsEmpty => size == 0;

    public int Count => size;

    public bool IsReadOnly => false;

    /// <summary>
    /// Gets or sets the capacity of the <see cref="NativeList&lt;T&gt;"/>.
    /// </summary>
    public int Capacity
    {
        get => capacity;
        set => EnsureCapacity(value);
    }

    public T this[int index]
    {
        get
        {
            Debug.Assert((uint)index < (uint)size);

            unsafe
            {
                return data[index];
            }
        }

        set
        {
            Debug.Assert((uint)index < (uint)size);

            unsafe
            {
                data[index] = value;
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

    ~NativeList() => Dispose();

#endregion

    /// <summary>
    /// Like <see cref="Add(T)"/>, except the value parameter is pass by ref.
    /// </summary>
    /// <param name="value"></param>
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

    /// <summary>
    /// Adds an object to the end of the <see cref="NativeList&lt;T&gt;"/>.
    /// </summary>
    /// <param name="value">The object to be added to the end of the <see cref="NativeList&lt;T&gt;"/>.</param>
    public void Add(T value) => Add(in value);

    /// <summary>
    /// Adds the elements of the specified collection to the end of the <see cref="NativeList&lt;T&gt;"/>.
    /// </summary>
    /// <param name="collection"></param>
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

    /// <summary>
    /// Adds the elements of the specified collection to the end of the <see cref="NativeList&lt;T&gt;"/>.
    /// </summary>
    /// <param name="collection"></param>
    public void AddRange(ReadOnlySpan<T> collection)
    {
        EnsureCapacity(capacity + collection.Length);

        unsafe
        {
            collection.CopyTo(new(data + size, capacity - size));
        }
    }

    /// <summary>
    /// Returns a span that contains all elements of the <see cref="NativeList&lt;T&gt;"/>.
    /// </summary>
    /// <returns></returns>
    public Span<T> AsSpan() => AsSpan(0, size);


    /// <summary>
    /// Returns a span that contains elements from the specified index to the end of the <see cref="NativeList&lt;T&gt;"/>.
    /// </summary>
    /// <param name="begin">The zero-based index at which the range starts.</param>
    /// <returns></returns>
    public Span<T> AsSpan(int begin)
    {
        Debug.Assert((uint)begin < (uint)size);

        unsafe
        {
            return new(data + begin, size - begin);
        }
    }

    /// <summary>
    /// Returns a span that contains elements from the specified index to the end of the <see cref="NativeList&lt;T&gt;"/>.
    /// </summary>
    /// <param name="begin">The zero-based index at which the range starts.</param>
    /// <param name="count">The number of elements in the range.</param>
    /// <returns></returns>
    public Span<T> AsSpan(int begin, int count)
    {
        Debug.Assert((uint)begin < (uint)size);
        Debug.Assert(count <= size - begin);

        unsafe
        {
            return new(data + begin, count);
        }
    }

    /// <summary>
    /// Removes all elements from the <see cref="NativeList&lt;T&gt;"/> and leave memory untouched.
    /// </summary>
    public void Clear() => size = 0;

    /// <summary>
    /// Determines whether an element is in the <see cref="NativeList&lt;T&gt;"/>.
    /// </summary>
    /// <param name="item"></param>
    /// <returns></returns>
    public bool Contains(T item) => IndexOf(item) != -1;

    public void CopyTo(T[] array, int arrayIndex) => AsSpan().CopyTo(array.AsSpan(arrayIndex));

    public void CopyTo(Span<T> span) => AsSpan().CopyTo(span);

    public bool Exists(Predicate<T> match) => FindIndex(match) != -1;

    public int FindIndex(Predicate<T> match) => FindIndex(0, size, match);

    public int FindIndex(int startIndex, Predicate<T> match) => FindIndex(startIndex, size - startIndex, match);

    public int FindIndex(int begin, int count, Predicate<T> match)
    {
        if ((uint)begin > (uint)size)
        {
            throw new ArgumentOutOfRangeException(nameof(begin));
        }

        if (count < 0 || begin > size - count)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }

        var endIndex = begin + count;
        for (var i = begin; i < endIndex; i++)
        {
            unsafe
            {
                if (match(data[i]))
                {
                    return i;
                }
            }
        }

        return -1;
    }

    /// <summary>
    /// Fills the entire <see cref="NativeList&lt;T&gt;"/> with the specified value.
    /// </summary>
    /// <param name="item"></param>
    public void Fill(in T item)
    {
        unsafe
        {
            var span = new Span<T>(data, size);
            span.Fill(item);
        }
    }

    /// <summary>
    /// Fills a range of elements in the <see cref="NativeList&lt;T&gt;"/> with the specified value.
    /// </summary>
    /// <param name="begin"></param>
    /// <param name="end"></param>
    /// <param name="item"></param>
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

    /// <summary>
    /// Like <see cref="ForEach(Action&lt;T&gt;)"/> but pass element by reference.
    /// </summary>
    public void ForEach(ForEachRefDelegate action)
    {
        unsafe
        {
            for (var i = 0; i < size; i++)
            {
                action(ref data[i]);
            }
        }
    }

    /// <summary>
    /// Performs the specified action on each element of the <see cref="NativeList&lt;T&gt;"/>.
    /// </summary>
    /// <param name="action">The delegate to perform on each element of the <see cref="NativeList&lt;T&gt;"/>.</param>
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

    /// <summary>
    /// Searches for the specified object and returns the zero-based index of the first occurrence within the entire <see cref="NativeList&lt;T&gt;"/>.
    /// </summary>
    /// <param name="value"></param>
    /// <param name="comparer"></param>
    /// <returns></returns>
    public int IndexOf(in T value, EqualityComparer<T>? comparer)
    {
        comparer ??= EqualityComparer<T>.Default;

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

    /// <summary>
    /// Inserts an element into the <see cref="NativeList&lt;T&gt;"/> at the specified index.
    /// </summary>
    /// <param name="index">The zero-based index at which item should be inserted.</param>
    /// <param name="value">The object to insert.</param>
    /// <exception cref="ArgumentOutOfRangeException"></exception>
    public void Insert(int index, T value) => Insert(index, in value);

    /// <summary>
    /// Inserts an element into the <see cref="NativeList&lt;T&gt;"/> at the specified index.
    /// </summary>
    /// <param name="index">The zero-based index at which item should be inserted.</param>
    /// <param name="value">The object to insert.</param>
    /// <exception cref="ArgumentOutOfRangeException"></exception>
    public void Insert(int index, in T value)
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

    /// <summary>
    /// Removes the first occurrence of a specific object from the <see cref="NativeList&lt;T&gt;"/>.
    /// </summary>
    /// <param name="value">The object to remove from the <see cref="NativeList&lt;T&gt;"/>.</param>
    /// <returns>true if item is successfully removed; otherwise, false. This method also returns false if item was not found in the <see cref="NativeList&lt;T&gt;"/>.</returns>
    public bool Remove(T value) => Remove(in value);

    /// <summary>
    /// Removes the first occurrence of a specific object from the <see cref="NativeList&lt;T&gt;"/>.
    /// </summary>
    /// <param name="value">The object to remove from the <see cref="NativeList&lt;T&gt;"/>.</param>
    /// <returns>true if item is successfully removed; otherwise, false. This method also returns false if item was not found in the <see cref="NativeList&lt;T&gt;"/>.</returns>
    public bool Remove(in T value)
    {
        var index = IndexOf(in value, null);
        if (index == -1)
        {
            return false;
        }

        RemoveAt(index);
        return true;
    }

    /// <summary>
    /// Removes the element at the specified index of the <see cref="NativeList&lt;T&gt;"/>.
    /// </summary>
    /// <param name="index">The zero-based index of the element to remove.</param>
    /// <exception cref="ArgumentOutOfRangeException"></exception>
    public void RemoveAt(int index)
    {
        if ((uint)index >= (uint)size)
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

    /// <summary>
    /// Resizes the <see cref="NativeList&lt;T&gt;"/> to the specified length and fill with default value.
    /// </summary>
    /// <param name="newLength"></param>
    public void Resize(int newLength)
    {
        Resize(newLength, new());
    }

    /// <summary>
    /// Resizes the <see cref="NativeList&lt;T&gt;"/> to the specified length and fill with the specified item.
    /// </summary>
    /// <param name="newLength"></param>
    /// <param name="item"></param>
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

    /// <summary>
    /// Shrinks the capacity of the <see cref="NativeList&lt;T&gt;"/> to match the size.
    /// </summary>
    public void ShirkToFit()
    {
        if (capacity > size)
        {
            unsafe
            {
                if (size != 0)
                {
                    var ptr = NativeMemory.AlignedAlloc((nuint)(size * sizeof(T)), Alignment);
                    NativeMemory.Copy(data, ptr, (nuint)(size * sizeof(T)));
                    NativeMemory.AlignedFree(data);

                    data = (T*)ptr;
                }
                else
                {
                    NativeMemory.AlignedFree(data);
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
                var ptr = NativeMemory.AlignedAlloc((nuint)(this.capacity * sizeof(T)), Alignment);
                NativeMemory.Copy(data, ptr, (nuint)(size * sizeof(T)));
                NativeMemory.AlignedFree(data);

                data = (T*)ptr;
            }
            else
            {
                data = (T*)NativeMemory.AlignedAlloc((nuint)(this.capacity * sizeof(T)), Alignment);
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

                NativeMemory.AlignedFree(data);
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
