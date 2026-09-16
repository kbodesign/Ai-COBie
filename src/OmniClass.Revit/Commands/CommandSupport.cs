using System;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.UI;
using OmniClass.Core.Configuration;
using OmniClass.Core.Loading;
using OmniClass.Core.Model;

namespace OmniClass.Revit.Commands
{
    internal static class CommandSupport
    {
        public static AddinSettings SettingsOrWarn()
        {
            return AddinSettings.Load(Path.GetDirectoryName(typeof(OmniClass.Revit.App).Assembly.Location));
        }

        public static ClassificationDictionary TryLoadDictionary(AddinSettings settings, bool warnIfMissing, bool requireClean)
        {
            if (!File.Exists(settings.DictionaryPath))
            {
                if (warnIfMissing)
                {
                    Warn("Dictionary not found",
                        "The alias dictionary was not found at:\n" + settings.DictionaryPath +
                        "\n\nPoint 'dictionary' in OmniClass.Rooms.config at the office copy of room_aliases.csv.");
                }
                return null;
            }

            var dictionary = AliasSheetLoader.LoadFile(settings.DictionaryPath);

            if (requireClean && dictionary.HasErrors)
            {
                var errors = dictionary.Messages
                    .Where(m => m.Severity == ValidationSeverity.Error)
                    .Take(8)
                    .Select(m => m.ToString());

                Warn("Dictionary has errors",
                    "Fix these before classifying. Harvest Names still works and will list " +
                    "what the sheet currently does with each room.\n\n" + string.Join("\n", errors));
                return null;
            }

            return dictionary;
        }

        public static void Warn(string title, string message)
        {
            var dialog = new TaskDialog(title)
            {
                MainInstruction = title,
                MainContent = message,
                CommonButtons = TaskDialogCommonButtons.Ok
            };
            dialog.Show();
        }

        public static void Inform(string title, string message)
        {
            var dialog = new TaskDialog(title)
            {
                MainInstruction = title,
                MainContent = message,
                CommonButtons = TaskDialogCommonButtons.Ok
            };
            dialog.Show();
        }

        public static string FormatFailures(System.Collections.Generic.IReadOnlyList<string> failures)
        {
            if (failures == null || failures.Count == 0) return string.Empty;

            var shown = failures.Take(8);
            var text = new StringBuilder();
            foreach (var line in shown) text.AppendLine(line);
            if (failures.Count > 8)
                text.AppendLine("...and " + (failures.Count - 8) + " more.");
            return text.ToString();
        }
    }
}
