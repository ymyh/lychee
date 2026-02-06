namespace lychee.extensions;

public static class ArrayExtensions
{
    extension<T>(T[] self)
    {
        public T[] ConcatCollection(ICollection<T> other)
        {
            var result = new T[self.Length + other.Count];
            self.CopyTo(result, 0);
            other.CopyTo(result, self.Length);

            return result;
        }

        public T[] ConcatSpan(ReadOnlySpan<T> other)
        {
            var result = new T[self.Length + other.Length];
            self.CopyTo(result, 0);
            other.CopyTo(result.AsSpan(self.Length));

            return result;
        }
    }
}
