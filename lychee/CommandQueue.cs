using System.Runtime.InteropServices;

namespace lychee;

/// <summary>
/// A private, single-writer command buffer owned by one <see cref="Commands"/>.
/// Commands are stored as an apply function pointer followed by the command body bytes, tightly packed
/// into a contiguous buffer so a batch can be applied without allocating per command.
/// </summary>
internal sealed class CommandQueue
{
#region Private Fields

    /// <summary>The fixed size of every command header: the apply pointer, the body size and a reserved word.</summary>
    private const int HeaderSize = 16;

    /// <summary>The alignment of every command body inside the buffer.</summary>
    private const int BodyAlignment = 8;

#endregion

#region Properties

    /// <summary>Whether the queue holds no command.</summary>
    public bool IsEmpty => Length == 0;

    /// <summary>The backing buffer, exposed so the applier can walk the commands with a single pin.</summary>
    internal byte[] Buffer { get; private set; } = [];

    /// <summary>The number of bytes written so far.</summary>
    internal int Length { get; private set; }

#endregion

#region Methods

    /// <summary>Records one fixed-size command: writes the applied pointer, the body size and the body value.</summary>
    /// <typeparam name="TCommand">The unmanaged command body type.</typeparam>
    /// <param name="apply">The function that applies the command to the world.</param>
    /// <param name="command">The command body.</param>
    public unsafe void Enqueue<TCommand>(delegate*<CommandApplier, byte*, void> apply, in TCommand command)
        where TCommand : unmanaged
    {
        fixed (TCommand* commandPtr = &command)
        {
            EnqueueCore(apply, commandPtr, sizeof(TCommand));
        }
    }

    /// <summary>Records one command whose body is written raw, for variable-length payloads.</summary>
    /// <param name="apply">The function that applies the command to the world.</param>
    /// <param name="data">A pointer to the raw body bytes.</param>
    /// <param name="size">The size of the body in bytes.</param>
    public unsafe void EnqueueRaw(delegate*<CommandApplier, byte*, void> apply, void* data, int size)
    {
        EnqueueCore(apply, data, size);
    }

    /// <summary>Drops every recorded command without releasing the buffer, so the capacity survives a batch.</summary>
    public void Clear()
    {
        Length = 0;
    }

    /// <summary>Gets the next command offset after a command whose body is <paramref name="bodySize"/> bytes.</summary>
    /// <param name="bodySize">The size of the command body.</param>
    /// <returns>The byte length the command occupies, header and padding included.</returns>
    internal static int CommandStride(int bodySize)
    {
        return HeaderSize + AlignUp(bodySize, BodyAlignment);
    }

#endregion

#region Private Methods

    private unsafe void EnqueueCore(delegate*<CommandApplier, byte*, void> apply, void* data, int size)
    {
        var stride = CommandStride(size);
        EnsureCapacity(Length + stride);

        fixed (byte* bufferPtr = Buffer)
        {
            var commandPtr = bufferPtr + Length;

            *(nint*)commandPtr = (nint)apply;
            *(int*)(commandPtr + 8) = size;
            *(int*)(commandPtr + 12) = 0;

            if (size > 0)
            {
                NativeMemory.Copy(data, commandPtr + HeaderSize, (nuint)size);
            }
        }

        Length += stride;
    }

    private void EnsureCapacity(int required)
    {
        if (Buffer.Length >= required)
        {
            return;
        }

        // The buffer starts lazily at 4 KB and doubles afterwards. It is never shrunk: command volume has
        // peaks, and keeping the high-water mark avoids reallocating every frame.
        var capacity = Buffer.Length == 0 ? 4096 : Buffer.Length;

        while (capacity < required)
        {
            capacity *= 2;
        }

        var newBuffer = new byte[capacity];
        Array.Copy(Buffer, newBuffer, Length);
        Buffer = newBuffer;
    }

    private static int AlignUp(int value, int alignment)
    {
        return (value + alignment - 1) & ~(alignment - 1);
    }

#endregion
}
