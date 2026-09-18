using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace OmniClass.Core.Io
{
    /// <summary>
    /// Minimal RFC 4180 reader/writer. Hand-rolled rather than taken from a package so the
    /// core library ships into Revit with no assembly-resolution risk.
    /// </summary>
    public static class DelimitedText
    {
        public static List<string[]> Parse(TextReader reader, char delimiter = ',')
        {
            if (reader == null) throw new ArgumentNullException(nameof(reader));

            var rows = new List<string[]>();
            var fields = new List<string>();
            var field = new StringBuilder();
            var inQuotes = false;
            var sawAnything = false;
            var atStart = true;

            int read;
            while ((read = reader.Read()) != -1)
            {
                var c = (char)read;

                if (atStart)
                {
                    atStart = false;
                    if (c == '\uFEFF') continue;
                }

                sawAnything = true;

                if (inQuotes)
                {
                    if (c != '"')
                    {
                        field.Append(c);
                    }
                    else if (reader.Peek() == '"')
                    {
                        reader.Read();
                        field.Append('"');
                    }
                    else
                    {
                        inQuotes = false;
                    }
                    continue;
                }

                if (c == '"')
                {
                    inQuotes = true;
                }
                else if (c == delimiter)
                {
                    fields.Add(field.ToString());
                    field.Clear();
                }
                else if (c == '\n')
                {
                    fields.Add(field.ToString());
                    field.Clear();
                    rows.Add(fields.ToArray());
                    fields.Clear();
                }
                else if (c != '\r')
                {
                    field.Append(c);
                }
            }

            if (sawAnything && (field.Length > 0 || fields.Count > 0))
            {
                fields.Add(field.ToString());
                rows.Add(fields.ToArray());
            }

            return rows;
        }

        public static List<string[]> ParseFile(string path, char delimiter = ',')
        {
            using (var reader = new StreamReader(path, Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
            {
                return Parse(reader, delimiter);
            }
        }

        public static bool IsBlankRow(string[] row)
        {
            return row == null || row.All(string.IsNullOrWhiteSpace);
        }

        public static string FormatRow(IEnumerable<string> fields, char delimiter = ',')
        {
            if (fields == null) throw new ArgumentNullException(nameof(fields));
            return string.Join(delimiter.ToString(), fields.Select(f => Escape(f, delimiter)));
        }

        private static string Escape(string value, char delimiter)
        {
            value = value ?? string.Empty;

            var needsQuotes = value.IndexOf(delimiter) >= 0
                              || value.IndexOf('"') >= 0
                              || value.IndexOf('\n') >= 0
                              || value.IndexOf('\r') >= 0
                              || value != value.Trim();

            return needsQuotes ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
        }

        /// <summary>
        /// Reads a cell by index, tolerating short rows produced by trailing empty columns.
        /// </summary>
        public static string Cell(string[] row, int index)
        {
            if (row == null || index < 0 || index >= row.Length) return string.Empty;
            return (row[index] ?? string.Empty).Trim();
        }
    }
}
