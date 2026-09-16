using System;
using System.Collections.Generic;
using System.Linq;
using OmniClass.Core.Io;
using OmniClass.Core.Text;

namespace OmniClass.Cli
{
    /// <summary>
    /// Reads room names out of a schedule export. Revit room schedules exported to CSV or
    /// tab-delimited text vary in shape, so the column is found by header name and falls
    /// back to the first column.
    /// </summary>
    internal static class RoomNameFile
    {
        public static List<string> Read(string path, string columnSpec)
        {
            var delimiter = path.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) ? '\t' : ',';
            var rows = DelimitedText.ParseFile(path, delimiter)
                .Where(r => !DelimitedText.IsBlankRow(r))
                .ToList();

            if (rows.Count == 0) return new List<string>();

            var columnIndex = ResolveColumn(rows[0], columnSpec, out var headerConsumed);

            return rows
                .Skip(headerConsumed ? 1 : 0)
                .Select(r => DelimitedText.Cell(r, columnIndex))
                .Where(name => name.Length > 0)
                .ToList();
        }

        private static int ResolveColumn(string[] header, string columnSpec, out bool headerConsumed)
        {
            if (!string.IsNullOrEmpty(columnSpec))
            {
                if (int.TryParse(columnSpec, out var explicitIndex))
                {
                    headerConsumed = LooksLikeHeader(header);
                    return explicitIndex;
                }

                var wanted = RoomNameNormalizer.Key(columnSpec);
                var byName = IndexOfHeader(header, h => RoomNameNormalizer.Key(h) == wanted);

                if (byName < 0) throw new ArgumentException($"No column named '{columnSpec}' in the header row.");

                headerConsumed = true;
                return byName;
            }

            var nameColumn = IndexOfHeader(header, h => RoomNameNormalizer.Key(h).Contains("NAME"));
            if (nameColumn >= 0)
            {
                headerConsumed = true;
                return nameColumn;
            }

            headerConsumed = LooksLikeHeader(header);
            return 0;
        }

        private static int IndexOfHeader(string[] header, Func<string, bool> predicate)
        {
            for (var i = 0; i < header.Length; i++)
            {
                var cell = DelimitedText.Cell(header, i);
                if (cell.Length > 0 && predicate(cell)) return i;
            }

            return -1;
        }

        private static bool LooksLikeHeader(string[] header)
        {
            var first = DelimitedText.Cell(header, 0);
            return first.Equals("name", StringComparison.OrdinalIgnoreCase)
                   || first.Equals("room", StringComparison.OrdinalIgnoreCase);
        }
    }
}
