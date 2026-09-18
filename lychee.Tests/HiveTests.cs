using lychee.collections;

namespace lychee.Tests;

public class HiveTests
{
#region Private Static Fields

    private static int DisposeCount;

#endregion

#region Constructors

    [Fact]
    public void DefaultConstructor_UsesDefaultLimits()
    {
        using var hive = new Hive<int>();

        Assert.Equal(Hive<int>.BlockCapacityDefaultLimits, hive.BlockCapacityLimits);
        Assert.Equal(0, hive.Count);
        Assert.Equal(0, hive.Capacity);
        Assert.Equal(0, hive.BlockCount);
        Assert.True(hive.IsEmpty);
    }

    [Fact]
    public void LimitsConstructor_StoresLimits()
    {
        using var hive = new Hive<int>(new HiveBlockLimits(4, 32));

        Assert.Equal(4, hive.BlockCapacityLimits.Min);
        Assert.Equal(32, hive.BlockCapacityLimits.Max);
    }

    [Fact]
    public void EnumerableConstructor_AddsAllElements()
    {
        using var hive = new Hive<int>(Enumerable.Range(0, 100));

        AssertValues(hive, Enumerable.Range(0, 100));
    }

    [Theory]
    [InlineData(0, 16)]
    [InlineData(1, 16)]
    [InlineData(2, 16)]
    public void LimitsConstructor_BelowHardMinimum_Throws(int min, int max)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Hive<int>(new HiveBlockLimits(min, max)));
    }

    [Fact]
    public void LimitsConstructor_AboveHardMaximum_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Hive<int>(new HiveBlockLimits(3, ushort.MaxValue + 1)));
    }

    [Fact]
    public void LimitsConstructor_InvertedLimits_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Hive<int>(new HiveBlockLimits(20, 10)));
    }

    [Fact]
    public void EveryBlockCapacity_StaysWithinLimits()
    {
        using var hive = new Hive<int>(new HiveBlockLimits(3, 16));

        List<HiveHandle> handles = [];
        for (var i = 0; i < 5000; i++)
        {
            handles.Add(hive.Add(i));
        }

        var removed = 0;
        for (var i = 0; i < handles.Count; i += 3)
        {
            hive.Remove(handles[i]);
            removed++;
        }

        for (var i = 0; i < 1000; i++)
        {
            hive.Add(i);
        }

        // ValidateInvariants checks that every block respects [Min, Max].
        hive.ValidateInvariants();
        Assert.Equal(5000 - removed + 1000, hive.Count);
    }

#endregion

