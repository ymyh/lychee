using System.Collections;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using lychee.utils;

namespace lychee.collections;

/// <summary>
/// Describes the minimum and maximum element capacity permitted for a single block of a <see cref="Hive{T}"/>.
/// </summary>
public readonly struct HiveBlockLimits : IEquatable<HiveBlockLimits>
{
#region Public Fields

    /// <summary>
    /// The smallest number of elements a block is allowed to hold.
    /// </summary>
    public readonly int Min;

    /// <summary>
    /// The largest number of elements a block is allowed to hold.
    /// </summary>
    public readonly int Max;

#endregion

#region Constructors

    /// <summary>
    /// Initializes a new set of block capacity limits.
    /// </summary>
    /// <param name="min">The smallest number of elements a block may hold.</param>
    /// <param name="max">The largest number of elements a block may hold.</param>
    public HiveBlockLimits(int min, int max)
    {
        Min = min;
        Max = max;
    }

#endregion

#region Public Methods

    /// <inheritdoc />
    public bool Equals(HiveBlockLimits other) => Min == other.Min && Max == other.Max;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is HiveBlockLimits other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Min, Max);

    /// <summary>
    /// Returns a readable representation of the limits.
    /// </summary>
    /// <returns>A string in the form "[Min, Max]".</returns>
    public override string ToString() => $"[{Min}, {Max}]";

    /// <summary>
    /// Determines whether two limit sets are equal.
    /// </summary>
    /// <param name="left">The first limit set.</param>
    /// <param name="right">The second limit set.</param>
    /// <returns>true when both limits are equal; otherwise, false.</returns>
    public static bool operator ==(HiveBlockLimits left, HiveBlockLimits right) => left.Equals(right);

    /// <summary>
    /// Determines whether two limit sets are different.
    /// </summary>
    /// <param name="left">The first limit set.</param>
    /// <param name="right">The second limit set.</param>
    /// <returns>true when the limits differ; otherwise, false.</returns>
    public static bool operator !=(HiveBlockLimits left, HiveBlockLimits right) => !left.Equals(right);

#endregion
}

/// <summary>
/// A stable, copyable reference to a single element of a <see cref="Hive{T}"/>.
/// The handle stays valid across insertions, removals, clear and reserve operations.
/// Use <see cref="Hive{T}.IsAlive"/> to verify that the referenced element still exists.
/// </summary>
/// <remarks>
/// A handle is invalidated for good once its block is emptied, even if the hive later recycles that block,
/// because a recycled block is always given a fresh identifier. A handle whose block is still active can,
/// however, alias a newly inserted element if the slot it refers to is reused: the skipfield tracked by this
/// container records slot occupancy, not element identity. Callers that need to distinguish elements across
/// removals must therefore store their own identity inside the element type.
/// </remarks>
public readonly struct HiveHandle : IEquatable<HiveHandle>
{
#region Public Fields

    /// <summary>
    /// The identifier of the block that owns the element. Identifiers are never reused.
    /// </summary>
    public readonly int BlockId;

    /// <summary>
    /// The index of the element inside its block.
    /// </summary>
    public readonly int Index;

#endregion

#region Constructors

    /// <summary>
    /// Initializes a new handle pointing at the specified block and index.
    /// </summary>
    /// <param name="blockId">The identifier of the owning block.</param>
    /// <param name="index">The index of the element inside the block.</param>
    public HiveHandle(int blockId, int index)
    {
        BlockId = blockId;
        Index = index;
    }

#endregion

#region Public Methods

    /// <inheritdoc />
    public bool Equals(HiveHandle other) => BlockId == other.BlockId && Index == other.Index;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is HiveHandle other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(BlockId, Index);

    /// <summary>
    /// Returns a readable representation of the handle.
    /// </summary>
    /// <returns>A string in the form "block:index".</returns>
    public override string ToString() => $"{BlockId}:{Index}";

    /// <summary>
    /// Determines whether two handles refer to the same slot.
    /// </summary>
    /// <param name="left">The first handle.</param>
    /// <param name="right">The second handle.</param>
    /// <returns>true when both handles are equal; otherwise, false.</returns>
    public static bool operator ==(HiveHandle left, HiveHandle right) => left.Equals(right);

    /// <summary>
    /// Determines whether two handles refer to different slots.
    /// </summary>
    /// <param name="left">The first handle.</param>
    /// <param name="right">The second handle.</param>
    /// <returns>true when the handles differ; otherwise, false.</returns>
    public static bool operator !=(HiveHandle left, HiveHandle right) => !left.Equals(right);

#endregion
}

/// <summary>
/// Internal metadata and native buffer that backs a single block of a <see cref="Hive{T}"/>.
/// The element area and the skipfield share one allocation so that a block costs a single allocation.
/// </summary>
internal sealed unsafe class HiveBlock
{
#region Public Fields

    /// <summary>
    /// The next active block in the active block chain, or null when this is the back block.
    /// </summary>
    public HiveBlock? NextBlock;

    /// <summary>
    /// The previous active block in the active block chain, or null when this is the front block.
    /// </summary>
    public HiveBlock? PreviousBlock;

