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

        public void ForEach(Action<T> action)
        {
            foreach (var t in self)
            {
                action(t);
            }
        }

        public void ForEach(Action<T, int> action)
        {
            for (var i = 0; i < self.Length; i++)
            {
                action(self[i], i);
            }
        }

        public void ForEach(Action<T, int, T[]> action)
        {
            for (var i = 0; i < self.Length; i++)
            {
                action(self[i], i, self);
            }
        }
    }

    public delegate void ForEachRefDelegate<T>(ref T item) where T : struct;

    public delegate void ForEachRefDelegate2<T>(ref T item, int index) where T : struct;

    public delegate void ForEachRefDelegate3<T>(ref T item, int index, T[] array) where T : struct;

    extension<T>(T[] self) where T : struct
    {
        public void ForEach(ForEachRefDelegate<T> action)
        {
            for (var i = 0; i < self.Length; i++)
            {
                action(ref self[i]);
            }
        }

        public void ForEach(ForEachRefDelegate2<T> action)
        {
            for (var i = 0; i < self.Length; i++)
            {
                action(ref self[i], i);
            }
        }

        public void ForEach(ForEachRefDelegate3<T> action)
        {
            for (var i = 0; i < self.Length; i++)
            {
                action(ref self[i], i, self);
            }
        }
    }
}
