using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OmniClass.Core.Io;
using OmniClass.Core.Text;

namespace OmniClass.Core.Transfer
{
    /// <summary>
    /// One room on the Export/Import sheet: reusable Name, then the OmniClass
    /// Table 13 number and title that Assign Classification would write. Room
    /// Number / Mark is not part of the sheet; it may still be present on older CSVs.
    /// </summary>
    public sealed class RoomTransferRow
    {
        public string RoomNumber { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string OmniClassNumber { get; set; } = string.Empty;
        public string OmniClassName { get; set; } = string.Empty;

        public bool HasOmniClass =>
            !string.IsNullOrWhiteSpace(OmniClassNumber) || !string.IsNullOrWhiteSpace(OmniClassName);

        public bool IsBlank =>
            string.IsNullOrWhiteSpace(RoomNumber)
            && string.IsNullOrWhiteSpace(Name)
            && !HasOmniClass;
    }

    public sealed class RoomTransferMergeResult
    {
        public int Added { get; set; }
        public int FilledClassification { get; set; }
        public int AlreadyPresent { get; set; }
        public List<RoomTransferRow> Rows { get; } = new List<RoomTransferRow>();
    }

    /// <summary>
    /// CSV used by Export Rooms / Import Rooms. Columns are Name, OmniClass Number,
    /// OmniClass Name. Room Number is not written: it is a unique Mark and is
    /// irrelevant to name reuse. Older files that still have a Room Number column
    /// are loaded; that column is ignored when matching on import.
    /// </summary>
    public static class RoomTransferSheet
    {
        public static readonly string[] Header =
        {
            "Name", "OmniClass Number", "OmniClass Name"
        };

        public static void Write(TextWriter writer, IEnumerable<RoomTransferRow> rows)
        {
            if (writer == null) throw new ArgumentNullException(nameof(writer));

            writer.WriteLine(DelimitedText.FormatRow(Header));
            foreach (var row in rows ?? Enumerable.Empty<RoomTransferRow>())
            {
                if (row == null || row.IsBlank) continue;
                writer.WriteLine(DelimitedText.FormatRow(new[]
                {
                    row.Name ?? string.Empty,
                    row.OmniClassNumber ?? string.Empty,
                    row.OmniClassName ?? string.Empty
                }));
            }
        }

        public static void WriteFile(string path, IEnumerable<RoomTransferRow> rows)
        {
            using (var writer = new StreamWriter(path, append: false))
            {
                Write(writer, rows);
            }
        }

        /// <summary>
        /// One row per distinct room name (normalized), so "W Room" and "W Room 2"
        /// count once. When the same name appears with and without OmniClass, the
        /// classified row wins.
        /// </summary>
        public static List<RoomTransferRow> Unique(IEnumerable<RoomTransferRow> rows)
        {
            var result = new List<RoomTransferRow>();
            var index = new Dictionary<string, int>(StringComparer.Ordinal);

            foreach (var row in rows ?? Enumerable.Empty<RoomTransferRow>())
            {
                if (row == null || row.IsBlank) continue;

                var key = UniqueKey(row);
                if (key.Length == 0) continue;

                if (!index.TryGetValue(key, out var at))
                {
                    index.Add(key, result.Count);
                    result.Add(Copy(row));
                    continue;
                }

                var existing = result[at];
                if (!existing.HasOmniClass && row.HasOmniClass)
                {
                    existing.OmniClassNumber = row.OmniClassNumber ?? string.Empty;
                    existing.OmniClassName = row.OmniClassName ?? string.Empty;
                }
            }

            return result;
        }

        /// <summary>
        /// Appends incoming names that are not already on the master list. Duplicate
        /// names are skipped. If a name is already present with blank OmniClass and
        /// the incoming row has a classification, that classification is filled in.
        /// </summary>
        public static RoomTransferMergeResult MergeUnique(
            IEnumerable<RoomTransferRow> existing,
            IEnumerable<RoomTransferRow> incoming)
        {
            var merged = new RoomTransferMergeResult();
            var index = new Dictionary<string, int>(StringComparer.Ordinal);

            foreach (var row in Unique(existing))
            {
                var key = UniqueKey(row);
                if (key.Length == 0) continue;
                index[key] = merged.Rows.Count;
                merged.Rows.Add(row);
            }

            foreach (var row in Unique(incoming))
            {
                var key = UniqueKey(row);
                if (key.Length == 0) continue;

                if (!index.TryGetValue(key, out var at))
                {
                    index.Add(key, merged.Rows.Count);
                    merged.Rows.Add(Copy(row));
                    merged.Added++;
                    continue;
                }

                var current = merged.Rows[at];
                if (!current.HasOmniClass && row.HasOmniClass)
                {
                    current.OmniClassNumber = row.OmniClassNumber ?? string.Empty;
                    current.OmniClassName = row.OmniClassName ?? string.Empty;
                    merged.FilledClassification++;
                    continue;
                }

                merged.AlreadyPresent++;
            }

            return merged;
        }