    /// <summary>
    /// The next block that currently contains at least one hole.
    /// </summary>
    public HiveBlock? ErasuresNext;

    /// <summary>
    /// The previous block that currently contains at least one hole.
    /// </summary>
    public HiveBlock? ErasuresPrevious;

    /// <summary>
    /// The next reserved (empty) block.
    /// </summary>
    public HiveBlock? NextUnused;

    /// <summary>
    /// Whether this block is currently linked into the erasures chain.
    /// </summary>
    public bool InErasuresChain;

    /// <summary>
    /// The identifier of this block. Identifiers are allocated monotonically and never reused.
    /// </summary>
    public int BlockId;

    /// <summary>
    /// The total number of element slots this block can hold.
    /// </summary>
    public int Capacity;

    /// <summary>
    /// The number of live elements currently held by this block.
    /// </summary>
    public int Size;

    /// <summary>
    /// The number of leading slots that have ever been used. Slots in [HighWater, Capacity) are untouched tail space.
    /// </summary>
    public int HighWater;

    /// <summary>
    /// The head of this block's hole segment list, or -1 when the block has no holes.
    /// </summary>
    public int FreeListHead;

    /// <summary>
    /// The distance in bytes between two consecutive element slots.
    /// </summary>
    public int Stride;

    /// <summary>
    /// The start of the element area.
    /// </summary>
    public byte* Elements;

    /// <summary>
    /// The start of the skipfield. It holds Capacity + 1 nodes; the trailing node is a permanent zero sentinel.
    /// </summary>
    public ushort* Skipfield;

#endregion

#region Public Static Methods

    /// <summary>
    /// Allocates a new block with a single native allocation holding both the element area and the skipfield.
    /// </summary>
    /// <param name="stride">The distance in bytes between two consecutive element slots.</param>
    /// <param name="capacity">The number of element slots the block can hold.</param>
    /// <param name="blockId">The identifier to assign to the block.</param>
    /// <returns>The freshly allocated block, with a fully zeroed skipfield.</returns>
    public static HiveBlock Allocate(int stride, int capacity, int blockId)
    {
        var elementBytes = (nuint)(stride * capacity);
        var skipfieldBytes = (nuint)((capacity + 1) * sizeof(ushort));
        var totalBytes = elementBytes + skipfieldBytes;
        var memory = (byte*)NativeMemory.AlignedAlloc(totalBytes, 64);
        NativeMemory.Clear(memory, totalBytes);

        return new HiveBlock
        {
            BlockId = blockId,
            Capacity = capacity,
            Stride = stride,
            Elements = memory,
            Skipfield = (ushort*)(memory + (nint)elementBytes),
            FreeListHead = -1,
        };
    }

#endregion

#region Public Methods

    /// <summary>
    /// Returns the address of the element slot at the specified index.
    /// </summary>
    /// <param name="index">The index of the slot.</param>
    /// <returns>A pointer to the slot.</returns>
    public byte* ElementAt(int index) => Elements + index * Stride;

    /// <summary>
    /// Reads the previous hole segment index stored in the slot at the specified index.
    /// </summary>
    /// <param name="index">The index of the hole segment start.</param>
    /// <returns>The stored index, or 0xFFFF when there is no previous segment.</returns>
    public ushort ReadFreePrevious(int index) => *(ushort*)ElementAt(index);

    /// <summary>
    /// Reads the next hole segment index stored in the slot at the specified index.
    /// </summary>
    /// <param name="index">The index of the hole segment start.</param>
    /// <returns>The stored index, or 0xFFFF when there is no next segment.</returns>
    public ushort ReadFreeNext(int index) => *(ushort*)(ElementAt(index) + sizeof(ushort));

    /// <summary>
    /// Writes the previous hole segment index into the slot at the specified index.
    /// </summary>
    /// <param name="index">The index of the hole segment start.</param>
    /// <param name="value">The index to store.</param>
    public void WriteFreePrevious(int index, ushort value) => *(ushort*)ElementAt(index) = value;

    /// <summary>
    /// Writes the next hole segment index into the slot at the specified index.
    /// </summary>
    /// <param name="index">The index of the hole segment start.</param>
    /// <param name="value">The index to store.</param>
    public void WriteFreeNext(int index, ushort value) => *(ushort*)(ElementAt(index) + sizeof(ushort)) = value;

    /// <summary>
    /// Resets the block into a pristine, empty state while keeping its buffer allocated.
    /// </summary>
    public void Reset()
    {
        Size = 0;
        HighWater = 0;
        FreeListHead = -1;
        InErasuresChain = false;
        ErasuresNext = null;
        ErasuresPrevious = null;
        NextUnused = null;
        NativeMemory.Clear(Skipfield, (nuint)((Capacity + 1) * sizeof(ushort)));
    }

    /// <summary>
    /// Releases the native buffer owned by this block.
    /// </summary>
    public void Free()
    {
        if (Elements == null)
        {
            return;
        }

        NativeMemory.AlignedFree(Elements);
        Elements = null;
        Skipfield = null;
        Capacity = 0;
        Size = 0;
        HighWater = 0;
    }

#endregion
}

