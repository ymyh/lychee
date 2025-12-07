using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace lychee.collections;

/// <summary>
/// Represents a collection of keys and values, which key type is <see cref="int"/>. <br/>
/// SparseMap has better performance than <see cref="Dictionary&lt;TKey,TValue&gt;"/> but may use more memory,
/// depends on the greatest key in the map.
/// </summary>
/// <typeparam name="T">The type of the values in the dictionary.</typeparam>
public sealed class SparseMap<T>() : IDisposable, IEnumerable<(int key, T value)>
{
#region Private fields

    private readonly NativeList<int> sparseArray = [];

    private readonly List<(int key, T value)> denseArray = [];

#endregion

#region Public properties

    /// <summary>
    /// Gets the number of elements contained in the <see cref="SparseMap&lt;T&gt;"/>
    /// </summary>
    public int Count => denseArray.Count;

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
    /// Add an element to the sparse map. Unlike <see cref="Dictionary&lt;TKey,TValue&gt;"/>,
    /// you can add same key multiple times.
    /// </summary>
    /// <param name="key">The id of the value</param>
    /// <param name="value">The value to add</param>
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
    /// Clear all elements in the sparse map
    /// </summary>
    public void Clear()
    {
        sparseArray.Clear();
        denseArray.Clear();
    }

    /// <summary>
    /// Check if the sparse map contains the specified key
    /// </summary>
    /// <param name="key">The key of the value</param>
    /// <returns>Returns true if the value is found, otherwise false</returns>
    public bool ContainsKey(int key)
    {
        return (uint)key < (uint)sparseArray.Count && sparseArray[key] != -1;
    }

    /// <summary>
    /// Performs the specified action on each element of the sparse map.
    /// </summary>
    /// <param name="action">The action to call</param>
    public void ForEach(Action<int, T> action)
    {
        denseArray.ForEach(x => { action(x.key, x.value); });
    }

    public delegate void ForEachRefDelegate(int key, ref T value);

    /// <summary>
    /// Like <see cref="ForEach"/>, except the action parameter is pass by ref.
    /// </summary>
    /// <param name="action"></param>
    public void ForEachRef(ForEachRefDelegate action)
    {
        denseArray.ForEach(x => { action(x.key, ref x.value); });
    }

    /// <summary>
    /// Get element index in dense array by key.
    /// </summary>
    /// <param name="key"></param>
    /// <returns></returns>
    public int GetIndex(int key)
    {
        if ((uint)key >= (uint)sparseArray.Count || sparseArray[key] == -1)
        {
            return -1;
        }

        return sparseArray[key];
    }

    /// <summary>
    /// Remove element by key
    /// </summary>
    /// <param name="key">The key of the value</param>
    /// <returns>Returns true if the value is found, otherwise false</returns>
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
    /// Try to get value by key
    /// </summary>
    /// <param name="key">The key of the value</param>
    /// <param name="value">The value</param>
    /// <returns>Returns true if the value is found, otherwise false</returns>
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
    /// Get dense array as span.
    /// </summary>
    /// <returns></returns>
    public Span<(int, T)> GetDenseAsSpan()
    {
        return CollectionsMarshal.AsSpan(denseArray);
    }

#endregion

#region IDisposable method

    public void Dispose()
    {
        sparseArray.Dispose();
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
