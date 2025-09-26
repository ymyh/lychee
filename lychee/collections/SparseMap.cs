using System.Collections;
using System.Diagnostics.CodeAnalysis;

namespace lychee.collections;

public sealed class SparseMap<T> : IDisposable, IEnumerable<(int, T)>
{
#region Private fields

    private readonly NativeList<int> sparseArray = [];

    private readonly List<(int, T)> denseArray = [];

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

    ~SparseMap()
    {
        Dispose();
    }

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
    /// Remove element by id
    /// </summary>
    /// <param name="key">要移除的值的id</param>
    /// <returns>如果成功移除则返回true，否则返回false</returns>
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
    /// 从denseArray的指定索引处移除元素
    /// </summary>
    /// <param name="denseIndex">denseArray中的索引</param>
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
    /// 尝试获取指定id的值
    /// </summary>
    /// <param name="id">要获取的值的id</param>
    /// <param name="value">获取到的值</param>
    /// <returns>如果找到指定id的值则返回true，否则返回false</returns>
    public bool TryGetValue(int id, [MaybeNullWhen(false)] out T value)
    {
        value = default;
        if (id < 0 || id >= sparseArray.Count || sparseArray[id] == -1)
        {
            return false;
        }

        value = denseArray[sparseArray[id]].Item2;
        return true;
    }

#endregion

#region IDisposable method

    public void Dispose()
    {
        sparseArray.Dispose();
    }

#endregion

#region ICollection members

    public bool IsReadOnly { get; } = false;

    public void Add((int, T) item)
    {
        Add(item.Item1, item.Item2);
    }

    public void Clear()
    {
        sparseArray.Clear();
        denseArray.Clear();
    }

    public bool Contains(int key)
    {
        if ((uint)key < (uint)sparseArray.Count && sparseArray[key] != -1)
        {
            return true;
        }

        return false;
    }

    public IEnumerator<(int, T)> GetEnumerator()
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
