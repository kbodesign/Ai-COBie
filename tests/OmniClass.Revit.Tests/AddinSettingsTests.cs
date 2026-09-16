using System.IO;
using OmniClass.Revit.Parameters;
using OmniClass.Revit.Ribbon;
using OmniClass.Revit.Settings;
using Xunit;

namespace OmniClass.Revit.Tests
{
    public class AddinSettingsTests
    {
        [Fact]
        public void DefaultsPointAtTheShippedDictionary()
        {
            var folder = Path.Combine(Path.GetTempPath(), "OmniClassRooms");
            var settings = AddinSettings.LoadFrom("", folder);

            Assert.Equal(Path.Combine(folder, "data", "room_aliases.csv"), settings.DictionaryPath);
            Assert.Equal("OmniClass Number", settings.NumberParameterName);
            Assert.Equal("OmniClass Title", settings.TitleParameterName);
            Assert.False(settings.OverwriteExisting);
        }

        [Fact]
        public void ResolvesRelativePathsAgainstTheAddinFolder()
        {
            var folder = Path.Combine(Path.GetTempPath(), "OmniClassRooms");
            var settings = AddinSettings.LoadFrom(
                "# comment\n" +
                "dictionary = data/custom.csv\n" +
                "numberParameter = OC Number\n" +
                "titleParameter = OC Title\n" +
                "overwriteExisting = true\n",
                folder);

            Assert.Equal(Path.GetFullPath(Path.Combine(folder, "data", "custom.csv")), settings.DictionaryPath);
            Assert.Equal("OC Number", settings.NumberParameterName);
            Assert.Equal("OC Title", settings.TitleParameterName);
            Assert.True(settings.OverwriteExisting);
        }

        [Fact]
        public void SharedParameterFileDefaultsBesideTheDictionary()
        {
            var addinFolder = Path.Combine(Path.GetTempPath(), "addin");
            var dictionary = Path.Combine(Path.GetTempPath(), "office", "room_aliases.csv");
            var settings = AddinSettings.LoadFrom("dictionary = " + dictionary + "\n", addinFolder);

            Assert.Equal(
                Path.Combine(Path.GetTempPath(), "office", "OmniClass Shared Parameters.txt"),
                settings.EffectiveSharedParameterFile());
        }

        [Fact]
        public void ParameterGuidsAreStable()
        {
            Assert.Equal("a7e4c2b1-5d8f-4a3e-9c12-6b8d0e4f1a73", OmniClassParameterIds.Number.ToString());
            Assert.Equal("b8f5d3c2-6e90-4b4f-8d23-7c9e1f5a2b84", OmniClassParameterIds.Title.ToString());
        }
    }

    public class RibbonPlacementTests
    {
        [Fact]
        public void LandsOnArchToolsRoomData()
        {
            Assert.Equal("Arch Tools", RibbonNames.Tab);
            Assert.Equal("Room Data", RibbonNames.Panel);
        }
    }
}
