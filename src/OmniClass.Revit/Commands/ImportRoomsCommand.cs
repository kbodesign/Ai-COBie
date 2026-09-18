using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using Microsoft.Win32;
using OmniClass.Core.Io;
using OmniClass.Core.Transfer;
using OmniClass.Revit.Rooms;
using OmniClass.Revit.Ui;

namespace OmniClass.Revit.Commands
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public sealed class ImportRoomsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var uiapp = commandData.Application;
            var document = uiapp.ActiveUIDocument?.Document;
            if (document == null)
            {
                message = "Open a project document first.";
                return Result.Failed;
            }

            try
            {
                return Run(uiapp, document);
            }
            catch (Exception ex)
            {
                CommandSupport.Warn("Import rooms failed", ex.Message);
                message = ex.Message;
                return Result.Failed;
            }
        }

        private static Result Run(UIApplication uiapp, Document document)
        {
            var dialog = new OpenFileDialog
            {
                Title = "Import rooms",
                Filter = "CSV (*.csv)|*.csv"
            };

            if (dialog.ShowDialog() != true) return Result.Cancelled;

            var raw = DelimitedText.ParseFile(dialog.FileName);
            if (RoomTransferSheet.LooksLikeAliasDictionary(raw))
            {
                CommandSupport.Warn(
                    "Not a room export",
                    "That file looks like the alias dictionary (Number, Name, Room Name 1, …).\n\n" +
                    "Use a file from Export Rooms: Name, OmniClass Number, OmniClass Name.");
                return Result.Cancelled;
            }

            var imported = RoomTransferSheet.FromRows(raw);
            if (imported.Count == 0)
            {
                CommandSupport.Inform("Nothing to import", "The CSV has no room rows.");
                return Result.Cancelled;
            }

            var settings = CommandSupport.SettingsOrWarn();
            var rooms = RoomCollector.Collect(document, settings);
            var refs = rooms.Select(ToRef).ToList();
            var cobieOnProject = refs.Any(r => r.HasCobieParameters);

            var actions = RoomImportPlanner.Plan(imported, refs);
            var rows = actions.Select(a => new ImportRow(a)).ToList();

            var window = new ImportWindow(rows);
            new WindowInteropHelper(window) { Owner = uiapp.MainWindowHandle };

            if (window.ShowDialog() != true || !window.ApplyConfirmed)
                return Result.Cancelled;

            var selected = window.CheckedRows.ToList();
            if (selected.Count == 0)
            {
                CommandSupport.Inform("Nothing to apply", "No writable rows were checked.");
                return Result.Cancelled;
            }

            var namesUpdated = 0;
            var classified = 0;
            var skipped = 0;
            var failures = new System.Collections.Generic.List<string>();

            using (var write = new Transaction(document, "OmniClass: import rooms"))
            {
                write.Start();

                foreach (var row in selected)
                {
                    if (!long.TryParse(row.RoomId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
                    {
                        skipped++;
                        continue;
                    }

                    var element = document.GetElement(RoomCollector.ToElementId(id));
                    var room = element as Room;
                    if (room == null)
                    {
                        skipped++;
                        failures.Add((row.RoomNumber + " " + row.ImportName).Trim() + ": room is no longer in the model.");
                        continue;
                    }

                    try
                    {
                        if (row.Action.UpdateName && ClassificationWriter.TrySetRoomName(room, row.ImportName))
                            namesUpdated++;

                        if (row.Action.UpdateClassification
                            && ClassificationWriter.TryWriteClassification(
                                room,
                                settings,
                                row.OmniClassNumber,
                                row.OmniClassName))
                            classified++;
                    }
                    catch (Exception ex)
                    {
                        skipped++;
                        failures.Add((row.RoomNumber + " " + row.ImportName).Trim() + ": " + ex.Message);
                    }
                }

                write.Commit();
            }

            var cobieNote = cobieOnProject
                ? classified + " room" + (classified == 1 ? "" : "s") + " classified."
                : "COBie/classification parameters are not on the rooms in this project, so OmniClass values were not written. Names can still be reused.";

            CommandSupport.Inform(
                "Rooms imported",
                namesUpdated + " name" + (namesUpdated == 1 ? "" : "s") + " updated. " +
                cobieNote +
                (skipped == 0 ? "" : " Skipped " + skipped + ".") +
                (failures.Count == 0 ? "" : "\n\n" + CommandSupport.FormatFailures(failures)));

            return Result.Succeeded;
        }

        private static ProjectRoomRef ToRef(RoomSnapshot room)
        {
            var reason = room.IsUnplaced ? "Unplaced"
                : room.IsNotEnclosed ? "Not enclosed"
                : room.OwnedByOtherUser
                    ? (string.IsNullOrEmpty(room.OwnerName) ? "Owned by another user" : "Owned by " + room.OwnerName)
                    : string.Empty;

            return new ProjectRoomRef
            {
                Id = room.Id.ToString(CultureInfo.InvariantCulture),
                Number = room.Number ?? string.Empty,
                Name = room.Name ?? string.Empty,
                IsWritable = room.IsWritable,
                HasCobieParameters = room.HasCobieParameters,
                CurrentOmniClassNumber = room.CurrentNumber ?? string.Empty,
                CurrentOmniClassName = room.CurrentTitle ?? string.Empty,
                SkipReason = reason
            };
        }
    }
}
