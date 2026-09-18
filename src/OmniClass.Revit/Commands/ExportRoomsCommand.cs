using System;
using System.IO;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Microsoft.Win32;
using OmniClass.Core.Matching;
using OmniClass.Core.Transfer;
using OmniClass.Revit.Rooms;

namespace OmniClass.Revit.Commands
{
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public sealed class ExportRoomsCommand : IExternalCommand
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
                CommandSupport.Warn("Export rooms failed", ex.Message);
                message = ex.Message;
                return Result.Failed;
            }
        }

        private static Result Run(Document document)
        {
            var settings = CommandSupport.SettingsOrWarn();
            var rooms = RoomCollector.Collect(document, settings);
            if (rooms.Count == 0)
            {
                CommandSupport.Inform("No rooms", "This project has no room elements to export.");
                return Result.Cancelled;
            }

            var dictionary = CommandSupport.TryLoadDictionary(settings, warnIfMissing: false, requireClean: false);
            var classifier = dictionary == null ? null : new RoomClassifier(dictionary);

            var rows = rooms.Select(room => ToTransfer(room, classifier)).ToList();

            var dialog = new SaveFileDialog
            {
                Title = "Export rooms",
                Filter = "CSV (*.csv)|*.csv",
                FileName = SuggestedFileName(document),
                OverwritePrompt = true
            };

            if (dialog.ShowDialog() != true) return Result.Cancelled;

            RoomTransferSheet.WriteFile(dialog.FileName, rows);

            var classified = rows.Count(r => r.HasOmniClass);
            CommandSupport.Inform(
                "Rooms exported",
                rooms.Count + " room" + (rooms.Count == 1 ? "" : "s") + " written.\n" +
                classified + " with an OmniClass number and name.\n\n" +
                "Columns: Room Number, Name, OmniClass Number, OmniClass Name.\n" +
                "Edit names for consistency, then use Import Rooms to apply them.\n\n" +
                "Saved to:\n" + dialog.FileName);

            return Result.Succeeded;
        }

        private static RoomTransferRow ToTransfer(RoomSnapshot room, RoomClassifier classifier)
        {
            var number = room.CurrentNumber ?? string.Empty;
            var title = room.CurrentTitle ?? string.Empty;

            if (string.IsNullOrWhiteSpace(number) && classifier != null)
            {
                var result = classifier.Classify(room.Name);
                if (result != null && result.Status != MatchStatus.Unmatched && !string.IsNullOrEmpty(result.Number))
                {
                    number = result.Number;
                    title = result.Title;
                }
            }

            return new RoomTransferRow
            {
                RoomNumber = room.Number ?? string.Empty,
                Name = room.Name ?? string.Empty,
                OmniClassNumber = number,
                OmniClassName = title
            };
        }

        private static string SuggestedFileName(Document document)
        {
            var title = string.IsNullOrEmpty(document.Title) ? "rooms" : Path.GetFileNameWithoutExtension(document.Title);
            return title + " rooms.csv";
        }
    }
}
