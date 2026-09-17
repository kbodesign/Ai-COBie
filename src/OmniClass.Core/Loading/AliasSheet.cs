using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
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
        public const string UnmatchedLabel = "n/a";

        public AliasSheetRow(string number, string name)
        {
            if (IsUnmatchedNumber(number))
            {
                Number = UnmatchedLabel;
                Name = UnmatchedLabel;
            }
            else
            {
                Number = number ?? string.Empty;
                Name = name ?? string.Empty;
            }
        }

        public string Number { get; }
        public string Name { get; }
        public List<string> Aliases { get; } = new List<string>();

        public bool IsUnmatched => IsUnmatchedNumber(Number);

        public bool TryAddUnique(string raw, HashSet<string> usedKeys, NormalizerOptions options = null)
        {
            var cleaned = AliasSheet.CleanHarvestAlias(raw);
            if (cleaned == null) return false;

            var key = RoomNameNormalizer.Key(cleaned, options);
            if (key.Length == 0) return false;

            if (Aliases.Any(existing => string.Equals(RoomNameNormalizer.Key(existing, options), key, StringComparison.Ordinal)))
                return false;
            if (usedKeys != null && !usedKeys.Add(key)) return false;

            Aliases.Add(cleaned);
            return true;
        }

        public bool RemoveKey(string key, NormalizerOptions options = null)
        {
            if (string.IsNullOrEmpty(key)) return false;

            var removed = Aliases.RemoveAll(existing =>
                string.Equals(RoomNameNormalizer.Key(existing, options), key, StringComparison.Ordinal));
            return removed > 0;
        }

        public static bool IsUnmatchedNumber(string number)
        {
            return string.IsNullOrWhiteSpace(number)
                   || string.Equals(number.Trim(), UnmatchedLabel, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// The harvest/dictionary sheet. Merge only appends names whose normalized form is
    /// not already on the sheet, so running Harvest twice does not duplicate options.
    /// Table 13 rows own their aliases; names with no Table 13 match are written as n/a.
    /// </summary>
    public sealed class AliasSheet
    {
        private static readonly Regex CountSuffix = new Regex(@"\s+\(\d+\)\s*$", RegexOptions.Compiled);

        private readonly List<AliasSheetRow> _rows = new List<AliasSheetRow>();
        private readonly HashSet<string> _usedKeys;
        private readonly HashSet<string> _classifiedKeys;

        public AliasSheet(NormalizerOptions options = null)
        {
            Options = options ?? NormalizerOptions.Default;
            _usedKeys = new HashSet<string>(StringComparer.Ordinal);
            _classifiedKeys = new HashSet<string>(StringComparer.Ordinal);
        }

        public NormalizerOptions Options { get; }
        public IReadOnlyList<AliasSheetRow> Rows => _rows;

        public static AliasSheet FromFile(string path, NormalizerOptions options = null, RoomClassifier classifier = null)
        {
            return FromRows(DelimitedText.ParseFile(path), options, classifier);
        }

        public static AliasSheet FromRows(IReadOnlyList<string[]> rows, NormalizerOptions options = null, RoomClassifier classifier = null)
        {
            var sheet = new AliasSheet(options);
            if (rows == null || rows.Count == 0) return sheet;

            var start = LooksLikeHeader(rows[0]) ? 1 : 0;
            var unmatchedCells = new List<string>();

            for (var i = start; i < rows.Count; i++)
            {
                var row = rows[i];
                if (DelimitedText.IsBlankRow(row)) continue;

                var number = DelimitedText.Cell(row, 0);
                var name = DelimitedText.Cell(row, 1);
                if (!TryCanonicalTable13(number, out var canonical))
                {
                    var aliasStart = AliasSheetLoader.AliasStartColumn(row);
                    for (var column = aliasStart; column < row.Length; column++)
                        unmatchedCells.Add(DelimitedText.Cell(row, column));
                    continue;
                }

                var target = sheet.GetOrAddClassifiedRow(canonical, name);
                var startColumn = AliasSheetLoader.AliasStartColumn(row);
                for (var column = startColumn; column < row.Length; column++)
                    sheet.AddClassifiedAlias(target, DelimitedText.Cell(row, column), omitOfficialTitle: false);
            }

            foreach (var cell in unmatchedCells)
                sheet.PlaceAlias(cell, classifier);

            sheet.MoveUnmatchedToEnd();
            return sheet;
        }

        public static AliasSheet FromTallies(IEnumerable<RoomNameTally> tallies, RoomClassifier classifier = null, NormalizerOptions options = null)
        {
            var sheet = new AliasSheet(options);
            var unmatched = new List<RoomNameTally>();

            foreach (var tally in tallies ?? Enumerable.Empty<RoomNameTally>())
            {
                var result = classifier?.Classify(tally.MostCommonVariant);
                if (HasTable13Match(result))
                {
                    var target = sheet.GetOrAddClassifiedRow(result.Number, result.Title);
                    foreach (var variant in tally.Variants)
                        sheet.AddClassifiedAlias(target, variant.Key, omitOfficialTitle: false);
                }
                else
                {
                    unmatched.Add(tally);
                }
            }

            foreach (var tally in unmatched)
            {
                foreach (var variant in tally.Variants)
                    sheet.AddUnmatchedAlias(variant.Key);
            }

            sheet.MoveUnmatchedToEnd();
            return sheet;
        }

        /// <summary>
        /// Appends incoming names that are not already on this sheet. Table 13 rows take
        /// ownership of a name even if it previously sat on an n/a row. Returns how many
        /// new cells were added.
        /// </summary>
        public int MergeUnique(AliasSheet incoming)
        {
            if (incoming == null) return 0;

            var added = 0;

            foreach (var row in incoming.Rows.Where(r => !r.IsUnmatched))
            {
                var target = GetOrAddClassifiedRow(row.Number, row.Name);
                foreach (var alias in row.Aliases)
                {
                    if (AddClassifiedAlias(target, alias, omitOfficialTitle: false)) added++;
                }
            }

            foreach (var row in incoming.Rows.Where(r => r.IsUnmatched))
            {
                foreach (var alias in row.Aliases)
                {
                    if (AddUnmatchedAlias(alias)) added++;
                }
            }

            DropEmptyUnmatchedRows();
            MoveUnmatchedToEnd();
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

        internal static string CleanHarvestAlias(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;

            var cleaned = CountSuffix.Replace(raw.Trim(), string.Empty).Trim();
            if (cleaned.Length == 0) return null;
            if (IsStatusToken(cleaned)) return null;
            return cleaned;
        }

        private void PlaceAlias(string raw, RoomClassifier classifier)
        {
            if (classifier == null)
            {
                AddUnmatchedAlias(raw);
                return;
            }

            var cleaned = CleanHarvestAlias(raw);
            if (cleaned == null) return;

            var result = classifier.Classify(cleaned);
            if (HasTable13Match(result))
            {
                var target = GetOrAddClassifiedRow(result.Number, result.Title);
                AddClassifiedAlias(target, cleaned, omitOfficialTitle: true);
                return;
            }

            AddUnmatchedAlias(cleaned);
        }

        private bool AddClassifiedAlias(AliasSheetRow target, string raw, bool omitOfficialTitle)
        {
            var cleaned = CleanHarvestAlias(raw);
            if (cleaned == null) return false;

            var key = RoomNameNormalizer.Key(cleaned, Options);
            if (key.Length == 0) return false;

            if (omitOfficialTitle && string.Equals(cleaned, target.Name, StringComparison.OrdinalIgnoreCase))
                return false;
            if (_classifiedKeys.Contains(key)) return false;

            if (_usedKeys.Contains(key))
                RemoveFromUnmatched(key);

            if (!target.TryAddUnique(cleaned, _usedKeys, Options)) return false;
            _classifiedKeys.Add(key);
            return true;
        }

        private bool AddUnmatchedAlias(string raw)
        {
            var cleaned = CleanHarvestAlias(raw);
            if (cleaned == null) return false;

            var key = RoomNameNormalizer.Key(cleaned, Options);
            if (key.Length == 0) return false;
            if (_usedKeys.Contains(key)) return false;

            var unmatched = new AliasSheetRow(AliasSheetRow.UnmatchedLabel, AliasSheetRow.UnmatchedLabel);
            if (!unmatched.TryAddUnique(cleaned, _usedKeys, Options)) return false;

            _rows.Add(unmatched);
            return true;
        }

        private void RemoveFromUnmatched(string key)
        {
            foreach (var row in _rows.Where(r => r.IsUnmatched).ToList())
            {
                if (!row.RemoveKey(key, Options)) continue;
                _usedKeys.Remove(key);
            }

            DropEmptyUnmatchedRows();
        }

        private void DropEmptyUnmatchedRows()
        {
            _rows.RemoveAll(r => r.IsUnmatched && r.Aliases.Count == 0);
        }

        private void MoveUnmatchedToEnd()
        {
            var classified = _rows.Where(r => !r.IsUnmatched).ToList();
            var unmatched = _rows.Where(r => r.IsUnmatched).ToList();
            _rows.Clear();
            _rows.AddRange(classified);
            _rows.AddRange(unmatched);
        }

        private AliasSheetRow GetOrAddClassifiedRow(string number, string name)
        {
            if (!TryCanonicalTable13(number, out var canonical))
                throw new ArgumentException("'" + number + "' is not a Table 13 number.", nameof(number));

            var existing = _rows.FirstOrDefault(r =>
                !r.IsUnmatched && string.Equals(r.Number, canonical, StringComparison.Ordinal));
            if (existing != null) return existing;

            var row = new AliasSheetRow(canonical, name ?? string.Empty);
            _rows.Add(row);
            return row;
        }

        private static bool HasTable13Match(ClassificationResult result)
        {
            return result != null
                   && result.Status != MatchStatus.Unmatched
                   && TryCanonicalTable13(result.Number, out _);
        }

        private static bool TryCanonicalTable13(string number, out string canonical)
        {
            canonical = null;
            if (!OmniClassNumber.TryParse(number, out var parsed, out _)) return false;
            canonical = parsed.Canonical;
            return true;
        }

        private static bool IsStatusToken(string value)
        {
            foreach (MatchStatus status in Enum.GetValues(typeof(MatchStatus)))
            {
                if (string.Equals(value, status.ToString(), StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private static bool LooksLikeHeader(string[] row)
        {
            var first = DelimitedText.Cell(row, 0);
            if (first.Length == 0) return false;
            if (AliasSheetRow.IsUnmatchedNumber(first)) return false;
            return !OmniClassNumber.TryParse(first, out _, out _);
        }
    }
}
