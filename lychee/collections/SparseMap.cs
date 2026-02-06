using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace lychee.collections;

/// <summary>
/// A sparse set map that provides O(1) lookup, insertion, and deletion for integer keys.
/// Uses more memory than <see cref="Dictionary&lt;TKey,TValue&gt;"/> but offers better cache locality
/// and iteration performance. Memory usage depends on the greatest key value in the map.
/// </summary>
/// <typeparam name="T">The type of values in the map.</typeparam>
public sealed class SparseMap<T>() : IDisposable, IEnumerable<(int key, T value)>
{
#region Private fields

    private readonly NativeList<int> sparseArray = [];

    private readonly List<(int key, T value)> denseArray = [];

#endregion

#region Public properties

    /// <summary>
    /// Gets the number of elements contained in the map.
    /// </summary>
    public int Count => denseArray.Count;

    /// <summary>
    /// Gets or sets the value associated with the specified key.
    /// </summary>
    /// <param name="key">The integer key of the value to get or set.</param>
    /// <returns>The value associated with the key.</returns>
    /// <exception cref="KeyNotFoundException">Thrown when the key is not found during get access.</exception>
    public T this[int key]
    {
        get
        {
            if ((uint)key >= (uint)sparseArray.Count || sparseArray[key] == -1)
            {
                throw new KeyNotFoundException();
            }

            return denseArray[sparseArray[key]].value;
        }

        set => Add(key, value);
    }

#endregion

#region Constructors & Destructors

    /// <summary>
    /// Initializes a new instance and populates it from the specified enumerable.
    /// </summary>
    /// <param name="enumerable">The collection of key-value pairs to populate the map.</param>
    public SparseMap(IEnumerable<(int, T)> enumerable) : this()
    {
        foreach (var valueTuple in enumerable)
        {
            Add(valueTuple.Item1, valueTuple.Item2);
        }
    }

    ~SparseMap()
    {
        Dispose();
    }

#endregion

#region Public Methods

    /// <summary>
    /// Adds or updates an element with the specified key and value.
    /// Unlike <see cref="Dictionary&lt;TKey,TValue&gt;"/>, adding the same key updates the existing entry.
    /// </summary>
    /// <param name="key">The integer key of the element.</param>
    /// <param name="value">The value to add or update.</param>
    public void Add(int key, T value)
    {
        if (key >= sparseArray.Count)
        {
            sparseArray.Resize(key + 1, -1);
        }

        if (sparseArray[key] != -1)
        {
            var existingIndex = sparseArray[key];
            denseArray[existingIndex] = (key, value);

            return;
        }

        denseArray.Add((key, value));
        sparseArray[key] = denseArray.Count - 1;
    }

    /// <summary>
    /// Removes all elements from the map.
    /// </summary>
    public void Clear()
    {
        sparseArray.Clear();
        denseArray.Clear();
    }

    /// <summary>
    /// Determines whether the map contains an element with the specified key.
    /// </summary>
    /// <param name="key">The key to locate in the map.</param>
    /// <returns>true if the map contains an element with the key; otherwise, false.</returns>
    public bool ContainsKey(int key)
    {
        return (uint)key < (uint)sparseArray.Count && sparseArray[key] != -1;
    }

    /// <summary>
    /// Performs the specified action on each element in the map.
    /// </summary>
    /// <param name="action">The action to perform on each element.</param>
    public void ForEach(Action<int, T> action)
    {
        denseArray.ForEach(x => { action(x.key, x.value); });
    }

    /// <summary>
    /// Delegate for performing an action on each element with the value passed by reference.
    /// </summary>
    /// <param name="key">The key of the current element.</param>
    /// <param name="value">A reference to the value of the current element.</param>
    public delegate void ForEachRefDelegate(int key, ref T value);

    /// <summary>
    /// Performs the specified action on each element, passing the value by reference.
    /// </summary>
    /// <param name="action">The action to perform on each element.</param>
    public void ForEachRef(ForEachRefDelegate action)
    {
        denseArray.ForEach(x => { action(x.key, ref x.value); });
    }

    /// <summary>
    /// Gets the index of an element in the dense array by its key.
    /// </summary>
    /// <param name="key">The key to locate.</param>
    /// <returns>The index in the dense array, or -1 if the key is not found.</returns>
    public int GetIndex(int key)
    {
        if ((uint)key >= (uint)sparseArray.Count || sparseArray[key] == -1)
        {
            return -1;
        }

        return sparseArray[key];
    }

    /// <summary>
    /// Removes the element with the specified key from the map.
    /// </summary>
    /// <param name="key">The key of the element to remove.</param>
    /// <returns>true if the element was found and removed; otherwise, false.</returns>
    public bool Remove(int key)
    {
        if ((uint)key >= (uint)sparseArray.Count || sparseArray[key] == -1)
        {
            return false;
        }

        RemoveAt(sparseArray[key]);
        return true;
    }

    /// <summary>
    /// Remove element by dense index
    /// </summary>
    /// <param name="denseIndex">The dense index of the value</param>
    private void RemoveAt(int denseIndex)
    {
        if ((uint)denseIndex >= (uint)denseArray.Count)
        {
            return;
        }

        var removedId = denseArray[denseIndex].key;

        if (denseIndex < denseArray.Count - 1)
        {
            var lastElement = denseArray[^1];
            denseArray[denseIndex] = lastElement;
            sparseArray[lastElement.key] = denseIndex;
        }

        // 移除最后一个元素
        denseArray.RemoveAt(denseArray.Count - 1);

        // 更新sparseArray中的映射
        sparseArray[removedId] = -1;
    }

    /// <summary>
    /// Gets the value associated with the specified key.
    /// </summary>
    /// <param name="key">The key of the value to get.</param>
    /// <param name="value">When this method returns, contains the value associated with the key if found; otherwise, the default value.</param>
    /// <returns>true if the map contains an element with the key; otherwise, false.</returns>
    public bool TryGetValue(int key, [MaybeNullWhen(false)] out T value)
    {
        value = default;
        if ((uint)key >= (uint)sparseArray.Count || sparseArray[key] == -1)
        {
            return false;
        }

        value = denseArray[sparseArray[key]].value;
        return true;
    }

    /// <summary>
    /// Gets the dense array as a span for efficient iteration.
    /// </summary>
    /// <returns>A span containing all key-value pairs in dense storage order.</returns>
    public Span<(int, T)> GetDenseAsSpan()
    {
        return CollectionsMarshal.AsSpan(denseArray);
    }

#endregion

#region IDisposable method

    public void Dispose()
    {
        sparseArray.Dispose();
        GC.SuppressFinalize(this);
    }

#endregion

#region IEnumerable members

    public IEnumerator<(int key, T value)> GetEnumerator()
    {
        foreach (var valueTuple in denseArray)
        {
            yield return valueTuple;
        }
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

#endregion
}
