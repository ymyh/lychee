using BenchmarkDotNet.Attributes;
using lychee.collections;

namespace lychee.Benchmarks;

/// <summary>
/// Measures the amortized cost of the hive capacity operations that promise not to invalidate pointers.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(invocationCount: 1)]
public class CapacityBenchmarks
{
    private BenchItem[] itemArray = [];

    private HiveHandle[] handleArray = [];

    private Hive<BenchItem> hive = null!;

    private List<BenchItem> list = null!;

    /// <summary>
    /// The number of elements the container holds before the operation runs.
    /// </summary>
    [Params(1_000, 100_000)]
    public int Count;

    /// <summary>
    /// Builds the immutable element array and the handle buffer.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        itemArray = BenchData.CreateItems(Count);
        handleArray = new HiveHandle[Count];
    }

    /// <summary>
    /// Rebuilds the mutable containers before every iteration. The cost of this method is not measured.
    /// </summary>
    [IterationSetup]
    public void SetupIteration()
    {
        hive = new Hive<BenchItem>();
        BenchData.FillHive(hive, itemArray, handleArray);
        list = new List<BenchItem>(itemArray);
    }

    /// <summary>
    /// Releases the native memory of the hive used by the benchmarks.
    /// </summary>
    [GlobalCleanup]
    public void Cleanup()
    {
        hive.Dispose();
    }

    /// <summary>
    /// Drops every element of the list without releasing its buffer. Serves as the baseline of this class.
    /// </summary>
    /// <returns>The remaining element count, which is always zero.</returns>
    [Benchmark(Baseline = true)]
    public int List_Clear()
    {
        list.Clear();

        return list.Count;
    }

    /// <summary>
    /// Drops every element of the hive without releasing any block. The cost is dominated by resetting the
    /// skipfield of every block.
    /// </summary>
    /// <returns>The remaining element count, which is always zero.</returns>
    [Benchmark]
    public int Hive_Clear()
    {
        hive.Clear();

        return hive.Count;
    }

    /// <summary>
    /// Drops every element and immediately refills the hive, which must reuse the reserved blocks instead of
    /// allocating new ones.
    /// </summary>
    /// <returns>The resulting element count.</returns>
    [Benchmark]
    public int Hive_ClearThenRefill()
    {
        hive.Clear();

        foreach (var item in itemArray)
        {
            hive.Add(in item);
        }

        return hive.Count;
    }

    /// <summary>
    /// Grows the capacity of an empty hive to <see cref="Count"/> without adding any element.
    /// </summary>
    /// <returns>The resulting capacity.</returns>
    [Benchmark]
    public int Hive_Reserve()
    {
        using var reservedHive = new Hive<BenchItem>();
        reservedHive.Reserve(Count);

        return reservedHive.Capacity;
    }
}
