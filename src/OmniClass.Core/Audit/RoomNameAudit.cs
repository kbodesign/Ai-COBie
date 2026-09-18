using System;
using System.Collections.Generic;
using System.Linq;
using OmniClass.Core.Text;

namespace OmniClass.Core.Audit
{
    /// <summary>
    /// One normalized room name as it actually occurs across real models, with every raw
    /// spelling that produced it.
    /// </summary>
    public sealed class RoomNameTally
    {
        // Ordinal, not ignore-case: the report exists to show exactly what was typed, and
        // "RestRoom" versus "Restroom" is part of what the curator wants to see.
        private readonly Dictionary<string, int> _variants = new Dictionary<string, int>(StringComparer.Ordinal);

        internal RoomNameTally(string key)
        {
            Key = key;
        }

        public string Key { get; }

        /// <summary>Total rooms that reduced to this key.</summary>
        public int Count { get; private set; }

        /// <summary>Raw spellings and how often each was used, most common first.</summary>
        public IEnumerable<KeyValuePair<string, int>> Variants =>
            _variants.OrderByDescending(v => v.Value).ThenBy(v => v.Key, StringComparer.Ordinal);

        public string MostCommonVariant => Variants.Select(v => v.Key).FirstOrDefault() ?? string.Empty;

        internal void Add(string raw)
        {
            Count++;
            _variants.TryGetValue(raw, out var existing);
            _variants[raw] = existing + 1;
        }

        public override string ToString() => $"{Key} x{Count}";
    }

    /// <summary>
    /// Builds the evidence base for the dictionary. Run this over finished models first:
    /// the frequency-ranked output is the alias list, derived from what people really typed
    /// rather than from what we imagine they type.
    /// </summary>
    public static class RoomNameAudit
    {
        public static IReadOnlyList<RoomNameTally> Tally(IEnumerable<string> roomNames, NormalizerOptions options = null)
        {
            var tallies = new Dictionary<string, RoomNameTally>(StringComparer.Ordinal);

            foreach (var raw in roomNames ?? Enumerable.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(raw)) continue;

                var key = RoomNameNormalizer.Key(raw, options);
                if (key.Length == 0) continue;

                if (!tallies.TryGetValue(key, out var tally))
                {
                    tally = new RoomNameTally(key);
                    tallies.Add(key, tally);
                }

                tally.Add(raw.Trim());
            }

            return tallies.Values
                .OrderByDescending(t => t.Count)
                .ThenBy(t => t.Key, StringComparer.Ordinal)
                .ToList();
        }
    }
}