        internal static string UniqueKey(RoomTransferRow row)
        {
            if (row == null) return string.Empty;
            var nameKey = RoomNameNormalizer.Key(row.Name);
            return nameKey.Length == 0 ? string.Empty : "N:" + nameKey;
        }

        private static RoomTransferRow Copy(RoomTransferRow row)
        {
            return new RoomTransferRow
            {
                RoomNumber = row.RoomNumber ?? string.Empty,
                Name = row.Name ?? string.Empty,
                OmniClassNumber = row.OmniClassNumber ?? string.Empty,
                OmniClassName = row.OmniClassName ?? string.Empty
            };
        }

        public static IReadOnlyList<RoomTransferRow> FromFile(string path)
        {
            return FromRows(DelimitedText.ParseFile(path));
        }

        public static IReadOnlyList<RoomTransferRow> FromRows(IReadOnlyList<string[]> rows)
        {
            var result = new List<RoomTransferRow>();
            if (rows == null || rows.Count == 0) return result;

            var start = 0;
            var map = DefaultMap();
            if (LooksLikeHeader(rows[0]))
            {
                map = MapHeader(rows[0]);
                start = 1;
            }

            for (var i = start; i < rows.Count; i++)
            {
                var cells = rows[i];
                if (DelimitedText.IsBlankRow(cells)) continue;

                var row = new RoomTransferRow
                {
                    RoomNumber = Cell(cells, map.RoomNumber),
                    Name = Cell(cells, map.Name),
                    OmniClassNumber = Cell(cells, map.OmniClassNumber),
                    OmniClassName = Cell(cells, map.OmniClassName)
                };

                if (!row.IsBlank) result.Add(row);
            }

            return result;
        }

        public static bool LooksLikeAliasDictionary(IReadOnlyList<string[]> rows)
        {
            if (rows == null || rows.Count == 0) return false;
            var header = rows[0];
            for (var i = 0; i < header.Length; i++)
            {
                var cell = NormalizeHeader(DelimitedText.Cell(header, i));
                if (cell.StartsWith("ROOM NAME", StringComparison.Ordinal)) return true;
            }

            return false;
        }

        private static string Cell(string[] row, int index)
        {
            return index < 0 ? string.Empty : DelimitedText.Cell(row, index);
        }

        private static ColumnMap DefaultMap()
        {
            return new ColumnMap { RoomNumber = -1, Name = 0, OmniClassNumber = 1, OmniClassName = 2 };
        }

        private static ColumnMap MapHeader(string[] header)
        {
            var map = new ColumnMap { RoomNumber = -1, Name = -1, OmniClassNumber = -1, OmniClassName = -1 };

            for (var i = 0; i < header.Length; i++)
            {
                var cell = NormalizeHeader(DelimitedText.Cell(header, i));
                if (cell.Length == 0) continue;

                if (IsOmniClassNumber(cell)) map.OmniClassNumber = i;
                else if (IsOmniClassName(cell)) map.OmniClassName = i;
                else if (IsRoomNumber(cell)) map.RoomNumber = i;
                else if (IsName(cell) && map.Name < 0) map.Name = i;
            }

            if (map.Name < 0) map.Name = map.RoomNumber >= 0 ? 1 : 0;
            if (map.OmniClassNumber < 0) map.OmniClassNumber = map.RoomNumber >= 0 ? 2 : 1;
            if (map.OmniClassName < 0) map.OmniClassName = map.RoomNumber >= 0 ? 3 : 2;
            return map;
        }

        private static bool LooksLikeHeader(string[] row)
        {
            var first = NormalizeHeader(DelimitedText.Cell(row, 0));
            if (first.Length == 0) return false;
            return IsName(first) || IsRoomNumber(first) || first == "NUMBER" || IsOmniClassNumber(first);
        }

        private static bool IsRoomNumber(string cell)
        {
            return cell == "ROOM NUMBER" || cell == "ROOMNUMBER" || cell == "NUMBER";
        }

        private static bool IsName(string cell)
        {
            return cell == "NAME" || cell == "ROOM NAME" || cell == "ROOMNAME";
        }

        private static bool IsOmniClassNumber(string cell)
        {
            return cell == "OMNICLASS NUMBER"
                   || cell == "OMNICLASSNUMBER"
                   || cell == "CLASSIFICATION.SPACE.NUMBER"
                   || cell == "CLASSIFICATION SPACE NUMBER";
        }

        private static bool IsOmniClassName(string cell)
        {
            return cell == "OMNICLASS NAME"
                   || cell == "OMNICLASSNAME"
                   || cell == "OMNICLASS TITLE"
                   || cell == "CLASSIFICATION.SPACE.DESCRIPTION"
                   || cell == "CLASSIFICATION SPACE DESCRIPTION";
        }

        private static string NormalizeHeader(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            var chars = value.Trim().ToUpperInvariant().ToCharArray();
            return new string(chars.Where(c => c != '_' && c != '-').ToArray());
        }

        private struct ColumnMap
        {
            public int RoomNumber;
            public int Name;
            public int OmniClassNumber;
            public int OmniClassName;
        }
    }
}
