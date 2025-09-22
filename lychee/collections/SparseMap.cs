using System.Diagnostics.CodeAnalysis;

namespace lychee.collections;

public interface AsInt
{
    int AsInt();
}

public sealed class SparseMap<T> : IDisposable where T : AsInt
{
#region Private fields

    private readonly NativeList<int> sparseArray = [];

    private readonly List<(int, T)> denseArray = [];

#endregion

#region Public Methods

    /// <summary>
    /// 添加一个值到稀疏映射中
    /// </summary>
    /// <param name="value">要添加的值</param>
    public void Add(T value)
    {
        var id = value.AsInt();

        // 确保sparseArray足够大以容纳新的id
        if (id >= sparseArray.Count)
        {
            sparseArray.Resize(id + 1, -1);
        }

        // 如果这个id已经存在，直接替换旧值而不改变数组结构
        if (sparseArray[id] != -1)
        {
            var existingIndex = sparseArray[id];
            denseArray[existingIndex] = (id, value);

            return;
        }

        // 添加新值到denseArray并更新sparseArray
        denseArray.Add((id, value));
        sparseArray[id] = denseArray.Count - 1;
    }

    /// <summary>
    /// 从稀疏映射中移除指定id的值
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

    /// <summary>
    /// 获取映射中的元素数量
    /// </summary>
    public int Count => denseArray.Count;

#endregion

#region IDisposable method

    public void Dispose()
    {
        sparseArray.Dispose();
    }

#endregion
}