#region Add & Remove

    [Fact]
    public void Add_Single_ValueIsReadable()
    {
        using var hive = new Hive<int>();

        var handle = hive.Add(42);

        Assert.Equal(1, hive.Count);
        Assert.False(hive.IsEmpty);
        Assert.True(hive.IsAlive(handle));
        Assert.Equal(42, hive.GetReference(handle));
    }

    [Fact]
    public void Add_ByReference_StoresValue()
    {
        using var hive = new Hive<int>();
        var value = 7;

        var handle = hive.Add(in value);

        Assert.Equal(7, hive.GetReference(handle));
    }

    [Fact]
    public void Add_ManyElements_AllValuesPreserved()
    {
        using var hive = new Hive<int>(new HiveBlockLimits(3, 16));

        List<HiveHandle> handles = [];
        for (var i = 0; i < 1000; i++)
        {
            handles.Add(hive.Add(i * 7));
        }

        for (var i = 0; i < handles.Count; i++)
        {
            Assert.True(hive.IsAlive(handles[i]));
            Assert.Equal(i * 7, hive.GetReference(handles[i]));
        }

        hive.ValidateInvariants();
    }

    [Fact]
    public void Remove_LiveElement_ReturnsTrueAndShrinksCount()
    {
        using var hive = new Hive<int>();

        var handle = hive.Add(1);
        hive.Add(2);

        Assert.True(hive.Remove(handle));
        Assert.Equal(1, hive.Count);
        Assert.False(hive.IsAlive(handle));
        hive.ValidateInvariants();
    }

    [Fact]
    public void Remove_AlreadyRemoved_ReturnsFalse()
    {
        using var hive = new Hive<int>();

        var handle = hive.Add(1);
        hive.Remove(handle);

        Assert.False(hive.Remove(handle));
    }

    [Fact]
    public void Remove_ForeignHandle_ReturnsFalse()
    {
        using var firstHive = new Hive<int>();
        using var secondHive = new Hive<int>();

        var handle = firstHive.Add(1);

        Assert.False(secondHive.Remove(handle));
    }

    [Fact]
    public void Remove_AllElements_EmptiesHiveAndKeepsCapacity()
    {
        using var hive = new Hive<int>(new HiveBlockLimits(3, 16));

        List<HiveHandle> handles = [];
        for (var i = 0; i < 100; i++)
        {
            handles.Add(hive.Add(i));
        }

        var capacityBefore = hive.Capacity;

        foreach (var handle in handles)
        {
            Assert.True(hive.Remove(handle));
        }

        Assert.Equal(0, hive.Count);
        Assert.True(hive.IsEmpty);
        Assert.Equal(capacityBefore, hive.Capacity);
        hive.ValidateInvariants();
    }

    [Fact]
    public void Add_AfterRemovals_ReusesHolesWithoutGrowingCapacity()
    {
        using var hive = new Hive<int>(new HiveBlockLimits(4, 16));

        List<HiveHandle> handles = [];
        for (var i = 0; i < 200; i++)
        {
            handles.Add(hive.Add(i));
        }

        // Create a hole for every other element, then refill the same number of slots.
        for (var i = 0; i < handles.Count; i += 2)
        {
            hive.Remove(handles[i]);
        }

        var capacityAfterRemovals = hive.Capacity;

        for (var i = 0; i < 100; i++)
        {
            hive.Add(10_000 + i);
        }

        Assert.Equal(200, hive.Count);
        Assert.Equal(capacityAfterRemovals, hive.Capacity);
        hive.ValidateInvariants();
    }

    [Fact]
    public void Remove_LastElement_IsFollowedByWorkingAdd()
    {
        using var hive = new Hive<int>(new HiveBlockLimits(3, 8));

        var handle = hive.Add(1);
        Assert.True(hive.Remove(handle));
        Assert.Equal(0, hive.Count);

        var replacement = hive.Add(2);

        Assert.Equal(1, hive.Count);
        Assert.Equal(2, hive.GetReference(replacement));
        hive.ValidateInvariants();
    }

    [Fact]
    public void AddUninitializedValues_AddsRequestedNumberOfDefaultElements()
    {
        using var hive = new Hive<int>();

        hive.AddUninitializedValues(500);

        Assert.Equal(500, hive.Count);
        Assert.All(Enumerate(hive), value => Assert.Equal(0, value));
        hive.ValidateInvariants();
    }

    [Fact]
    public void AddUninitializedValues_NegativeCount_Throws()
    {
        using var hive = new Hive<int>();

        Assert.Throws<ArgumentOutOfRangeException>(() => hive.AddUninitializedValues(-1));
    }

    [Fact]
    public void AddRange_AddsEveryElement()
    {
        using var hive = new Hive<int>(new HiveBlockLimits(3, 8));
        var values = Enumerable.Range(0, 777).ToArray();

        hive.AddRange(values);

        AssertValues(hive, values);
    }

#endregion

#region Pointer & Handle Stability

    [Fact]
    public unsafe void Pointers_StayValidAcrossRemovalsAndInsertions()
    {
        using var hive = new Hive<int>(new HiveBlockLimits(3, 16));

        var handles = new HiveHandle[256];
        var pointers = new int*[256];

        for (var i = 0; i < handles.Length; i++)
        {
            handles[i] = hive.Add(i);
            pointers[i] = hive.GetPointer(handles[i]);
        }

        for (var i = 0; i < handles.Length; i += 3)
        {
            hive.Remove(handles[i]);
        }

        // A handle is dead as soon as its element is removed.
        for (var i = 0; i < handles.Length; i += 3)
        {
            Assert.False(hive.IsAlive(handles[i]));
        }

        for (var i = 0; i < 512; i++)
        {
            hive.Add(10_000 + i);
        }

        // The remaining pointers must still refer to the very same elements.
        for (var i = 0; i < handles.Length; i++)
        {
            if (i % 3 == 0)
            {
                continue;
            }

            Assert.True(hive.IsAlive(handles[i]));
            Assert.Equal(i, *pointers[i]);
            Assert.Equal(i, hive.GetReference(handles[i]));
        }

        hive.ValidateInvariants();
    }

    [Fact]
    public unsafe void GetHandle_RoundTripsWithGetPointer()
    {
        using var hive = new Hive<int>(new HiveBlockLimits(3, 8));

        List<HiveHandle> handles = [];
        for (var i = 0; i < 60; i++)
        {
            handles.Add(hive.Add(i));
        }

        for (var i = 0; i < handles.Count; i += 4)
        {
            hive.Remove(handles[i]);
        }

        foreach (var handle in handles)
        {
            if (!hive.IsAlive(handle))
            {
                continue;
            }

            var pointer = hive.GetPointer(handle);

            Assert.Equal(handle, hive.GetHandle(pointer));
        }
    }

    [Fact]
    public unsafe void GetHandle_UnknownPointer_Throws()
    {
        using var hive = new Hive<int>();
        hive.Add(1);

        var foreign = stackalloc int[1];

        Assert.Throws<ArgumentOutOfRangeException>(() => hive.GetHandle(foreign));
    }

    [Fact]
    public void IsAlive_HandlesForReservedBlocks_ReportFalse()
    {
        using var hive = new Hive<int>(new HiveBlockLimits(3, 8));

        var handle = hive.Add(1);
        hive.Clear();

        Assert.False(hive.IsAlive(handle));
    }

