using System;
using Autodesk.Revit.UI;
using OmniClass.Core.Configuration;

namespace OmniClass.Revit.Ribbon
{
    /// <summary>
    /// Lands on the firm's Arch Tools tab so classification sits with the other
    /// architectural tools instead of on the generic Add-Ins tab. If the tab or the
    /// Room Data panel already exists (another add-in created them), they are reused
    /// rather than duplicated.
    /// </summary>
    internal static class ArchToolsRibbon
    {
        public static RibbonPanel GetOrCreatePanel(UIControlledApplication application)
        {
            if (application == null) throw new ArgumentNullException(nameof(application));

            try
            {
                application.CreateRibbonTab(RibbonPlacement.Tab);
            }
            catch (ArgumentException)
            {
                // Tab already exists - that is the common case once other Arch Tools are installed.
            }

            foreach (var panel in application.GetRibbonPanels(RibbonPlacement.Tab))
            {
                if (string.Equals(panel.Name, RibbonPlacement.Panel, StringComparison.OrdinalIgnoreCase))
                    return panel;
            }

            return application.CreateRibbonPanel(RibbonPlacement.Tab, RibbonPlacement.Panel);
        }
    }
}
