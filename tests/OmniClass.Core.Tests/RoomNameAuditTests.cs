using System.IO;
using System.Linq;
using OmniClass.Core.Audit;
using OmniClass.Core.Matching;
using OmniClass.Core.Reporting;
using Xunit;

namespace OmniClass.Core.Tests
{
    public class RoomNameAuditTests
    {
        [Fact]
        public void GroupsSpellingVariantsAndRanksByFrequency()
        {
            var tallies = RoomNameAudit.Tally(new[]
            {
                "Restroom", "RestRoom", "Rest_Room", "Restroom 101",
                "Corridor", "Corridor"
            });

            Assert.Equal(2, tallies.Count);

            var restrooms = tallies[0];
            Assert.Equal("RESTROOM", restrooms.Key);
            Assert.Equal(4, restrooms.Count);
            Assert.Equal(4, restrooms.Variants.Count());

            Assert.Equal("CORRIDOR", tallies[1].Key);
            Assert.Equal(2, tallies[1].Count);
        }

        [Fact]
        public void CountsRepeatedSpellingsSeparately()
        {
            var tally = RoomNameAudit.Tally(new[] { "RestRoom", "RestRoom", "Restroom" }).Single();

            Assert.Equal(3, tally.Count);
            Assert.Equal("RestRoom", tally.MostCommonVariant);
            Assert.Equal(2, tally.Variants.First().Value);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("  ")]
        [InlineData("101")]
        public void IgnoresNamesThatCannotBeMatched(string name)
        {
            Assert.Empty(RoomNameAudit.Tally(new[] { name }));
        }

        [Fact]
        public void AuditReportCarriesTheProposedClassification()
        {
            var classifier = new RoomClassifier(Fixture.Restrooms());
            var tallies = RoomNameAudit.Tally(new[] { "Rest_Room", "Rest_Room", "Broom Closet" });

            var writer = new StringWriter();
            ReportWriter.WriteAudit(writer, tallies, classifier);
            var lines = writer.ToString().Split('\n').Where(l => l.Trim().Length > 0).ToList();

            Assert.Equal(3, lines.Count);
            Assert.StartsWith("Rooms,Normalized,", lines[0]);
            Assert.Contains("Exact", lines[1]);
            Assert.Contains("13-23 17", lines[1]);
            Assert.Contains("Unmatched", lines[2]);
        }

        [Fact]
        public void ReportEscapesCommasAndQuotes()
        {
            var writer = new StringWriter();
            ReportWriter.WriteResults(writer, new RoomClassifier(Fixture.Restrooms())
                .ClassifyAll(new[] { "Lounge, Staff \"Quiet\"" }));

            Assert.Contains("\"Lounge, Staff \"\"Quiet\"\"\"", writer.ToString());
        }
    }
}
