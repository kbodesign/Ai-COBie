using System;
using System.IO;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Microsoft.Win32;
using OmniClass.Core.Audit;
using OmniClass.Core.Matching;
using OmniClass.Core.Reporting;
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

            var dialog = new SaveFileDialog
            {
                Title = "Save room name harvest",
                Filter = "CSV (*.csv)|*.csv",
                FileName = SuggestedFileName(document),
                OverwritePrompt = true
            };

            if (dialog.ShowDialog() != true) return Result.Cancelled;

            using (var writer = new StreamWriter(dialog.FileName))
            {
                ReportWriter.WriteAudit(writer, tallies, classifier);
            }

            var unmatched = classifier == null
                ? 0
                : tallies.Count(t => classifier.Classify(t.MostCommonVariant).Status == MatchStatus.Unmatched);

            var top = tallies
                .Where(t => classifier == null || classifier.Classify(t.MostCommonVariant).Status == MatchStatus.Unmatched)
                .Take(8)
                .Select(t => "  " + t.Count + "  " + t.MostCommonVariant);

            CommandSupport.Inform(
                "Harvest saved",
                names.Count + " rooms, " + tallies.Count + " distinct names after normalizing.\n" +
                (classifier == null
                    ? "No dictionary loaded, so the report has names only."
                    : unmatched + " distinct names have no classification yet.") +
                "\n\nSaved to:\n" + dialog.FileName +
                (top.Any() ? "\n\nMost common names to add next:\n" + string.Join("\n", top) : ""));

            return Result.Succeeded;
        }

        private static string SuggestedFileName(Document document)
        {
            var title = string.IsNullOrEmpty(document.Title) ? "rooms" : Path.GetFileNameWithoutExtension(document.Title);
            return title + " room names.csv";
        }
    }
}
