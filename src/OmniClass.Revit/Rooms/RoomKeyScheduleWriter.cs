using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;
using OmniClass.Core.Configuration;
using OmniClass.Core.Model;
using OmniClass.Core.Transfer;
using RevitArgumentException = Autodesk.Revit.Exceptions.ArgumentException;

namespace OmniClass.Revit.Rooms
{
    /// <summary>
    /// Finds or creates a Rooms key schedule and writes unique names (and OmniClass
    /// when those parameters exist) onto the key rows. The schedule belongs in the
    /// Arch template so new projects already have the Room Type dropdown.
    /// </summary>
    internal static class RoomKeyScheduleWriter
    {
        public static ViewSchedule Find(Document document, AddinSettings settings)
        {
            var keys = RoomKeySchedules(document).ToList();
            if (keys.Count == 0) return null;

            var named = keys.FirstOrDefault(s => NamesMatch(s.Name, settings.KeyScheduleName));
            if (named != null) return named;

            var byParameter = keys.FirstOrDefault(s => NamesMatch(s.KeyScheduleParameterName, settings.KeyScheduleParameterName));
            if (byParameter != null) return byParameter;

            return keys.Count == 1 ? keys[0] : null;
        }

        public static IReadOnlyList<RoomKeyRef> CollectKeys(Document document, ViewSchedule schedule, AddinSettings settings)
        {
            var result = new List<RoomKeyRef>();
            if (document == null || schedule == null) return result;

            foreach (var element in KeyElements(document, schedule))
            {
                result.Add(ToRef(element, settings));
            }

            return result;
        }

        public static ViewSchedule Ensure(Document document, AddinSettings settings, out bool created)
        {
            created = false;
            var schedule = Find(document, settings);
            if (schedule == null)
            {
                var category = document.Settings.Categories.get_Item(BuiltInCategory.OST_Rooms);
                schedule = ViewSchedule.CreateKeySchedule(document, category.Id);
                schedule.Name = UniqueViewName(document, settings.KeyScheduleName);
                try
                {
                    schedule.KeyScheduleParameterName = settings.KeyScheduleParameterName;
                }
                catch (RevitArgumentException)
                {
                    // Keep the default key-parameter name if this one is already taken.
                }

                created = true;
            }

            EnsureFields(document, schedule, settings);
            return schedule;
        }

        public static void Apply(
            Document document,
            ViewSchedule schedule,
            RoomKeyPlan plan,
            AddinSettings settings,
            List<string> failures,
            out int added,
            out int updated)
        {
            added = 0;
            updated = 0;
            if (plan == null || plan.Actions.Count == 0) return;

            InsertKeyRows(schedule, plan.NewRowCount, failures);
            document.Regenerate();
            TryRefresh(schedule);

            var elements = KeyElements(document, schedule).ToList();
            var byId = new Dictionary<string, Element>(StringComparer.Ordinal);
            foreach (var element in elements)
                byId[IdOf(element)] = element;

            var claimed = new HashSet<string>(StringComparer.Ordinal);
            foreach (var action in plan.Actions)
            {
                if (action.Target != null && !string.IsNullOrEmpty(action.Target.Id))
                    claimed.Add(action.Target.Id);
            }

            var unused = new Queue<Element>(
                elements.Where(e => !claimed.Contains(IdOf(e)))
                    .OrderBy(e => string.IsNullOrWhiteSpace(KeyNameOf(e)) ? 0 : 1)
                    .ThenBy(e => IdOf(e), StringComparer.Ordinal));

            foreach (var action in plan.Actions)
            {
                Element target = null;
                if (action.Target != null && !string.IsNullOrEmpty(action.Target.Id))
                    byId.TryGetValue(action.Target.Id, out target);
                if (target == null)
                {
                    if (unused.Count == 0)
                    {
                        failures.Add((action.Source?.Name ?? string.Empty) + ": could not add a key row.");
                        continue;
                    }

                    target = unused.Dequeue();
                }

                try
                {
                    var wrote = false;
                    if (action.UpdateKeyName && ClassificationWriter.TrySetKeyName(target, action.Source.Name))
                        wrote = true;
                    if (action.UpdateName && ClassificationWriter.TrySetRoomName(target, action.Source.Name))
                        wrote = true;
                    if (action.UpdateClassification
                        && ClassificationWriter.TryWriteClassification(
                            target,
                            settings,
                            action.Source.OmniClassNumber,
                            action.Source.OmniClassName))
                        wrote = true;

                    if (action.Kind == "Add") added++;
                    else if (wrote || action.Kind == "Update") updated++;
                }
                catch (Exception ex)
                {
                    failures.Add((action.Source?.Name ?? string.Empty) + ": " + ex.Message);
                }
            }
        }

        private static IEnumerable<ViewSchedule> RoomKeySchedules(Document document)
        {
            var category = document.Settings.Categories.get_Item(BuiltInCategory.OST_Rooms);
            var roomsId = category.Id;

            foreach (ViewSchedule schedule in new FilteredElementCollector(document).OfClass(typeof(ViewSchedule)))
            {
                if (schedule?.Definition == null || !schedule.Definition.IsKeySchedule) continue;
                if (schedule.Definition.CategoryId != roomsId) continue;
                yield return schedule;
            }
        }

