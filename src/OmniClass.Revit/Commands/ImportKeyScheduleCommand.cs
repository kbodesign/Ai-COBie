using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Microsoft.Win32;
using OmniClass.Core.Io;
using OmniClass.Core.Transfer;
using OmniClass.Revit.Rooms;

namespace OmniClass.Revit.Commands
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public sealed class ImportKeyScheduleCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var document = commandData.Application.ActiveUIDocument?.Document;
            if (document == null)
            {
                message = "Open a project or template first.";
                return Result.Failed;
            }

            try
            {
                return Run(document);
            }
            catch (Exception ex)
            {
                CommandSupport.Warn("Key schedule failed", ex.Message);
                message = ex.Message;
                return Result.Failed;
            }
        }

        private static Result Run(Document document)
        {
            var dialog = new OpenFileDialog
            {
                Title = "Import room names into a key schedule",
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

            var imported = RoomTransferSheet.Unique(RoomTransferSheet.FromRows(raw));
            if (imported.Count == 0)
            {
                CommandSupport.Inform("Nothing to import", "The CSV has no room names.");
                return Result.Cancelled;
            }

            var settings = CommandSupport.SettingsOrWarn();
            var existingSchedule = RoomKeyScheduleWriter.Find(document, settings);
            var existingKeys = RoomKeyScheduleWriter.CollectKeys(document, existingSchedule, settings);
            var plan = RoomKeySchedulePlanner.Plan(imported, existingKeys);

            if (plan.AddCount == 0 && plan.UpdateCount == 0)
            {
                CommandSupport.Inform(
                    "Key schedule already matches",
                    imported.Count + " unique name" + (imported.Count == 1 ? "" : "s") +
                    " in the CSV are already on " + ScheduleLabel(existingSchedule, settings) + ".");
                return Result.Cancelled;
            }

            var confirm = new TaskDialog("Import room key schedule")
            {
                MainInstruction = existingSchedule == null
                    ? "Create a Room Type key schedule and load unique names from the CSV?"
                    : "Load unique room names into " + existingSchedule.Name + "?",
                MainContent =
                    imported.Count + " unique name" + (imported.Count == 1 ? "" : "s") + " in the CSV.\n" +
                    plan.AddCount + " new key" + (plan.AddCount == 1 ? "" : "s") + ", " +
                    plan.UpdateCount + " update" + (plan.UpdateCount == 1 ? "" : "s") + ", " +
                    plan.SkipCount + " already present.\n\n" +
                    "Each key is a reusable room name. Pick it on a room to set Name and, " +
                    "when COBie/Classification.Space parameters exist, OmniClass.\n\n" +
                    "Best place to run this is the Arch template, then save the template so " +
                    "new projects already have the dropdown.",
                CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No
            };

            if (confirm.Show() != TaskDialogResult.Yes) return Result.Cancelled;

            var created = false;
            var added = 0;
            var updated = 0;
            var failures = new System.Collections.Generic.List<string>();
            string scheduleName;

            using (var write = new Transaction(document, "OmniClass: import room key schedule"))
            {
                write.Start();
                var schedule = RoomKeyScheduleWriter.Ensure(document, settings, out created);
                scheduleName = schedule.Name;
                var keys = RoomKeyScheduleWriter.CollectKeys(document, schedule, settings);
                var applyPlan = RoomKeySchedulePlanner.Plan(imported, keys);
                RoomKeyScheduleWriter.Apply(document, schedule, applyPlan, settings, failures, out added, out updated);
                write.Commit();
            }

            CommandSupport.Inform(
                created ? "Room key schedule created" : "Room key schedule updated",
                added + " key" + (added == 1 ? "" : "s") + " added, " +
                updated + " updated on " + scheduleName + ".\n\n" +
                "On a room, Identity Data → " + settings.KeyScheduleParameterName +
                " is the dropdown. Save the Arch template if this should exist on new projects." +
                (failures.Count == 0 ? "" : "\n\n" + CommandSupport.FormatFailures(failures)));

            return Result.Succeeded;
        }

        private static string ScheduleLabel(ViewSchedule schedule, OmniClass.Core.Configuration.AddinSettings settings)
        {
            return schedule != null ? schedule.Name : settings.KeyScheduleName;
        }
    }
}
