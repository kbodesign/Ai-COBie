using System;
using System.Collections.Generic;
using System.Linq;
using OmniClass.Core.Text;

namespace OmniClass.Core.Transfer
{
    /// <summary>One row already on a Room key schedule.</summary>
    public sealed class RoomKeyRef
    {
        public string Id { get; set; } = string.Empty;
        public string KeyName { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string OmniClassNumber { get; set; } = string.Empty;
        public string OmniClassName { get; set; } = string.Empty;

        public bool IsBlank =>
            string.IsNullOrWhiteSpace(KeyName) && string.IsNullOrWhiteSpace(Name);
    }

    public sealed class RoomKeyAction
    {
        public RoomTransferRow Source { get; set; }
        public RoomKeyRef Target { get; set; }
        public string Kind { get; set; } = "Skip";

        public bool UpdateKeyName { get; set; }
        public bool UpdateName { get; set; }
        public bool UpdateClassification { get; set; }
        public bool NeedsNewRow => Kind == "Add" && (Target == null || string.IsNullOrEmpty(Target.Id));
    }

    public sealed class RoomKeyPlan
    {
        public List<RoomKeyAction> Actions { get; } = new List<RoomKeyAction>();

        public int AddCount => Actions.Count(a => a.Kind == "Add");
        public int UpdateCount => Actions.Count(a => a.Kind == "Update");
        public int SkipCount => Actions.Count(a => a.Kind == "Skip");
        public int NewRowCount => Actions.Count(a => a.NeedsNewRow);
    }

    /// <summary>
    /// Turns unique CSV room names into key-schedule rows. Matching is by Key Name
    /// then Name (normalized). Room Number / Mark is never used. Existing keys that
    /// are not in the CSV are left alone.
    /// </summary>
    public static class RoomKeySchedulePlanner
    {
        public static RoomKeyPlan Plan(
            IEnumerable<RoomTransferRow> rows,
            IEnumerable<RoomKeyRef> existingKeys)
        {
            var plan = new RoomKeyPlan();
            var existing = (existingKeys ?? Enumerable.Empty<RoomKeyRef>()).Where(k => k != null).ToList();
            var used = new HashSet<string>(StringComparer.Ordinal);
            var byKeyName = Index(existing, k => k.KeyName);
            var byName = Index(existing, k => k.Name);

            foreach (var row in RoomTransferSheet.Unique(rows))
            {
                if (string.IsNullOrWhiteSpace(row.Name)) continue;

                var key = RoomNameNormalizer.Key(row.Name);
                var match = FirstUnused(byKeyName, key, used) ?? FirstUnused(byName, key, used);

                if (match != null)
                {
                    used.Add(match.Id ?? string.Empty);
                    plan.Actions.Add(UpdateOrSkip(row, match));
                    continue;
                }

                var blank = existing.FirstOrDefault(k => k.IsBlank && !used.Contains(k.Id ?? string.Empty));
                if (blank != null) used.Add(blank.Id ?? string.Empty);
                plan.Actions.Add(Add(row, blank));
            }

            return plan;
        }

        private static RoomKeyAction UpdateOrSkip(RoomTransferRow row, RoomKeyRef match)
        {
            var action = new RoomKeyAction
            {
                Source = row,
                Target = match,
                UpdateKeyName = Differs(match.KeyName, row.Name),
                UpdateName = Differs(match.Name, row.Name),
                UpdateClassification = row.HasOmniClass
                    && (Differs(match.OmniClassNumber, row.OmniClassNumber)
                        || Differs(match.OmniClassName, row.OmniClassName))
            };

            action.Kind = action.UpdateKeyName || action.UpdateName || action.UpdateClassification
                ? "Update"
                : "Skip";
            return action;
        }

        private static RoomKeyAction Add(RoomTransferRow row, RoomKeyRef blank)
        {
            return new RoomKeyAction
            {
                Source = row,
                Target = blank,
                Kind = "Add",
                UpdateKeyName = true,
                UpdateName = true,
                UpdateClassification = row.HasOmniClass
            };
        }

        private static RoomKeyRef FirstUnused(
            Dictionary<string, List<RoomKeyRef>> index,
            string key,
            HashSet<string> used)
        {
            if (key.Length == 0 || !index.TryGetValue(key, out var list)) return null;
            return list.FirstOrDefault(k => !used.Contains(k.Id ?? string.Empty));
        }

        private static Dictionary<string, List<RoomKeyRef>> Index(
            IEnumerable<RoomKeyRef> keys,
            Func<RoomKeyRef, string> selector)
        {
            var map = new Dictionary<string, List<RoomKeyRef>>(StringComparer.Ordinal);
            foreach (var key in keys)
            {
                var value = RoomNameNormalizer.Key(selector(key));
                if (value.Length == 0) continue;
                if (!map.TryGetValue(value, out var list))
                {
                    list = new List<RoomKeyRef>();
                    map.Add(value, list);
                }

                list.Add(key);
            }

            return map;
        }

        private static bool Differs(string left, string right)
        {
            return !string.Equals((left ?? string.Empty).Trim(), (right ?? string.Empty).Trim(), StringComparison.Ordinal);
        }
    }
}
