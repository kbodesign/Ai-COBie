using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OmniClass.Core.Io;
using OmniClass.Core.Model;
using OmniClass.Core.Text;

namespace OmniClass.Core.Loading
{
    /// <summary>
    /// Reads the wide authoring sheet (number, title, level, then any number of alias
    /// columns) and unpivots it into one row per alias. Authors keep the layout they like;
    /// everything downstream gets an indexable, conflict-checked lookup.
    /// </summary>
    public static class AliasSheetLoader
    {
        public const int NumberColumn = 0;
        public const int TitleColumn = 1;

        /// <summary>
        /// Column C is the first room-name option, unless it is a leftover level digit
        /// from the older sheet (1-6), in which case aliases start at D.
        /// </summary>
        public const int FirstAliasColumn = 2;

        public static ClassificationDictionary LoadFile(string path, NormalizerOptions options = null)
        {
            using (var reader = new StreamReader(path))
            {
                return Load(reader, options);
            }
        }

        public static ClassificationDictionary Load(TextReader reader, NormalizerOptions options = null)
        {
            return Load(DelimitedText.Parse(reader), options);
        }

        public static ClassificationDictionary Load(IReadOnlyList<string[]> rows, NormalizerOptions options = null)
        {
            options = options ?? NormalizerOptions.Default;

            var messages = new List<ValidationMessage>();
            var entries = new List<OmniClassEntry>();
            var aliases = new List<RoomAlias>();

            var entriesByNumber = new Dictionary<string, OmniClassEntry>(StringComparer.Ordinal);
            var aliasesByKey = new Dictionary<string, RoomAlias>(StringComparer.Ordinal);

            var startIndex = LooksLikeHeader(rows) ? 1 : 0;

            for (var i = startIndex; i < rows.Count; i++)
            {
                var row = rows[i];
                var rowNumber = i + 1;

                if (DelimitedText.IsBlankRow(row)) continue;

                var entry = ReadEntry(row, rowNumber, entriesByNumber, messages);
                if (entry == null) continue;

                entries.Add(entry);
                entriesByNumber[entry.Number.Canonical] = entry;

                CheckOptionalLevelColumn(row, rowNumber, entry, messages);
                ReadAliases(row, rowNumber, entry, aliasesByKey, aliases, messages, options);
            }

            CheckParents(entries, entriesByNumber, messages);

            return new ClassificationDictionary(entries, aliases, messages, options);
        }

        private static OmniClassEntry ReadEntry(
            string[] row,
            int rowNumber,
            Dictionary<string, OmniClassEntry> entriesByNumber,
            List<ValidationMessage> messages)
        {
            var rawNumber = DelimitedText.Cell(row, NumberColumn);
            var title = DelimitedText.Cell(row, TitleColumn);

            if (!OmniClassNumber.TryParse(rawNumber, out var number, out var error))
            {
                messages.Add(new ValidationMessage(
                    ValidationSeverity.Error, "MalformedNumber", error, rowNumber, ColumnName(NumberColumn)));
                return null;
            }

            if (title.Length == 0)
            {
                messages.Add(new ValidationMessage(
                    ValidationSeverity.Error, "MissingTitle",
                    $"{number} has no title. The title is written into the model, so it cannot be blank.",
                    rowNumber, ColumnName(TitleColumn)));
                return null;
            }

            if (entriesByNumber.TryGetValue(number.Canonical, out var existing))
            {
                messages.Add(new ValidationMessage(
                    ValidationSeverity.Error, "DuplicateNumber",
                    $"{number} is already defined on row {existing.SourceRow} as '{existing.Title}'. " +
                    "Merge the two rows so each classification appears once.",
                    rowNumber, ColumnName(NumberColumn)));
                return null;
            }

            return new OmniClassEntry(number, title, rowNumber);
        }

        /// <summary>
        /// Older sheets stored a level digit in column C. New sheets put the first room
        /// name there. A cell that is only 1-6 is treated as the old level column.
        /// </summary>
        internal static int AliasStartColumn(string[] row)
        {
            return LooksLikeLevelCell(DelimitedText.Cell(row, FirstAliasColumn)) ? 3 : 2;
        }

        private static bool LooksLikeLevelCell(string value)
        {
            return int.TryParse(value, out var level) && level >= 1 && level <= 6;
        }

        private static void CheckOptionalLevelColumn(string[] row, int rowNumber, OmniClassEntry entry, List<ValidationMessage> messages)
        {
            if (AliasStartColumn(row) != 3) return;

            var rawLevel = DelimitedText.Cell(row, FirstAliasColumn);
            if (!int.TryParse(rawLevel, out var declared) || declared == entry.Number.Depth) return;

            messages.Add(new ValidationMessage(
                ValidationSeverity.Warning, "LevelMismatch",
                $"Level says '{rawLevel}' but {entry.Number} is level {entry.Number.Depth}. " +
                "Level is derived from the number, so this column can be dropped.",
                rowNumber, ColumnName(FirstAliasColumn)));
        }

