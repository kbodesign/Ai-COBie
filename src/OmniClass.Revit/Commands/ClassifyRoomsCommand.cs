using System;
using System.Linq;
using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using OmniClass.Core.Matching;
using OmniClass.Revit.Parameters;
using OmniClass.Revit.Rooms;
using OmniClass.Revit.Ui;

namespace OmniClass.Revit.Commands
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public sealed class ClassifyRoomsCommand : IExternalCommand
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
                CommandSupport.Warn("Classify rooms failed", ex.Message);
                message = ex.Message;
                return Result.Failed;
            }
        }

        private static Result Run(UIApplication uiapp, Document document)
        {
            var settings = CommandSupport.SettingsOrWarn();
            var dictionary = CommandSupport.TryLoadDictionary(settings, warnIfMissing: true, requireClean: true);
            if (dictionary == null) return Result.Cancelled;

            var rooms = RoomCollector.Collect(document, settings);
            if (rooms.Count == 0)
            {
                CommandSupport.Inform("No rooms", "This project has no room elements to classify.");
                return Result.Cancelled;
            }

            var classifier = new RoomClassifier(dictionary);
            var rows = rooms
                .Select(room => new PreviewRow(room, classifier.Classify(room.Name), settings))
                .ToList();

            var window = new PreviewWindow(rows);
            new WindowInteropHelper(window) { Owner = uiapp.MainWindowHandle };

            if (window.ShowDialog() != true || !window.ApplyConfirmed)
                return Result.Cancelled;

            var selected = window.CheckedRows.ToList();
            if (selected.Count == 0)
            {
                CommandSupport.Inform("Nothing to apply", "No writable rows were checked.");
                return Result.Cancelled;
            }

            using (var group = new TransactionGroup(document, "OmniClass: classify rooms"))
            {
                group.Start();

                using (var bind = new Transaction(document, "Bind OmniClass parameters"))
                {
                    bind.Start();
                    try
                    {
                        SharedParameterBinder.EnsureBound(document, settings);
                        bind.Commit();
                    }
                    catch
                    {
                        bind.RollBack();
                    }
                }

                WriteResult written;
                using (var write = new Transaction(document, "Write OmniClass values"))
                {
                    write.Start();
                    written = ClassificationWriter.Apply(document, selected, settings);
                    write.Commit();
                }

                group.Assimilate();

                var failures = CommandSupport.FormatFailures(written.Failures);
                CommandSupport.Inform(
                    "Classification applied",
                    "Wrote " + written.Written + " room" + (written.Written == 1 ? "" : "s") + ". " +
                    (written.AlreadyPopulated == 0
                        ? ""
                        : written.AlreadyPopulated + " already had a value and were left alone. ") +
                    (written.Unchanged == 0 ? "" : written.Unchanged + " already matched. ") +
                    (written.Skipped == 0 ? "" : "Skipped " + written.Skipped + ". ") +
                    (failures.Length == 0 ? "" : "\n\n" + failures));
            }

            return Result.Succeeded;
        }
    }
}
