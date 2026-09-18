using Autodesk.Revit.UI;
using OmniClass.Core.Configuration;
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
                RibbonPlacement.ClassifyButton,
                assembly,
                typeof(ClassifyRoomsCommand).FullName)
            {
                ToolTip = "Match room names to OmniClass Table 13 and assign the same values as " +
                          "Interoperability → Assign Classification: Classification.Space.Number, " +
                          "Classification.Space.Description, COBie.Space.Category, and ClassificationCode.",
                LongDescription = "Reads the alias dictionary next to this add-in, classifies " +
                                  "every placed room in the model, and shows a preview. Only " +
                                  "checked rows are written. Unplaced rooms, rooms owned by " +
                                  "another user, and rooms that already have a value are left alone " +
                                  "unless overwrite is turned on in OmniClass.Rooms.config.",
                AvailabilityClassName = availability,
                LargeImage = RibbonIcons.Load("classify32.png"),
                Image = RibbonIcons.Load("classify16.png")
            };

            var export = new PushButtonData(
                "OmniClassExportRooms",
                RibbonPlacement.ExportButton,
                assembly,
                typeof(ExportRoomsCommand).FullName)
            {
                ToolTip = "Export Room Number, Name, OmniClass Number, and OmniClass Name to a CSV file.",
                LongDescription = "Writes one row per room. OmniClass columns come from " +
                                  "Classification.Space.* when already filled, otherwise from a " +
                                  "dictionary match. Edit the names in Excel for consistency, then " +
                                  "use Import Rooms to apply them.",
                AvailabilityClassName = availability,
                LargeImage = RibbonIcons.Load("export32.png"),
                Image = RibbonIcons.Load("export16.png")
            };

            var import = new PushButtonData(
                "OmniClassImportRooms",
                RibbonPlacement.ImportButton,
                assembly,
                typeof(ImportRoomsCommand).FullName)
            {
                ToolTip = "Import a room CSV to reuse names and, when COBie is on the project, " +
                          "write OmniClass classifications.",
                LongDescription = "Matches by Room Number, then by Name. Checked rows update the " +
                                  "room name for consistency. If Classification.Space.* or " +
                                  "COBie.Space.Category is on the room, OmniClass Number and Name " +
                                  "are written the same way as Assign Classification.",
                AvailabilityClassName = availability,
                LargeImage = RibbonIcons.Load("import32.png"),
                Image = RibbonIcons.Load("import16.png")
            };

            panel.AddItem(classify);
            panel.AddSeparator();
            panel.AddStackedItems(export, import);

            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication application) => Result.Succeeded;
    }
}
