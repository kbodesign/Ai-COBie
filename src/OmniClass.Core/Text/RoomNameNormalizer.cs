using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace OmniClass.Core.Text
{
    public sealed class NormalizerOptions
    {
        /// <summary>
        /// Drops trailing room-identifier tokens so "Restroom 101", "Restroom-2" and
        /// "Restroom 101A" all reduce to the same key as "Restroom".
        /// </summary>
        public bool StripTrailingIdentifiers { get; set; } = true;

        /// <summary>
        /// Treats a lower-to-upper transition as a word break, which is what makes
        /// "RestRoom" and "Rest Room" collapse together.
        /// </summary>
        public bool SplitCamelCase { get; set; } = true;

        public static NormalizerOptions Default { get; } = new NormalizerOptions();
    }

    /// <summary>
    /// Collapses the punctuation, casing and numbering habits that make one room type look
    /// like four different room types. Everything downstream matches on these outputs, never
    /// on raw text, and never by substring.
    /// </summary>
    public static class RoomNameNormalizer
    {
        private static readonly char[] DroppedEntirely = { '\'', '\u2018', '\u2019', '`', '\u00B4' };

        /// <summary>
        /// Canonical comparison key: uppercase alphanumerics with every separator removed.
        /// "Restroom", "RestRoom", "Rest_Room", "REST ROOM" and "Restroom 101" all return "RESTROOM".
        /// </summary>
        public static string Key(string raw, NormalizerOptions options = null)
        {
            return string.Concat(Tokens(raw, options));
        }

        /// <summary>
        /// Word tokens used by the fuzzy tier. Order is preserved but callers should treat
        /// these as a set; "Mens Restroom" and "Restroom Mens" are the same room.
        /// </summary>
        public static IReadOnlyList<string> Tokens(string raw, NormalizerOptions options = null)
        {
            options = options ?? NormalizerOptions.Default;

            if (string.IsNullOrWhiteSpace(raw)) return Array.Empty<string>();

            var spaced = InsertBreaks(StripDiacritics(raw), options);

            var tokens = spaced
                .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(t => t.ToUpperInvariant())
                .ToList();

            if (options.StripTrailingIdentifiers) StripTrailing(tokens);

            return tokens;
        }

        private static string InsertBreaks(string raw, NormalizerOptions options)
        {
            var sb = new StringBuilder(raw.Length * 2);
            var previous = '\0';

            for (var i = 0; i < raw.Length; i++)
            {
                var c = raw[i];

                if (Array.IndexOf(DroppedEntirely, c) >= 0) continue;

                if (!char.IsLetterOrDigit(c))
                {
                    sb.Append(' ');
                    previous = ' ';
                    continue;
                }

                if (previous != '\0' && previous != ' ' && NeedsBreak(previous, c, raw, i, options))
                {
                    sb.Append(' ');
                }

                sb.Append(c);
                previous = c;
            }

            return sb.ToString();
        }

        private static bool NeedsBreak(char previous, char current, string raw, int index, NormalizerOptions options)
        {
            // Digits never belong to the word beside them: "Room2" is "Room" then "2".
            if (char.IsLetter(previous) && char.IsDigit(current)) return true;
            if (char.IsDigit(previous) && char.IsLetter(current)) return true;

            if (!options.SplitCamelCase) return false;
            if (!char.IsLetter(previous) || !char.IsLetter(current)) return false;

            // "RestRoom" -> "Rest Room"
            if (char.IsLower(previous) && char.IsUpper(current)) return true;

            // "RRoom" -> "R Room", but leave a run of initials like "RR" intact.
            if (char.IsUpper(previous) && char.IsUpper(current))
            {
                var next = index + 1 < raw.Length ? raw[index + 1] : '\0';
                return char.IsLower(next);
            }

            return false;
        }

        private static void StripTrailing(List<string> tokens)
        {
            // A room called only "101" legitimately reduces to nothing; the classifier
            // reports that as unmatched rather than guessing.
            while (tokens.Count > 0 && IsIdentifierToken(tokens[tokens.Count - 1]))
            {
                tokens.RemoveAt(tokens.Count - 1);
            }
        }

        private static bool IsIdentifierToken(string token)
        {
            if (token.Length == 0) return true;
            if (token.Length == 1 && char.IsLetter(token[0])) return true;
            return token.All(char.IsDigit);
        }

        private static string StripDiacritics(string value)
        {
            var decomposed = value.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(decomposed.Length);

            foreach (var c in decomposed)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                {
                    sb.Append(c);
                }
            }

            return sb.ToString().Normalize(NormalizationForm.FormC);
        }
    }
}
