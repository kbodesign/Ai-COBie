using System;
using Autodesk.Revit.UI;

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
        public const string TabName = RibbonNames.Tab;
        public const string PanelName = RibbonNames.Panel;

        public static RibbonPanel GetOrCreatePanel(UIControlledApplication application)
        {
            if (application == null) throw new ArgumentNullException(nameof(application));

            try
            {
                application.CreateRibbonTab(RibbonNames.Tab);
            }
            catch (ArgumentException)
            {
                // Tab already exists - that is the common case once other Arch Tools are installed.
            }

            foreach (var panel in application.GetRibbonPanels(RibbonNames.Tab))
            {
                if (string.Equals(panel.Name, RibbonNames.Panel, StringComparison.OrdinalIgnoreCase))
                    return panel;
            }

            return application.CreateRibbonPanel(RibbonNames.Tab, RibbonNames.Panel);
        }
    }
}
