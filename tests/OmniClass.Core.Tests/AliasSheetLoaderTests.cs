using System.Linq;
using OmniClass.Core.Loading;
using OmniClass.Core.Model;
using OmniClass.Core.Text;
using Xunit;

namespace OmniClass.Core.Tests
{
    public class AliasSheetLoaderTests
    {
        [Fact]
        public void UnpivotsWideColumnsIntoOneAliasPerRow()
        {
            var dictionary = Fixture.Restrooms();

            Assert.Equal(4, dictionary.Entries.Count);
            Assert.False(dictionary.HasErrors);

            var mens = dictionary.FindByNumber("13-23 17 11");
            Assert.Equal("Men's Restroom", mens.Title);

            foreach (var alias in new[] { "MR", "M Room", "Men's Restroom" })
            {
                var match = dictionary.FindExact(RoomNameNormalizer.Key(alias));
                Assert.Equal("13-23 17 11", match.Entry.Number.Canonical);
            }
        }

        [Fact]
        public void TitleIsAlwaysAnAliasEvenWhenNotRepeatedInAnAliasColumn()
        {
            var dictionary = Fixture.Load(
                "Number,Title,Level\n" +
                "13-23 17,Restroom,3\n");

            Assert.NotNull(dictionary.FindExact("RESTROOM"));
        }

        [Fact]
        public void NormalizationCollapsesTheRedundantAliasColumns()
        {
            var dictionary = Fixture.Restrooms();

            // "Restroom", "RestRoom" and "Rest_Room" are one word with three punctuation
            // habits, so two of the four authored cells on that row earn their keep.
            var redundant = dictionary.Messages
                .Where(m => m.Code == "RedundantAlias")
                .ToList();

            Assert.All(redundant, m => Assert.Equal(ValidationSeverity.Info, m.Severity));
            Assert.Equal(8, redundant.Count);
        }

        [Fact]
        public void ConflictingAliasIsAnError()
        {
            var dictionary = Fixture.Load(
                "Number,Title,Level,Alias 1\n" +
                "13-23 17 11,Men's Restroom,4,Rest Room\n" +
                "13-23 17 13,Women's Restroom,4,Rest_Room\n");

            var conflict = Assert.Single(dictionary.Messages, m => m.Code == "ConflictingAlias");
            Assert.Equal(ValidationSeverity.Error, conflict.Severity);
            Assert.Equal("D3", conflict.Location);
            Assert.True(dictionary.HasErrors);
        }

        [Fact]
        public void AliasThatNormalizesToNothingIsReported()
        {
            var dictionary = Fixture.Load(
                "Number,Title,Level,Alias 1\n" +
                "13-23 17,Restroom,3,\"- -\"\n");

            Assert.Contains(dictionary.Messages, m => m.Code == "EmptyAlias" && m.Severity == ValidationSeverity.Warning);
        }

        [Fact]
        public void BareRoomNumberIsNeverUsableAsAnAlias()
        {
            const string sheet =
                "Number,Title,Level,Alias 1\n" +
                "13-23 17,Restroom,3,101\n";

            // With the default options the digits are stripped and nothing is left.
            var stripped = Fixture.Load(sheet);
            Assert.Contains(stripped.Messages, m => m.Code == "EmptyAlias");
            Assert.Null(stripped.FindExact("101"));

            // With stripping disabled the alias survives normalization, so the numeric
            // guard is what stops rooms being classified by their room number.
            var kept = Fixture.Load(sheet, new NormalizerOptions { StripTrailingIdentifiers = false });
            Assert.Contains(kept.Messages, m => m.Code == "NumericAlias" && m.Severity == ValidationSeverity.Error);
            Assert.Null(kept.FindExact("101"));
        }

        [Fact]
        public void DuplicateNumberIsAnErrorAndTheSecondRowIsDropped()
        {
            var dictionary = Fixture.Load(
                "Number,Title,Level\n" +
                "13-23 17,Restroom,3\n" +
                "13-23 17,Toilet Room,3\n");

            Assert.Contains(dictionary.Messages, m => m.Code == "DuplicateNumber");
            Assert.Equal("Restroom", dictionary.FindByNumber("13-23 17").Title);
        }

        [Fact]
        public void MalformedNumberDropsTheRowWithACellReference()
        {
            var dictionary = Fixture.Load(
                "Number,Title,Level\n" +
                "13/23/2017,Restroom,3\n");

            var message = Assert.Single(dictionary.Messages, m => m.Code == "MalformedNumber");
            Assert.Equal("A2", message.Location);
            Assert.Empty(dictionary.Entries);
        }

        [Fact]
        public void MissingTitleIsAnError()
        {
            var dictionary = Fixture.Load(
                "Number,Title,Level\n" +
                "13-23 17,,3\n");

            Assert.Contains(dictionary.Messages, m => m.Code == "MissingTitle");
        }

        [Fact]
        public void LevelColumnIsCrossCheckedNotTrusted()
        {
            var dictionary = Fixture.Load(
                "Number,Title,Level\n" +
                "13-23 17 11,Men's Restroom,3\n");

            var message = Assert.Single(dictionary.Messages, m => m.Code == "LevelMismatch");
            Assert.Equal(ValidationSeverity.Warning, message.Severity);
            Assert.Equal(4, dictionary.FindByNumber("13-23 17 11").Number.Depth);
        }

        [Fact]
        public void MissingParentIsAWarning()
        {
            var dictionary = Fixture.Load(
                "Number,Title,Level\n" +
                "13-23 17 11,Men's Restroom,4\n");

            Assert.Contains(dictionary.Messages, m => m.Code == "MissingParent" && m.Severity == ValidationSeverity.Warning);
        }

        [Fact]
        public void SkipsBlankRowsAndToleratesRaggedRows()
        {
            var dictionary = Fixture.Load(
                "Number,Title,Level,Alias 1,Alias 2\n" +
                "13-23 17,Restroom,3,RR\n" +
                "\n" +
                ",,,,\n" +
                "13-23 17 11,Men's Restroom,4,MR,M Room\n");

            Assert.Equal(2, dictionary.Entries.Count);
            Assert.False(dictionary.HasErrors);
        }

        [Fact]
        public void WorksWithoutAHeaderRow()
        {
            var dictionary = Fixture.Load("13-23 17,Restroom,3,RR\n");
            Assert.Single(dictionary.Entries);
        }

        [Theory]
        [InlineData(0, "A")]
        [InlineData(3, "D")]
        [InlineData(6, "G")]
        [InlineData(25, "Z")]
        [InlineData(26, "AA")]
        public void ColumnNamesMatchSpreadsheetLetters(int index, string expected)
        {
            Assert.Equal(expected, AliasSheetLoader.ColumnName(index));
        }

        [Fact]
        public void ShippedDictionaryHasNoErrors()
        {
            var dictionary = AliasSheetLoader.LoadFile(Fixture.ShippedDictionaryPath);

            var errors = dictionary.Messages
                .Where(m => m.Severity == ValidationSeverity.Error)
                .Select(m => m.ToString())
                .ToList();

            Assert.Empty(errors);
            Assert.NotEmpty(dictionary.Entries);
        }
    }
}
