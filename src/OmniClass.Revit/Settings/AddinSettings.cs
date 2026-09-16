using System;
using System.IO;
using System.Reflection;

namespace OmniClass.Revit.Settings
{
    /// <summary>
    /// Plain key=value settings read from a file beside the assembly. Deliberately not JSON:
    /// Revit loads every add-in into one process, so pulling in a serializer invites the
    /// assembly version conflicts that make add-ins fail in ways nobody can diagnose.
    /// </summary>
    public sealed class AddinSettings
    {
        public const string FileName = "OmniClass.Rooms.config";

        public string DictionaryPath { get; private set; }
        public string NumberParameterName { get; private set; } = "OmniClass Number";
        public string TitleParameterName { get; private set; } = "OmniClass Title";
        public string SharedParameterFilePath { get; private set; }
        public bool OverwriteExisting { get; private set; }

        public static string AssemblyFolder =>
            Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

        public static AddinSettings Load()
        {
            var folder = AssemblyFolder;
            var path = Path.Combine(folder, FileName);
            return File.Exists(path) ? LoadFrom(File.ReadAllText(path), folder) : Defaults(folder);
        }

        public static AddinSettings LoadFrom(string contents, string folder)
        {
            var settings = Defaults(folder);
            if (string.IsNullOrEmpty(contents)) return settings;

            foreach (var line in contents.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
            {
                var text = line.Trim();
                if (text.Length == 0 || text.StartsWith("#", StringComparison.Ordinal)) continue;

                var split = text.IndexOf('=');
                if (split <= 0) continue;

                var key = text.Substring(0, split).Trim().ToLowerInvariant();
                var value = text.Substring(split + 1).Trim();
                if (value.Length == 0) continue;

                switch (key)
                {
                    case "dictionary":
                        settings.DictionaryPath = Resolve(folder, value);
                        break;
                    case "numberparameter":
                        settings.NumberParameterName = value;
                        break;
                    case "titleparameter":
                        settings.TitleParameterName = value;
                        break;
                    case "sharedparameterfile":
                        settings.SharedParameterFilePath = Resolve(folder, value);
                        break;
                    case "overwriteexisting":
                        settings.OverwriteExisting = value.Equals("true", StringComparison.OrdinalIgnoreCase);
                        break;
                }
            }

            return settings;
        }

        private static AddinSettings Defaults(string folder)
        {
            return new AddinSettings
            {
                DictionaryPath = Path.Combine(folder ?? string.Empty, "data", "room_aliases.csv")
            };
        }

        /// <summary>
        /// Where a generated shared parameter file goes when none is configured: beside the
        /// dictionary, so the definitions travel with the data they describe.
        /// </summary>
        public string EffectiveSharedParameterFile()
        {
            if (!string.IsNullOrEmpty(SharedParameterFilePath)) return SharedParameterFilePath;

            var folder = Path.GetDirectoryName(DictionaryPath);
            if (string.IsNullOrEmpty(folder)) folder = AssemblyFolder;

            return Path.Combine(folder, "OmniClass Shared Parameters.txt");
        }

        private static string Resolve(string folder, string value)
        {
            return Path.IsPathRooted(value) ? value : Path.GetFullPath(Path.Combine(folder, value));
        }
    }
}