        private static void ReadAliases(
            string[] row,
            int rowNumber,
            OmniClassEntry entry,
            Dictionary<string, RoomAlias> aliasesByKey,
            List<RoomAlias> aliases,
            List<ValidationMessage> messages,
            NormalizerOptions options)
        {
            // The official title is the single most likely thing someone typed, so it is
            // always an alias whether or not it was repeated in an alias column.
            var candidates = new List<KeyValuePair<string, int>>
            {
                new KeyValuePair<string, int>(entry.Title, TitleColumn)
            };

            for (var column = AliasStartColumn(row); column < row.Length; column++)
            {
                var value = DelimitedText.Cell(row, column);
                if (value.Length > 0) candidates.Add(new KeyValuePair<string, int>(value, column));
            }

            foreach (var candidate in candidates)
            {
                var raw = candidate.Key;
                var columnName = ColumnName(candidate.Value);
                var key = RoomNameNormalizer.Key(raw, options);

                if (key.Length == 0)
                {
                    messages.Add(new ValidationMessage(
                        ValidationSeverity.Warning, "EmptyAlias",
                        $"'{raw}' normalizes to nothing, so it can never match a room name.",
                        rowNumber, columnName));
                    continue;
                }

                if (key.All(char.IsDigit))
                {
                    messages.Add(new ValidationMessage(
                        ValidationSeverity.Error, "NumericAlias",
                        $"'{raw}' is only digits. Matching on a bare number would classify rooms by " +
                        "their room number, so this alias is rejected.",
                        rowNumber, columnName));
                    continue;
                }

                if (aliasesByKey.TryGetValue(key, out var existing))
                {
                    if (existing.Entry.Number.Equals(entry.Number))
                    {
                        messages.Add(new ValidationMessage(
                            ValidationSeverity.Info, "RedundantAlias",
                            $"'{raw}' already matches this classification via '{existing.Raw}' " +
                            $"({existing.SourceColumn}{existing.SourceRow}); both normalize to '{key}'. " +
                            "This cell can be deleted.",
                            rowNumber, columnName));
                    }
                    else
                    {
                        messages.Add(new ValidationMessage(
                            ValidationSeverity.Error, "ConflictingAlias",
                            $"'{raw}' normalizes to '{key}', which row {existing.SourceRow} already maps to " +
                            $"{existing.Entry.Number}. One name cannot mean two classifications - " +
                            "remove it from one row or make it more specific.",
                            rowNumber, columnName));
                    }
                    continue;
                }

                if (key.Length < 2)
                {
                    messages.Add(new ValidationMessage(
                        ValidationSeverity.Warning, "ShortAlias",
                        $"'{raw}' is a single character after normalizing. It will match aggressively; " +
                        "consider removing it.",
                        rowNumber, columnName));
                }

                var alias = new RoomAlias(raw, key, entry, rowNumber, columnName, options);
                aliasesByKey.Add(key, alias);
                aliases.Add(alias);
            }
        }

        private static void CheckParents(
            List<OmniClassEntry> entries,
            Dictionary<string, OmniClassEntry> entriesByNumber,
            List<ValidationMessage> messages)
        {
            foreach (var entry in entries)
            {
                var parent = entry.Number.Parent;
                if (parent == null || parent.Depth < 3) continue;
                if (entriesByNumber.ContainsKey(parent.Canonical)) continue;

                messages.Add(new ValidationMessage(
                    ValidationSeverity.Warning, "MissingParent",
                    $"{entry.Number} has no parent row for {parent}. Rooms that only match the " +
                    "generic parent will come back unmatched until it is added.",
                    entry.SourceRow, ColumnName(NumberColumn)));
            }
        }

        private static bool LooksLikeHeader(IReadOnlyList<string[]> rows)
        {
            if (rows.Count == 0) return false;
            var first = DelimitedText.Cell(rows[0], NumberColumn);
            return !OmniClassNumber.TryParse(first, out _, out _);
        }

        /// <summary>Spreadsheet column letter, so validation messages point at a real cell.</summary>
        public static string ColumnName(int zeroBasedIndex)
        {
            var name = string.Empty;
            var index = zeroBasedIndex;

            while (index >= 0)
            {
                name = (char)('A' + index % 26) + name;
                index = index / 26 - 1;
            }

            return name;
        }
    }
}
