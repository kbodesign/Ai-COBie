using Autodesk.Revit.UI;
using OmniClass.Revit.Commands;
using OmniClass.Revit.Ribbon;

namespace OmniClass.Revit
{
    /// <summary>
    /// Registers the Arch Tools / Room Data panel. Commands themselves are invoked from
    /// the push buttons, so they do not need their own .addin entries.
    /// </summary>
    public sealed class App : IExternalApplication
    {
        public Result OnStartup(UIControlledApplication application)
        {
            var panel = ArchToolsRibbon.GetOrCreatePanel(application);
            var assembly = GetType().Assembly.Location;
            var availability = typeof(ProjectDocumentAvailability).FullName;

            var classify = new PushButtonData(
                "OmniClassClassifyRooms",
                "Classify\nRooms",
                assembly,
                typeof(ClassifyRoomsCommand).FullName)
            {
                ToolTip = "Match room names to OmniClass Table 13 and fill " +
                          "Classification.Space.Number, Classification.Space.Description, and " +
                          "COBie.Space.Category. Exact matches are pre-selected; everything else waits for review.",
                LongDescription = "Reads the alias dictionary next to this add-in, classifies " +
                                  "every placed room in the model, and shows a preview. Only " +
                                  "checked rows are written. Unplaced rooms, rooms owned by " +
                                  "another user, and rooms that already have a value are left alone " +
                                  "unless overwrite is turned on in OmniClass.Rooms.config.",
                AvailabilityClassName = availability,
                LargeImage = RibbonIcons.Load("classify32.png"),
                Image = RibbonIcons.Load("classify16.png")
            };

            var harvest = new PushButtonData(
                "OmniClassHarvestNames",
                "Harvest\nNames",
                assembly,
                typeof(HarvestNamesCommand).FullName)
            {
                ToolTip = "Export the distinct room names in this model, ranked by how often they occur.",
                LongDescription = "Builds the evidence base for the alias dictionary. The CSV is " +
                                  "the list to curate: add the frequent unmatched names to the " +
                                  "sheet and the next Classify run will pick them up.",
                AvailabilityClassName = availability,
                LargeImage = RibbonIcons.Load("harvest32.png"),
                Image = RibbonIcons.Load("harvest16.png")
            };

            panel.AddItem(classify);
            panel.AddSeparator();
            panel.AddItem(harvest);

            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication application) => Result.Succeeded;
    }
}
