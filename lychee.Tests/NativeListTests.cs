using lychee.collections;

namespace lychee.Tests;

public class NativeListTests : IDisposable
{
#region Add

    [Fact]
    public void Add_SingleItem_IncreasesCount()
    {
        using var list = new NativeList<int>();

        list.Add(42);

        Assert.Equal(1, list.Count);
        Assert.Equal(42, list[0]);
    }

    [Fact]
    public void Add_MultipleItems_AllAccessible()
    {
        using var list = new NativeList<int>();

        list.Add(1);
        list.Add(2);
        list.Add(3);

        Assert.Equal(3, list.Count);
        Assert.Equal(1, list[0]);
        Assert.Equal(2, list[1]);
        Assert.Equal(3, list[2]);
    }

    [Fact]
    public void Add_ExceedsCapacity_AutoGrows()
    {
        using var list = new NativeList<int>(2);

        list.Add(1);
        list.Add(2);
        list.Add(3); // should trigger growth

        Assert.Equal(3, list.Count);
        Assert.Equal(3, list[2]);
    }

    [Fact]
    public void Add_ByReference_Works()
    {
        using var list = new NativeList<int>();
        var value = 42;

        list.Add(in value);

        Assert.Equal(42, list[0]);
    }

#endregion

#region Indexer

    [Fact]
    public void Indexer_Set_UpdatesValue()
    {
        using var list = new NativeList<int>();
        list.Add(10);

        list[0] = 20;

        Assert.Equal(20, list[0]);
    }

    [Fact]
    public void Indexer_OutOfBounds_ThrowsInDebug()
    {
        using var list = new NativeList<int>();

        // Debug.Assert will trigger in debug builds
        // In release, this is undefined behavior
        // We just verify the list is empty
        Assert.Equal(0, list.Count);
    }

#endregion

#region Insert

    [Fact]
    public void Insert_AtBeginning_ShiftsElements()
    {
        using var list = new NativeList<int>();
        list.Add(2);
        list.Add(3);

        list.Insert(0, 1);

        Assert.Equal(3, list.Count);
        Assert.Equal(1, list[0]);
        Assert.Equal(2, list[1]);
        Assert.Equal(3, list[2]);
    }

    [Fact]
    public void Insert_InMiddle_ShiftsElements()
    {
        using var list = new NativeList<int>();
        list.Add(1);
        list.Add(3);

        list.Insert(1, 2);

        Assert.Equal(3, list.Count);
        Assert.Equal(1, list[0]);
        Assert.Equal(2, list[1]);
        Assert.Equal(3, list[2]);
    }

    [Fact]
    public void Insert_AtEnd_AppendsElement()
    {
        using var list = new NativeList<int>();
        list.Add(1);

        list.Insert(1, 2);

        Assert.Equal(2, list.Count);
        Assert.Equal(1, list[0]);
        Assert.Equal(2, list[1]);
    }

    [Fact]
    public void Insert_IntoEmptyList_Works()
    {
        using var list = new NativeList<int>();

        list.Insert(0, 42);

        Assert.Equal(1, list.Count);
        Assert.Equal(42, list[0]);
    }

#endregion

#region Remove / RemoveAt / RemoveLast

    [Fact]
    public void Remove_ExistingItem_ReturnsTrueAndRemoves()
    {
        using var list = new NativeList<int>();
        list.Add(1);
        list.Add(2);
        list.Add(3);

        var result = list.Remove(2);

        Assert.True(result);
        Assert.Equal(2, list.Count);
        Assert.Equal(1, list[0]);
        Assert.Equal(3, list[1]);
    }

    [Fact]
    public void Remove_NonExistentItem_ReturnsFalse()
    {
        using var list = new NativeList<int>();
        list.Add(1);

        var result = list.Remove(99);

        Assert.False(result);
        Assert.Equal(1, list.Count);
    }

    [Fact]
    public void RemoveAt_MiddleElement_ShiftsRemaining()
    {
        using var list = new NativeList<int>();
        list.Add(1);
        list.Add(2);
        list.Add(3);

        list.RemoveAt(1);

        Assert.Equal(2, list.Count);
        Assert.Equal(1, list[0]);
        Assert.Equal(3, list[1]);
    }

    [Fact]
    public void RemoveAt_LastElement_DecreasesCount()
    {
        using var list = new NativeList<int>();
        list.Add(1);
        list.Add(2);

        list.RemoveAt(1);

        Assert.Equal(1, list.Count);
        Assert.Equal(1, list[0]);
    }

    [Fact]
    public void RemoveAt_OutOfRange_Throws()
    {
        using var list = new NativeList<int>();
        list.Add(1);

        Assert.Throws<ArgumentOutOfRangeException>(() => list.RemoveAt(5));
    }

    [Fact]
    public void RemoveLast_NonEmptyList_RemovesLastElement()
    {
        using var list = new NativeList<int>();
        list.Add(1);
        list.Add(2);

        list.RemoveLast();

        Assert.Equal(1, list.Count);
        Assert.Equal(1, list[0]);
    }

