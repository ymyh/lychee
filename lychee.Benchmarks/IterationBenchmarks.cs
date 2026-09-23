using System.Runtime.InteropServices;
using BenchmarkDotNet.Attributes;
using lychee.collections;

namespace lychee.Benchmarks;

/// <summary>
/// Measures the cost of walking every live element, and how that cost reacts to the hole density of the hive.
/// </summary>
/// <remarks>
/// Both containers are prepared to hold the <b>same number of live elements</b>; only the hive carries holes
/// underneath. That isolates the question the skipfield design has to answer: how much does a sparse block
/// layout cost during iteration?
/// </remarks>
[MemoryDiagnoser]
public class IterationBenchmarks
{
    private Hive<BenchItem> hive = null!;

    private List<BenchItem> list = null!;

    /// <summary>
    /// The number of elements inserted before the holes are punched.
    /// </summary>
    [Params(1_000, 100_000)]
    public int Count;

    /// <summary>
    /// The fraction of elements that is removed before the iteration runs.
    /// </summary>
    [Params(0.0, 0.25, 0.5, 0.9)]
    public double HoleRatio;

    /// <summary>
    /// Builds both containers. The list receives exactly the elements that survive in the hive.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        var itemArray = BenchData.CreateItems(Count);

        hive = new Hive<BenchItem>();
        var handleArray = BenchData.FillHive(hive, itemArray);

        var removedCount = (int)(Count * HoleRatio);

        if (removedCount > 0)
        {
            var shuffleIndexArray = BenchData.CreateShuffledIndices(Count, 12345);

            for (var i = 0; i < removedCount; i++)
            {
                hive.Remove(handleArray[shuffleIndexArray[i]]);
            }
        }

        list = new List<BenchItem>(Count - removedCount);

        foreach (var item in hive)
        {
            list.Add(item);
        }
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
    /// Walks the contiguous list. Serves as the baseline of this class.
    /// </summary>
    /// <returns>The sum of the payloads of every visited element.</returns>
    [Benchmark(Baseline = true)]
    public long List_Iterate()
    {
        var sum = 0L;
        var span = CollectionsMarshal.AsSpan(list);

        for (var i = 0; i < span.Length; i++)
        {
            sum += span[i].Ticks;
        }

        return sum;
    }

    /// <summary>
    /// Walks the hive, jumping over every hole.
    /// </summary>
    /// <returns>The sum of the payloads of every visited element.</returns>
    [Benchmark]
    public long Hive_Iterate()
    {
        var sum = 0L;
        var enumerator = hive.GetEnumerator();

        while (enumerator.MoveNext())
        {
            sum += enumerator.Current.Ticks;
        }

        return sum;
    }
}
