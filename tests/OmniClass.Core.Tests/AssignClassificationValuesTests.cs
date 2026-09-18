using OmniClass.Core.Model;
using Xunit;

namespace OmniClass.Core.Tests
{
    public class AssignClassificationValuesTests
    {
        [Fact]
        public void MatchesWhatAssignClassificationWritesForASpace()
        {
            var values = AssignClassificationValues.ForSpace("13-23 17 11", "Men's Restroom");

            Assert.Equal("13-23 17 11", Value(values, "Classification.Space.Number"));
            Assert.Equal("Men's Restroom", Value(values, "Classification.Space.Description"));
            Assert.Equal("13-23 17 11: Men's Restroom", Value(values, "COBie.Space.Category"));
            Assert.Equal(
                "[OmniClass Table 13]13-23 17 11: Men's Restroom",
                Value(values, "ClassificationCode"));
        }

        [Fact]
        public void EmptyNumberDoesNotInventACode()
        {
            Assert.Equal(string.Empty, AssignClassificationValues.IfcClassificationCode("", ""));
        }

        private static string Value(System.Collections.Generic.IReadOnlyList<System.Collections.Generic.KeyValuePair<string, string>> pairs, string name)
        {
            foreach (var pair in pairs)
            {
                if (pair.Key == name) return pair.Value;
            }

            return null;
        }
    }
}
