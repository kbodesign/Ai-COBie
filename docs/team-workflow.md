# Purge Unused Elements - team approval routine

Purging is quick to run and impossible to reverse once a workshared model is synchronised.
This is the routine the graph is built around: report first, decide together, purge with a log.

## 1. Report (anyone on the team, any time)

1. Open the model. For a workshared model, open your local copy and synchronise first so the
   report reflects the central state.
2. Run `dynamo/PurgeUnusedElements_ReportAndConfirm_Revit<year>.dyn` with the defaults
   (`Confirm Purge` = `false`, `Confirmation Keyword` blank). Nothing is modified.
3. Circulate `PurgeUnused_<model>_<timestamp>.xlsx`.

Reading the workbook:

- **By Purge Group** first - it shows where the weight is (usually family types and materials).
- **Unused Elements** for the line-by-line review. Filter `Purge Group` to walk one discipline's
  content at a time; `Family`, `Category` and `Last Changed By` tell you who to ask.
- **Summary** to confirm which model, which Revit version and which detection method produced
  the numbers.

## 2. Decide

Sign-off should cover, at minimum:

- Content that is unused today but is expected to be used shortly (a design option about to be
  placed, a consultant family staged ahead of a package).
- Titleblocks, annotation and view types owned by the company standard rather than the project.
- Linked CAD/Revit types and images that a consultant may reissue.

Record the decision where the team normally records decisions, and attach the workbook. The
workbook filename carries the timestamp so it can be matched to the log produced in step 3.

## 3. Purge (one nominated person, in an agreed window)

1. Tell the team the model will be purged and ask everyone to synchronise and close.
2. Run the same graph with:
   - `Confirm Purge` = `true`
   - `Confirmation Keyword` = `PURGE`
   - `Show Confirmation Dialog in Revit` = `true`
3. Revit shows the element count, the group breakdown and the path of the report. Check the count
   matches the workbook that was approved, then choose **Yes**.
4. Keep `PurgeUnused_<model>_<timestamp>_PurgeLog.xlsx`. It records who confirmed, when, what was
   removed, what Revit refused to remove and why.
5. Review the model (a quick pass over key views and schedules), then synchronise with a comment
   such as `Purge Unused - see PurgeUnused_<model>_<timestamp>_PurgeLog.xlsx`.

## If something was purged in error

- Before synchronising: `Ctrl+Z` in the Revit session undoes the purge transaction.
- After synchronising: restore the element from the previous central backup or reload the family
  from the library. The purge log lists the family and type names needed to do so.

## Notes on repeat runs

- Elements that only become unused once their users are deleted are picked up by later passes in
  the same run; `Max Purge Passes` (default 5) bounds this.
- Rows logged as `Failed` are normally elements Revit will not delete - for example the last
  remaining type of a system family. They are expected to reappear in the next report; leave them.
- A model that has just been purged should report `0` unused elements. If it does not, read the
  `Details` column in the log before running again.
