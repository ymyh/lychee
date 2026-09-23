using BenchmarkDotNet.Attributes;
using lychee.collections;

namespace lychee.Benchmarks;

/// <summary>
/// Compares removing elements while walking the container, which is the idiomatic hive workload.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(invocationCount: 1)]
public class MutationDuringIterationBenchmarks
{
    private static readonly Predicate<BenchItem> RemovablePredicate = IsRemovable;

    private BenchItem[] itemArray = [];

    private HiveHandle[] handleArray = [];

    private Hive<BenchItem> hive = null!;

    private List<BenchItem> list = null!;

    /// <summary>
    /// The number of elements the container holds before the walk starts.
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
    /// Removes every third element through <see cref="List{T}.RemoveAll"/>, which is the efficient BCL path.
    /// Serves as the baseline of this class.
    /// </summary>
    /// <returns>The number of removed elements.</returns>
    [Benchmark(Baseline = true)]
    public int List_RemoveAll()
    {
        return list.RemoveAll(RemovablePredicate);
    }

    /// <summary>
    /// Removes every third element through <see cref="Hive{T}.Enumerator.RemoveCurrent"/> while walking the hive.
    /// </summary>
    /// <returns>The number of removed elements.</returns>
    [Benchmark]
    public int Hive_RemoveCurrent()
    {
        var removed = 0;
        var enumerator = hive.GetEnumerator();

        while (enumerator.MoveNext())
        {
            if (IsRemovable(enumerator.Current))
            {
                enumerator.RemoveCurrent();
                removed++;
            }
        }

        return removed;
    }

    private static bool IsRemovable(BenchItem item)
    {
        return item.Id % 3 == 0;
    }
}