#endregion

#region Enumeration

    [Fact]
    public void Enumeration_EmptyHive_YieldsNothing()
    {
        using var hive = new Hive<int>();

        Assert.Empty(Enumerate(hive));
        Assert.False(hive.GetEnumerator().MoveNext());
    }

    [Fact]
    public void Enumeration_WalksOverHolesInEveryPosition()
    {
        using var hive = new Hive<int>(new HiveBlockLimits(3, 8));

        List<HiveHandle> handles = [];
        for (var i = 0; i < 120; i++)
        {
            handles.Add(hive.Add(i));
        }

        // Holes at the very start, the very end, in the middle and across whole blocks.
        hive.Remove(handles[0]);
        hive.Remove(handles[^1]);
        for (var i = 2; i < 20; i++)
        {
            hive.Remove(handles[i]);
        }

        for (var i = 40; i < 60; i++)
        {
            hive.Remove(handles[i]);
        }

        var aliveValues = new HashSet<int>();
        foreach (var handle in handles)
        {
            if (hive.IsAlive(handle))
            {
                aliveValues.Add(hive.GetReference(handle));
            }
        }

        AssertValues(hive, aliveValues);
    }

    [Fact]
    public void Enumeration_WholeBlocksOfHolesAreSkipped()
    {
        using var hive = new Hive<int>(new HiveBlockLimits(3, 3));

        List<HiveHandle> handles = [];
        for (var i = 0; i < 30; i++)
        {
            handles.Add(hive.Add(i));
        }

        // Entire first, fourth and last blocks are emptied.
        var removedValues = new HashSet<int> { 0, 1, 2, 9, 10, 11, 27, 28, 29 };
        foreach (var value in removedValues)
        {
            hive.Remove(handles[value]);
        }

        AssertValues(hive, Enumerable.Range(0, 30).Where(value => !removedValues.Contains(value)));
    }

    [Fact]
    public void RemoveCurrent_DeletesWhileIterating()
    {
        using var hive = new Hive<int>(new HiveBlockLimits(3, 8));

        for (var i = 0; i < 200; i++)
        {
            hive.Add(i);
        }

        var enumerator = hive.GetEnumerator();
        while (enumerator.MoveNext())
        {
            if (enumerator.Current % 3 == 0)
            {
                enumerator.RemoveCurrent();
            }
        }

        AssertValues(hive, Enumerable.Range(0, 200).Where(x => x % 3 != 0));
    }

    [Fact]
    public void RemoveCurrent_EveryElement_EmptiesHive()
    {
        using var hive = new Hive<int>(new HiveBlockLimits(3, 5));

        for (var i = 0; i < 33; i++)
        {
            hive.Add(i);
        }

        var enumerator = hive.GetEnumerator();
        while (enumerator.MoveNext())
        {
            enumerator.RemoveCurrent();
        }

        Assert.Equal(0, hive.Count);
        Assert.False(hive.GetEnumerator().MoveNext());
        hive.ValidateInvariants();
    }

    [Fact]
    public void RemoveCurrent_OnUnpositionedEnumerator_Throws()
    {
        using var hive = new Hive<int>();
        hive.Add(1);

        var enumerator = hive.GetEnumerator();

        Assert.Throws<InvalidOperationException>(() => enumerator.RemoveCurrent());
    }

    [Fact]
    public void Enumerator_Reset_RestartsEnumeration()
    {
        using var hive = new Hive<int>(new HiveBlockLimits(3, 5));

        for (var i = 0; i < 12; i++)
        {
            hive.Add(i);
        }

        var enumerator = hive.GetEnumerator();
        var count = 0;
        while (enumerator.MoveNext())
        {
            count++;
        }

        Assert.Equal(12, count);

        enumerator.Reset();
        count = 0;
        while (enumerator.MoveNext())
        {
            count++;
        }

        Assert.Equal(12, count);
    }

    [Fact]
    public void ForEach_VisitsEveryElementOnce()
    {
        using var hive = new Hive<int>(new HiveBlockLimits(3, 8));

        for (var i = 0; i < 100; i++)
        {
            hive.Add(i);
        }

        var visited = new List<int>();
        hive.ForEach((ref int item) => visited.Add(item));

        AssertValues(hive, visited);
    }

    [Fact]
    public void ForEach_CanMutateElements()
    {
        using var hive = new Hive<int>();

        for (var i = 0; i < 10; i++)
        {
            hive.Add(i);
        }

        hive.ForEach((ref int item) => item *= 2);

        AssertValues(hive, Enumerable.Range(0, 10).Select(x => x * 2));
    }

