using lychee.collections;

namespace lychee.Benchmarks;

/// <summary>
/// Builds the deterministic data sets the benchmarks share.
/// Every generator is allocation free with regard to the benchmark body: all arrays are produced during setup.
/// </summary>
internal static class BenchData
{
    /// <summary>
    /// Creates <paramref name="count"/> distinct items.
    /// </summary>
    /// <param name="count">The number of items to create.</param>
    /// <returns>An array holding the created items.</returns>
    public static BenchItem[] CreateItems(int count)
    {
        var itemArray = new BenchItem[count];

        for (var i = 0; i < count; i++)
        {
            itemArray[i] = new BenchItem(i, i * 0.5f, i * 31L);
        }

        return itemArray;
    }

    /// <summary>
    /// Creates a deterministically shuffled array of the indices [0, count).
    /// </summary>
    /// <param name="count">The number of indices to produce.</param>
    /// <param name="seed">The seed of the shuffle, so that every run removes the same elements.</param>
    /// <returns>An array holding every index in a fixed pseudo random order.</returns>
    public static int[] CreateShuffledIndices(int count, int seed)
    {
        var indexArray = new int[count];

        for (var i = 0; i < count; i++)
        {
            indexArray[i] = i;
        }

        var random = new Random(seed);

        for (var i = count - 1; i > 0; i--)
        {
            var j = random.Next(i + 1);

            (indexArray[i], indexArray[j]) = (indexArray[j], indexArray[i]);
        }

        return indexArray;
    }

    /// <summary>
    /// Appends every item of the specified array to the hive.
    /// </summary>
    /// <param name="hive">The hive that receives the items.</param>
    /// <param name="itemArray">The items to append.</param>
    /// <returns>An array holding the handle of each inserted item, in the same order as <paramref name="itemArray"/>.</returns>
    public static HiveHandle[] FillHive(Hive<BenchItem> hive, BenchItem[] itemArray)
    {
        var handleArray = new HiveHandle[itemArray.Length];

        FillHive(hive, itemArray, handleArray);

        return handleArray;
    }

    /// <summary>
    /// Appends every item of the specified array to the hive and writes the handles into a caller owned buffer,
    /// so that a benchmark which runs many invocations does not allocate a large handle array every time.
    /// </summary>
    /// <param name="hive">The hive that receives the items.</param>
    /// <param name="itemArray">The items to append.</param>
    /// <param name="handleArray">The destination buffer, which must be at least as long as <paramref name="itemArray"/>.</param>
    public static void FillHive(Hive<BenchItem> hive, BenchItem[] itemArray, HiveHandle[] handleArray)
    {
        for (var i = 0; i < itemArray.Length; i++)
        {
            handleArray[i] = hive.Add(in itemArray[i]);
        }
    }
}
