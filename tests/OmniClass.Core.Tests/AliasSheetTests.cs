using System.IO;
using System.Linq;
using OmniClass.Core.Audit;
using OmniClass.Core.Loading;
using OmniClass.Core.Matching;
using Xunit;

namespace OmniClass.Core.Tests
{
    public class AliasSheetTests
    {
        [Fact]
        public void MergeDoesNotRepeatANameThatNormalizesTheSame()
        {
            var existing = AliasSheet.FromRows(new[]
            {
                new[] { "Number", "Name", "Room Name 1" },
                new[] { "13-23 17", "Restroom", "RestRoom" }
            });

            var incoming = AliasSheet.FromRows(new[]
            {
                new[] { "13-23 17", "Restroom", "Rest_Room", "RR" }
            });

            var added = existing.MergeUnique(incoming);

            Assert.Equal(1, added);
            var row = Assert.Single(existing.Rows);
            Assert.Equal(new[] { "RestRoom", "RR" }, row.Aliases);
        }

        [Fact]
        public void SecondHarvestOfTheSameNamesAddsNothing()
        {
            var first = RoomNameAudit.Tally(new[] { "Restroom", "Corridor" });
            var sheet = AliasSheet.FromTallies(first, new RoomClassifier(Fixture.Restrooms()));
            var again = AliasSheet.FromTallies(first, new RoomClassifier(Fixture.Restrooms()));

            Assert.Equal(0, sheet.MergeUnique(again));
        }

        [Fact]
        public void UnmatchedNameIsNotRepeatedOnASecondMerge()
        {
            var sheet = AliasSheet.FromRows(new[]
            {
                new[] { "", "", "Corridor" }
            });

            var incoming = AliasSheet.FromRows(new[]
            {
                new[] { "", "", "Corridor" },
                new[] { "", "", "Office" }
            });

            Assert.Equal(1, sheet.MergeUnique(incoming));
            Assert.Equal(2, sheet.Rows.Count);
            Assert.Contains(sheet.Rows, r => r.Aliases.Contains("Office"));
        }

        [Fact]
        public void WriteThenLoadKeepsUniqueColumns()
        {
            var sheet = AliasSheet.FromTallies(
                RoomNameAudit.Tally(new[] { "RestRoom", "Rest_Room", "RR" }),
                new RoomClassifier(Fixture.Restrooms()));

            var writer = new StringWriter();
            sheet.Write(writer);
            var text = writer.ToString();

            Assert.Contains("13-23 17", text);
            Assert.Contains("Restroom", text);
            Assert.DoesNotContain("Rest_Room", text);

            var reloaded = AliasSheet.FromFile(WriteTemp(text));
            Assert.Single(reloaded.Rows);
            Assert.Equal(2, reloaded.Rows[0].Aliases.Count);
        }

        private static string WriteTemp(string text)
        {
            var path = Path.Combine(Path.GetTempPath(), "omniclass-alias-sheet-test.csv");
            File.WriteAllText(path, text);
            return path;
        }
    }
}
