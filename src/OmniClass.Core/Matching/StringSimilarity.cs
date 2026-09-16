using System;
using System.Collections.Generic;
using System.Linq;

namespace OmniClass.Core.Matching
{
    public static class StringSimilarity
    {
        /// <summary>
        /// 1.0 for identical strings, scaled down by edit distance. Catches plurals and
        /// typos ("Restrooms", "Resroom") without any substring behaviour.
        /// </summary>
        public static double Characters(string left, string right)
        {
            if (string.IsNullOrEmpty(left) || string.IsNullOrEmpty(right)) return 0d;
            if (string.Equals(left, right, StringComparison.Ordinal)) return 1d;

            var distance = Levenshtein(left, right);
            var longest = Math.Max(left.Length, right.Length);
            return 1d - (double)distance / longest;
        }

        /// <summary>
        /// Dice coefficient over word sets, so word order and extra qualifiers degrade the
        /// score smoothly instead of failing outright.
        /// </summary>
        public static double Tokens(IReadOnlyList<string> left, IReadOnlyList<string> right)
        {
            if (left == null || right == null || left.Count == 0 || right.Count == 0) return 0d;

            var leftSet = new HashSet<string>(left, StringComparer.Ordinal);
            var rightSet = new HashSet<string>(right, StringComparer.Ordinal);
            var shared = leftSet.Count(rightSet.Contains);

            return 2d * shared / (leftSet.Count + rightSet.Count);
        }

        public static int Levenshtein(string left, string right)
        {
            if (left == null) throw new ArgumentNullException(nameof(left));
            if (right == null) throw new ArgumentNullException(nameof(right));

            var previous = new int[right.Length + 1];
            var current = new int[right.Length + 1];

            for (var j = 0; j <= right.Length; j++) previous[j] = j;

            for (var i = 1; i <= left.Length; i++)
            {
                current[0] = i;

                for (var j = 1; j <= right.Length; j++)
                {
                    var substitution = previous[j - 1] + (left[i - 1] == right[j - 1] ? 0 : 1);
                    current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), substitution);
                }

                Array.Copy(current, previous, current.Length);
            }

            return previous[right.Length];
        }
    }
}
