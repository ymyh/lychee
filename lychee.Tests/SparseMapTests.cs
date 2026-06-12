using lychee.collections;

namespace lychee.Tests;

public class SparseMapTests
{
#region Add

    [Fact]
    public void Add_NewKey_AddsSuccessfully()
    {
        var map = new SparseMap<string>();

        map.Add(5, "hello");

        Assert.Equal(1, map.Count);
        Assert.Equal("hello", map[5]);
    }

    [Fact]
    public void Add_DuplicateKey_ThrowsArgumentException()
    {
        var map = new SparseMap<int>();

        map.Add(1, 100);

        Assert.Throws<ArgumentException>(() => map.Add(1, 200));
    }

    [Fact]
    public void Add_MultipleKeys_AllAccessible()
    {
        var map = new SparseMap<int>();

        map.Add(0, 10);
        map.Add(100, 20);
        map.Add(200, 30);

        Assert.Equal(3, map.Count);
        Assert.Equal(10, map[0]);
        Assert.Equal(20, map[100]);
        Assert.Equal(30, map[200]);
    }

    [Fact]
    public void Add_LargeKey_ExpandsSparseArray()
    {
        var map = new SparseMap<int>();

        map.Add(10000, 42);

        Assert.Equal(1, map.Count);
        Assert.Equal(42, map[10000]);
    }

#endregion

#region AddOrUpdate

    [Fact]
    public void AddOrUpdate_NewKey_AddsValue()
    {
        var map = new SparseMap<string>();

        map.AddOrUpdate(5, "hello");

        Assert.Equal("hello", map[5]);
    }

    [Fact]
    public void AddOrUpdate_ExistingKey_UpdatesValue()
    {
        var map = new SparseMap<string>();

        map.AddOrUpdate(5, "hello");
        map.AddOrUpdate(5, "world");

        Assert.Equal(1, map.Count);
        Assert.Equal("world", map[5]);
    }

    [Fact]
    public void AddOrUpdate_ViaIndexer_UpdatesValue()
    {
        var map = new SparseMap<int>();

        map[10] = 100;
        map[10] = 200;

        Assert.Equal(1, map.Count);
        Assert.Equal(200, map[10]);
    }

#endregion

#region Remove

    [Fact]
    public void Remove_ExistingKey_ReturnsTrue()
    {
        var map = new SparseMap<int>();
        map.Add(5, 100);

        var result = map.Remove(5);

        Assert.True(result);
        Assert.Equal(0, map.Count);
        Assert.False(map.ContainsKey(5));
    }

    [Fact]
    public void Remove_NonExistentKey_ReturnsFalse()
    {
        var map = new SparseMap<int>();

        var result = map.Remove(999);

        Assert.False(result);
    }

    [Fact]
    public void Remove_KeyOutOfBounds_ReturnsFalse()
    {
        var map = new SparseMap<int>();

        var result = map.Remove(-1);

        Assert.False(result);
    }

    [Fact]
    public void Remove_MiddleElement_OtherElementsStillAccessible()
    {
        var map = new SparseMap<int>();
        map.Add(0, 10);
        map.Add(1, 20);
        map.Add(2, 30);

        map.Remove(1);

        Assert.Equal(2, map.Count);
        Assert.Equal(10, map[0]);
        Assert.Equal(30, map[2]);
        Assert.False(map.ContainsKey(1));
    }

    [Fact]
    public void Remove_LastElement_OtherElementsStillAccessible()
    {
        var map = new SparseMap<int>();
        map.Add(0, 10);
        map.Add(1, 20);
        map.Add(2, 30);

        map.Remove(2);

        Assert.Equal(2, map.Count);
        Assert.Equal(10, map[0]);
        Assert.Equal(20, map[1]);
    }

    [Fact]
    public void Remove_ThenAddSameKey_Works()
    {
        var map = new SparseMap<int>();
        map.Add(5, 100);
        map.Remove(5);

        map.Add(5, 200);

        Assert.Equal(1, map.Count);
        Assert.Equal(200, map[5]);
    }

    [Fact]
    public void Remove_AllElements_MapIsEmpty()
    {
        var map = new SparseMap<int>();
        map.Add(0, 10);
        map.Add(1, 20);
        map.Add(2, 30);

        map.Remove(0);
        map.Remove(1);
        map.Remove(2);

        Assert.Equal(0, map.Count);
    }

#endregion

#region ContainsKey

    [Fact]
    public void ContainsKey_ExistingKey_ReturnsTrue()
    {
        var map = new SparseMap<int>();
        map.Add(5, 100);

        Assert.True(map.ContainsKey(5));
    }

    [Fact]
    public void ContainsKey_NonExistentKey_ReturnsFalse()
    {
        var map = new SparseMap<int>();

        Assert.False(map.ContainsKey(5));
    }

    [Fact]
    public void ContainsKey_NegativeKey_ReturnsFalse()
    {
        var map = new SparseMap<int>();

        Assert.False(map.ContainsKey(-1));
    }

    [Fact]
    public void ContainsKey_RemovedKey_ReturnsFalse()
    {
        var map = new SparseMap<int>();
        map.Add(5, 100);
        map.Remove(5);

        Assert.False(map.ContainsKey(5));
    }

#endregion

#region TryGetValue

