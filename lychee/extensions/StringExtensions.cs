namespace lychee.extensions;

public static class StringExtensions
{
    extension(string self)
    {
        /// <summary>
        /// Gets every Unicode value of every char in string.
        /// </summary>
        /// <returns></returns>
        public IEnumerable<int> GetUnicodes()
        {
            for (var i = 0; i < self.Length; i++)
            {
                if (char.IsSurrogate(self[i]))
                {
                    yield return char.ConvertToUtf32(self, i);
                    i++;
                }
                else
                {
                    yield return (int)self[i];
                }
            }
        }
    }
}
