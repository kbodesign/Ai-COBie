using System;
using System.IO;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Microsoft.Win32;
using OmniClass.Core.Audit;
using OmniClass.Core.Loading;
using OmniClass.Core.Matching;
using OmniClass.Revit.Rooms;

namespace OmniClass.Revit.Commands
{
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public sealed class HarvestNamesCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var document = commandData.Application.ActiveUIDocument?.Document;
            if (document == null)
            {
                message = "Open a project document first.";
                return Result.Failed;
            }

            try
            {
                return Run(document);
            }
            catch (Exception ex)
            {
                CommandSupport.Warn("Harvest names failed", ex.Message);
                message = ex.Message;
                return Result.Failed;
            }
        }

        private static Result Run(Document document)
        {
            var settings = CommandSupport.SettingsOrWarn();
            var rooms = RoomCollector.Collect(document, settings);
            var names = rooms
                .Where(r => !r.IsUnplaced)
                .Select(r => r.Name)
                .ToList();

            if (names.Count == 0)
            {
                CommandSupport.Inform("No rooms", "This project has no placed rooms to harvest.");
                return Result.Cancelled;
            }

            var tallies = RoomNameAudit.Tally(names);
            var dictionary = CommandSupport.TryLoadDictionary(settings, warnIfMissing: false, requireClean: false);
            var classifier = dictionary == null ? null : new RoomClassifier(dictionary);
            var incoming = AliasSheet.FromTallies(tallies, classifier);

            var dialog = new SaveFileDialog
            {
                Title = "Save or merge room names",
                Filter = "CSV (*.csv)|*.csv",
                FileName = SuggestedFileName(document),
                OverwritePrompt = false
            };

            if (dialog.ShowDialog() != true) return Result.Cancelled;

            int added;
            int already;
            if (File.Exists(dialog.FileName))
            {
                var existing = AliasSheet.FromFile(dialog.FileName, classifier: classifier);
                added = existing.MergeUnique(incoming);
                already = incoming.Rows.Sum(r => r.Aliases.Count) - added;
                if (already < 0) already = 0;
                existing.WriteFile(dialog.FileName);
            }
            else
            {
                incoming.WriteFile(dialog.FileName);
                added = incoming.Rows.Sum(r => r.Aliases.Count);
                already = 0;
            }

            CommandSupport.Inform(
                "Harvest saved",
                names.Count + " rooms harvested.\n" +
                added + " unique name" + (added == 1 ? "" : "s") + " written" +
                (already == 0 ? "." : "; " + already + " already on the sheet and were not repeated.") +
                "\n\nSaved to:\n" + dialog.FileName);

            return Result.Succeeded;
        }

        private static string SuggestedFileName(Document document)
        {
            var title = string.IsNullOrEmpty(document.Title) ? "rooms" : Path.GetFileNameWithoutExtension(document.Title);
            return title + " room names.csv";
        }
    }
}
