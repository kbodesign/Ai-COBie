using OmniClass.Core.Matching;
using Xunit;

namespace OmniClass.Core.Tests
{
    public class ApplyPolicyTests
    {
        [Fact]
        public void ExactWritableHitIsChecked()
        {
            Assert.True(ApplyPolicy.DefaultChecked(
                canAutoApply: true, alreadyClassified: false, overwriteExisting: false, isWritable: true));
        }

        [Fact]
        public void ExistingValueIsLeftAloneUnlessOverwriteIsOn()
        {
            Assert.False(ApplyPolicy.DefaultChecked(
                canAutoApply: true, alreadyClassified: true, overwriteExisting: false, isWritable: true));

            Assert.True(ApplyPolicy.DefaultChecked(
                canAutoApply: true, alreadyClassified: true, overwriteExisting: true, isWritable: true));
        }

        [Fact]
        public void ReviewAndUnwritableRowsStayUnchecked()
        {
            Assert.False(ApplyPolicy.DefaultChecked(
                canAutoApply: false, alreadyClassified: false, overwriteExisting: false, isWritable: true));

            Assert.False(ApplyPolicy.DefaultChecked(
                canAutoApply: true, alreadyClassified: false, overwriteExisting: false, isWritable: false));
        }

        [Theory]
        [InlineData("13-23 17", "", true)]
        [InlineData("", "Restroom", true)]
        [InlineData("  ", "  ", false)]
        [InlineData("", "", false)]
        public void ExistingClassificationLooksAtEitherParameter(string number, string title, bool expected)
        {
            Assert.Equal(expected, ApplyPolicy.HasExistingClassification(number, title));
        }

        [Fact]
        public void ReasonNamesTheBlockerFirst()
        {
            Assert.Equal("Unplaced", ApplyPolicy.Reason(true, false, false, null, false, false, MatchStatus.Exact));
            Assert.Equal("Owned by Ada", ApplyPolicy.Reason(false, false, true, "Ada", false, false, MatchStatus.Exact));
            Assert.Equal("Already classified", ApplyPolicy.Reason(false, false, false, null, true, false, MatchStatus.Exact));
            Assert.Equal("Needs review", ApplyPolicy.Reason(false, false, false, null, false, false, MatchStatus.Probable));
        }
    }
}
