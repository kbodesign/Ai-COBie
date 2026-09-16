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
        }
    }
}
