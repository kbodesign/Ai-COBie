using System;
using System.Collections.Generic;
using System.Linq;
using OmniClass.Core.Text;

namespace OmniClass.Core.Model
{
    /// <summary>One OmniClass Table 13 classification.</summary>
    public sealed class OmniClassEntry
    {
        public OmniClassEntry(OmniClassNumber number, string title, int sourceRow = 0)
        {
            Number = number ?? throw new ArgumentNullException(nameof(number));
            Title = title ?? string.Empty;
            SourceRow = sourceRow;
        }

        public OmniClassNumber Number { get; }
        public string Title { get; }
        public int SourceRow { get; }

        public override string ToString() => Number + ": " + Title;
    }

    /// <summary>
    /// One name a human might actually type for a classification. Authored in the wide
    /// spreadsheet, stored here one alias per row.
    /// </summary>
    public sealed class RoomAlias
    {
        public RoomAlias(string raw, string key, OmniClassEntry entry, int sourceRow, string sourceColumn, NormalizerOptions options = null)
        {
            Raw = raw;
            Key = key;
            Entry = entry;
            SourceRow = sourceRow;
            SourceColumn = sourceColumn;
            Tokens = RoomNameNormalizer.Tokens(raw, options);
        }

        public string Raw { get; }

        /// <summary>Normalized form; unique across the whole dictionary.</summary>
        public string Key { get; }

        public OmniClassEntry Entry { get; }
        public int SourceRow { get; }
        public string SourceColumn { get; }
        public IReadOnlyList<string> Tokens { get; }

        public override string ToString() => Raw + " -> " + Entry.Number;
    }

    public sealed class ClassificationDictionary
    {
        private readonly Dictionary<string, RoomAlias> _byKey;
        private readonly Dictionary<string, OmniClassEntry> _byNumber;
        private readonly List<RoomAlias> _aliases;

        public ClassificationDictionary(
            IEnumerable<OmniClassEntry> entries,
            IEnumerable<RoomAlias> aliases,
            IEnumerable<ValidationMessage> messages = null,
            NormalizerOptions normalizerOptions = null)
        {
            NormalizerOptions = normalizerOptions ?? NormalizerOptions.Default;

            _byNumber = (entries ?? Enumerable.Empty<OmniClassEntry>())
                .ToDictionary(e => e.Number.Canonical, StringComparer.Ordinal);

            _aliases = (aliases ?? Enumerable.Empty<RoomAlias>()).ToList();

            _byKey = new Dictionary<string, RoomAlias>(StringComparer.Ordinal);
            foreach (var alias in _aliases)
            {
                // Conflicts are reported by the loader's validator; first writer wins here
                // so a bad row cannot take the whole dictionary down.
                if (!_byKey.ContainsKey(alias.Key)) _byKey.Add(alias.Key, alias);
            }

            Messages = (messages ?? Enumerable.Empty<ValidationMessage>()).ToList();
        }

        public IReadOnlyCollection<OmniClassEntry> Entries => _byNumber.Values;
        public IReadOnlyList<RoomAlias> Aliases => _aliases;
        public IReadOnlyList<ValidationMessage> Messages { get; }

        /// <summary>
        /// The options the aliases were normalized with. Room names must be normalized the
        /// same way or the keys will not line up, so the classifier reads them from here.
        /// </summary>
        public NormalizerOptions NormalizerOptions { get; }

        public bool HasErrors => Messages.Any(m => m.Severity == ValidationSeverity.Error);

        public RoomAlias FindExact(string normalizedKey)
        {
            if (string.IsNullOrEmpty(normalizedKey)) return null;
            return _byKey.TryGetValue(normalizedKey, out var alias) ? alias : null;
        }

        public OmniClassEntry FindByNumber(string canonicalNumber)
        {
            if (string.IsNullOrEmpty(canonicalNumber)) return null;
            return _byNumber.TryGetValue(canonicalNumber, out var entry) ? entry : null;
        }
    }
}
