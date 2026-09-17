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

    /// <summary>
    /// Writes the same space-classification payload as Interoperability → Assign
    /// Classification: Number, Description, COBie category, and ClassificationCode
    /// when that parameter exists. Parameters are never created here.
    /// </summary>
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

                var assignments = Assignments(settings, row.ProposedNumber, row.ProposedTitle);
                var found = new List<KeyValuePair<Parameter, string>>();

                foreach (var assignment in assignments)
                {
                    var parameter = FindParameter(room, assignment.Key);
                    if (parameter != null) found.Add(new KeyValuePair<Parameter, string>(parameter, assignment.Value));
                }

                if (found.Count == 0)
                {
                    result.Skipped++;
                    result.Failures.Add(row.RoomNumber + " " + row.RoomName +
                                        ": Assign Classification parameters are not on this room " +
                                        "(Classification.Space.Number / Description).");
                    continue;
                }

                var number = FindParameter(room, settings.NumberParameterName);
                var title = FindParameter(room, settings.TitleParameterName);
                var category = FindParameter(room, settings.CategoryParameterName);
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

                if (found.TrueForAll(pair => Same(TextOf(pair.Key), pair.Value)))
                {
                    result.Unchanged++;
                    continue;
                }

                try
                {
                    foreach (var pair in found) TrySet(pair.Key, pair.Value);
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

        private static IReadOnlyList<KeyValuePair<string, string>> Assignments(AddinSettings settings, string number, string title)
        {
            var values = AssignClassificationValues.ForSpace(number, title);
            var mapped = new List<KeyValuePair<string, string>>(values.Count);

            foreach (var pair in values)
            {
                var name = pair.Key;
                if (name == AssignClassificationValues.NumberParameter) name = settings.NumberParameterName;
                else if (name == AssignClassificationValues.DescriptionParameter) name = settings.TitleParameterName;
                else if (name == AssignClassificationValues.CobieCategoryParameter) name = settings.CategoryParameterName;

                if (!string.IsNullOrEmpty(name))
                    mapped.Add(new KeyValuePair<string, string>(name, pair.Value));
            }

            return mapped;
        }

        internal static Parameter FindParameter(Element element, string name)
        {
            if (element == null || string.IsNullOrEmpty(name)) return null;

            var exact = element.LookupParameter(name);
            if (exact != null) return exact;

            foreach (Parameter parameter in element.Parameters)
            {
                if (parameter?.Definition != null
                    && string.Equals(parameter.Definition.Name, name, StringComparison.OrdinalIgnoreCase))
                    return parameter;
            }

            return null;
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