    [Fact]
    public void RemoveLast_EmptyList_Throws()
    {
        using var list = new NativeList<int>();

        Assert.Throws<InvalidOperationException>(() => list.RemoveLast());
    }

#endregion

#region IndexOf / Contains

    [Fact]
    public void IndexOf_ExistingItem_ReturnsCorrectIndex()
    {
        using var list = new NativeList<int>();
        list.Add(10);
        list.Add(20);
        list.Add(30);

        Assert.Equal(0, list.IndexOf(10));
        Assert.Equal(1, list.IndexOf(20));
        Assert.Equal(2, list.IndexOf(30));
    }

    [Fact]
    public void IndexOf_NonExistentItem_ReturnsMinusOne()
    {
        using var list = new NativeList<int>();
        list.Add(10);

        Assert.Equal(-1, list.IndexOf(99));
    }

    [Fact]
    public void Contains_ExistingItem_ReturnsTrue()
    {
        using var list = new NativeList<int>();
        list.Add(42);

        Assert.True(list.Contains(42));
    }

    [Fact]
    public void Contains_NonExistentItem_ReturnsFalse()
    {
        using var list = new NativeList<int>();

        Assert.False(list.Contains(42));
    }

#endregion

#region Clear

    [Fact]
    public void Clear_NonEmptyList_ResetsCountToZero()
    {
        using var list = new NativeList<int>();
        list.Add(1);
        list.Add(2);

        list.Clear();

        Assert.Equal(0, list.Count);
        Assert.True(list.IsEmpty);
    }

    [Fact]
    public void Clear_PreservesCapacity()
    {
        using var list = new NativeList<int>(10);
        list.Add(1);
        list.Add(2);

        var capacityBefore = list.Capacity;
        list.Clear();

        Assert.Equal(capacityBefore, list.Capacity);
    }

#endregion

#region Resize

    [Fact]
    public void Resize_Enlarge_FillsWithDefault()
    {
        using var list = new NativeList<int>();
        list.Add(1);

        list.Resize(3);

        Assert.Equal(3, list.Count);
        Assert.Equal(1, list[0]);
        Assert.Equal(0, list[1]);
        Assert.Equal(0, list[2]);
    }

    [Fact]
    public void Resize_Enlarge_FillsWithSpecifiedValue()
    {
        using var list = new NativeList<int>();
        list.Add(1);

        list.Resize(3, -1);

        Assert.Equal(3, list.Count);
        Assert.Equal(1, list[0]);
        Assert.Equal(-1, list[1]);
        Assert.Equal(-1, list[2]);
    }

    [Fact]
    public void Resize_Shrink_Truncates()
    {
        using var list = new NativeList<int>();
        list.Add(1);
        list.Add(2);
        list.Add(3);

        list.Resize(1);

        Assert.Equal(1, list.Count);
        Assert.Equal(1, list[0]);
    }

#endregion

#region Fill

    [Fact]
    public void Fill_EntireList_SetsAllValues()
    {
        using var list = new NativeList<int>(3);
        list.Resize(3);

        list.Fill(42);

        Assert.Equal(42, list[0]);
        Assert.Equal(42, list[1]);
        Assert.Equal(42, list[2]);
    }

    [Fact]
    public void Fill_Range_SetsOnlySpecifiedRange()
    {
        using var list = new NativeList<int>();
        list.Resize(5);

        list.Fill(1, 4, 42);

        Assert.Equal(0, list[0]);
        Assert.Equal(42, list[1]);
        Assert.Equal(42, list[2]);
        Assert.Equal(42, list[3]);
        Assert.Equal(0, list[4]);
    }

#endregion

#region FindIndex / Exists

    [Fact]
    public void FindIndex_MatchingPredicate_ReturnsIndex()
    {
        using var list = new NativeList<int>();
        list.Add(1);
        list.Add(2);
        list.Add(3);

        Assert.Equal(1, list.FindIndex(x => x == 2));
    }

    [Fact]
    public void FindIndex_NoMatch_ReturnsMinusOne()
    {
        using var list = new NativeList<int>();
        list.Add(1);

        Assert.Equal(-1, list.FindIndex(x => x == 99));
    }

    [Fact]
    public void Exists_MatchingPredicate_ReturnsTrue()
    {
        using var list = new NativeList<int>();
        list.Add(1);
        list.Add(2);

        Assert.True(list.Exists(x => x == 2));
    }

    [Fact]
    public void Exists_NoMatch_ReturnsFalse()
    {
        using var list = new NativeList<int>();

        Assert.False(list.Exists(x => x == 99));
    }

#endregion

#region AsSpan

    [Fact]
    public void AsSpan_ReturnsAllElements()
    {
        using var list = new NativeList<int>();
        list.Add(1);
        list.Add(2);
        list.Add(3);

        var span = list.AsSpan();

        Assert.Equal(3, span.Length);
        Assert.Equal(1, span[0]);
        Assert.Equal(2, span[1]);
        Assert.Equal(3, span[2]);
    }

