using System.Runtime.InteropServices;
using BenchmarkDotNet.Attributes;
using lychee.collections;

namespace lychee.Benchmarks;

/// <summary>
/// Measures the cost of the stable references the hive promises: reading through a long lived pointer, and
/// converting such a pointer back into a handle.
/// </summary>
[MemoryDiagnoser]
public class ReferenceBenchmarks
{
    private Hive<BenchItem> hive = null!;

    private List<BenchItem> list = null!;

    private nint[] pointerArray = [];

    /// <summary>
    /// The number of elements the containers hold. Kept smaller than the other benchmark classes because
    /// <see cref="Hive{T}.GetHandle"/> scans the active blocks for every lookup.
    /// </summary>
    [Params(1_000, 10_000)]
    public int Count;

    /// <summary>
    /// Builds both containers and harvests one stable pointer per element.
    /// </summary>
    [GlobalSetup]
    public unsafe void Setup()
    {
        var itemArray = BenchData.CreateItems(Count);

        hive = new Hive<BenchItem>();
        var handleArray = BenchData.FillHive(hive, itemArray);

        pointerArray = new nint[handleArray.Length];

        for (var i = 0; i < handleArray.Length; i++)
        {
            pointerArray[i] = (nint)hive.GetPointer(handleArray[i]);
        }

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
    /// Walks the list through a span, which is the closest equivalent the BCL offers to a stable pointer.
    /// Serves as the baseline of this class.
    /// </summary>
    /// <returns>The sum of the payloads of every visited element.</returns>
    [Benchmark(Baseline = true)]
    public long List_ReadBySpan()
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
    /// Reads every element through the pointer that was harvested during setup.
    /// </summary>
    /// <returns>The sum of the payloads of every visited element.</returns>
    [Benchmark]
    public unsafe long Hive_ReadByPointer()
    {
        var sum = 0L;

        for (var i = 0; i < pointerArray.Length; i++)
        {
            sum += ((BenchItem*)pointerArray[i])->Ticks;
        }

        return sum;
    }

    /// <summary>
    /// Converts every harvested pointer back into a handle, which costs one scan of the active block list per
    /// lookup. Use this to decide whether handles should be cached instead of recovered.
    /// </summary>
    /// <returns>The sum of the recovered element indices.</returns>
    [Benchmark]
    public unsafe int Hive_GetHandle()
    {
        var sum = 0;

        for (var i = 0; i < pointerArray.Length; i++)
        {
            sum += hive.GetHandle((BenchItem*)pointerArray[i]).Index;
        }

        return sum;
    }
}
