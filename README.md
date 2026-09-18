# Ai-COBie

## Purge Unused Elements - Report & Confirm (Dynamo for Revit 2025 / 2026 / 2027)

A Dynamo graph that answers the question "what would *Purge Unused* actually delete?",
exports the answer to an Excel workbook with headers, and then performs the full
**Purge Unused Elements** action only after the team has confirmed it.

It runs on the **out-of-the-box Dynamo install** - the built-in CPython3 engine and the
Python standard library only. No Dynamo packages, no Excel installation, and no COM
interop: the `.xlsx` is written directly as an Office Open XML package.

### What is in the repository

| Path | Purpose |
| --- | --- |
| `dynamo/PurgeUnusedElements_ReportAndConfirm_Revit2025.dyn` | Graph for Revit 2025 |
| `dynamo/PurgeUnusedElements_ReportAndConfirm_Revit2026.dyn` | Graph for Revit 2026 |
| `dynamo/PurgeUnusedElements_ReportAndConfirm_Revit2027.dyn` | Graph for Revit 2027 |
| `scripts/purge_unused_report.py` | Source of truth for the Python node embedded in all three graphs |
| `tools/build_dyn.py` | Regenerates the graphs from the script (`--check` verifies they are current) |
| `tools/make_sample_report.py` | Produces sample workbooks without Revit, for previewing the output |
| `tests/` | Tests that run the script against a stubbed Revit API |
| `docs/team-workflow.md` | The approval routine for BIM/project teams |

The three graphs are deliberately identical apart from their name and release banner: the
Python inside adapts to the Revit API it finds at run time, so one behaviour is maintained
instead of three. They are stamped as Dynamo 3.0 (the version shipped with Revit 2025)
because newer Dynamo releases open that schema silently, whereas a newer stamp makes older
Dynamo warn about a file "from a newer version".

## Requirements

- Revit 2025, 2026 or 2027 with Dynamo for Revit (any 3.x).
- Python engine **CPython3** - the default in Dynamo 3.x. IronPython is not needed or used.
- Write access to the folder the report is written to.
- Microsoft Excel is **not** required to create the report (only to read it comfortably).

## Running it

### From Dynamo

1. Open the model, then `Manage > Dynamo`.
2. Open the `.dyn` for your Revit release. The graph opens in **Manual** run mode on purpose,
   so nothing touches the model until you press Run.
3. Set the inputs in the green **1. Settings** group (see below). Leave `Confirm Purge`
   as `false` for the first, report-only run.
4. Press **Run**. The `Results` watch node lists what was found and where the report was written.
5. Circulate the workbook. When the team approves, set `Confirm Purge` to `true`, type
   `PURGE` into `Confirmation Keyword`, and run again. Revit then asks for a final Yes/No.

### From Dynamo Player

All six inputs are exposed as Player inputs, in order, so the graph can be run from
`Manage > Dynamo Player` without opening Dynamo. The report-only default (`Confirm Purge`
off, keyword blank) means a careless click cannot delete anything.

### Inputs

| # | Input | Default | Notes |
| --- | --- | --- | --- |
| 0 | Export Folder | blank | Blank = the model's folder, or `Documents` if the model is unsaved. Falls back to the temp folder if the path cannot be written. |
| 1 | Report File Name | blank | Blank = `PurgeUnused_<model>_<yyyymmdd_hhmmss>`. A `.xlsx` suffix is not doubled. |
| 2 | Confirm Purge | `false` | `false` = report only. `true` = the team authorises deletion. |
| 3 | Confirmation Keyword | blank | Must read `PURGE` (case and surrounding spaces are ignored) before anything is deleted. |
| 4 | Show Confirmation Dialog in Revit | `true` | Shows a Yes/No dialog with the element count, the group breakdown and the report path. |
| 5 | Max Purge Passes | `5` | Purging repeats until nothing new appears, because some elements only become unused once the elements that referenced them are gone. |

## The Excel output

### `<name>.xlsx` - written on every run, before anything is deleted

- **Summary** (`Item`, `Value`) - report and script version, timestamp, who ran it, model name
  and path, model type, worksharing state, Revit version and build, the detection method used,
  the totals, the run status, and a count for every purge group.
- **Unused Elements** (`#`, `Purge Group`, `Category`, `Family`, `Name / Type`, `Element Id`,
  `Unique Id`, `API Class`, `Workset`, `Last Changed By`, `Notes`) - one row per element,
  sorted by group, category and name. `Workset` and `Last Changed By` are filled in for
  workshared models up to 2,500 rows.
- **By Purge Group** (`Purge Group`, `Unused Count`) - the one-screen view for a coordination call.

Headers are bold on a dark fill, the header row is frozen, AutoFilter is switched on, and
column widths are fitted to the content.

### `<name>_PurgeLog.xlsx` - written only when a purge was approved and run

- **Summary** - the same metadata plus who confirmed the purge and when, passes run, elements
  removed (including dependents Revit takes with them), elements that could not be deleted,
  unused elements still remaining, and a pointer back to the pre-purge report.
- **Purge Log** (`Pass`, `Purge Group`, `Category`, `Family`, `Name / Type`, `Element Id`,
  `Result`, `Details`) - the audit trail, one row per element per pass. `Result` is `Deleted`,
  `Skipped` (Revit already removed it as a dependent of another element), or `Failed` with the
  reason from the API.

If the workbook cannot be written for any reason, the script falls back to a CSV with the same
headers and says so in the results.

## How "unused" is determined

The script calls the same engine Revit's own **Purge Unused** command uses:
`Document.GetAllUnusedElements`, falling back to `Document.GetUnusedElements`. Both are probed
by name at run time, so a signature change in a future release cannot break the graph. If
neither is available, the script runs a heuristic scan - it walks everything referenced by
placed instances and by the types those instances use, then reports the remaining types,
materials, patterns, appearance assets, filters, group types and families. The method that was
used is recorded in the Summary sheet, so a report never hides which path it took.

Deletion runs inside a single Revit transaction per pass, in chunks, with a per-element retry
so that one element Revit refuses to delete cannot abandon the rest of the pass.

## Safety notes

- Nothing is deleted unless all gates are satisfied: the toggle, the typed keyword, and the
  in-Revit dialog (when enabled).
- The report is always written before the purge, so there is a record of the pre-purge state
  even if the purge is interrupted.
- The purge is a normal Revit transaction, so `Ctrl+Z` still undoes it in the session - but once
  a workshared model is synchronised it is permanent. Confirm on a local copy first.
- Purging removes content other people may be about to use. Agree a window with the team; the
  workbook plus the log is the paper trail.

## Development

```bash
python tools/build_dyn.py            # rebuild the three .dyn files from the script
python tools/build_dyn.py --check    # fail if a .dyn is out of date
python -m unittest discover -s tests # run the test suite
python tools/make_sample_report.py --out sample-output --purge   # preview the workbooks
```

Edit `scripts/purge_unused_report.py`, never the code inside a `.dyn`, then rebuild. The tests
stub the Revit API (`tests/fake_revit.py`) so the whole flow - detection, report, the
confirmation gates, chunked deletion, multi-pass purging - is exercised on a plain CPython
interpreter. `openpyxl` is a test-only dependency used to read the generated workbooks back;
the script itself never imports it.
