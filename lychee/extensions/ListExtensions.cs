using System.Runtime.InteropServices;

namespace lychee.extensions;

public static class ListExtensions
{
    extension<T>(List<T> self)
    {
        public void Resize(int size, T value)
        {
            var count = self.Count;
            CollectionsMarshal.SetCount(self, size);

            if (size > count)
            {
                CollectionsMarshal.AsSpan(self)[count..].Fill(value);
            }
        }

        public void RemoveLast()
        {
            self.RemoveAt(self.Count - 1);
        }
    }
}