        private static IEnumerable<Element> KeyElements(Document document, ViewSchedule schedule)
        {
            foreach (var element in new FilteredElementCollector(document, schedule.Id).WhereElementIsNotElementType())
            {
                if (element == null || element.Id == schedule.Id) continue;
                if (element is View) continue;
                yield return element;
            }
        }

        private static void EnsureFields(Document document, ViewSchedule schedule, AddinSettings settings)
        {
            var definition = schedule.Definition;
            var available = definition.GetSchedulableFields();
            foreach (var wanted in WantedFields(settings))
            {
                var field = available.FirstOrDefault(sf => Matches(document, sf, wanted));
                if (field == null || HasField(definition, field.ParameterId)) continue;
                definition.AddField(field);
            }
        }

        private static IEnumerable<string> WantedFields(AddinSettings settings)
        {
            yield return "Key Name";
            yield return "Name";
            if (!string.IsNullOrEmpty(settings.NumberParameterName)) yield return settings.NumberParameterName;
            if (!string.IsNullOrEmpty(settings.TitleParameterName)) yield return settings.TitleParameterName;
            if (!string.IsNullOrEmpty(settings.CategoryParameterName)) yield return settings.CategoryParameterName;
            yield return AssignClassificationValues.IfcClassificationCodeParameter;
        }

        private static bool Matches(Document document, SchedulableField field, string wanted)
        {
            if (field == null || string.IsNullOrEmpty(wanted)) return false;
            if (NamesMatch(wanted, "Key Name") && IsBuiltIn(field.ParameterId, BuiltInParameter.REF_TABLE_ELEM_NAME))
                return true;
            if (NamesMatch(wanted, "Name") && IsBuiltIn(field.ParameterId, BuiltInParameter.ROOM_NAME))
                return true;
            return NamesMatch(field.GetName(document), wanted);
        }

        private static bool HasField(ScheduleDefinition definition, ElementId parameterId)
        {
            if (parameterId == null || parameterId == ElementId.InvalidElementId) return false;
            for (var i = 0; i < definition.GetFieldCount(); i++)
            {
                if (definition.GetField(i).ParameterId == parameterId) return true;
            }

            return false;
        }

        private static void InsertKeyRows(ViewSchedule schedule, int count, List<string> failures)
        {
            for (var i = 0; i < count; i++)
            {
                var body = schedule.GetTableData().GetSectionData(SectionType.Body);
                var index = InsertIndex(body);
                if (!body.CanInsertRow(index))
                {
                    failures.Add("Could not insert key row " + (i + 1) + ".");
                    return;
                }

                try
                {
                    body.InsertRow(index);
                }
                catch (RevitArgumentException ex)
                {
                    failures.Add("Could not insert key row " + (i + 1) + ": " + ex.Message);
                    return;
                }
            }
        }

        private static int InsertIndex(TableSectionData body)
        {
            if (body.CanInsertRow(body.LastRowNumber)) return body.LastRowNumber;
            if (body.CanInsertRow(body.LastRowNumber + 1)) return body.LastRowNumber + 1;
            if (body.CanInsertRow(body.FirstRowNumber)) return body.FirstRowNumber;
            return body.LastRowNumber;
        }

        private static void TryRefresh(ViewSchedule schedule)
        {
            try
            {
                schedule.RefreshData();
            }
            catch (RevitArgumentException)
            {
            }
        }

        private static string UniqueViewName(Document document, string preferred)
        {
            if (string.IsNullOrWhiteSpace(preferred)) preferred = "Room Type";
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (View view in new FilteredElementCollector(document).OfClass(typeof(View)))
            {
                if (!string.IsNullOrEmpty(view.Name)) names.Add(view.Name);
            }

            if (!names.Contains(preferred)) return preferred;
            for (var n = 2; n < 1000; n++)
            {
                var candidate = preferred + " " + n;
                if (!names.Contains(candidate)) return candidate;
            }

            return preferred + " " + Guid.NewGuid().ToString("N").Substring(0, 6);
        }

        private static RoomKeyRef ToRef(Element element, AddinSettings settings)
        {
            return new RoomKeyRef
            {
                Id = IdOf(element),
                KeyName = KeyNameOf(element),
                Name = TextOf(element.get_Parameter(BuiltInParameter.ROOM_NAME)),
                OmniClassNumber = TextOf(ClassificationWriter.FindParameter(element, settings.NumberParameterName)),
                OmniClassName = TextOf(ClassificationWriter.FindParameter(element, settings.TitleParameterName))
            };
        }

        private static string IdOf(Element element)
        {
            return RoomCollector.ElementIdValue(element.Id).ToString(CultureInfo.InvariantCulture);
        }

        private static string KeyNameOf(Element element)
        {
            return TextOf(element.get_Parameter(BuiltInParameter.REF_TABLE_ELEM_NAME));
        }

        private static string TextOf(Parameter parameter)
        {
            return parameter?.AsString() ?? string.Empty;
        }

        private static bool NamesMatch(string left, string right)
        {
            return string.Equals((left ?? string.Empty).Trim(), (right ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsBuiltIn(ElementId id, BuiltInParameter parameter)
        {
            if (id == null || id == ElementId.InvalidElementId) return false;
            return id == new ElementId(parameter);
        }
    }
}