/// <summary>
/// A single-type sequence container optimized for workloads that insert and remove elements randomly
/// while external code keeps long-lived references to those elements.
/// </summary>
/// <remarks>
/// <para>
/// Elements live inside fixed native blocks that are never relocated, so a pointer obtained from
/// <see cref="GetPointer"/> or a <see cref="HiveHandle"/> keeps referring to the same element no matter how many
/// insertions, removals, <see cref="Reserve"/> or <see cref="TrimCapacity()"/> calls happen in between.
/// </para>
/// <para>
/// Removals leave holes behind instead of moving other elements. Holes are tracked by a skipfield and are reused
/// by later insertions. Iteration order is the physical order of elements inside the blocks and is therefore
/// unrelated to insertion order. The container is a bidirectional (forward-only in this implementation) sequence
/// and does not support random access.
/// </para>
/// </remarks>
/// <typeparam name="T">The type of elements in the hive. Must be unmanaged.</typeparam>
public sealed unsafe class Hive<T> : IDisposable, IEnumerable<T> where T : unmanaged
{
#region Private Static Fields

    /// <summary>
    /// The sentinel used by the block level hole segment list to mark the absence of a link.
    /// </summary>
    internal const ushort None = ushort.MaxValue;

    private static readonly int Stride;

    private static readonly bool IsDisposableElement;

#endregion

#region Private Fields

    private readonly HiveBlockLimits limits;

    private readonly Dictionary<int, HiveBlock> blockMap = [];

    private HiveBlock? activeHead;

    private HiveBlock? backBlock;

    private HiveBlock? erasureHead;

    private HiveBlock? unusedHead;

    private int count;

    private int capacity;

    private int nextBlockId;

#endregion

#region Public Static Properties

    /// <summary>
    /// Gets the block capacity limits used when a hive does not specify its own.
    /// </summary>
    public static HiveBlockLimits BlockCapacityDefaultLimits { get; } = new(8, 8192);

    /// <summary>
    /// Gets the absolute block capacity limits any hive must respect.
    /// </summary>
    public static HiveBlockLimits BlockCapacityHardLimits { get; } = new(3, ushort.MaxValue);

#endregion

#region Public Properties

    /// <summary>
    /// Gets the number of live elements contained in the hive.
    /// </summary>
    public int Count => count;

    /// <summary>
    /// Gets the sum of the capacities of every block, including reserved blocks, unused tail space and holes.
    /// This is not the number of elements the hive can hold contiguously.
    /// </summary>
    public int Capacity => capacity;

    /// <summary>
    /// Gets the total number of blocks, both active and reserved.
    /// </summary>
    public int BlockCount => blockMap.Count;

    /// <summary>
    /// Gets a value indicating whether the hive contains no elements.
    /// </summary>
    public bool IsEmpty => count == 0;

    /// <summary>
    /// Gets the block capacity limits this hive was constructed with.
    /// </summary>
    public HiveBlockLimits BlockCapacityLimits => limits;

    /// <summary>
    /// Gets the current back block. Intended for tests and diagnostics only.
    /// </summary>
    internal HiveBlock? DebugBackBlock => backBlock;

#endregion

#region Constructors & Destructor

    static Hive()
    {
        var alignment = Math.Max(TypeUtils.GetOrGuessAlignment<T>(), 1);
        var minimumStride = Math.Max(sizeof(T), 2 * sizeof(ushort));

        Stride = (minimumStride + alignment - 1) / alignment * alignment;

        // Resolving the interface once keeps a reflection call out of every Clear and Dispose.
        IsDisposableElement = typeof(T).GetInterface(typeof(IDisposable).FullName!) != null;
    }

    /// <summary>
    /// Initializes a new instance using the default block capacity limits.
    /// </summary>
    public Hive() : this(BlockCapacityDefaultLimits)
    {
    }

    /// <summary>
    /// Initializes a new instance using the specified block capacity limits.
    /// </summary>
    /// <param name="limits">The minimum and maximum capacity of a single block.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the limits fall outside the hard limits or are inverted.</exception>
    public Hive(HiveBlockLimits limits)
    {
        ValidateLimits(limits);
        this.limits = limits;
    }

    /// <summary>
    /// Initializes a new instance and adds all elements of the specified sequence.
    /// </summary>
    /// <param name="source">The sequence whose elements are added to the hive.</param>
    public Hive(IEnumerable<T> source) : this()
    {
        foreach (var item in source)
        {
            Add(in item);
        }
    }

    ~Hive() => Dispose();

#endregion

#region Public Methods

    /// <summary>
    /// Adds an element to the hive, reusing a hole when one is available.
    /// </summary>
    /// <param name="value">The element to add.</param>
    /// <returns>A handle to the newly added element.</returns>
    public HiveHandle Add(in T value)
    {
        var (block, index) = AcquireSlot();

        *(T*)block.ElementAt(index) = value;

        return new HiveHandle(block.BlockId, index);
    }

