using System.Runtime.InteropServices;

namespace lychee.extensions;

public static class ListExtensions
{
    extension<T>(List<T> self)
    {
        /// <summary>
        /// Resizes the list to the specified size, filling new elements with the specified value if expanding.
        /// </summary>
        /// <param name="size">The new size of the list.</param>
        /// <param name="value">The value to fill new elements with when expanding.</param>
        public void Resize(int size, T value)
        {
            var count = self.Count;
            CollectionsMarshal.SetCount(self, size);

            if (size > count)
            {
                CollectionsMarshal.AsSpan(self)[count..].Fill(value);
            }
        }

        /// <summary>
        /// Removes the last element from the list.
        /// </summary>
        public void RemoveLast()
        {
            self.RemoveAt(self.Count - 1);
        }
    }
}
