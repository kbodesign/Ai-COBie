using OmniClass.Core.Configuration;
using Xunit;

namespace OmniClass.Core.Tests
{
    public class RibbonPlacementTests
    {
        [Fact]
        public void LandsOnArchToolsRoomData()
        {
            Assert.Equal("Arch Tools", RibbonPlacement.Tab);
            Assert.Equal("Room Data", RibbonPlacement.Panel);
            Assert.Equal("Classify\nRooms", RibbonPlacement.ClassifyButton);
            Assert.Equal("Export\nRooms", RibbonPlacement.ExportButton);
            Assert.Equal("Import\nRooms", RibbonPlacement.ImportButton);
            Assert.Equal("Key\nSchedule", RibbonPlacement.KeyScheduleButton);
        }
    }
}