    /// <summary>
    /// Adds all elements of the specified span to the hive.
    /// </summary>
    /// <param name="values">The elements to add.</param>
    public void AddRange(ReadOnlySpan<T> values)
    {
        for (var i = 0; i < values.Length; i++)
        {
            Add(in values[i]);
        }
    }

    /// <summary>
    /// Adds the specified number of default-valued elements to the hive.
    /// </summary>
    /// <param name="count">The number of elements to add.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="count"/> is negative.</exception>
    public void AddUninitializedValues(int count)
    {
        if (count < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }

        for (var i = 0; i < count; i++)
        {
            Add(default);
        }
    }

    /// <summary>
    /// Removes the element referenced by the specified handle.
    /// The element memory immediately becomes reusable by later insertions.
    /// </summary>
    /// <param name="handle">The handle of the element to remove.</param>
    /// <returns>true when the element existed and was removed; otherwise, false.</returns>
    public bool Remove(HiveHandle handle)
    {
        if (!blockMap.TryGetValue(handle.BlockId, out var block) ||
            (uint)handle.Index >= (uint)block.HighWater ||
            block.Skipfield[handle.Index] != 0)
        {
            return false;
        }

        RemoveAt(block, handle.Index);
        return true;
    }

    /// <summary>
    /// Removes every element in the half-open range [<paramref name="first"/>, <paramref name="last"/>).
    /// An enumerator that has not been advanced yet denotes the position of the first element.
    /// </summary>
    /// <param name="first">An enumerator positioned on the first element to remove.</param>
    /// <param name="last">An enumerator positioned one past the last element to remove.</param>
    public void RemoveRange(in Enumerator first, in Enumerator last)
    {
        // Measure the range before touching the hive so that the walk is not disturbed by the removals.
        var probe = first;
        if (!probe.HasCurrent && !probe.MoveNext())
        {
            return;
        }

        var remaining = 0;
        while (probe.HasCurrent && !probe.SamePosition(in last))
        {
            remaining++;
            probe.MoveNext();
        }

        if (remaining == 0)
        {
            return;
        }

        var enumerator = first;
        if (!enumerator.HasCurrent)
        {
            enumerator.MoveNext();
        }

        for (var i = 0; i < remaining; i++)
        {
            enumerator.RemoveCurrent();

            if (i + 1 < remaining)
            {
                enumerator.MoveNext();
            }
        }
    }

    /// <summary>
    /// Removes every element while keeping all blocks allocated. <see cref="Capacity"/> is unchanged.
    /// </summary>
    public void Clear()
    {
        if (count != 0 && IsDisposableElement)
        {
            ForEach((ref T item) => { (item as IDisposable)!.Dispose(); });
        }

        var block = activeHead;
        while (block != null)
        {
            var next = block.NextBlock;
            block.Reset();
            block.NextBlock = null;
            block.PreviousBlock = null;
            block.NextUnused = unusedHead;
            unusedHead = block;
            block = next;
        }

        activeHead = null;
        backBlock = null;
        erasureHead = null;
        count = 0;
    }

    /// <summary>
    /// Determines whether the handle refers to a live element of this hive.
    /// </summary>
    /// <param name="handle">The handle to verify.</param>
    /// <returns>true when the handle refers to a live element; otherwise, false.</returns>
    public bool IsAlive(HiveHandle handle)
    {
        return blockMap.TryGetValue(handle.BlockId, out var block) &&
               (uint)handle.Index < (uint)block.HighWater &&
               block.Skipfield[handle.Index] == 0;
    }

    /// <summary>
    /// Gets a reference to the element referenced by the specified handle.
    /// The reference must not be stored beyond the immediate call site; use <see cref="GetPointer"/> for long-lived access.
    /// </summary>
    /// <param name="handle">The handle of the element.</param>
    /// <returns>A reference to the element.</returns>
    /// <exception cref="KeyNotFoundException">Thrown when the handle does not belong to a live block of this hive.</exception>
    public ref T GetReference(HiveHandle handle)
    {
        var block = blockMap[handle.BlockId];

        return ref Unsafe.AsRef<T>(block.ElementAt(handle.Index));
    }

    /// <summary>
    /// Gets the stable raw pointer to the element referenced by the specified handle.
    /// The pointer stays valid until the element is removed or the hive is disposed.
    /// </summary>
    /// <param name="handle">The handle of the element.</param>
    /// <returns>A pointer to the element.</returns>
    /// <exception cref="KeyNotFoundException">Thrown when the handle does not belong to a live block of this hive.</exception>
    public T* GetPointer(HiveHandle handle)
    {
        var block = blockMap[handle.BlockId];

        return (T*)block.ElementAt(handle.Index);
    }

    /// <summary>
    /// Finds the handle of the element that lives at the specified address.
    /// </summary>
    /// <param name="pointer">A pointer previously obtained from <see cref="GetPointer"/>.</param>
    /// <returns>The handle of the element.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the pointer does not belong to any active block of this hive.</exception>
    public HiveHandle GetHandle(T* pointer)
    {
        for (var block = activeHead; block != null; block = block.NextBlock)
        {
            var start = block.Elements;
            var end = start + (nint)block.Stride * block.HighWater;

            if ((byte*)pointer >= start && (byte*)pointer < end)
            {
                return new HiveHandle(block.BlockId, (int)(((byte*)pointer - start) / block.Stride));
            }
        }

        throw new ArgumentOutOfRangeException(nameof(pointer), "The pointer does not belong to any active element of this hive.");
    }

