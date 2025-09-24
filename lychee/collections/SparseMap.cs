using System.Diagnostics.CodeAnalysis;

namespace lychee.collections;

public sealed class SparseMap<T> : IDisposable
{
#region Private fields

    private readonly NativeList<int> sparseArray = [];

    private readonly List<(int, T)> denseArray = [];

#endregion

#region Public properties

    public int Count => denseArray.Count;

    public T this[int id]
    {
        get
        {
            if (id < 0 || id >= sparseArray.Count || sparseArray[id] == -1)
            {
                throw new KeyNotFoundException();
            }

            return denseArray[sparseArray[id]].Item2;
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
    /// <param name="id">要移除的值的id</param>
    /// <returns>如果成功移除则返回true，否则返回false</returns>
    public bool Remove(int id)
    {
        if (id < 0 || id >= sparseArray.Count || sparseArray[id] == -1)
        {
            return false;
        }

        RemoveAt(sparseArray[id]);
        return true;
    }

    /// <summary>
    /// 从denseArray的指定索引处移除元素
    /// </summary>
    /// <param name="denseIndex">denseArray中的索引</param>
    private void RemoveAt(int denseIndex)
    {
        if (denseIndex < 0 || denseIndex >= denseArray.Count)
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
}
