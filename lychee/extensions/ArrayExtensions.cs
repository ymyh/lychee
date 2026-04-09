namespace lychee.extensions;

public static class ArrayExtensions
{
    extension<T>(T[] self)
    {
        /// <summary>
        /// Concatenates the array with a collection and returns a new array.
        /// </summary>
        /// <param name="other">The collection to concatenate.</param>
        /// <returns>A new array containing elements from this array followed by elements from <paramref name="other"/>.</returns>
        public T[] ConcatCollection(ICollection<T> other)
        {
            if (other.Count == 0)
            {
                return self;
            }

            var result = new T[self.Length + other.Count];
            self.CopyTo(result, 0);
            other.CopyTo(result, self.Length);

            return result;
        }

        /// <summary>
        /// Concatenates the array with a span and returns a new array.
        /// </summary>
        /// <param name="other">The span to concatenate.</param>
        /// <returns>A new array containing elements from this array followed by elements from <paramref name="other"/>.</returns>
        public T[] ConcatSpan(ReadOnlySpan<T> other)
        {
            if (other.Length == 0)
            {
                return self;
            }

            var result = new T[self.Length + other.Length];
            self.CopyTo(result, 0);
            other.CopyTo(result.AsSpan(self.Length));

            return result;
        }

        /// <summary>
        /// Performs the specified action on each element of the array.
        /// </summary>
        /// <param name="action">The action to perform on each element.</param>
        public void ForEach(Action<T> action)
        {
            foreach (var t in self)
            {
                action(t);
            }
        }

        /// <summary>
        /// Performs the specified action on each element of the array, providing the element and its index.
        /// </summary>
        /// <param name="action">The action to perform on each element.</param>
        public void ForEach(Action<T, int> action)
        {
            for (var i = 0; i < self.Length; i++)
            {
                action(self[i], i);
            }
        }

        /// <summary>
        /// Performs the specified action on each element of the array, providing the element, its index, and the array itself.
        /// </summary>
        /// <param name="action">The action to perform on each element.</param>
        public void ForEach(Action<T, int, T[]> action)
        {
            for (var i = 0; i < self.Length; i++)
            {
                action(self[i], i, self);
            }
        }
    }

    /// <summary>
    /// Delegate for performing an action on each element passed by reference.
    /// </summary>
    /// <typeparam name="T">The type of elements in the array.</typeparam>
    /// <param name="item">A reference to the current element.</param>
    public delegate void ForEachRefDelegate<T>(ref T item) where T : struct;

    /// <summary>
    /// Delegate for performing an action on each element passed by reference with its index.
    /// </summary>
    /// <typeparam name="T">The type of elements in the array.</typeparam>
    /// <param name="item">A reference to the current element.</param>
    /// <param name="index">The index of the current element.</param>
    public delegate void ForEachRefDelegate2<T>(ref T item, int index) where T : struct;

    /// <summary>
    /// Delegate for performing an action on each element passed by reference with its index and the array.
    /// </summary>
    /// <typeparam name="T">The type of elements in the array.</typeparam>
    /// <param name="item">A reference to the current element.</param>
    /// <param name="index">The index of the current element.</param>
    /// <param name="array">The array being iterated.</param>
    public delegate void ForEachRefDelegate3<T>(ref T item, int index, T[] array) where T : struct;

    extension<T>(T[] self) where T : struct
    {
        /// <summary>
        /// Performs the specified action on each element of the array, passing the element by reference.
        /// </summary>
        /// <param name="action">The action to perform on each element.</param>
        public void ForEach(ForEachRefDelegate<T> action)
        {
            for (var i = 0; i < self.Length; i++)
            {
                action(ref self[i]);
            }
        }

        /// <summary>
        /// Performs the specified action on each element of the array, passing the element by reference along with its index.
        /// </summary>
        /// <param name="action">The action to perform on each element.</param>
        public void ForEach(ForEachRefDelegate2<T> action)
        {
            for (var i = 0; i < self.Length; i++)
            {
                action(ref self[i], i);
            }
        }

        /// <summary>
        /// Performs the specified action on each element of the array, passing the element by reference along with its index and the array.
        /// </summary>
        /// <param name="action">The action to perform on each element.</param>
        public void ForEach(ForEachRefDelegate3<T> action)
        {
            for (var i = 0; i < self.Length; i++)
            {
                action(ref self[i], i, self);
            }
        }
    }
}
