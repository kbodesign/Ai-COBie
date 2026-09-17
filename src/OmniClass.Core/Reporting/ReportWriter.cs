using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using OmniClass.Core.Audit;
using OmniClass.Core.Io;
using OmniClass.Core.Matching;
using OmniClass.Core.Model;

namespace OmniClass.Core.Reporting
{
    /// <summary>
    /// CSV reports shaped like the authoring sheet: Number and Name in A and B, then
    /// each room-name spelling as the next column, so Harvest output can be pasted
    /// straight back into the dictionary.
    /// </summary>
    public static class ReportWriter
    {
        public static void WriteAudit(TextWriter writer, IEnumerable<RoomNameTally> tallies, RoomClassifier classifier = null)
        {
            WriteAliasDatabase(writer, tallies, classifier);
        }

        public static void WriteAliasDatabase(TextWriter writer, IEnumerable<RoomNameTally> tallies, RoomClassifier classifier = null)
        {
            if (writer == null) throw new ArgumentNullException(nameof(writer));

            var groups = GroupByClassification(tallies, classifier);
            var extraColumns = groups.Count == 0 ? 1 : groups.Max(g => g.Names.Count);
            if (extraColumns < 1) extraColumns = 1;

            var header = new List<string> { "Number", "Name" };
            for (var i = 1; i <= extraColumns; i++) header.Add("Room Name " + i);
            writer.WriteLine(DelimitedText.FormatRow(header));

            foreach (var group in groups)
            {
                var fields = new List<string> { group.Number, group.Name };
                fields.AddRange(group.Names);
                while (fields.Count < 2 + extraColumns) fields.Add(string.Empty);
                writer.WriteLine(DelimitedText.FormatRow(fields));
            }
        }

        public static void WriteResults(TextWriter writer, IEnumerable<ClassificationResult> results)
        {
            if (writer == null) throw new ArgumentNullException(nameof(writer));

            writer.WriteLine(DelimitedText.FormatRow(new[]
            {
                "Room Name", "Status", "Number", "Name", "Matched Alias", "Score", "Other Candidates"
            }));

            foreach (var result in results ?? Enumerable.Empty<ClassificationResult>())
            {
                writer.WriteLine(DelimitedText.FormatRow(new[]
                {
                    result.RoomName,
                    result.Status.ToString(),
                    result.Number,
                    result.Title,
                    result.MatchedAlias,
                    result.Score == 0d ? string.Empty : result.Score.ToString("0.00", CultureInfo.InvariantCulture),
                    string.Join(" | ", result.Candidates.Skip(1).Select(c => c.ToString()))
                }));
            }
        }

        public static void WriteValidation(TextWriter writer, IEnumerable<ValidationMessage> messages)
        {
            if (writer == null) throw new ArgumentNullException(nameof(writer));

            writer.WriteLine(DelimitedText.FormatRow(new[] { "Severity", "Code", "Cell", "Message" }));

            foreach (var message in messages ?? Enumerable.Empty<ValidationMessage>())
            {
                writer.WriteLine(DelimitedText.FormatRow(new[]
                {
                    message.Severity.ToString(),
                    message.Code,
                    message.Location,
                    message.Message
                }));
            }
        }

        private sealed class AliasGroup
        {
            public string Number { get; set; }
            public string Name { get; set; }
            public List<string> Names { get; } = new List<string>();
        }

        private static List<AliasGroup> GroupByClassification(IEnumerable<RoomNameTally> tallies, RoomClassifier classifier)
        {
            var classified = new Dictionary<string, AliasGroup>(StringComparer.Ordinal);
            var unmatched = new List<AliasGroup>();

            foreach (var tally in tallies ?? Enumerable.Empty<RoomNameTally>())
            {
                var result = classifier?.Classify(tally.MostCommonVariant);
                var number = result == null || result.Status == MatchStatus.Unmatched
                    ? string.Empty
                    : result.Number;
                var name = result == null || result.Status == MatchStatus.Unmatched
                    ? string.Empty
                    : result.Title;

                var spellings = tally.Variants.Select(v => v.Key).ToList();

                if (number.Length == 0)
                {
                    unmatched.Add(new AliasGroup
                    {
                        Number = string.Empty,
                        Name = string.Empty,
                        Names = { tally.MostCommonVariant }
                    });
                    continue;
                }

                if (!classified.TryGetValue(number, out var group))
                {
                    group = new AliasGroup { Number = number, Name = name };
                    classified.Add(number, group);
                }

                foreach (var spelling in spellings)
                {
                    if (!group.Names.Any(existing => string.Equals(existing, spelling, StringComparison.OrdinalIgnoreCase)))
                        group.Names.Add(spelling);
                }
            }

            return classified.Values
                .OrderBy(g => g.Number, StringComparer.Ordinal)
                .Concat(unmatched)
                .ToList();
        }
    }
}
