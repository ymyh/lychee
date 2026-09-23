using BenchmarkDotNet.Attributes;
using lychee.collections;

namespace lychee.Benchmarks;

/// <summary>
/// Compares the O(1) hole based removal of <see cref="Hive{T}"/> with the shifting removal of <see cref="List{T}"/>.
/// </summary>
/// <remarks>
/// This is <b>not</b> a same-semantics comparison. <c>Hive.Remove</c> leaves a hole behind and never moves another
/// element; <c>List.RemoveAt</c> memmoves the tail on every removal. Both benchmarks remove through the same fixed,
/// deterministically shuffled sequence so that the two runs stay repeatable, and the report should be read as a
/// cost model comparison rather than as "who wins".
/// </remarks>
[MemoryDiagnoser]
[SimpleJob(invocationCount: 1)]
public class RemoveBenchmarks
{
    private BenchItem[] itemArray = [];

    private int[] shuffleIndexArray = [];

    private Hive<BenchItem> hive = null!;

    private List<BenchItem> list = null!;

    private HiveHandle[] handleArray = [];

    /// <summary>
    /// The number of elements the container holds before the removals start.
    /// </summary>
    [Params(1_000, 100_000)]
    public int Count;

    /// <summary>
    /// Builds the immutable element array, the shuffled removal order and the handle buffer.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        itemArray = BenchData.CreateItems(Count);
        shuffleIndexArray = BenchData.CreateShuffledIndices(Count, 20240918);
        handleArray = new HiveHandle[Count];
    }

    /// <summary>
    /// Rebuilds the mutable containers before every iteration. The cost of this method is not measured, so the
    /// benchmark itself always starts from a full container. The handle buffer is reused to keep the setup free
    /// of large allocations.
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
    /// Removes half of the list through arbitrary positions. Serves as the baseline of this class.
    /// </summary>
    /// <returns>The remaining element count.</returns>
    [Benchmark(Baseline = true)]
    public int List_RemoveAt_Random()
    {
        for (var i = 0; i < shuffleIndexArray.Length; i += 2)
        {
            list.RemoveAt(shuffleIndexArray[i] % list.Count);
        }

        return list.Count;
    }

    /// <summary>
    /// Removes half of the hive through the handles of a fixed shuffled element order.
    /// </summary>
    /// <returns>The remaining element count.</returns>
    [Benchmark]
    public int Hive_Remove_Random()
    {
        for (var i = 0; i < shuffleIndexArray.Length; i += 2)
        {
            hive.Remove(handleArray[shuffleIndexArray[i]]);
        }

        return hive.Count;
    }

    /// <summary>
    /// Removes every element one by one, which forces every block to be emptied and recycled.
    /// </summary>
    /// <returns>The remaining element count, which is always zero.</returns>
    [Benchmark]
    public int Hive_Remove_All()
    {
        for (var i = 0; i < handleArray.Length; i++)
        {
            hive.Remove(handleArray[i]);
        }

        return hive.Count;
    }
}
