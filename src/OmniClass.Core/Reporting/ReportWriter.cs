using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using OmniClass.Core.Audit;
using OmniClass.Core.Io;
using OmniClass.Core.Loading;
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
            AliasSheet.FromTallies(tallies, classifier).Write(writer);
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

    }
}