    /// <summary>
    /// Grows <see cref="Capacity"/> to at least the specified value by allocating reserved blocks.
    /// No pointer or handle is invalidated.
    /// </summary>
    /// <param name="count">The minimum capacity to guarantee.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="count"/> is negative.</exception>
    public void Reserve(int count)
    {
        if (count < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }

        while (capacity < count)
        {
            var block = CreateBlock(count - capacity);
            block.NextUnused = unusedHead;
            unusedHead = block;
        }
    }

    /// <summary>
    /// Releases every reserved block. Active blocks and all pointers and handles are left untouched.
    /// </summary>
    public void TrimCapacity() => TrimCapacity(0);

    /// <summary>
    /// Releases reserved blocks until <see cref="Capacity"/> would drop below the specified value.
    /// Active blocks and all pointers and handles are left untouched.
    /// </summary>
    /// <param name="count">The capacity that must be retained.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="count"/> is negative.</exception>
    public void TrimCapacity(int count)
    {
        if (count < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }

        while (unusedHead != null && capacity - unusedHead.Capacity >= count)
        {
            var block = unusedHead;
            unusedHead = block.NextUnused;
            capacity -= block.Capacity;
            blockMap.Remove(block.BlockId);
            block.Free();
        }
    }

    /// <summary>
    /// Performs the specified action on each element, passing it by reference.
    /// </summary>
    /// <param name="action">The action to perform on each element.</param>
    public void ForEach(ForEachRefDelegate action)
    {
        var enumerator = GetEnumerator();
        while (enumerator.MoveNext())
        {
            action(ref enumerator.Current);
        }
    }

    /// <summary>
    /// Returns a jump-based enumerator over the live elements of the hive.
    /// </summary>
    /// <returns>An enumerator positioned before the first element.</returns>
    public Enumerator GetEnumerator() => new(this);

#endregion

#region IEnumerable Implementation

    IEnumerator<T> IEnumerable<T>.GetEnumerator() => new Enumerator(this);

    IEnumerator IEnumerable.GetEnumerator() => new Enumerator(this);

#endregion

#region IDisposable Implementation

    /// <summary>
    /// Releases every native block owned by the hive. When <typeparamref name="T"/> implements
    /// <see cref="IDisposable"/> the live elements are disposed first.
    /// </summary>
    public void Dispose()
    {
        if (count != 0 && IsDisposableElement)
        {
            ForEach((ref T item) => { (item as IDisposable)!.Dispose(); });
        }

        foreach (var block in blockMap.Values)
        {
            block.Free();
        }

        blockMap.Clear();
        activeHead = null;
        backBlock = null;
        erasureHead = null;
        unusedHead = null;
        count = 0;
        capacity = 0;

        GC.SuppressFinalize(this);
    }

#endregion

#region Private Static Methods

    private static void ValidateLimits(HiveBlockLimits limits)
    {
        if (limits.Min < BlockCapacityHardLimits.Min || limits.Max > BlockCapacityHardLimits.Max)
        {
            throw new ArgumentOutOfRangeException(nameof(limits), limits, $"Block capacity limits must lie within the hard limits {BlockCapacityHardLimits}.");
        }

        if (limits.Min > limits.Max)
        {
            throw new ArgumentOutOfRangeException(nameof(limits), limits, "The minimum block capacity must not exceed the maximum.");
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException($"Hive invariant violated: {message}");
        }
    }

#endregion

#region Private Methods

    private HiveBlock CreateBlock(int minimumCapacity)
    {
        var desired = Math.Max(minimumCapacity, Math.Max(count, limits.Min));
        var block = HiveBlock.Allocate(Stride, Math.Clamp(desired, limits.Min, limits.Max), nextBlockId);
        nextBlockId++;

        blockMap.Add(block.BlockId, block);
        capacity += block.Capacity;

        return block;
    }

    private (HiveBlock block, int index) AcquireSlot()
    {
        var holeBlock = erasureHead;
        if (holeBlock != null)
        {
            var holeIndex = TakeFromHole(holeBlock);
            holeBlock.Size++;
            count++;

            return (holeBlock, holeIndex);
        }

        var tailBlock = backBlock;
        if (tailBlock != null && tailBlock.HighWater < tailBlock.Capacity)
        {
            var tailIndex = tailBlock.HighWater++;
            tailBlock.Size++;
            count++;

            return (tailBlock, tailIndex);
        }

        var block = unusedHead != null ? TakeUnusedBlock() : CreateBlock(count + 1);
        AttachActiveBlock(block);

        var index = block.HighWater++;
        block.Size++;
        count++;

        return (block, index);
    }