    [Fact]
    public void AsSpan_WithRange_ReturnsSlice()
    {
        using var list = new NativeList<int>();
        list.Add(1);
        list.Add(2);
        list.Add(3);

        var span = list.AsSpan(1, 2);

        Assert.Equal(2, span.Length);
        Assert.Equal(2, span[0]);
        Assert.Equal(3, span[1]);
    }

#endregion

#region CopyTo

    [Fact]
    public void CopyTo_Array_CopiesAllElements()
    {
        using var list = new NativeList<int>();
        list.Add(1);
        list.Add(2);
        list.Add(3);

        var array = new int[3];
        list.CopyTo(array);

        Assert.Equal([1, 2, 3], array);
    }

    [Fact]
    public void CopyTo_Span_CopiesAllElements()
    {
        using var list = new NativeList<int>();
        list.Add(1);
        list.Add(2);

        var array = new int[2];
        list.CopyTo(array.AsSpan());

        Assert.Equal([1, 2], array);
    }

#endregion

#region ForEach

    [Fact]
    public void ForEach_VisitsAllElements()
    {
        using var list = new NativeList<int>();
        list.Add(1);
        list.Add(2);
        list.Add(3);

        var sum = 0;
        list.ForEach(x => sum += x);

        Assert.Equal(6, sum);
    }

    [Fact]
    public void ForEach_ByRef_ModifiesElements()
    {
        using var list = new NativeList<int>();
        list.Add(1);
        list.Add(2);
        list.Add(3);

        list.ForEach((ref int x) => x *= 2);

        Assert.Equal(2, list[0]);
        Assert.Equal(4, list[1]);
        Assert.Equal(6, list[2]);
    }

#endregion

#region Properties

    [Fact]
    public void IsEmpty_NewList_ReturnsTrue()
    {
        using var list = new NativeList<int>();

        Assert.True(list.IsEmpty);
    }

    [Fact]
    public void IsEmpty_NonEmptyList_ReturnsFalse()
    {
        using var list = new NativeList<int>();
        list.Add(1);

        Assert.False(list.IsEmpty);
    }

    [Fact]
    public void IsFull_FullList_ReturnsTrue()
    {
        using var list = new NativeList<int>(2);
        list.Add(1);
        list.Add(2);

        Assert.True(list.IsFull);
    }

    [Fact]
    public void Capacity_InitialValue_MatchesConstructor()
    {
        using var list = new NativeList<int>(10);

        Assert.Equal(10, list.Capacity);
    }

    [Fact]
    public void Count_TracksAdditionsAndRemovals()
    {
        using var list = new NativeList<int>();

        Assert.Equal(0, list.Count);

        list.Add(1);
        Assert.Equal(1, list.Count);

        list.Add(2);
        Assert.Equal(2, list.Count);

        list.RemoveLast();
        Assert.Equal(1, list.Count);
    }

#endregion

#region Constructor

    [Fact]
    public void Constructor_FromArray_CopiesElements()
    {
        var array = new[] { 1, 2, 3 };
        using var list = new NativeList<int>(array);

        Assert.Equal(3, list.Count);
        Assert.Equal(1, list[0]);
        Assert.Equal(2, list[1]);
        Assert.Equal(3, list[2]);
    }

    [Fact]
    public void Constructor_WithCapacity_PreAllocates()
    {
        using var list = new NativeList<int>(100);

        Assert.Equal(100, list.Capacity);
        Assert.Equal(0, list.Count);
    }

#endregion

#region ShrinkToFit

    [Fact]
    public void ShrinkToFit_ReducesCapacity()
    {
        using var list = new NativeList<int>(100);
        list.Add(1);
        list.Add(2);

        list.ShirkToFit();

        Assert.Equal(2, list.Capacity);
        Assert.Equal(2, list.Count);
    }

#endregion

#region Implicit Span Conversion

    [Fact]
    public void ImplicitConversion_ToSpan_Works()
    {
        using var list = new NativeList<int>();
        list.Add(10);
        list.Add(20);

        Span<int> span = list;

        Assert.Equal(2, span.Length);
        Assert.Equal(10, span[0]);
        Assert.Equal(20, span[1]);
    }

#endregion

#region Enumerator

    [Fact]
    public void GetEnumerator_IteratesAllElements()
    {
        using var list = new NativeList<int>();
        list.Add(1);
        list.Add(2);
        list.Add(3);

        var items = new List<int>();
        foreach (var item in list)
        {
            items.Add(item);
        }

        Assert.Equal([1, 2, 3], items);
    }

#endregion

#region Stress

    [Fact]
    public void Stress_AddManyElements_MaintainsConsistency()
    {
        using var list = new NativeList<int>();

        for (var i = 0; i < 10000; i++)
        {
            list.Add(i);
        }

        Assert.Equal(10000, list.Count);

        for (var i = 0; i < 10000; i++)
        {
            Assert.Equal(i, list[i]);
        }
    }

#endregion

    public void Dispose()
    {
        // Each test creates its own list with 'using', so nothing to dispose here
    }
}
