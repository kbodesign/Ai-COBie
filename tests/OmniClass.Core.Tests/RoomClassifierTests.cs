using System.Linq;
using OmniClass.Core.Loading;
using OmniClass.Core.Matching;
using Xunit;

namespace OmniClass.Core.Tests
{
    public class RoomClassifierTests
    {
        private static RoomClassifier Restrooms() => new RoomClassifier(Fixture.Restrooms());

        [Theory]
        [InlineData("Restroom", "13-23 17")]
        [InlineData("RestRoom", "13-23 17")]
        [InlineData("Rest_Room", "13-23 17")]
        [InlineData("RR", "13-23 17")]
        [InlineData("R Room", "13-23 17")]
        [InlineData("Restroom 214", "13-23 17")]
        [InlineData("Men's Restroom", "13-23 17 11")]
        [InlineData("Mens Restroom", "13-23 17 11")]
        [InlineData("MR", "13-23 17 11")]
        [InlineData("M Room", "13-23 17 11")]
        [InlineData("Women's Restroom", "13-23 17 13")]
        [InlineData("WR", "13-23 17 13")]
        [InlineData("Unisex Restroom", "13-23 17 15")]
        [InlineData("NB", "13-23 17 15")]
        public void DictionaryHitsAreExactAndSafeToApply(string roomName, string expectedNumber)
        {
            var result = Restrooms().Classify(roomName);

            Assert.Equal(MatchStatus.Exact, result.Status);
            Assert.Equal(expectedNumber, result.Number);
            Assert.True(result.CanAutoApply);
            Assert.Equal(1d, result.Score);
        }

        [Fact]
        public void PluralIsOfferedForReviewRatherThanApplied()
        {
            var result = Restrooms().Classify("Restrooms");

            Assert.Equal(MatchStatus.Probable, result.Status);
            Assert.Equal("13-23 17", result.Number);
            Assert.False(result.CanAutoApply);
            Assert.True(result.NeedsReview);
        }

        [Fact]
        public void WordOrderDoesNotMatterAndTheSpecificClassificationWins()
        {
            var result = Restrooms().Classify("Restroom Mens");

            Assert.Equal("13-23 17 11", result.Number);
            Assert.Equal(MatchStatus.Probable, result.Status);

            // The generic parent is still in the running, just beaten.
            Assert.Contains(result.Candidates, c => c.Entry.Number.Canonical == "13-23 17");
        }

        [Fact]
        public void EquallyPlausibleRivalsComeBackAmbiguous()
        {
            var result = Restrooms().Classify("E Room");

            Assert.Equal(MatchStatus.Ambiguous, result.Status);
            Assert.False(result.CanAutoApply);
            Assert.True(result.Candidates.Count > 1);
        }

        [Fact]
        public void CharacterDriftAloneIsNotEnoughToBeACandidate()
        {
            // "BREAKROOM" is three edits from "RESTROOM", which scores respectably as a
            // ratio and is a completely different room.
            var result = Restrooms().Classify("Break Room");

            Assert.Equal(MatchStatus.Unmatched, result.Status);
            Assert.Empty(result.Candidates);
        }

        [Theory]
        [InlineData("Corridor")]
        [InlineData("Corridor 101")]
        [InlineData("Stair")]
        [InlineData("Storage")]
        [InlineData("Electrical Room")]
        [InlineData("Work Room")]
        [InlineData("Dining Room")]
        public void ShortAliasesNeverMatchUnrelatedRoomsBySubstring(string roomName)
        {
            // "RR" must not classify a corridor and "WR" must not classify a work room.
            var result = Restrooms().Classify(roomName);

            Assert.Equal(MatchStatus.Unmatched, result.Status);
            Assert.Equal(string.Empty, result.Number);
        }

        [Theory]
        [InlineData("WC")]
        [InlineData("DR")]
        [InlineData("BR")]
        public void TwoLetterCodesDoNotCrossMatchEachOther(string roomName)
        {
            Assert.Equal(MatchStatus.Unmatched, Restrooms().Classify(roomName).Status);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("101")]
        public void UnusableNamesAreUnmatched(string roomName)
        {
            var result = Restrooms().Classify(roomName);

            Assert.Equal(MatchStatus.Unmatched, result.Status);
            Assert.Null(result.Best);
        }

        [Fact]
        public void OnlyExactMatchesAreEverAutoApplied()
        {
            var results = Restrooms().ClassifyAll(new[] { "Restroom", "Restrooms", "E Room", "Corridor" });

            Assert.Equal(4, results.Count);
            Assert.All(results.Where(r => r.CanAutoApply), r => Assert.Equal(MatchStatus.Exact, r.Status));
            Assert.Single(results, r => r.CanAutoApply);
        }

        [Fact]
        public void ThresholdsAreTunable()
        {
            var strict = new RoomClassifier(Fixture.Restrooms(), new ClassifierOptions
            {
                ProbableThreshold = 0.99,
                ReviewThreshold = 0.95
            });

            Assert.Equal(MatchStatus.Unmatched, strict.Classify("Restrooms").Status);
        }

        [Theory]
        [InlineData("Toilet", "13-23 17")]
        [InlineData("WC", "13-23 17")]
        [InlineData("Gents", "13-23 17 11")]
        [InlineData("Ladies", "13-23 17 13")]
        [InlineData("All Gender Restroom", "13-23 17 15")]
        public void ShippedDictionaryCoversTheCommonSynonyms(string roomName, string expectedNumber)
        {
            var classifier = new RoomClassifier(AliasSheetLoader.LoadFile(Fixture.ShippedDictionaryPath));
            var result = classifier.Classify(roomName);

            Assert.Equal(MatchStatus.Exact, result.Status);
            Assert.Equal(expectedNumber, result.Number);
        }
    }
}