    private int TakeFromHole(HiveBlock block)
    {
        var start = block.FreeListHead;
        var length = block.Skipfield[start];

        // Take the trailing slot of the segment so the free-list node stored in the leading slot is never disturbed.
        var index = start + length - 1;
        block.Skipfield[index] = 0;

        if (length == 1)
        {
            PopFreeListHead(block);

            if (block.FreeListHead == -1)
            {
                RemoveFromErasures(block);
            }
        }
        else
        {
            var remaining = length - 1;

            block.Skipfield[start] = (ushort)remaining;
            if (remaining > 1)
            {
                block.Skipfield[index - 1] = (ushort)remaining;
            }
        }

        return index;
    }

    private HiveBlock TakeUnusedBlock()
    {
        var block = unusedHead!;
        unusedHead = block.NextUnused;
        block.NextUnused = null;

        // Retire the identifier the block used during its previous activation so that stale handles
        // can never alias an element that is inserted into the recycled block.
        blockMap.Remove(block.BlockId);
        block.BlockId = nextBlockId++;
        blockMap.Add(block.BlockId, block);

        return block;
    }

    private void AttachActiveBlock(HiveBlock block)
    {
        if (backBlock == null)
        {
            activeHead = block;
        }
        else
        {
            backBlock.NextBlock = block;
            block.PreviousBlock = backBlock;
        }

        backBlock = block;
    }

    private void RemoveFromActive(HiveBlock block)
    {
        if (block.PreviousBlock != null)
        {
            block.PreviousBlock.NextBlock = block.NextBlock;
        }
        else
        {
            activeHead = block.NextBlock;
        }

        if (block.NextBlock != null)
        {
            block.NextBlock.PreviousBlock = block.PreviousBlock;
        }
        else
        {
            backBlock = block.PreviousBlock;
        }

        block.NextBlock = null;
        block.PreviousBlock = null;
    }

    private void AddToErasures(HiveBlock block)
    {
        block.ErasuresPrevious = null;
        block.ErasuresNext = erasureHead;

        if (erasureHead != null)
        {
            erasureHead.ErasuresPrevious = block;
        }

        erasureHead = block;
        block.InErasuresChain = true;
    }

    private void RemoveFromErasures(HiveBlock block)
    {
        if (block.ErasuresPrevious != null)
        {
            block.ErasuresPrevious.ErasuresNext = block.ErasuresNext;
        }
        else
        {
            erasureHead = block.ErasuresNext;
        }

        if (block.ErasuresNext != null)
        {
            block.ErasuresNext.ErasuresPrevious = block.ErasuresPrevious;
        }

        block.ErasuresNext = null;
        block.ErasuresPrevious = null;
        block.InErasuresChain = false;
    }

    private void ReleaseEmptyBlock(HiveBlock block)
    {
        RemoveFromActive(block);

        if (block.InErasuresChain)
        {
            RemoveFromErasures(block);
        }

        block.Reset();
        block.NextUnused = unusedHead;
        unusedHead = block;
    }

    private void PopFreeListHead(HiveBlock block)
    {
        var node = block.FreeListHead;
        var next = block.ReadFreeNext(node);

        block.FreeListHead = next == None ? -1 : next;

        if (next != None)
        {
            block.WriteFreePrevious(next, None);
        }
    }

    private void PushFreeNode(HiveBlock block, int node)
    {
        var head = block.FreeListHead;

        block.WriteFreePrevious(node, None);

        if (head == -1)
        {
            block.WriteFreeNext(node, None);
        }
        else
        {
            block.WriteFreeNext(node, (ushort)head);
            block.WriteFreePrevious(head, (ushort)node);
        }

        block.FreeListHead = node;
    }

    private void UnlinkFreeNode(HiveBlock block, int node)
    {
        var previous = block.ReadFreePrevious(node);
        var next = block.ReadFreeNext(node);

        if (previous == None)
        {
            block.FreeListHead = next == None ? -1 : next;
        }
        else
        {
            block.WriteFreeNext(previous, next);
        }

        if (next != None)
        {
            block.WriteFreePrevious(next, previous);
        }
    }

    private void RelinkFreeNode(HiveBlock block, int from, int to)
    {
        var previous = block.ReadFreePrevious(from);
        var next = block.ReadFreeNext(from);

        block.WriteFreePrevious(to, previous);
        block.WriteFreeNext(to, next);

        if (previous == None)
        {
            block.FreeListHead = to;
        }
        else
        {
            block.WriteFreeNext(previous, (ushort)to);
        }

        if (next != None)
        {
            block.WriteFreePrevious(next, (ushort)to);
        }
    }

