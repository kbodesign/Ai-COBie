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

                if (!TryWriteAssignments(room, found, settings, overwrite: settings.OverwriteExisting, result, row.RoomNumber, row.RoomName))
                    continue;
            }

            return result;
        }

        public static bool HasCobieParameters(Element room, AddinSettings settings)
        {
            if (room == null || settings == null) return false;
            return FindParameter(room, settings.NumberParameterName) != null
                   || FindParameter(room, settings.TitleParameterName) != null
                   || FindParameter(room, settings.CategoryParameterName) != null;
        }

        public static bool TrySetRoomName(Element element, string name)
        {
            return TrySetBuiltIn(element, BuiltInParameter.ROOM_NAME, name);
        }

        public static bool TrySetKeyName(Element element, string name)
        {
            return TrySetBuiltIn(element, BuiltInParameter.REF_TABLE_ELEM_NAME, name);
        }

        public static bool TryWriteClassification(Element element, AddinSettings settings, string number, string title)
        {
            if (element == null || settings == null) return false;

            var assignments = Assignments(settings, number, title);
            var found = new List<KeyValuePair<Parameter, string>>();
            foreach (var assignment in assignments)
            {
                var parameter = FindParameter(element, assignment.Key);
                if (parameter != null) found.Add(new KeyValuePair<Parameter, string>(parameter, assignment.Value));
            }

            if (found.Count == 0) return false;

            var dummy = new WriteResult();
            return TryWriteAssignments(element, found, settings, overwrite: true, dummy, "", "");
        }

        private static bool TryWriteAssignments(
            Element element,
            List<KeyValuePair<Parameter, string>> found,
            AddinSettings settings,
            bool overwrite,
            WriteResult result,
            string roomNumber,
            string roomName)
        {
            var number = FindParameter(element, settings.NumberParameterName);
            var title = FindParameter(element, settings.TitleParameterName);
            var category = FindParameter(element, settings.CategoryParameterName);
            var already = ApplyPolicy.HasExistingClassification(TextOf(number), TextOf(title))
                          || !string.IsNullOrWhiteSpace(TextOf(category));

            if (already && !overwrite)
            {
                result.AlreadyPopulated++;
                return false;
            }

            if (found.TrueForAll(pair => Same(TextOf(pair.Key), pair.Value)))
            {
                result.Unchanged++;
                return false;
            }

            try
            {
                foreach (var pair in found) TrySet(pair.Key, pair.Value);
                result.Written++;
                return true;
            }
            catch (Exception ex)
            {
                result.Skipped++;
                result.Failures.Add((roomNumber ?? string.Empty) + " " + (roomName ?? string.Empty) + ": " + ex.Message);
                return false;
            }
        }

        private static bool TrySetBuiltIn(Element element, BuiltInParameter bip, string name)
        {
            if (element == null || string.IsNullOrWhiteSpace(name)) return false;

            var parameter = element.get_Parameter(bip);
            if (parameter == null || parameter.IsReadOnly) return false;
            if (Same(TextOf(parameter), name)) return false;
            parameter.Set(name.Trim());
            return true;
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
