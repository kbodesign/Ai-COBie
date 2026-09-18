using OmniClass.Core.Text;
using Xunit;

namespace OmniClass.Core.Tests
{
    public class RoomNameNormalizerTests
    {
        [Theory]
        [InlineData("Restroom")]
        [InlineData("RestRoom")]
        [InlineData("Rest_Room")]
        [InlineData("Rest Room")]
        [InlineData("REST-ROOM")]
        [InlineData("  restroom  ")]
        [InlineData("Restroom 101")]
        [InlineData("RESTROOM-2")]
        [InlineData("Restroom 101A")]
        public void CollapsesPunctuationCasingAndNumbering(string raw)
        {
            Assert.Equal("RESTROOM", RoomNameNormalizer.Key(raw));
        }

        [Theory]
        [InlineData("Men's Restroom")]
        [InlineData("Mens Restroom")]
        [InlineData("Men's-Restroom")]
        [InlineData("MENS RESTROOM")]
        public void DropsApostrophes(string raw)
        {
            Assert.Equal("MENSRESTROOM", RoomNameNormalizer.Key(raw));
        }

        [Fact]
        public void KeepsRunsOfInitialsIntact()
        {
            Assert.Equal("RR", RoomNameNormalizer.Key("RR"));
            Assert.Equal(new[] { "RR" }, RoomNameNormalizer.Tokens("RR"));
        }

        [Fact]
        public void SplitsLeadingInitialFromWord()
        {
            Assert.Equal(new[] { "R", "ROOM" }, RoomNameNormalizer.Tokens("RRoom"));
            Assert.Equal("RROOM", RoomNameNormalizer.Key("R Room"));
        }

        [Fact]
        public void SplitsCamelCaseIntoWords()
        {
            Assert.Equal(new[] { "REST", "ROOM" }, RoomNameNormalizer.Tokens("RestRoom"));
        }

        [Fact]
        public void SeparatesDigitsFromWords()
        {
            Assert.Equal(new[] { "CONFERENCE" }, RoomNameNormalizer.Tokens("Conference101"));
        }

        [Fact]
        public void NameThatIsOnlyAnIdentifierReducesToNothing()
        {
            // Better to return nothing and report the room as unmatched than to guess.
            Assert.Equal(string.Empty, RoomNameNormalizer.Key("101"));
            Assert.Equal(string.Empty, RoomNameNormalizer.Key("101A"));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("---")]
        public void HandlesEmptyAndPunctuationOnlyInput(string raw)
        {
            Assert.Equal(string.Empty, RoomNameNormalizer.Key(raw));
        }

        [Fact]
        public void StripsDiacritics()
        {
            Assert.Equal("CAFE", RoomNameNormalizer.Key("Caf\u00e9"));
        }

        [Fact]
        public void TrailingIdentifierStrippingCanBeDisabled()
        {
            var options = new NormalizerOptions { StripTrailingIdentifiers = false };
            Assert.Equal("RESTROOM101", RoomNameNormalizer.Key("Restroom 101", options));
        }
    }
}
