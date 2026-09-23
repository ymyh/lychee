namespace lychee.Benchmarks;

/// <summary>
/// The unmanaged element type used by the benchmarks.
/// It is exactly 16 bytes wide and 8 byte aligned, so the hive stride equals <c>sizeof(BenchItem)</c>.
/// </summary>
public struct BenchItem
{
    /// <summary>
    /// The identity of the item.
    /// </summary>
    public int Id;

    /// <summary>
    /// A 32 bit floating point payload.
    /// </summary>
    public float Weight;

    /// <summary>
    /// A 64 bit payload used as the summation target of the iteration benchmarks.
    /// </summary>
    public long Ticks;

    /// <summary>
    /// Initializes a new item.
    /// </summary>
    /// <param name="id">The identity of the item.</param>
    /// <param name="weight">The floating point payload.</param>
    /// <param name="ticks">The 64 bit payload.</param>
    public BenchItem(int id, float weight, long ticks)
    {
        Id = id;
        Weight = weight;
        Ticks = ticks;
    }
}
