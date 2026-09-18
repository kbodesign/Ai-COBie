using System;
using System.Collections.Generic;
using System.Linq;

namespace OmniClass.Core.Transfer
{
    /// <summary>A room in the open project, as Import Rooms needs it to plan writes.</summary>
    public sealed class ProjectRoomRef
    {
        public string Id { get; set; } = string.Empty;
        public string Number { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public bool IsWritable { get; set; }
        public bool HasCobieParameters { get; set; }
        public string CurrentOmniClassNumber { get; set; } = string.Empty;
        public string CurrentOmniClassName { get; set; } = string.Empty;
        public string SkipReason { get; set; } = string.Empty;
    }

    public sealed class RoomImportAction
    {
        public RoomTransferRow Source { get; set; }
        public ProjectRoomRef Target { get; set; }
        public string Match { get; set; } = "None";
        public string Note { get; set; } = string.Empty;

        public bool UpdateName =>
            Target != null
            && !string.IsNullOrWhiteSpace(Source?.Name)
            && !string.Equals((Target.Name ?? string.Empty).Trim(), Source.Name.Trim(), StringComparison.Ordinal);

        public bool UpdateClassification =>
            Target != null
            && Target.HasCobieParameters
            && Source != null
            && Source.HasOmniClass
            && (!string.Equals((Target.CurrentOmniClassNumber ?? string.Empty).Trim(), (Source.OmniClassNumber ?? string.Empty).Trim(), StringComparison.Ordinal)
                || !string.Equals((Target.CurrentOmniClassName ?? string.Empty).Trim(), (Source.OmniClassName ?? string.Empty).Trim(), StringComparison.Ordinal));

        public bool CanApply =>
            Target != null
            && Target.IsWritable
            && (UpdateName || UpdateClassification);

        public bool DefaultApply => CanApply;
    }

    /// <summary>
    /// Matches exported rows to rooms in the model: Room Number first, then Name.
    /// Classification is only planned when the project already has the COBie /
    /// Classification.Space parameters (COBie is "activated").
    /// </summary>
    public static class RoomImportPlanner
    {
        public static IReadOnlyList<RoomImportAction> Plan(
            IEnumerable<RoomTransferRow> rows,
            IEnumerable<ProjectRoomRef> rooms)
        {
            var roomList = (rooms ?? Enumerable.Empty<ProjectRoomRef>()).Where(r => r != null).ToList();
            var byNumber = Index(roomList, r => r.Number);
            var byName = Index(roomList, r => r.Name);
            var actions = new List<RoomImportAction>();

            foreach (var row in rows ?? Enumerable.Empty<RoomTransferRow>())
            {
                if (row == null || row.IsBlank) continue;

                IReadOnlyList<ProjectRoomRef> matches = Array.Empty<ProjectRoomRef>();
                var match = "None";

                if (!string.IsNullOrWhiteSpace(row.RoomNumber)
                    && byNumber.TryGetValue(row.RoomNumber.Trim(), out var numbered))
                {
                    matches = numbered;
                    match = "Number";
                }
                else if (!string.IsNullOrWhiteSpace(row.Name)
                         && byName.TryGetValue(row.Name.Trim(), out var named))
                {
                    matches = named;
                    match = "Name";
                }

                if (matches.Count == 0)
                {
                    actions.Add(new RoomImportAction
                    {
                        Source = row,
                        Target = null,
                        Match = "None",
                        Note = "No matching room in this project."
                    });
                    continue;
                }

                foreach (var target in matches)
                {
                    actions.Add(Build(row, target, match));
                }
            }

            return actions;
        }

        private static RoomImportAction Build(RoomTransferRow row, ProjectRoomRef target, string match)
        {
            var action = new RoomImportAction
            {
                Source = row,
                Target = target,
                Match = match
            };

            if (!target.IsWritable)
            {
                action.Note = string.IsNullOrEmpty(target.SkipReason) ? "Not writable" : target.SkipReason;
                return action;
            }

            if (action.UpdateClassification && action.UpdateName)
                action.Note = "Update name and classification";
            else if (action.UpdateClassification)
                action.Note = "Update classification";
            else if (action.UpdateName)
                action.Note = target.HasCobieParameters
                    ? "Update name"
                    : "Update name (COBie/classification parameters are not on this room)";
            else if (row.HasOmniClass && !target.HasCobieParameters)
                action.Note = "COBie/classification parameters are not on this room";
            else
                action.Note = "Already matches";

            return action;
        }

        private static Dictionary<string, List<ProjectRoomRef>> Index(
            IEnumerable<ProjectRoomRef> rooms,
            Func<ProjectRoomRef, string> key)
        {
            var map = new Dictionary<string, List<ProjectRoomRef>>(StringComparer.Ordinal);
            foreach (var room in rooms)
            {
                var value = (key(room) ?? string.Empty).Trim();
                if (value.Length == 0) continue;
                if (!map.TryGetValue(value, out var list))
                {
                    list = new List<ProjectRoomRef>();
                    map.Add(value, list);
                }

                list.Add(room);
            }

            return map;
        }
    }
}