    private int RemoveAt(HiveBlock block, int index)
    {
        block.Size--;
        count--;

        var skipfield = block.Skipfield;
        var previousIsHole = index > 0 && skipfield[index - 1] != 0;
        var nextIsHole = index + 1 < block.HighWater && skipfield[index + 1] != 0;
        int segmentEnd;

        if (previousIsHole && nextIsHole)
        {
            // Merge both neighbours into one segment, keeping the leading segment's free-list node.
            var previousLength = skipfield[index - 1];
            var previousStart = index - previousLength;
            var nextLength = skipfield[index + 1];
            var mergedLength = (ushort)(previousLength + 1 + nextLength);

            UnlinkFreeNode(block, index + 1);

            // The former end node of the leading segment and the former start node of the trailing
            // segment both become interior nodes and must read as one.
            skipfield[index] = 1;

            if (previousLength > 1)
            {
                skipfield[index - 1] = 1;
            }

            if (nextLength > 1)
            {
                skipfield[index + 1] = 1;
            }

            segmentEnd = index + nextLength;
            skipfield[previousStart] = mergedLength;
            skipfield[segmentEnd] = mergedLength;
        }
        else if (previousIsHole)
        {
            var previousLength = skipfield[index - 1];
            var previousStart = index - previousLength;
            var mergedLength = (ushort)(previousLength + 1);

            if (previousLength > 1)
            {
                skipfield[index - 1] = 1;
            }

            segmentEnd = index;
            skipfield[previousStart] = mergedLength;
            skipfield[segmentEnd] = mergedLength;
        }
        else if (nextIsHole)
        {
            var nextLength = skipfield[index + 1];
            var mergedLength = (ushort)(nextLength + 1);

            RelinkFreeNode(block, index + 1, index);

            if (nextLength > 1)
            {
                skipfield[index + 1] = 1;
            }

            segmentEnd = index + nextLength;
            skipfield[index] = mergedLength;
            skipfield[segmentEnd] = mergedLength;
        }
        else
        {
            skipfield[index] = 1;
            PushFreeNode(block, index);

            if (!block.InErasuresChain)
            {
                AddToErasures(block);
            }

            segmentEnd = index;
        }

        if (block.Size == 0)
        {
            ReleaseEmptyBlock(block);
        }

        return segmentEnd;
    }

    /// <summary>
    /// Verifies every structural invariant of the hive. Intended for tests and debugging.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when an invariant is broken.</exception>
    internal void ValidateInvariants()
    {
        var liveCount = 0;
        var totalCapacity = 0;
        var blockTotal = 0;
        var erasureTotal = 0;
        HiveBlock? previous = null;

        for (var block = activeHead; block != null; block = block.NextBlock)
        {
            Check(block.Size > 0, $"block {block.BlockId} is active but has no elements");
            Check(block.PreviousBlock == previous, $"block {block.BlockId} has a broken backward link");
            Check(block.HighWater > 0 && block.HighWater <= block.Capacity, $"block {block.BlockId} has an out-of-range high water mark");
            Check(block.Capacity >= limits.Min && block.Capacity <= limits.Max, $"block {block.BlockId} violates the capacity limits");

            if (block.NextBlock == null)
            {
                Check(backBlock == block, "the back block is not the tail of the active chain");
            }
            else
            {
                Check(block.HighWater == block.Capacity, $"non-tail block {block.BlockId} has unused tail space");
            }

            liveCount += block.Size;
            totalCapacity += block.Capacity;
            blockTotal++;

            var holes = 0;
            var index = 0;
            while (index < block.HighWater)
            {
                var length = block.Skipfield[index];
                if (length == 0)
                {
                    index++;
                    continue;
                }

                var end = index + length - 1;

                Check(end < block.HighWater, $"block {block.BlockId} has a hole segment past the high water mark");
                Check(block.Skipfield[end] == length, $"block {block.BlockId} has a hole segment with a mismatched end node");

                for (var i = index + 1; i < end; i++)
                {
                    Check(block.Skipfield[i] == 1, $"block {block.BlockId} has a hole segment with a bad interior node");
                }

                holes++;
                index = end + 1;
            }

            for (var i = block.HighWater; i <= block.Capacity; i++)
            {
                Check(block.Skipfield[i] == 0, $"block {block.BlockId} has a non-zero node in its unused tail space");
            }

            Check(block.Skipfield[block.Capacity] == 0, $"block {block.BlockId} has a corrupted sentinel node");
            Check(block.InErasuresChain == (holes > 0), $"block {block.BlockId} has an inconsistent erasures chain membership");
            Check((block.FreeListHead == -1) == (holes == 0), $"block {block.BlockId} has an inconsistent free list head");

            var segments = 0;
            var node = block.FreeListHead;

            if (node != -1)
            {
                Check(block.ReadFreePrevious(node) == None, $"block {block.BlockId} has a free list head with a previous node");
            }

            while (node != -1)
            {
                var length = block.Skipfield[node];

                Check(segments++ <= holes, $"block {block.BlockId} has a free list cycle");
                Check(length > 0 && node + length <= block.HighWater, $"block {block.BlockId} has a free list node that does not start a valid segment");

                var next = block.ReadFreeNext(node);
                node = next == None ? -1 : next;
            }

            Check(segments == holes, $"block {block.BlockId} does not expose every hole segment through its free list");

            previous = block;
        }

        Check(count == liveCount, $"the element count {count} does not match the block sizes {liveCount}");
        Check(backBlock == previous, "the back block does not match the tail of the active chain");

        for (var block = unusedHead; block != null; block = block.NextUnused)
        {
            Check(block.Size == 0 && block.HighWater == 0 && block.FreeListHead == -1, $"reserved block {block.BlockId} was not reset");
            Check(block.Skipfield[0] == 0, $"reserved block {block.BlockId} has a dirty skipfield");
            totalCapacity += block.Capacity;
            blockTotal++;
        }

        for (var block = erasureHead; block != null; block = block.ErasuresNext)
        {
            Check(block.InErasuresChain, $"block {block.BlockId} is in the erasures chain but does not know it");
            erasureTotal++;
        }

        Check(capacity == totalCapacity, $"the capacity {capacity} does not match the block capacities {totalCapacity}");
        Check(blockMap.Count == blockTotal, "the block map does not contain exactly the live blocks");
        Check(erasureTotal <= blockTotal, "the erasures chain is longer than the total number of blocks");
    }

#endregion

#region Nested Types

