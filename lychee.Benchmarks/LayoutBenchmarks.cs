using BenchmarkDotNet.Attributes;

namespace lychee.Benchmarks;

/// <summary>
/// Isolates the cost of the jump-counting skipfield algorithm itself from the cost of the <c>Hive&lt;T&gt;</c>
/// enumerator API, by running three loop shapes over one and the same dense data set.
/// </summary>
/// <remarks>
/// <para>
/// <c>Dense_PointerWalk</c> is the floor: a plain contiguous pointer walk with no skipfield at all.
/// <c>Skipfield_PointerWalk</c> reproduces the reference implementation's iterator arithmetic
/// (<c>element_pointer += *(++skipfield_pointer) + 1; skipfield_pointer += *skipfield_pointer;</c>) from
/// plf::hive, which keeps the resolved element pointer as its iteration state.
/// <c>Skipfield_IndexWalk</c> reproduces what the C# enumerator does instead: keep an index, read the skipfield
/// node, and re-derive the element address from the index.
/// </para>
/// <para>
/// The point of the comparison is to decide whether the enumerator state representation is worth changing, or
/// whether the remaining cost is inherent to the algorithm and therefore shared with the reference
/// implementation. If the two skipfield walks land on top of each other, the state representation is not the
/// bottleneck.
/// </para>
/// </remarks>
[MemoryDiagnoser]
public class LayoutBenchmarks
{
    private BenchItem[] itemArray = [];

    private ushort[] skipfieldArray = [];

    /// <summary>
    /// The number of elements walked by every benchmark of this class.
    /// </summary>
    [Params(100_000)]
    public int Count;

    /// <summary>
    /// Builds a dense element array and an all-zero skipfield, which is the layout of a hive with no holes.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        itemArray = BenchData.CreateItems(Count);
        skipfieldArray = new ushort[Count + 1];
    }

    /// <summary>
    /// Walks the elements contiguously without consulting any skipfield. This is the lower bound of the data
    /// layout.
    /// </summary>
    /// <returns>The sum of the payloads of every visited element.</returns>
    [Benchmark(Baseline = true)]
    public unsafe long Dense_PointerWalk()
    {
        var sum = 0L;

        fixed (BenchItem* elementBase = itemArray)
        {
            var elementPointer = elementBase;
            var endPointer = elementBase + itemArray.Length;

            while (elementPointer != endPointer)
            {
                sum += elementPointer->Ticks;
                elementPointer++;
            }
        }

        return sum;
    }

    /// <summary>
    /// Walks the elements with the reference implementation's iterator arithmetic, keeping the resolved element
    /// pointer and the skipfield pointer in lockstep.
    /// </summary>
    /// <returns>The sum of the payloads of every visited element.</returns>
    [Benchmark]
    public unsafe long Skipfield_PointerWalk()
    {
        var sum = 0L;

        fixed (BenchItem* elementBase = itemArray)
        fixed (ushort* skipfieldBase = skipfieldArray)
        {
            var elementPointer = elementBase;
            var skipfieldPointer = skipfieldBase;
            var endPointer = elementBase + itemArray.Length;

            // The reference implementation terminates on "!= end". `<` is used here instead so that a non-zero
            // skipfield node, which would make the element pointer step over the end, cannot spin forever.
            while (elementPointer < endPointer)
            {
                sum += elementPointer->Ticks;
                elementPointer += *(++skipfieldPointer) + 1;
                skipfieldPointer += *skipfieldPointer;
            }
        }

        return sum;
    }

    /// <summary>
    /// Walks the elements keeping an index as the iteration state and re-deriving the element address from it,
    /// which is what the <c>Hive&lt;T&gt;</c> enumerator currently does.
    /// </summary>
    /// <returns>The sum of the payloads of every visited element.</returns>
    [Benchmark]
    public unsafe long Skipfield_IndexWalk()
    {
        var sum = 0L;

        fixed (BenchItem* elementBase = itemArray)
        fixed (ushort* skipfieldBase = skipfieldArray)
        {
            var index = 0;
            var end = itemArray.Length;

            while (index < end)
            {
                // Pointer arithmetic already scales by sizeof(BenchItem), so the index is used directly.
                sum += (elementBase + index)->Ticks;

                index++;
                index += skipfieldBase[index];
            }
        }

        return sum;
    }
}
