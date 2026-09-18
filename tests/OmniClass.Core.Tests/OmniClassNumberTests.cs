using OmniClass.Core.Model;
using Xunit;

namespace OmniClass.Core.Tests
{
    public class OmniClassNumberTests
    {
        [Theory]
        [InlineData("13-23 17", "13-23 17")]
        [InlineData("13-23-17", "13-23 17")]
        [InlineData("13.23.17", "13-23 17")]
        [InlineData("  13-23 17 11  ", "13-23 17 11")]
        [InlineData("13", "13")]
        public void AcceptsCommonSpellingsAndCanonicalizes(string raw, string expected)
        {
            Assert.True(OmniClassNumber.TryParse(raw, out var number, out var error), error);
            Assert.Equal(expected, number.Canonical);
        }

        [Theory]
        [InlineData("13-23 17", 3)]
        [InlineData("13-23 17 11", 4)]
        [InlineData("13-23", 2)]
        [InlineData("13", 1)]
        public void DepthCountsTheTableAsLevelOne(string raw, int expected)
        {
            Assert.Equal(expected, OmniClassNumber.Parse(raw).Depth);
        }

        [Fact]
        public void ParentDropsTheLastGroup()
        {
            Assert.Equal("13-23 17", OmniClassNumber.Parse("13-23 17 11").Parent.Canonical);
            Assert.Equal("13-23", OmniClassNumber.Parse("13-23 17").Parent.Canonical);
            Assert.Null(OmniClassNumber.Parse("13").Parent);
        }

        [Fact]
        public void RecognizesDescendants()
        {
            var restroom = OmniClassNumber.Parse("13-23 17");
            var mens = OmniClassNumber.Parse("13-23 17 11");
            var womens = OmniClassNumber.Parse("13-23 17 13");

            Assert.True(mens.IsDescendantOf(restroom));
            Assert.False(restroom.IsDescendantOf(mens));
            Assert.False(mens.IsDescendantOf(womens));
            Assert.False(mens.IsDescendantOf(mens));
        }

        [Fact]
        public void RejectsExcelDateMangling()
        {
            Assert.False(OmniClassNumber.TryParse("13/23/2017", out _, out var error));
            Assert.Contains("date", error);
            Assert.Contains("Text", error);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("23-17", "not a Table 13 number")]
        [InlineData("13-233 17", "two digits")]
        [InlineData("13-23 17 Restroom", "letters")]
        public void RejectsMalformedNumbers(string raw, string expectedFragment = null)
        {
            Assert.False(OmniClassNumber.TryParse(raw, out _, out var error));
            Assert.False(string.IsNullOrEmpty(error));
            if (expectedFragment != null) Assert.Contains(expectedFragment, error);
        }
    }
}
