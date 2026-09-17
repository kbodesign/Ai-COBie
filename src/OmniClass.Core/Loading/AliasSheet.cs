using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OmniClass.Core.Audit;
using OmniClass.Core.Io;
using OmniClass.Core.Matching;
using OmniClass.Core.Model;
using OmniClass.Core.Text;

namespace OmniClass.Core.Loading
{
    /// <summary>
    /// One classification row in the wide authoring sheet: Number, Name, then unique
    /// room-name options. Uniqueness is by normalized key so "RestRoom" and "Rest_Room"
    /// are the same option and are not appended twice.
    /// </summary>
    public sealed class AliasSheetRow
    {
        public AliasSheetRow(string number, string name)
        {
            Number = number ?? string.Empty;
            Name = name ?? string.Empty;
        }

        public string Number { get; }
        public string Name { get; }
        public List<string> Aliases { get; } = new List<string>();

        public bool TryAddUnique(string raw, HashSet<string> usedKeys, NormalizerOptions options = null)
        {
            if (string.IsNullOrWhiteSpace(raw)) return false;

            var key = RoomNameNormalizer.Key(raw, options);
            if (key.Length == 0) return false;
            if (usedKeys != null && !usedKeys.Add(key)) return false;

            if (Aliases.Any(existing => string.Equals(RoomNameNormalizer.Key(existing, options), key, StringComparison.Ordinal)))
                return false;

            Aliases.Add(raw.Trim());
            return true;
        }
    }

    /// <summary>
    /// The harvest/dictionary sheet. Merge only appends names whose normalized form is
    /// not already on the sheet, so running Harvest twice does not duplicate options.
    /// </summary>
    public sealed class AliasSheet
    {
        private readonly List<AliasSheetRow> _rows = new List<AliasSheetRow>();
        private readonly HashSet<string> _usedKeys;

        public AliasSheet(NormalizerOptions options = null)
        {
            Options = options ?? NormalizerOptions.Default;
            _usedKeys = new HashSet<string>(StringComparer.Ordinal);
        }

        public NormalizerOptions Options { get; }
        public IReadOnlyList<AliasSheetRow> Rows => _rows;

        public static AliasSheet FromFile(string path, NormalizerOptions options = null)
        {
            return FromRows(DelimitedText.ParseFile(path), options);
        }

        public static AliasSheet FromRows(IReadOnlyList<string[]> rows, NormalizerOptions options = null)
        {
            var sheet = new AliasSheet(options);
            if (rows == null || rows.Count == 0) return sheet;

            var start = LooksLikeHeader(rows[0]) ? 1 : 0;
            for (var i = start; i < rows.Count; i++)
            {
                var row = rows[i];
                if (DelimitedText.IsBlankRow(row)) continue;

                var number = DelimitedText.Cell(row, 0);
                var name = DelimitedText.Cell(row, 1);
                var target = sheet.GetOrAddRow(number, name);
                var aliasStart = AliasSheetLoader.AliasStartColumn(row);

                for (var column = aliasStart; column < row.Length; column++)
                    target.TryAddUnique(DelimitedText.Cell(row, column), sheet._usedKeys, sheet.Options);
            }

            return sheet;
        }

        public static AliasSheet FromTallies(IEnumerable<RoomNameTally> tallies, RoomClassifier classifier = null, NormalizerOptions options = null)
        {
            var sheet = new AliasSheet(options);

            foreach (var tally in tallies ?? Enumerable.Empty<RoomNameTally>())
            {
                var result = classifier?.Classify(tally.MostCommonVariant);
                var classified = result != null && result.Status != MatchStatus.Unmatched && result.Number.Length > 0;
                var number = classified ? result.Number : string.Empty;
                var name = classified ? result.Title : string.Empty;
                var target = sheet.GetOrAddRow(number, name);

                foreach (var variant in tally.Variants)
                    target.TryAddUnique(variant.Key, sheet._usedKeys, sheet.Options);
            }

            return sheet;
        }

        /// <summary>
        /// Appends incoming names that are not already on this sheet. Returns how many
        /// new cells were added.
        /// </summary>
        public int MergeUnique(AliasSheet incoming)
        {
            if (incoming == null) return 0;

            var added = 0;
            foreach (var row in incoming.Rows)
            {
                if (row.Number.Length == 0)
                {
                    foreach (var alias in row.Aliases)
                    {
                        if (ContainsKey(alias)) continue;
                        var unmatched = new AliasSheetRow(string.Empty, string.Empty);
                        if (unmatched.TryAddUnique(alias, _usedKeys, Options))
                        {
                            _rows.Add(unmatched);
                            added++;
                        }
                    }
                    continue;
                }

                var target = GetOrAddRow(row.Number, row.Name);
                foreach (var alias in row.Aliases)
                {
                    if (target.TryAddUnique(alias, _usedKeys, Options)) added++;
                }
            }

            return added;
        }

        public void Write(TextWriter writer)
        {
            if (writer == null) throw new ArgumentNullException(nameof(writer));

            var extras = _rows.Count == 0 ? 1 : Math.Max(1, _rows.Max(r => r.Aliases.Count));
            var header = new List<string> { "Number", "Name" };
            for (var i = 1; i <= extras; i++) header.Add("Room Name " + i);
            writer.WriteLine(DelimitedText.FormatRow(header));

            foreach (var row in _rows)
            {
                var fields = new List<string> { row.Number, row.Name };
                fields.AddRange(row.Aliases);
                while (fields.Count < 2 + extras) fields.Add(string.Empty);
                writer.WriteLine(DelimitedText.FormatRow(fields));
            }
        }

        public void WriteFile(string path)
        {
            using (var writer = new StreamWriter(path, append: false))
            {
                Write(writer);
            }
        }

        private bool ContainsKey(string raw)
        {
            var key = RoomNameNormalizer.Key(raw, Options);
            return key.Length > 0 && _usedKeys.Contains(key);
        }

        private AliasSheetRow GetOrAddRow(string number, string name)
        {
            number = number ?? string.Empty;
            name = name ?? string.Empty;

            if (number.Length > 0)
            {
                var existing = _rows.FirstOrDefault(r =>
                    string.Equals(r.Number, number, StringComparison.Ordinal));
                if (existing != null) return existing;
            }

            var row = new AliasSheetRow(number, name);
            _rows.Add(row);
            return row;
        }

        private static bool LooksLikeHeader(string[] row)
        {
            var first = DelimitedText.Cell(row, 0);
            if (first.Length == 0) return false;
            return !OmniClassNumber.TryParse(first, out _, out _);
        }
    }
}
