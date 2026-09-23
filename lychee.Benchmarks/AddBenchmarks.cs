using BenchmarkDotNet.Attributes;
using lychee.collections;

namespace lychee.Benchmarks;

/// <summary>
/// Compares the block based growth of <see cref="Hive{T}"/> with the array based growth of <see cref="List{T}"/>.
/// </summary>
[MemoryDiagnoser]
public class AddBenchmarks
{
    private BenchItem[] itemArray = [];

    private HiveHandle[] handleArray = [];

    /// <summary>
    /// The number of elements that are inserted by every benchmark of this class.
    /// </summary>
    [Params(1_000, 100_000)]
    public int Count;

    /// <summary>
    /// Builds the immutable element array the benchmarks insert, plus the handle buffer they write into.
    /// Both are reused by every invocation so that the measurements are not polluted by benchmark side allocations.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        itemArray = BenchData.CreateItems(Count);
        handleArray = new HiveHandle[Count];
    }

    /// <summary>
    /// Appends <see cref="Count"/> elements to a pre-sized list. Serves as the baseline of this class.
    /// </summary>
    /// <returns>The resulting element count.</returns>
    [Benchmark(Baseline = true)]
    public int List_Add_Sequential()
    {
        var list = new List<BenchItem>(Count);

        foreach (var item in itemArray)
        {
            list.Add(item);
        }

        return list.Count;
    }

    /// <summary>
    /// Appends <see cref="Count"/> elements to a hive that starts empty, so the block allocation strategy
    /// is exercised from scratch.
    /// </summary>
    /// <returns>The resulting element count.</returns>
    [Benchmark]
    public int Hive_Add_Sequential()
    {
        using var hive = new Hive<BenchItem>();

        foreach (var item in itemArray)
        {
            hive.Add(in item);
        }

        return hive.Count;
    }

    /// <summary>
    /// Appends <see cref="Count"/> elements to a hive that already held and lost the same number of elements,
    /// so the free list fast path is exercised instead of the tail path.
    /// </summary>
    /// <returns>The resulting element count.</returns>
    [Benchmark]
    public int Hive_Add_AfterMassRemoval()
    {
        using var hive = new Hive<BenchItem>();
        BenchData.FillHive(hive, itemArray, handleArray);

        var half = handleArray.Length / 2;

        for (var i = 0; i < half; i++)
        {
            hive.Remove(handleArray[i]);
        }

        for (var i = 0; i < half; i++)
        {
            hive.Add(in itemArray[i]);
        }

        return hive.Count;
    }
}
