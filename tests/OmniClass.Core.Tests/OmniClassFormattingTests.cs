using OmniClass.Core.Model;
using Xunit;

namespace OmniClass.Core.Tests
{
    public class OmniClassFormattingTests
    {
        [Fact]
        public void CategoryJoinsNumberAndName()
        {
            Assert.Equal("13-23 17 11: Men's Restroom",
                OmniClassFormatting.Category("13-23 17 11", "Men's Restroom"));
        }

        [Theory]
        [InlineData("13-23 17", "", "13-23 17")]
        [InlineData("", "Restroom", "Restroom")]
        [InlineData("  ", "  ", "")]
        public void CategorySurvivesAMissingHalf(string number, string title, string expected)
        {
            Assert.Equal(expected, OmniClassFormatting.Category(number, title));
        }
    }
}
