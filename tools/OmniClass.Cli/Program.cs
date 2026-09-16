using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OmniClass.Core.Audit;
using OmniClass.Core.Loading;
using OmniClass.Core.Matching;
using OmniClass.Core.Model;
using OmniClass.Core.Reporting;

namespace OmniClass.Cli
{
    /// <summary>
    /// Lets the matching rules be judged on real room names before any Revit code exists.
    /// Export a room schedule to CSV, run 'audit', and the hit rate tells you whether the
    /// dictionary is worth wiring into the model.
    /// </summary>
    internal static class Program
    {
        private const string Usage = @"omniclass - OmniClass Table 13 room classification

  validate <dictionary.csv>
      Check the authoring sheet and print every problem with its cell reference.

  audit <rooms.csv> [dictionary.csv] [--column <name|index>] [--out <report.csv>]
      Group room names by their normalized form, rank by frequency, and show what
      each one would be classified as. This is the report that grows the dictionary.

  classify <rooms.csv> <dictionary.csv> [--column <name|index>] [--out <report.csv>]
      One row per room name with its status, proposed number and rival candidates.

Room names are read from the column whose header contains 'Name', or the first
column. Use --column to choose explicitly.";

        private static int Main(string[] args)
        {
            try
            {
                return Run(args);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("Error: " + ex.Message);
                return 1;
            }
        }

        private static int Run(string[] args)
        {
            if (args.Length == 0 || IsHelp(args[0]))
            {
                Console.WriteLine(Usage);
                return args.Length == 0 ? 1 : 0;
            }

            var options = ParseOptions(args, out var positional);

            switch (positional[0].ToLowerInvariant())
            {
                case "validate":
                    return Validate(Require(positional, 1, "dictionary.csv"));

                case "audit":
                    return Audit(
                        Require(positional, 1, "rooms.csv"),
                        positional.Count > 2 ? positional[2] : null,
                        options);

                case "classify":
                    return Classify(
                        Require(positional, 1, "rooms.csv"),
                        Require(positional, 2, "dictionary.csv"),
                        options);

                default:
                    Console.Error.WriteLine($"Unknown command '{positional[0]}'.");
                    Console.Error.WriteLine(Usage);
                    return 1;
            }
        }

        private static int Validate(string dictionaryPath)
        {
            var dictionary = AliasSheetLoader.LoadFile(dictionaryPath);

            Console.WriteLine($"{dictionary.Entries.Count} classifications, {dictionary.Aliases.Count} distinct aliases.");
            Console.WriteLine();

            foreach (var severity in new[] { ValidationSeverity.Error, ValidationSeverity.Warning, ValidationSeverity.Info })
            {
                var messages = dictionary.Messages.Where(m => m.Severity == severity).ToList();
                if (messages.Count == 0) continue;

                Console.WriteLine($"{severity} ({messages.Count})");
                foreach (var message in messages)
                {
                    Console.WriteLine($"  {message.Location,-6} {message.Code,-18} {message.Message}");
                }
                Console.WriteLine();
            }

            if (dictionary.HasErrors)
            {
                Console.WriteLine("Fix the errors above before publishing this dictionary.");
                return 1;
            }

            Console.WriteLine("No errors.");
            return 0;
        }

        private static int Audit(string roomsPath, string dictionaryPath, CliOptions options)
        {
            var names = RoomNameFile.Read(roomsPath, options.Column);
            var tallies = RoomNameAudit.Tally(names);

            var classifier = dictionaryPath == null
                ? null
                : new RoomClassifier(AliasSheetLoader.LoadFile(dictionaryPath));

            Console.WriteLine($"{names.Count} rooms, {tallies.Count} distinct names after normalizing.");

            if (classifier != null)
            {
                var results = tallies
                    .Select(t => new { Tally = t, Result = classifier.Classify(t.MostCommonVariant) })
                    .ToList();

                Console.WriteLine();
                Summarize(results.Select(r => new KeyValuePair<MatchStatus, int>(r.Result.Status, r.Tally.Count)), names.Count);

                var unmatched = results
                    .Where(r => r.Result.Status == MatchStatus.Unmatched)
                    .OrderByDescending(r => r.Tally.Count)
                    .Take(20)
                    .ToList();

                if (unmatched.Count > 0)
                {
                    Console.WriteLine();
                    Console.WriteLine("Most common names with no classification - add these to the sheet first:");
                    foreach (var row in unmatched)
                    {
                        Console.WriteLine($"  {row.Tally.Count,6}  {row.Tally.MostCommonVariant}");
                    }
                }
            }

            WriteReport(options.Out, writer => ReportWriter.WriteAudit(writer, tallies, classifier));
            return 0;
        }

        private static int Classify(string roomsPath, string dictionaryPath, CliOptions options)
        {
            var dictionary = AliasSheetLoader.LoadFile(dictionaryPath);

            if (dictionary.HasErrors)
            {
                Console.Error.WriteLine("The dictionary has errors. Run 'validate' first.");
                return 1;
            }

            var names = RoomNameFile.Read(roomsPath, options.Column);
            var results = new RoomClassifier(dictionary).ClassifyAll(names);

            Summarize(results.Select(r => new KeyValuePair<MatchStatus, int>(r.Status, 1)), names.Count);

            WriteReport(options.Out, writer => ReportWriter.WriteResults(writer, results));
            return 0;
        }

        private static void Summarize(IEnumerable<KeyValuePair<MatchStatus, int>> statuses, int total)
        {
            var counts = statuses
                .GroupBy(s => s.Key)
                .ToDictionary(g => g.Key, g => g.Sum(s => s.Value));

            foreach (var status in new[] { MatchStatus.Exact, MatchStatus.Probable, MatchStatus.Ambiguous, MatchStatus.Unmatched })
            {
                counts.TryGetValue(status, out var count);
                var share = total == 0 ? 0d : 100d * count / total;
                var note = status == MatchStatus.Exact ? "applied unattended" : "needs a human";
                Console.WriteLine($"  {status,-10} {count,6}  {share,5:0.0}%  ({note})");
            }
        }

        private static void WriteReport(string path, Action<TextWriter> write)
        {
            if (path == null)
            {
                write(Console.Out);
                return;
            }

            using (var writer = new StreamWriter(path, append: false))
            {
                write(writer);
            }

            Console.WriteLine();
            Console.WriteLine("Report written to " + path);
        }

        private sealed class CliOptions
        {
            public string Column { get; set; }
            public string Out { get; set; }
        }

        private static CliOptions ParseOptions(string[] args, out List<string> positional)
        {
            var options = new CliOptions();
            positional = new List<string>();

            for (var i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--column":
                    case "-c":
                        options.Column = NextValue(args, ref i, "--column");
                        break;

                    case "--out":
                    case "-o":
                        options.Out = NextValue(args, ref i, "--out");
                        break;

                    default:
                        positional.Add(args[i]);
                        break;
                }
            }

            return options;
        }

        private static string NextValue(string[] args, ref int index, string name)
        {
            if (index + 1 >= args.Length) throw new ArgumentException($"{name} needs a value.");
            return args[++index];
        }

        private static string Require(List<string> positional, int index, string name)
        {
            if (positional.Count <= index) throw new ArgumentException($"Missing argument <{name}>.");
            return positional[index];
        }

        private static bool IsHelp(string arg) =>
            arg == "-h" || arg == "--help" || arg == "help";
    }
}