#endregion

#region RemoveRange

    [Fact]
    public void RemoveRange_EmptyRange_RemovesNothing()
    {
        using var hive = new Hive<int>(new HiveBlockLimits(3, 8));

        for (var i = 0; i < 20; i++)
        {
            hive.Add(i);
        }

        var first = At(hive, 5);
        var last = At(hive, 5);

        hive.RemoveRange(first, last);

        Assert.Equal(20, hive.Count);
    }

    [Fact]
    public void RemoveRange_WithinOneBlock_MatchesIndividualRemoval()
    {
        using var hive = new Hive<int>(new HiveBlockLimits(3, 8));

        for (var i = 0; i < 40; i++)
        {
            hive.Add(i);
        }

        var first = At(hive, 4);
        var last = At(hive, 12);
        var removedValues = CollectRange(first, last);

        hive.RemoveRange(first, last);

        AssertValues(hive, Enumerable.Range(0, 40).Where(x => !removedValues.Contains(x)));
    }

    [Fact]
    public void RemoveRange_AcrossBlocks_MatchesIndividualRemoval()
    {
        using var hive = new Hive<int>(new HiveBlockLimits(3, 8));

        for (var i = 0; i < 200; i++)
        {
            hive.Add(i);
        }

        var first = At(hive, 10);
        var last = At(hive, 150);
        var removedValues = CollectRange(first, last);

        hive.RemoveRange(first, last);

        AssertValues(hive, Enumerable.Range(0, 200).Where(x => !removedValues.Contains(x)));
    }

    [Fact]
    public void RemoveRange_ToEndOfHive_RemovesTail()
    {
        using var hive = new Hive<int>(new HiveBlockLimits(3, 8));

        for (var i = 0; i < 50; i++)
        {
            hive.Add(i);
        }

        var first = At(hive, 30);
        var last = End(hive);

        hive.RemoveRange(first, last);

        AssertValues(hive, Enumerable.Range(0, 30));
    }

    [Fact]
    public void RemoveRange_FromFirstElement_RemovesHead()
    {
        using var hive = new Hive<int>(new HiveBlockLimits(3, 8));

        for (var i = 0; i < 40; i++)
        {
            hive.Add(i);
        }

        // The unstarted enumerator denotes the first element.
        hive.RemoveRange(hive.GetEnumerator(), At(hive, 30));

        AssertValues(hive, Enumerable.Range(30, 10));
    }

    [Fact]
    public void RemoveRange_SequentialRanges_RemoveEverything()
    {
        using var hive = new Hive<int>(new HiveBlockLimits(3, 8));

        for (var i = 0; i < 60; i++)
        {
            hive.Add(i);
        }

        hive.RemoveRange(At(hive, 0), At(hive, 30));
        Assert.Equal(30, hive.Count);

        hive.RemoveRange(hive.GetEnumerator(), End(hive));
        Assert.Equal(0, hive.Count);
        hive.ValidateInvariants();
    }

#endregion

