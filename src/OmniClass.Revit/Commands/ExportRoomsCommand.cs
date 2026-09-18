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

            var incoming = RoomTransferSheet.Unique(rooms.Select(room => ToTransfer(room, classifier)));

            var dialog = new SaveFileDialog
            {
                Title = "Export rooms — unique names (append to an existing master list)",
                Filter = "CSV (*.csv)|*.csv",
                FileName = "master rooms.csv",
                OverwritePrompt = false
            };

            if (dialog.ShowDialog() != true) return Result.Cancelled;

            if (File.Exists(dialog.FileName))
            {
                var raw = OmniClass.Core.Io.DelimitedText.ParseFile(dialog.FileName);
                if (RoomTransferSheet.LooksLikeAliasDictionary(raw))
                {
                    CommandSupport.Warn(
                        "Not a room export",
                        "That file looks like the alias dictionary (Number, Name, Room Name 1, …).\n\n" +
                        "Pick the CSV from Export Rooms, or a new file, to build a master list.");
                    return Result.Cancelled;
                }

                var merged = RoomTransferSheet.MergeUnique(RoomTransferSheet.FromRows(raw), incoming);
                RoomTransferSheet.WriteFile(dialog.FileName, merged.Rows);

                CommandSupport.Inform(
                    "Master list updated",
                    incoming.Count + " unique name" + (incoming.Count == 1 ? "" : "s") + " in this project.\n" +
                    merged.Added + " added to the master list" +
                    (merged.FilledClassification == 0
                        ? ""
                        : ", " + merged.FilledClassification + " existing name" +
                          (merged.FilledClassification == 1 ? "" : "s") + " filled in with OmniClass") +
                    (merged.AlreadyPresent == 0
                        ? "."
                        : ", " + merged.AlreadyPresent + " already on the list.") +
                    "\n\n" + merged.Rows.Count + " unique names in:\n" + dialog.FileName);
            }
            else
            {
                RoomTransferSheet.WriteFile(dialog.FileName, incoming);
                CommandSupport.Inform(
                    "Rooms exported",
                    incoming.Count + " unique name" + (incoming.Count == 1 ? "" : "s") + " written.\n" +
                    incoming.Count(r => r.HasOmniClass) + " with an OmniClass number and name.\n\n" +
                    "Save to the same file from other projects to append unique names only.\n\n" +
                    "Saved to:\n" + dialog.FileName);
            }

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
                Name = room.Name ?? string.Empty,
                OmniClassNumber = number,
                OmniClassName = title
            };
        }
    }
}
