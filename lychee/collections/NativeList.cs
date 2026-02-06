using System.Collections;
using System.Diagnostics;
using System.Runtime.InteropServices;
using lychee.utils;

namespace lychee.collections;

/// <summary>
/// A list implementation that manually manages unmanaged memory for unmanaged types.
/// Similar to <see cref="List&lt;T&gt;"/> but with explicit memory allocation and deallocation for improved performance.
/// </summary>
/// <typeparam name="T">The type of elements in the list. Must be unmanaged.</typeparam>
public sealed class NativeList<T>() : IDisposable, IList<T>, IReadOnlyList<T> where T : unmanaged
{
    private unsafe T* data;

    private int size;

    private int capacity;

    private static readonly unsafe nuint Alignment = (nuint)TypeUtils.GetOrGuessAlignment(typeof(T), sizeof(T));

#region Public properties

    /// <summary>
    /// Gets a value indicating whether the list has reached its current capacity.
    /// </summary>
    public bool IsFull => size == capacity;

    /// <summary>
    /// Gets a value indicating whether the list contains no elements.
    /// </summary>
    public bool IsEmpty => size == 0;

    /// <summary>
    /// Gets the number of elements contained in the list.
    /// </summary>
    public int Count => size;

    /// <summary>
    /// Gets a value indicating whether the list is read-only. Always returns false.
    /// </summary>
    public bool IsReadOnly => false;

    /// <summary>
    /// Gets or sets the total number of elements the internal data structure can hold without resizing.
    /// </summary>
    public int Capacity
    {
        get => capacity;
        set => EnsureCapacity(value);
    }

    /// <summary>
    /// Gets or sets the element at the specified index.
    /// </summary>
    /// <param name="index">The zero-based index of the element.</param>
    /// <returns>The element at the specified index.</returns>
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

    /// <summary>
    /// Implicitly converts a <see cref="NativeList&lt;T&gt;"/> to a <see cref="Span&lt;T&gt;"/>.
    /// </summary>
    /// <param name="list">The list to convert.</param>
    public static implicit operator Span<T>(NativeList<T> list) => list.AsSpan();

#endregion

#region Constructors & Destructors