    [Fact]
    public void TryGetValue_ExistingKey_ReturnsTrueWithValue()
    {
        var map = new SparseMap<string>();
        map.Add(5, "hello");

        var result = map.TryGetValue(5, out var value);

        Assert.True(result);
        Assert.Equal("hello", value);
    }

    [Fact]
    public void TryGetValue_NonExistentKey_ReturnsFalse()
    {
        var map = new SparseMap<string>();

        var result = map.TryGetValue(999, out var value);

        Assert.False(result);
        Assert.Null(value);
    }

#endregion

#region Indexer

    [Fact]
    public void Indexer_Get_NonExistentKey_ThrowsKeyNotFoundException()
    {
        var map = new SparseMap<int>();

        Assert.Throws<KeyNotFoundException>(() => map[999]);
    }

    [Fact]
    public void Indexer_Get_NegativeKey_ThrowsKeyNotFoundException()
    {
        var map = new SparseMap<int>();

        Assert.Throws<KeyNotFoundException>(() => map[-1]);
    }

#endregion

#region GetOrDefault

    [Fact]
    public void GetOrDefault_ExistingKey_ReturnsValue()
    {
        var map = new SparseMap<int>();
        map.Add(5, 100);

        Assert.Equal(100, map.GetOrDefault(5, -1));
    }

    [Fact]
    public void GetOrDefault_NonExistentKey_ReturnsDefault()
    {
        var map = new SparseMap<int>();

        Assert.Equal(-1, map.GetOrDefault(999, -1));
    }

#endregion

#region GetIndex

    [Fact]
    public void GetIndex_ExistingKey_ReturnsDenseIndex()
    {
        var map = new SparseMap<int>();
        map.Add(0, 10);
        map.Add(1, 20);

        Assert.Equal(0, map.GetIndex(0));
        Assert.Equal(1, map.GetIndex(1));
    }

    [Fact]
    public void GetIndex_NonExistentKey_ReturnsMinusOne()
    {
        var map = new SparseMap<int>();

        Assert.Equal(-1, map.GetIndex(999));
    }

    [Fact]
    public void GetIndex_OutOfBounds_ReturnsMinusOne()
    {
        var map = new SparseMap<int>();

        Assert.Equal(-1, map.GetIndex(-1));
    }

#endregion

#region Clear

    [Fact]
    public void Clear_NonEmptyMap_RemovesAllElements()
    {
        var map = new SparseMap<int>();
        map.Add(0, 10);
        map.Add(1, 20);
        map.Add(2, 30);

        map.Clear();

        Assert.Equal(0, map.Count);
        Assert.False(map.ContainsKey(0));
        Assert.False(map.ContainsKey(1));
        Assert.False(map.ContainsKey(2));
    }

#endregion

#region ForEach

    [Fact]
    public void ForEach_VisitsAllElements()
    {
        var map = new SparseMap<int>();
        map.Add(0, 10);
        map.Add(1, 20);
        map.Add(2, 30);

        var sum = 0;
        map.ForEach((_, v) => sum += v);

        Assert.Equal(60, sum);
    }

#endregion

#region ForEachRef

    [Fact]
    public void ForEachRef_VisitsAllElements()
    {
        var map = new SparseMap<int>();
        map.Add(0, 10);
        map.Add(1, 20);
        map.Add(2, 30);

        var sum = 0;
        map.ForEachRef((int _, ref int v) => sum += v);

        Assert.Equal(60, sum);
    }

#endregion

#region GetDenseAsSpan

    [Fact]
    public void GetDenseAsSpan_ReturnsAllElements()
    {
        var map = new SparseMap<int>();
        map.Add(0, 10);
        map.Add(1, 20);

        var span = map.GetDenseAsSpan();

        Assert.Equal(2, span.Length);
    }

#endregion

#region IEnumerable

    [Fact]
    public void GetEnumerator_IteratesAllElements()
    {
        var map = new SparseMap<int>();
        map.Add(0, 10);
        map.Add(1, 20);
        map.Add(2, 30);

        var keys = new List<int>();
        var values = new List<int>();

        foreach (var (key, value) in map)
        {
            keys.Add(key);
            values.Add(value);
        }

        Assert.Equal([0, 1, 2], keys);
        Assert.Equal([10, 20, 30], values);
    }

#endregion

#region Constructor

    [Fact]
    public void Constructor_FromEnumerable_PopulatesCorrectly()
    {
        var source = new List<(int, string)> { (0, "a"), (5, "b"), (10, "c") };
        var map = new SparseMap<string>(source);

        Assert.Equal(3, map.Count);
        Assert.Equal("a", map[0]);
        Assert.Equal("b", map[5]);
        Assert.Equal("c", map[10]);
    }

#endregion

#region Stress

    [Fact]
    public void Stress_AddAndRemoveMany_MaintainsConsistency()
    {
        var map = new SparseMap<int>();

        for (var i = 0; i < 1000; i++)
        {
            map.Add(i, i * 10);
        }

        Assert.Equal(1000, map.Count);

        for (var i = 0; i < 1000; i += 2)
        {
            map.Remove(i);
        }

        Assert.Equal(500, map.Count);

        for (var i = 1; i < 1000; i += 2)
        {
            Assert.Equal(i * 10, map[i]);
        }

        for (var i = 0; i < 1000; i += 2)
        {
            Assert.False(map.ContainsKey(i));
        }
    }

#endregion
}
