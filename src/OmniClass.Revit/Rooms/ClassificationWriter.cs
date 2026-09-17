using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using OmniClass.Core.Configuration;
using OmniClass.Core.Model;
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
                var category = string.IsNullOrEmpty(settings.CategoryParameterName)
                    ? null
                    : room.LookupParameter(settings.CategoryParameterName);

                if (number == null || title == null)
                {
                    result.Skipped++;
                    result.Failures.Add(row.RoomNumber + " " + row.RoomName +
                                        ": " + settings.NumberParameterName + " or " +
                                        settings.TitleParameterName + " is not on this room.");
                    continue;
                }

                if (number.IsReadOnly || title.IsReadOnly || (category != null && category.IsReadOnly))
                {
                    result.Skipped++;
                    result.Failures.Add(row.RoomNumber + " " + row.RoomName + ": parameter is read-only.");
                    continue;
                }

                try
                {
                    number.Set(row.ProposedNumber);
                    title.Set(row.ProposedTitle);
                    if (category != null)
                        category.Set(OmniClassFormatting.Category(row.ProposedNumber, row.ProposedTitle));
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
