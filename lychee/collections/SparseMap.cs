using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace lychee.collections;

public sealed class SparseMap<T>() : IDisposable, IEnumerable<(int key, T value)>
{
#region Private fields

    private readonly NativeList<int> sparseArray = [];

    private readonly List<(int key, T value)> denseArray = [];

#endregion

#region Public properties

    public int Count => denseArray.Count;

    public T this[int key]
    {
        get
        {
            if ((uint)key >= (uint)sparseArray.Count || sparseArray[key] == -1)
            {
                throw new KeyNotFoundException();
            }

            return denseArray[sparseArray[key]].Item2;
        }
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
    /// Add an element to the sparse map, take the return value of AsInt() as the key.
    /// </summary>
    /// <param name="id">The id of the value</param>
    /// <param name="value">The value to add</param>
    public void Add(int id, T value)
    {
        if (id >= sparseArray.Count)
        {
            sparseArray.Resize(id + 1, -1);
        }

        if (sparseArray[id] != -1)
        {
            var existingIndex = sparseArray[id];
            denseArray[existingIndex] = (id, value);

            return;
        }

        denseArray.Add((id, value));
        sparseArray[id] = denseArray.Count - 1;
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
    public bool Contains(int key)
    {
        if ((uint)key < (uint)sparseArray.Count && sparseArray[key] != -1)
        {
            return true;
        }

        return false;
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

        // 获取要移除的元素的id
        var removedId = denseArray[denseIndex].Item1;

        // 如果移除的不是最后一个元素，则用最后一个元素填充空位
        if (denseIndex < denseArray.Count - 1)
        {
            var lastElement = denseArray[^1];
            denseArray[denseIndex] = lastElement;
            sparseArray[lastElement.Item1] = denseIndex;
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

        value = denseArray[sparseArray[key]].Item2;
        return true;
    }

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