    /// <summary>
    /// Initializes a new instance of the <see cref="NativeList&lt;T&gt;"/> class with the specified initial capacity.
    /// </summary>
    /// <param name="capacity">The initial capacity of the list.</param>
    public NativeList(int capacity) : this()
    {
        EnsureCapacity(capacity);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="NativeList&lt;T&gt;"/> class with elements copied from the specified array.
    /// </summary>
    /// <param name="array">The array whose elements are copied to the new list.</param>
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
    /// Adds an element to the end of the list by reference.
    /// </summary>
    /// <param name="value">The element to add by reference.</param>
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
    /// Adds an element to the end of the list.
    /// </summary>
    /// <param name="value">The element to add to the end of the list.</param>
    public void Add(T value) => Add(in value);

    /// <summary>
    /// Adds the elements of the specified collection to the end of the list.
    /// </summary>
    /// <param name="collection">The collection whose elements should be added.</param>
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
    /// Adds the elements of the specified read-only span to the end of the list.
    /// </summary>
    /// <param name="collection">The read-only span whose elements should be added.</param>
    public void AddRange(ReadOnlySpan<T> collection)
    {
        EnsureCapacity(capacity + collection.Length);

        unsafe
        {
            collection.CopyTo(new(data + size, capacity - size));
        }
    }

    /// <summary>
    /// Returns a span that contains all elements of the list.
    /// </summary>
    /// <returns>A span containing all elements of the list.</returns>
    public Span<T> AsSpan() => AsSpan(0, size);


    /// <summary>
    /// Returns a span that contains elements from the specified index to the end of the list.
    /// </summary>
    /// <param name="begin">The zero-based index at which the range starts.</param>
    /// <returns>A span containing elements from the specified index to the end.</returns>
    public Span<T> AsSpan(int begin)
    {
        Debug.Assert((uint)begin < (uint)size);

        unsafe
        {
            return new(data + begin, size - begin);
        }
    }

    /// <summary>
    /// Returns a span that contains the specified number of elements starting from the specified index.
    /// </summary>
    /// <param name="begin">The zero-based index at which the range starts.</param>
    /// <param name="count">The number of elements in the range.</param>
    /// <returns>A span containing the specified range of elements.</returns>
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
    /// Removes all elements from the list without releasing the allocated memory.
    /// </summary>
    public void Clear() => size = 0;

    /// <summary>
    /// Determines whether an element is in the list.
    /// </summary>
    /// <param name="item">The element to locate in the list.</param>
    /// <returns>true if the element is found; otherwise, false.</returns>
    public bool Contains(T item) => IndexOf(item) != -1;

    /// <summary>
    /// Copies the entire list to a compatible one-dimensional array, starting at the specified index of the target array.
    /// </summary>
    /// <param name="array">The destination array.</param>
    /// <param name="arrayIndex">The zero-based index in the destination array at which copying begins.</param>
    public void CopyTo(T[] array, int arrayIndex) => AsSpan().CopyTo(array.AsSpan(arrayIndex));

    /// <summary>
    /// Copies the entire list to the specified span.
    /// </summary>
    /// <param name="span">The destination span.</param>
    public void CopyTo(Span<T> span) => AsSpan().CopyTo(span);

    /// <summary>
    /// Determines whether the list contains elements that match the conditions defined by the specified predicate.
    /// </summary>
    /// <param name="match">The predicate that defines the conditions to search for.</param>
    /// <returns>true if the list contains one or more elements that match; otherwise, false.</returns>
    public bool Exists(Predicate<T> match) => FindIndex(match) != -1;

    /// <summary>
    /// Searches for an element that matches the conditions defined by the specified predicate, and returns the zero-based index of the first occurrence.
    /// </summary>
    /// <param name="match">The predicate that defines the conditions to search for.</param>
    /// <returns>The zero-based index of the first element that matches; -1 if not found.</returns>
    public int FindIndex(Predicate<T> match) => FindIndex(0, size, match);

    /// <summary>
    /// Searches for an element that matches the conditions defined by the specified predicate, starting from the specified index.
    /// </summary>
    /// <param name="startIndex">The zero-based starting index of the search.</param>
    /// <param name="match">The predicate that defines the conditions to search for.</param>
    /// <returns>The zero-based index of the first element that matches; -1 if not found.</returns>
    public int FindIndex(int startIndex, Predicate<T> match) => FindIndex(startIndex, size - startIndex, match);

    /// <summary>
    /// Searches for an element that matches the conditions defined by the specified predicate within a range of elements.
    /// </summary>
    /// <param name="begin">The zero-based starting index of the search.</param>
    /// <param name="count">The number of elements in the range to search.</param>
    /// <param name="match">The predicate that defines the conditions to search for.</param>
    /// <returns>The zero-based index of the first element that matches; -1 if not found.</returns>
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
    /// Fills the entire list with the specified value.
    /// </summary>
    /// <param name="item">The value to fill the list with.</param>
    public void Fill(in T item)
    {
        unsafe
        {
            var span = new Span<T>(data, size);
            span.Fill(item);
        }
    }

    /// <summary>
    /// Fills a range of elements in the list with the specified value.
    /// </summary>
    /// <param name="begin">The zero-based starting index of the range.</param>
    /// <param name="end">The exclusive ending index of the range.</param>
    /// <param name="item">The value to fill the range with.</param>
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

    /// <summary>
    /// Delegate for performing an action on each element by reference.
    /// </summary>
    /// <param name="item">A reference to the current element.</param>
    public delegate void ForEachRefDelegate(ref T item);

    /// <summary>
    /// Performs the specified action on each element of the list, passing each element by reference.
    /// </summary>
    /// <param name="action">The delegate to perform on each element by reference.</param>
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
    /// Performs the specified action on each element of the list.
    /// </summary>
    /// <param name="act">The delegate to perform on each element of the list.</param>
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
    /// Searches for the specified object and returns the zero-based index of the first occurrence.
    /// </summary>
    /// <param name="value">The object to locate.</param>
    /// <param name="comparer">The equality comparer to use; null to use the default.</param>
    /// <returns>The zero-based index of the first occurrence; -1 if not found.</returns>
    public int IndexOf(in T value, EqualityComparer<T>? comparer)
    {
        comparer ??= EqualityComparer<T>.Default;

        unsafe
        {
            var span = new Span<T>(data, size);
            return span.IndexOf(value, comparer);
        }
    }

    /// <summary>
    /// Searches for the specified object and returns the zero-based index of the first occurrence using the default comparer.
    /// </summary>
    /// <param name="value">The object to locate.</param>
    /// <returns>The zero-based index of the first occurrence; -1 if not found.</returns>
    public int IndexOf(T value)
    {
        return IndexOf(in value, null);
    }

    /// <summary>
    /// Inserts an element into the list at the specified index.
    /// </summary>
    /// <param name="index">The zero-based index at which the element should be inserted.</param>
    /// <param name="value">The element to insert.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when index is negative or greater than the list size.</exception>
    public void Insert(int index, T value) => Insert(index, in value);

    /// <summary>
    /// Inserts an element into the list at the specified index by reference.
    /// </summary>
    /// <param name="index">The zero-based index at which the element should be inserted.</param>
    /// <param name="value">The element to insert by reference.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when index is negative or greater than the list size.</exception>
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
    /// Removes the first occurrence of a specific object from the list.
    /// </summary>
    /// <param name="value">The object to remove from the list.</param>
    /// <returns>true if the item was successfully removed; otherwise, false.</returns>
    public bool Remove(T value) => Remove(in value);

    /// <summary>
    /// Removes the first occurrence of a specific object from the list by reference.
    /// </summary>
    /// <param name="value">The object to remove from the list.</param>
    /// <returns>true if the item was successfully removed; otherwise, false.</returns>
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
    /// Removes the element at the specified index from the list.
    /// </summary>
    /// <param name="index">The zero-based index of the element to remove.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when index is negative or not less than the list size.</exception>
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

    /// <summary>
    /// Removes the last element from the list.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when the list is empty.</exception>
    public void RemoveLast()
    {
        if (size == 0)
        {
            throw new InvalidOperationException("List is empty");
        }

        size--;
    }

    /// <summary>
    /// Resizes the list to the specified length, filling new elements with default values.
    /// </summary>
    /// <param name="newLength">The new size of the list.</param>
    public void Resize(int newLength)
    {
        Resize(newLength, new());
    }

    /// <summary>
    /// Resizes the list to the specified length, filling new elements with the specified value.
    /// </summary>
    /// <param name="newLength">The new size of the list.</param>
    /// <param name="item">The value to fill new elements with.</param>
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
    /// Reduces the capacity of the list to match its current size.
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

        GC.SuppressFinalize(this);
    }

    public IEnumerator<T> GetEnumerator()
    {
        return new Enumerator(this);
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return ((IEnumerable)this).GetEnumerator();
    }

    /// <summary>
    /// Enumerates the elements of a <see cref="NativeList&lt;T&gt;"/>.
    /// </summary>
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