    /// <summary>
    /// Delegate for performing an action on each element by reference.
    /// </summary>
    /// <param name="item">A reference to the current element.</param>
    public delegate void ForEachRefDelegate(ref T item);

    /// <summary>
    /// A jump-based enumerator over the live elements of a <see cref="Hive{T}"/>.
    /// Supports removing the current element while iterating.
    /// </summary>
    public struct Enumerator : IEnumerator<T>
    {
        private readonly Hive<T> hive;

        private HiveBlock? block;

        private int index;

        private bool removedCurrent;

        internal Enumerator(Hive<T> hive)
        {
            this.hive = hive;
            block = hive.activeHead;
            index = -1;
            removedCurrent = false;
        }

        /// <summary>
        /// Gets a reference to the element the enumerator is currently positioned on.
        /// </summary>
        /// <exception cref="InvalidOperationException">Thrown when the enumerator is not positioned on an element.</exception>
        public ref T Current
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                if (!HasCurrent)
                {
                    ThrowNotPositioned();
                }

                return ref Unsafe.AsRef<T>(block!.ElementAt(index));
            }
        }

        /// <summary>
        /// Throws the exception used by <see cref="Current"/>. Kept out of line so that the property body stays
        /// small enough for the JIT to inline it into the caller's loop.
        /// </summary>
        private static void ThrowNotPositioned()
        {
            throw new InvalidOperationException("The enumerator is not positioned on an element.");
        }

        T IEnumerator<T>.Current => Current;

        object IEnumerator.Current => Current!;

        /// <summary>
        /// Gets a value indicating whether the enumerator is currently positioned on a live element.
        /// </summary>
        public readonly bool HasCurrent => block != null && index >= 0 && !removedCurrent;

        /// <summary>
        /// Advances the enumerator to the next live element, jumping over holes.
        /// </summary>
        /// <returns>true when another element exists; otherwise, false.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool MoveNext()
        {
            if (block == null)
            {
                return false;
            }

            // The flag only has to be cleared after RemoveCurrent, so the common read-only step skips the store.
            if (removedCurrent)
            {
                removedCurrent = false;
            }

            var currentBlock = block;

            index++;
            index += currentBlock.Skipfield[index];

            if (index < currentBlock.HighWater)
            {
                return true;
            }

            var next = currentBlock.NextBlock;
            block = next;

            if (next == null)
            {
                index = -1;

                return false;
            }

            index = next.Skipfield[0];

            return true;
        }

        /// <summary>
        /// Removes the element the enumerator is currently positioned on.
        /// The following call to <see cref="MoveNext"/> moves on to the element after the removed one.
        /// </summary>
        /// <exception cref="InvalidOperationException">Thrown when the enumerator is not positioned on an element.</exception>
        public void RemoveCurrent()
        {
            if (!HasCurrent)
            {
                throw new InvalidOperationException("The enumerator is not positioned on an element.");
            }

            var removedBlock = block!;
            var continuationBlock = removedBlock.NextBlock;
            var segmentEnd = hive.RemoveAt(removedBlock, index);

            if (removedBlock.Size == 0)
            {
                // The block was detached and reset, so the walk continues at the following block.
                block = continuationBlock;
                index = -1;
                removedCurrent = false;

                return;
            }

            // Park the enumerator on the trailing slot of the hole that now covers the removed element.
            // The next MoveNext steps over the whole hole in a single skip.
            index = segmentEnd;
            removedCurrent = true;
        }

        /// <summary>
        /// Restores the enumerator to its initial position, before the first element.
        /// </summary>
        public void Reset()
        {
            block = hive.activeHead;
            index = -1;
            removedCurrent = false;
        }

        /// <summary>
        /// Determines whether this enumerator is positioned on the same slot as another enumerator.
        /// </summary>
        /// <param name="other">The enumerator to compare against.</param>
        /// <returns>true when both enumerators point at the same slot; otherwise, false.</returns>
        internal readonly bool SamePosition(in Enumerator other) =>
            ReferenceEquals(block, other.block) && index == other.index && removedCurrent == other.removedCurrent;

        /// <summary>
        /// Releases the resources used by the enumerator. The enumerator does not own any resources.
        /// </summary>
        public void Dispose()
        {
        }
    }

#endregion
}
