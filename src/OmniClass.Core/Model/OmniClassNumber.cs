using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace OmniClass.Core.Model
{
    /// <summary>
    /// An OmniClass Table 13 (Spaces by Function) number such as "13-23 17 11".
    /// Depth and parentage are derived from the number itself, so the spreadsheet's
    /// hand-maintained level column is only ever cross-checked, never trusted.
    /// </summary>
    public sealed class OmniClassNumber : IEquatable<OmniClassNumber>
    {
        public const string Table = "13";

        private static readonly Regex DigitRun = new Regex(@"\d+", RegexOptions.Compiled);

        private OmniClassNumber(IReadOnlyList<string> groups)
        {
            Groups = groups;
        }

        public IReadOnlyList<string> Groups { get; }

        /// <summary>"13-23 17 11" regardless of how the source spelled it.</summary>
        public string Canonical =>
            Groups.Count == 1 ? Groups[0] : Groups[0] + "-" + string.Join(" ", Groups.Skip(1));

        /// <summary>Table digits count as level 1, so "13-23 17" is level 3.</summary>
        public int Depth => Groups.Count;

        public OmniClassNumber Parent =>
            Depth <= 1 ? null : new OmniClassNumber(Groups.Take(Depth - 1).ToList());

        public static bool TryParse(string raw, out OmniClassNumber number, out string error)
        {
            number = null;
            error = null;

            if (string.IsNullOrWhiteSpace(raw))
            {
                error = "Number is blank.";
                return false;
            }

            raw = raw.Trim();

            // Excel eats "13-23 17"-shaped text if the column is not formatted as Text.
            if (raw.IndexOf('/') >= 0 || raw.IndexOf(':') >= 0)
            {
                error = $"'{raw}' looks like Excel converted the number to a date. " +
                        "Format the number column as Text and re-enter it.";
                return false;
            }

            if (raw.Any(char.IsLetter))
            {
                error = $"'{raw}' contains letters; Table 13 numbers are digit groups only.";
                return false;
            }

            var groups = DigitRun.Matches(raw).Cast<Match>().Select(m => m.Value).ToList();

            if (groups.Count == 0)
            {
                error = $"'{raw}' contains no digits.";
                return false;
            }

            if (groups[0] != Table)
            {
                error = $"'{raw}' is not a Table 13 number (expected it to start with '{Table}-').";
                return false;
            }

            var malformed = groups.FirstOrDefault(g => g.Length != 2);
            if (malformed != null)
            {
                error = $"'{raw}' has a '{malformed}' group; every group must be exactly two digits.";
                return false;
            }

            if (groups.Count > 6)
            {
                error = $"'{raw}' has {groups.Count} groups, which is deeper than Table 13 goes.";
                return false;
            }

            number = new OmniClassNumber(groups);
            return true;
        }

        public static OmniClassNumber Parse(string raw)
        {
            if (TryParse(raw, out var number, out var error)) return number;
            throw new FormatException(error);
        }

        public bool IsDescendantOf(OmniClassNumber other)
        {
            if (other == null || other.Depth >= Depth) return false;
            return !other.Groups.Where((g, i) => Groups[i] != g).Any();
        }

        public bool Equals(OmniClassNumber other) =>
            other != null && string.Equals(Canonical, other.Canonical, StringComparison.Ordinal);

        public override bool Equals(object obj) => Equals(obj as OmniClassNumber);

        public override int GetHashCode() => Canonical.GetHashCode();

        public override string ToString() => Canonical;
    }
}
