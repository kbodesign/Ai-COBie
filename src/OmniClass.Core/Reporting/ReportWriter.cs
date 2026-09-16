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
    /// CSV reports. These are the hand-off between the tool and the people curating the
    /// dictionary, so they are shaped to be pasted straight back into the authoring sheet.
    /// </summary>
    public static class ReportWriter
    {
        public static void WriteAudit(TextWriter writer, IEnumerable<RoomNameTally> tallies, RoomClassifier classifier = null)
        {
            if (writer == null) throw new ArgumentNullException(nameof(writer));

            writer.WriteLine(DelimitedText.FormatRow(new[]
            {
                "Rooms", "Normalized", "Spellings Seen", "Status", "Proposed Number", "Proposed Title", "Matched Alias", "Score"
            }));

            foreach (var tally in tallies ?? Enumerable.Empty<RoomNameTally>())
            {
                var result = classifier?.Classify(tally.MostCommonVariant);

                writer.WriteLine(DelimitedText.FormatRow(new[]
                {
                    tally.Count.ToString(CultureInfo.InvariantCulture),
                    tally.Key,
                    string.Join(" | ", tally.Variants.Select(v => v.Key + " (" + v.Value + ")")),
                    result == null ? string.Empty : result.Status.ToString(),
                    result?.Number ?? string.Empty,
                    result?.Title ?? string.Empty,
                    result?.MatchedAlias ?? string.Empty,
                    result == null || result.Score == 0d
                        ? string.Empty
                        : result.Score.ToString("0.00", CultureInfo.InvariantCulture)
                }));
            }
        }

        public static void WriteResults(TextWriter writer, IEnumerable<ClassificationResult> results)
        {
            if (writer == null) throw new ArgumentNullException(nameof(writer));

            writer.WriteLine(DelimitedText.FormatRow(new[]
            {
                "Room Name", "Status", "Number", "Title", "Matched Alias", "Score", "Other Candidates"
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
    }
}
