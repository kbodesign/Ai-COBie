using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using OmniClass.Core.Configuration;
using OmniClass.Core.Matching;
using OmniClass.Core.Model;
using OmniClass.Revit.Ui;

namespace OmniClass.Revit.Rooms
{
    internal sealed class WriteResult
    {
        public int Written { get; set; }
        public int AlreadyPopulated { get; set; }
        public int Unchanged { get; set; }
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

                if (number == null && title == null && category == null)
                {
                    result.Skipped++;
                    result.Failures.Add(row.RoomNumber + " " + row.RoomName +
                                        ": none of " + settings.NumberParameterName + ", " +
                                        settings.TitleParameterName + " or " +
                                        settings.CategoryParameterName + " is on this room.");
                    continue;
                }

                var currentNumber = TextOf(number);
                var currentTitle = TextOf(title);
                var currentCategory = TextOf(category);
                var already = ApplyPolicy.HasExistingClassification(currentNumber, currentTitle)
                              || !string.IsNullOrWhiteSpace(currentCategory);

                if (already && !settings.OverwriteExisting)
                {
                    result.AlreadyPopulated++;
                    continue;
                }

                var nextNumber = row.ProposedNumber;
                var nextTitle = row.ProposedTitle;
                var nextCategory = OmniClassFormatting.Category(nextNumber, nextTitle);

                if (Same(currentNumber, nextNumber) && Same(currentTitle, nextTitle)
                    && (category == null || Same(currentCategory, nextCategory)))
                {
                    result.Unchanged++;
                    continue;
                }

                try
                {
                    TrySet(number, nextNumber);
                    TrySet(title, nextTitle);
                    TrySet(category, nextCategory);
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

        private static string TextOf(Parameter parameter)
        {
            if (parameter == null) return string.Empty;
            return parameter.AsString() ?? string.Empty;
        }

        private static bool Same(string left, string right)
        {
            return string.Equals((left ?? string.Empty).Trim(), (right ?? string.Empty).Trim(), StringComparison.Ordinal);
        }

        private static void TrySet(Parameter parameter, string value)
        {
            if (parameter == null || parameter.IsReadOnly) return;
            if (Same(parameter.AsString(), value)) return;
            parameter.Set(value ?? string.Empty);
        }
    }
}