#region Capacity Management

    [Fact]
    public void Reserve_GrowsCapacityWithoutAddingElements()
    {
        using var hive = new Hive<int>(new HiveBlockLimits(3, 16));

        hive.Reserve(500);

        Assert.True(hive.Capacity >= 500);
        Assert.Equal(0, hive.Count);
        Assert.True(hive.BlockCount > 0);
        hive.ValidateInvariants();
    }

    [Fact]
    public unsafe void Reserve_DoesNotInvalidatePointers()
    {
        using var hive = new Hive<int>(new HiveBlockLimits(3, 16));

        List<HiveHandle> handles = [];
        for (var i = 0; i < 50; i++)
        {
            handles.Add(hive.Add(i));
        }

        var pointers = handles.Select(handle => (nint)hive.GetPointer(handle)).ToArray();

        hive.Reserve(10_000);

        for (var i = 0; i < handles.Count; i++)
        {
            Assert.Equal(i, *(int*)pointers[i]);
        }

        hive.ValidateInvariants();
    }

    [Fact]
    public unsafe void TrimCapacity_ReleasesReservedBlocksAndKeepsElements()
    {
        using var hive = new Hive<int>(new HiveBlockLimits(3, 16));

        List<HiveHandle> handles = [];
        for (var i = 0; i < 60; i++)
        {
            handles.Add(hive.Add(i));
        }

        var pointers = handles.Select(handle => (nint)hive.GetPointer(handle)).ToArray();
        hive.Reserve(5000);
        var capacityAfterReserve = hive.Capacity;

        hive.TrimCapacity();

        Assert.True(hive.Capacity < capacityAfterReserve);
        Assert.Equal(60, hive.Count);

        for (var i = 0; i < handles.Count; i++)
        {
            Assert.Equal(i, *(int*)pointers[i]);
        }

        hive.ValidateInvariants();
    }

    [Fact]
    public void TrimCapacity_WithTarget_StopsAtRequestedCapacity()
    {
        using var hive = new Hive<int>(new HiveBlockLimits(3, 16));

        hive.Reserve(20000);

        var capacityAfterReserve = hive.Capacity;
        Assert.True(capacityAfterReserve >= 20000);

        hive.TrimCapacity(1000);

        Assert.True(hive.Capacity >= 1000);
        Assert.True(hive.Capacity < capacityAfterReserve);
        hive.ValidateInvariants();
    }

    [Fact]
    public void TrimCapacity_NegativeTarget_Throws()
    {
        using var hive = new Hive<int>();

        Assert.Throws<ArgumentOutOfRangeException>(() => hive.TrimCapacity(-1));
    }

    [Fact]
    public void Reserve_NegativeCount_Throws()
    {
        using var hive = new Hive<int>();

        Assert.Throws<ArgumentOutOfRangeException>(() => hive.Reserve(-1));
    }

    [Fact]
    public void Clear_RemovesElementsButKeepsBlocks()
    {
        using var hive = new Hive<int>(new HiveBlockLimits(3, 16));

        for (var i = 0; i < 200; i++)
        {
            hive.Add(i);
        }

        var capacityBefore = hive.Capacity;
        var blockCountBefore = hive.BlockCount;

        hive.Clear();

        Assert.Equal(0, hive.Count);
        Assert.Equal(capacityBefore, hive.Capacity);
        Assert.Equal(blockCountBefore, hive.BlockCount);
        Assert.False(hive.GetEnumerator().MoveNext());
        hive.ValidateInvariants();
    }

    [Fact]
    public void Clear_ThenAdd_ReusesReservedBlocks()
    {
        using var hive = new Hive<int>(new HiveBlockLimits(3, 16));

        for (var i = 0; i < 100; i++)
        {
            hive.Add(i);
        }

        var capacityBefore = hive.Capacity;
        hive.Clear();

        for (var i = 0; i < 100; i++)
        {
            hive.Add(i);
        }

        Assert.Equal(100, hive.Count);
        Assert.Equal(capacityBefore, hive.Capacity);
        hive.ValidateInvariants();
    }

    [Fact]
    public void Clear_IsIdempotent()
    {
        using var hive = new Hive<int>(new HiveBlockLimits(3, 16));

        for (var i = 0; i < 10; i++)
        {
            hive.Add(i);
        }

        hive.Clear();
        hive.Clear();

        Assert.Equal(0, hive.Count);
        hive.ValidateInvariants();
    }

#endregion

