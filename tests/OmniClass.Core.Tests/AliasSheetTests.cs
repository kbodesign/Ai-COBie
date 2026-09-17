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

        [Fact]
        public void WRoomAndWrrBothLandOnWomensRestroom()
        {
            var sheet = AliasSheet.FromTallies(
                RoomNameAudit.Tally(new[] { "WRR", "W Room" }),
                new RoomClassifier(Fixture.Restrooms()));

            var womens = Assert.Single(sheet.Rows);
            Assert.Equal("13-23 17 13", womens.Number);
            Assert.Equal("Women's Restroom", womens.Name);
            Assert.Contains("WRR", womens.Aliases);
            Assert.Contains("W Room", womens.Aliases);
        }

        [Fact]
        public void NamesWithNoTable13MatchAreWrittenAsNotApplicable()
        {
            var sheet = AliasSheet.FromTallies(
                RoomNameAudit.Tally(new[] { "W Room", "Automatic Door Controls and Operators" }),
                new RoomClassifier(Fixture.Restrooms()));

            var womens = Assert.Single(sheet.Rows, r => r.Number == "13-23 17 13");
            Assert.Equal(new[] { "W Room" }, womens.Aliases);

            var unmatched = Assert.Single(sheet.Rows, r => r.IsUnmatched);
            Assert.Equal(AliasSheetRow.UnmatchedLabel, unmatched.Number);
            Assert.Equal(AliasSheetRow.UnmatchedLabel, unmatched.Name);
            Assert.Equal(new[] { "Automatic Door Controls and Operators" }, unmatched.Aliases);

            var writer = new StringWriter();
            sheet.Write(writer);
            Assert.Contains("n/a,n/a,Automatic Door Controls and Operators", writer.ToString());
        }

        [Fact]
        public void NonTable13HarvestRowIsNotAClassificationAndWRoomMovesToWomens()
        {
            var classifier = new RoomClassifier(Fixture.Restrooms());
            var sheet = AliasSheet.FromRows(UploadedDoorsHarvest, classifier: classifier);

            Assert.DoesNotContain(sheet.Rows, r => r.Number == "1");
            Assert.DoesNotContain(sheet.Rows, r => r.Name == "AUTOMATICDOORCONTROLSANDOPERATORS");
            Assert.DoesNotContain(sheet.Rows, r => r.Aliases.Contains("Unmatched") || r.Aliases.Contains("Exact"));

            var womens = Assert.Single(sheet.Rows, r => r.Number == "13-23 17 13");
            Assert.Equal("Women's Restroom", womens.Name);
            Assert.Equal("WRR", womens.Aliases[0]);
            Assert.Contains("W Room", womens.Aliases);
            Assert.Equal("W Room", womens.Aliases[1]);

            var mens = Assert.Single(sheet.Rows, r => r.Number == "13-23 17 11");
            Assert.Contains("M Room", mens.Aliases);

            Assert.All(sheet.Rows.Where(r => r.IsUnmatched), r =>
            {
                Assert.Equal("n/a", r.Number);
                Assert.Equal("n/a", r.Name);
            });
            Assert.Contains(sheet.Rows, r => r.IsUnmatched && r.Aliases.Contains("Automatic Door Controls and Operators"));
            Assert.DoesNotContain(sheet.Rows.Where(r => r.IsUnmatched), r => r.Aliases.Contains("W Room"));
            Assert.DoesNotContain(sheet.Rows.Where(r => r.IsUnmatched), r => r.Aliases.Contains("M Room"));
        }

        [Fact]
        public void MergePromotesANameOffAnNaRowOntoItsTable13Row()
        {
            var existing = AliasSheet.FromRows(new[]
            {
                new[] { "Number", "Name", "Room Name 1" },
                new[] { "1", "AUTOMATICDOORCONTROLSANDOPERATORS", "W Room (1)", "Automatic Door Controls and Operators (1)" },
                new[] { "13-23 17 13", "Women's Restroom", "WRR" }
            });

            Assert.Contains(existing.Rows, r => r.IsUnmatched && r.Aliases.Contains("W Room"));

            var incoming = AliasSheet.FromTallies(
                RoomNameAudit.Tally(new[] { "W Room", "WRR" }),
                new RoomClassifier(Fixture.Restrooms()));

            existing.MergeUnique(incoming);

            var womens = Assert.Single(existing.Rows, r => r.Number == "13-23 17 13");
            Assert.Equal("WRR", womens.Aliases[0]);
            Assert.Contains("W Room", womens.Aliases);
            Assert.DoesNotContain(existing.Rows.Where(r => r.IsUnmatched), r => r.Aliases.Contains("W Room"));
        }

        [Fact]
        public void UnmatchedRowsAreNotTreatedAsAHeaderOnReload()
        {
            var sheet = AliasSheet.FromTallies(
                RoomNameAudit.Tally(new[] { "Corridor" }),
                new RoomClassifier(Fixture.Restrooms()));

            var writer = new StringWriter();
            sheet.Write(writer);
            var reloaded = AliasSheet.FromFile(WriteTemp(writer.ToString()));
            var unmatched = Assert.Single(reloaded.Rows);
            Assert.True(unmatched.IsUnmatched);
            Assert.Equal(new[] { "Corridor" }, unmatched.Aliases);
        }

        // The harvested ArchTemplate_Doors CSV: a leftover audit dump in row 2 claimed
        // "W Room", so Women's Restroom only kept WRR.
        private static readonly string[][] UploadedDoorsHarvest =
        {
            new[] { "Number", "Name", "Room Name 1", "Room Name 2", "Room Name 3", "Room Name 4", "Room Name 5", "Room Name 6", "Room Name 7", "Room Name 8", "Room Name 9" },
            new[] { "1", "AUTOMATICDOORCONTROLSANDOPERATORS", "Automatic Door Controls and Operators (1)", "Unmatched", "Forced Entry and Ballistic Resistant Door (1)", "M Room (1)", "Exact", "Men's Restroom", "Overhead Metal Doors (1)", "W Room (1)", "Women's Restroom" },
            new[] { "13-23 17 11", "Men's Restroom" },
            new[] { "13-23 17", "Restroom", "RRR" },
            new[] { "13-23 17 13", "Women's Restroom", "WRR" }
        };

        private static string WriteTemp(string text)
        {
            var path = Path.Combine(Path.GetTempPath(), "omniclass-alias-sheet-test.csv");
            File.WriteAllText(path, text);
            return path;
        }
    }
}
