using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OmniClass.Core.Io;

namespace OmniClass.Core.Transfer
{
    /// <summary>
    /// One room on the Export/Import sheet: architectural Number and Name, then the
    /// OmniClass Table 13 number and title that Assign Classification would write.
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

    /// <summary>
    /// CSV used by Export Rooms / Import Rooms. Columns are Room Number, Name,
    /// OmniClass Number, OmniClass Name. Header aliases are accepted so an Excel
    /// save-as or a Classification.Space.* schedule still loads.
    /// </summary>
    public static class RoomTransferSheet
    {
        public static readonly string[] Header =
        {
            "Room Number", "Name", "OmniClass Number", "OmniClass Name"
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
                    row.RoomNumber ?? string.Empty,
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
            return new ColumnMap { RoomNumber = 0, Name = 1, OmniClassNumber = 2, OmniClassName = 3 };
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

            if (map.RoomNumber < 0) map.RoomNumber = 0;
            if (map.Name < 0) map.Name = 1;
            if (map.OmniClassNumber < 0) map.OmniClassNumber = 2;
            if (map.OmniClassName < 0) map.OmniClassName = 3;
            return map;
        }

        private static bool LooksLikeHeader(string[] row)
        {
            var first = NormalizeHeader(DelimitedText.Cell(row, 0));
            if (first.Length == 0) return false;
            return IsRoomNumber(first) || first == "NUMBER" || IsOmniClassNumber(first);
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