#region Randomized Oracle

    [Fact]
    public unsafe void Randomized_AddRemove_MatchesOracleAndKeepsPointersStable()
    {
        var random = new Random(20240918);
        using var hive = new Hive<int>(new HiveBlockLimits(3, 16));

        var valueByIdDict = new Dictionary<int, int>();
        var handleByIdDict = new Dictionary<int, HiveHandle>();
        var pointerByIdDict = new Dictionary<int, nint>();
        var nextId = 0;

        for (var step = 0; step < 6000; step++)
        {
            if (valueByIdDict.Count == 0 || random.Next(100) < 58)
            {
                var id = nextId++;
                var handle = hive.Add(id * 3 + 1);

                handleByIdDict[id] = handle;
                valueByIdDict[id] = id * 3 + 1;
                pointerByIdDict[id] = (nint)hive.GetPointer(handle);
            }
            else
            {
                var ids = valueByIdDict.Keys.ToArray();
                var id = ids[random.Next(ids.Length)];
                var handle = handleByIdDict[id];

                Assert.True(hive.Remove(handle));

                // A handle is dead as soon as its element is removed.
                Assert.False(hive.IsAlive(handle));

                valueByIdDict.Remove(id);
                handleByIdDict.Remove(id);
                pointerByIdDict.Remove(id);
            }

            if (step % 250 == 0)
            {
                Assert.Equal(valueByIdDict.Count, hive.Count);
                hive.ValidateInvariants();

                foreach (var (id, pointer) in pointerByIdDict)
                {
                    Assert.True(hive.IsAlive(handleByIdDict[id]));
                    Assert.Equal(id * 3 + 1, *(int*)pointer);
                }
            }
        }

        Assert.Equal(valueByIdDict.Count, hive.Count);
        hive.ValidateInvariants();

        var expected = valueByIdDict.Values.ToList();
        expected.Sort();

        var actual = Enumerate(hive);
        actual.Sort();

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Handle_IsPermanentlyInvalidatedWhenItsBlockIsRecycled()
    {
        using var hive = new Hive<int>(new HiveBlockLimits(4, 4));

        List<HiveHandle> handles = [];
        for (var i = 0; i < 8; i++)
        {
            handles.Add(hive.Add(i));
        }

        // Empty the second block so that the hive recycles it.
        foreach (var handle in handles.Skip(4))
        {
            hive.Remove(handle);
        }

        foreach (var handle in handles.Skip(4))
        {
            Assert.False(hive.IsAlive(handle));
        }

        for (var i = 0; i < 4; i++)
        {
            hive.Add(100 + i);
        }

        // The recycled block must carry a fresh identifier, so the old handles stay dead.
        foreach (var handle in handles.Skip(4))
        {
            Assert.False(hive.IsAlive(handle));
        }

        hive.ValidateInvariants();
    }

    [Fact]
    public void Randomized_RemoveRange_MatchesIndividualRemoval()
    {
        var random = new Random(4242);
        using var hive = new Hive<int>(new HiveBlockLimits(3, 16));

        for (var i = 0; i < 400; i++)
        {
            hive.Add(i);
        }

        for (var round = 0; round < 40; round++)
        {
            var total = hive.Count;
            if (total == 0)
            {
                break;
            }

            var start = random.Next(total);
            var end = Math.Min(start + random.Next(1, 31), total);

            var first = At(hive, start);
            var last = At(hive, end);
            var removedValues = CollectRange(first, last);

            hive.RemoveRange(first, last);

            Assert.Equal(total - removedValues.Count, hive.Count);

            var alive = new HashSet<int>(Enumerate(hive));
            foreach (var value in removedValues)
            {
                Assert.DoesNotContain(value, alive);
            }

            hive.ValidateInvariants();
        }
    }

#endregion

#region Stress & Type Variants

    [Fact]
    public void SequentialInserts_DoNotCreateHoles()
    {
        using var hive = new Hive<int>(new HiveBlockLimits(4, 64));

        for (var i = 0; i < 500; i++)
        {
            hive.Add(i);
            hive.ValidateInvariants();
        }

        Assert.Equal(500, hive.Count);
    }

    [Fact]
    public void AlternatingInsertRemove_KeepsHiveConsistent()
    {
        using var hive = new Hive<int>(new HiveBlockLimits(3, 16));

        List<HiveHandle> handles = [];
        var next = 0;

        for (var round = 0; round < 2000; round++)
        {
            handles.Add(hive.Add(next));
            next++;

            if (handles.Count > 17)
            {
                var index = round % handles.Count;

                Assert.True(hive.Remove(handles[index]));
                handles.RemoveAt(index);
            }

            Assert.Equal(handles.Count, hive.Count);
        }

        hive.ValidateInvariants();
    }

    [Fact]
    public void MassRemoveThenMassInsert_ReusesHoles()
    {
        using var hive = new Hive<int>(new HiveBlockLimits(3, 16));

        List<HiveHandle> handles = [];
        for (var i = 0; i < 3000; i++)
        {
            handles.Add(hive.Add(i));
        }

        foreach (var handle in handles)
        {
            hive.Remove(handle);
        }

        var capacityAfterRemovals = hive.Capacity;

        for (var i = 0; i < 3000; i++)
        {
            hive.Add(i);
        }

        Assert.Equal(3000, hive.Count);
        Assert.Equal(capacityAfterRemovals, hive.Capacity);
        hive.ValidateInvariants();
    }

    [Fact]
    public void ByteElements_WorkWithWidenedStride()
    {
        RunRandomStress(4000, 77, i => (byte)i);
    }

    [Fact]
    public void LongElements_WorkWithNativeStride()
    {
        RunRandomStress(4000, 99, i => (long)i * 1_000_003);
    }

    [Fact]
    public void DateTimeElements_WorkWithEightByteAlignment()
    {
        RunRandomStress(2000, 123, i => new DateTime(2020, 1, 1).AddSeconds(i));
    }

#endregion

#region Staging (HighWater)

    [Fact]
    public void TailInserts_AdvanceHighWaterWithoutGrowingCapacity()
    {
        using var hive = new Hive<int>(new HiveBlockLimits(4, 4));

        hive.Add(0);
        hive.Add(1);
        hive.Add(2);

        Assert.Equal(3, hive.DebugBackBlock!.HighWater);
        Assert.Equal(4, hive.Capacity);
        Assert.Equal(1, hive.BlockCount);

        hive.Add(3);

        Assert.Equal(4, hive.DebugBackBlock!.HighWater);
        Assert.Equal(4, hive.Capacity);

        hive.Add(4);

        Assert.Equal(2, hive.BlockCount);
        Assert.Equal(8, hive.Capacity);
        Assert.Equal(1, hive.DebugBackBlock!.HighWater);
        hive.ValidateInvariants();
    }

    [Fact]
    public void RemovingTailElement_ThenAdding_ReusesTheHole()
    {
        using var hive = new Hive<int>(new HiveBlockLimits(4, 4));

        List<HiveHandle> handles = [];
        for (var i = 0; i < 4; i++)
        {
            handles.Add(hive.Add(i));
        }

        hive.Remove(handles[^1]);

        Assert.Equal(4, hive.Capacity);

        hive.Add(99);

        Assert.Equal(4, hive.Count);
        Assert.Equal(4, hive.Capacity);
        Assert.Equal(1, hive.BlockCount);
        hive.ValidateInvariants();
    }

    [Fact]
    public void EmptiedBackBlock_IsResetAndReused()
    {
        using var hive = new Hive<int>(new HiveBlockLimits(4, 4));

        List<HiveHandle> handles = [];
        for (var i = 0; i < 8; i++)
        {
            handles.Add(hive.Add(i));
        }

        Assert.Equal(2, hive.BlockCount);

        // Empty the second block completely.
        foreach (var handle in handles.Skip(4))
        {
            hive.Remove(handle);
        }

        hive.ValidateInvariants();

        // Reusing the freed block must not allocate more capacity.
        var capacityBefore = hive.Capacity;
        hive.Add(100);

        Assert.Equal(5, hive.Count);
        Assert.Equal(capacityBefore, hive.Capacity);
        hive.ValidateInvariants();
    }

    [Fact]
    public void FullBlockBoundary_AddsNewBlockAndKeepsInvariants()
    {
        using var hive = new Hive<int>(new HiveBlockLimits(4, 4));

        for (var i = 0; i < 5; i++)
        {
            hive.Add(i);
            hive.ValidateInvariants();
        }

        Assert.Equal(2, hive.BlockCount);
        Assert.Equal(8, hive.Capacity);
    }

#endregion

#region IDisposable Elements

    private struct DisposableItem : IDisposable
    {
        public int Value;

        public DisposableItem(int value)
        {
            Value = value;
        }

        public void Dispose() => DisposeCount++;
    }

    [Fact]
    public void Dispose_DisposesEveryLiveElement()
    {
        DisposeCount = 0;

        var hive = new Hive<DisposableItem>();
        for (var i = 0; i < 40; i++)
        {
            hive.Add(new DisposableItem(i));
        }

        hive.Dispose();

        Assert.Equal(40, DisposeCount);
    }

    [Fact]
    public void Clear_DisposesEveryLiveElement()
    {
        DisposeCount = 0;

        using var hive = new Hive<DisposableItem>();
        for (var i = 0; i < 25; i++)
        {
            hive.Add(new DisposableItem(i));
        }

        // Removed elements are no longer disposed by Clear.
        var removed = hive.Add(new DisposableItem(999));
        hive.Remove(removed);

        hive.Clear();

        Assert.Equal(25, DisposeCount);
    }

#endregion

#region Private Static Methods

    private static List<int> Enumerate(Hive<int> hive)
    {
        List<int> values = [];
        foreach (var item in hive)
        {
            values.Add(item);
        }

        return values;
    }

    private static List<T> EnumerateValues<T>(Hive<T> hive) where T : unmanaged
    {
        List<T> values = [];
        foreach (var item in hive)
        {
            values.Add(item);
        }

        return values;
    }

    private static Hive<int>.Enumerator At(Hive<int> hive, int index)
    {
        var enumerator = hive.GetEnumerator();
        for (var i = 0; i <= index; i++)
        {
            if (!enumerator.MoveNext())
            {
                break;
            }
        }

        return enumerator;
    }

    private static Hive<int>.Enumerator End(Hive<int> hive)
    {
        var enumerator = hive.GetEnumerator();
        while (enumerator.MoveNext())
        {
        }

        return enumerator;
    }

    private static List<int> CollectRange(Hive<int>.Enumerator first, Hive<int>.Enumerator last)
    {
        List<int> values = [];
        var probe = first;
        while (probe.HasCurrent && !probe.SamePosition(in last))
        {
            values.Add(probe.Current);
            probe.MoveNext();
        }

        return values;
    }

    private static void AssertValues(Hive<int> hive, IEnumerable<int> expected)
    {
        var expectedList = expected.ToList();
        var actualList = Enumerate(hive);

        expectedList.Sort();
        actualList.Sort();

        Assert.Equal(expectedList, actualList);
        Assert.Equal(expectedList.Count, hive.Count);
        hive.ValidateInvariants();
    }

    private static void RunRandomStress<T>(int steps, int seed, Func<int, T> factory) where T : unmanaged
    {
        var random = new Random(seed);
        using var hive = new Hive<T>(new HiveBlockLimits(3, 16));

        var valueByIdDict = new Dictionary<int, T>();
        var handleByIdDict = new Dictionary<int, HiveHandle>();
        var nextId = 0;

        for (var step = 0; step < steps; step++)
        {
            if (valueByIdDict.Count == 0 || random.Next(100) < 55)
            {
                var id = nextId++;
                var value = factory(id);

                handleByIdDict[id] = hive.Add(value);
                valueByIdDict[id] = value;
            }
            else
            {
                var ids = valueByIdDict.Keys.ToArray();
                var id = ids[random.Next(ids.Length)];

                Assert.True(hive.Remove(handleByIdDict[id]));
                valueByIdDict.Remove(id);
                handleByIdDict.Remove(id);
            }

            if (step % 97 == 0)
            {
                Assert.Equal(valueByIdDict.Count, hive.Count);
                hive.ValidateInvariants();
            }
        }

        Assert.Equal(valueByIdDict.Count, hive.Count);
        hive.ValidateInvariants();

        var expectedCounts = new Dictionary<T, int>();
        foreach (var value in valueByIdDict.Values)
        {
            expectedCounts[value] = expectedCounts.GetValueOrDefault(value) + 1;
        }

        var actualCounts = new Dictionary<T, int>();
        foreach (var value in EnumerateValues(hive))
        {
            actualCounts[value] = actualCounts.GetValueOrDefault(value) + 1;
        }

        Assert.Equal(expectedCounts.Count, actualCounts.Count);
        foreach (var (value, expectedCount) in expectedCounts)
        {
            Assert.Equal(expectedCount, actualCounts.GetValueOrDefault(value));
        }
    }

#endregion
}
