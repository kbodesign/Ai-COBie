using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using OmniClass.Revit.Rooms;
using OmniClass.Revit.Settings;
using OmniClass.Revit.Ui;

namespace OmniClass.Revit.Rooms
{
    internal sealed class WriteResult
    {
        public int Written { get; set; }
        public int Skipped { get; set; }
        public List<string> Failures { get; } = new List<string>();
    }

    internal static class ClassificationWriter
    {
        public static WriteResult Apply(Document document, IEnumerable<PreviewRow> rows, AddinSettings settings)
        {
            var result = new WriteResult();

            foreach (var row in rows)
            {
                var element = document.GetElement(RoomCollector.ToElementId(row.Room.Id));
                var room = element as Room;
                if (room == null)
                {
                    result.Skipped++;
                    result.Failures.Add(row.RoomNumber + " " + row.RoomName + ": room is no longer in the model.");
                    continue;
                }

                var number = room.LookupParameter(settings.NumberParameterName);
                var title = room.LookupParameter(settings.TitleParameterName);

                if (number == null || title == null)
                {
                    result.Skipped++;
                    result.Failures.Add(row.RoomNumber + " " + row.RoomName +
                                        ": OmniClass parameters are not bound on this room.");
                    continue;
                }

                if (number.IsReadOnly || title.IsReadOnly)
                {
                    result.Skipped++;
                    result.Failures.Add(row.RoomNumber + " " + row.RoomName + ": parameter is read-only.");
                    continue;
                }

                try
                {
                    number.Set(row.ProposedNumber);
                    title.Set(row.ProposedTitle);
                    result.Written++;
                }
                catch (Exception ex)
                {
                    result.Skipped++;
                    result.Failures.Add(row.RoomNumber + " " + row.RoomName + ": " + ex.Message);
                }
            }

            return result;
        }
    }
}
