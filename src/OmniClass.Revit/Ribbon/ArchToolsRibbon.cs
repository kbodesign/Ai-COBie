using System;
using Autodesk.Revit.UI;
using OmniClass.Core.Configuration;
using RevitArgumentException = Autodesk.Revit.Exceptions.ArgumentException;

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

            EnsureTab(application, RibbonPlacement.Tab);

            foreach (var panel in application.GetRibbonPanels(RibbonPlacement.Tab))
            {
                if (string.Equals(panel.Name, RibbonPlacement.Panel, StringComparison.OrdinalIgnoreCase))
                    return panel;
            }

            try
            {
                return application.CreateRibbonPanel(RibbonPlacement.Tab, RibbonPlacement.Panel);
            }
            catch (RevitArgumentException)
            {
                foreach (var panel in application.GetRibbonPanels(RibbonPlacement.Tab))
                {
                    if (string.Equals(panel.Name, RibbonPlacement.Panel, StringComparison.OrdinalIgnoreCase))
                        return panel;
                }

                throw;
            }
        }

        private static void EnsureTab(UIControlledApplication application, string tab)
        {
            if (TabExists(application, tab)) return;

            try
            {
                application.CreateRibbonTab(tab);
            }
            catch (RevitArgumentException)
            {
                // Another add-in created the tab between the check and the create.
                // Revit throws Autodesk.Revit.Exceptions.ArgumentException here, which
                // does not inherit from System.ArgumentException, so catching the
                // system type lets the add-in fail to load.
            }
        }

        private static bool TabExists(UIControlledApplication application, string tab)
        {
            try
            {
                application.GetRibbonPanels(tab);
                return true;
            }
            catch (RevitArgumentException)
            {
                return false;
            }
        }
    }
}
