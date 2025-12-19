using System.Runtime.CompilerServices;

namespace lychee.extensions;

public static class SpanExtensions
{
    extension<T>(Span<T> self) where T : unmanaged
    {
        public unsafe T* GetPtr()
        {
            return (T*)Unsafe.AsPointer(ref self[0]);
        }
    }

    extension<T>(ReadOnlySpan<T> self) where T : unmanaged
    {
        public unsafe T* GetPtr()
        {
            return (T*)Unsafe.AsPointer(in self[0]);
        }
    }
}
