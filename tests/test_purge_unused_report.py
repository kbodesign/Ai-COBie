# -*- coding: utf-8 -*-
"""End-to-end tests for the Dynamo script, driven by the fake Revit runtime."""

import os
import sys
import tempfile
import unittest

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import fake_revit  # noqa: E402
from fake_revit import FakeDocument, FakeTaskDialog, make_element, run_script  # noqa: E402

import openpyxl  # noqa: E402  (test-only dependency, never used inside Revit)


def sample_document(**kwargs):
    elements = [
        make_element("FamilySymbol", 101, "600 x 900mm", category="Doors", family="Single-Flush"),
        make_element("FamilySymbol", 102, "900 x 2100mm", category="Doors", family="Single-Flush"),
        make_element("Material", 201, 'Concrete "Cast <in> Place" & Precast'),
        make_element("LinePatternElement", 301, "Dash 3mm"),
        make_element("WallType", 401, "Generic - 200mm", category="Walls"),
        make_element("ParameterFilterElement", 501, "Unused filter"),
        make_element("Family", 601, "Obsolete Casework"),
        make_element("GroupType", 701, "Room Kit (old)"),
    ]
    used = [make_element("WallType", 999, "Generic - 100mm (in use)", category="Walls")]
    unused_ids = [101, 102, 201, 301, 401, 501, 601, 701]
    return FakeDocument(elements=elements + used, unused=unused_ids, **kwargs)


def load_workbook_rows(path, sheet_name):
    workbook = openpyxl.load_workbook(path)
    sheet = workbook[sheet_name]
    return [[cell for cell in row] for row in sheet.iter_rows(values_only=True)]


def summary_lookup(path):
    return {row[0]: row[1] for row in load_workbook_rows(path, "Summary") if row and row[0]}


class ReportOnlyTests(unittest.TestCase):
    def setUp(self):
        self.folder = tempfile.mkdtemp(prefix="purge-report-")
        self.document = sample_document()

    def run_report(self, inputs=None, document=None, ui_available=True):
        inputs = inputs or [self.folder, "UnusedReport", False, "", False, 5]
        out, _ = run_script(document or self.document, inputs, ui_available=ui_available)
        return out

    def test_report_is_written_and_nothing_is_deleted(self):
        out = self.run_report()
        report = os.path.join(self.folder, "UnusedReport.xlsx")

        self.assertTrue(os.path.isfile(report), out)
        self.assertIn("Unused elements found: 8", out)
        self.assertTrue(any("REPORT ONLY" in line for line in out), out)
        self.assertEqual([], self.document.deleted_ids)

    def test_workbook_structure_and_headers(self):
        self.run_report()
        workbook = openpyxl.load_workbook(os.path.join(self.folder, "UnusedReport.xlsx"))

        self.assertEqual(["Summary", "Unused Elements", "By Purge Group"], workbook.sheetnames)
        detail = workbook["Unused Elements"]
        headers = [cell.value for cell in detail[1]]
        self.assertEqual(
            [
                "#",
                "Purge Group",
                "Category",
                "Family",
                "Name / Type",
                "Element Id",
                "Unique Id",
                "API Class",
                "Workset",
                "Last Changed By",
                "Notes",
            ],
            headers,
        )
        self.assertEqual(9, detail.max_row)  # header + 8 unused elements
        self.assertEqual("A2", detail.freeze_panes)
        self.assertTrue(detail.auto_filter.ref)
        self.assertTrue(detail["A1"].font.b)

    def test_rows_carry_classification_and_escaped_text(self):
        self.run_report()
        rows = load_workbook_rows(os.path.join(self.folder, "UnusedReport.xlsx"), "Unused Elements")
        by_name = {row[4]: row for row in rows[1:]}

        self.assertEqual("Family Types", by_name["600 x 900mm"][1])
        self.assertEqual("Doors", by_name["600 x 900mm"][2])
        self.assertEqual("Single-Flush", by_name["600 x 900mm"][3])
        self.assertEqual(101, by_name["600 x 900mm"][5])
        self.assertEqual("Wall Types", by_name["Generic - 200mm"][1])
        self.assertEqual("Loadable Families", by_name["Obsolete Casework"][1])
        self.assertEqual("View Filters", by_name["Unused filter"][1])
        self.assertEqual("Group Types", by_name["Room Kit (old)"][1])
        self.assertIn('Concrete "Cast <in> Place" & Precast', by_name)
        self.assertEqual("Materials", by_name['Concrete "Cast <in> Place" & Precast'][1])

        self.assertNotIn("Generic - 100mm (in use)", by_name)

    def test_summary_and_group_sheets(self):
        self.run_report()
        report = os.path.join(self.folder, "UnusedReport.xlsx")
        summary = summary_lookup(report)

        self.assertEqual("Sample Model", summary["Model"])
        self.assertEqual(8, summary["Unused elements found"])
        self.assertEqual("team.member", summary["Run by"])
        self.assertEqual("Autodesk Revit 2026", summary["Revit version"])
        self.assertIn("GetAllUnusedElements", summary["Detection method"])
        self.assertIn("awaiting team confirmation", summary["Status"])
        self.assertEqual(2, summary["Unused - Family Types"])

        groups = load_workbook_rows(report, "By Purge Group")
        self.assertEqual(["Purge Group", "Unused Count"], groups[0])
        self.assertEqual(8, sum(row[1] for row in groups[1:]))

    def test_worksharing_columns_filled_when_workshared(self):
        document = sample_document(workshared=True)
        self.run_report(document=document)
        rows = load_workbook_rows(os.path.join(self.folder, "UnusedReport.xlsx"), "Unused Elements")

        self.assertEqual({"Workset1"}, {row[8] for row in rows[1:]})
        self.assertEqual({"fake.user"}, {row[9] for row in rows[1:]})

    def test_auto_file_name_and_default_folder(self):
        model = os.path.join(self.folder, "Tower.rvt")
        document = sample_document(title="Tower", path=model)
        out = self.run_report(inputs=["", "", False, "", False, 5], document=document)

        reports = [name for name in os.listdir(self.folder) if name.endswith(".xlsx")]
        self.assertEqual(1, len(reports), reports)
        self.assertTrue(reports[0].startswith("PurgeUnused_Tower_"), reports[0])
        self.assertTrue(any(reports[0] in line for line in out), out)

    def test_empty_model_reports_nothing_to_purge(self):
        document = FakeDocument(elements=[], unused=[])
        out = self.run_report(inputs=[self.folder, "Empty", True, "PURGE", False, 5], document=document)

        self.assertIn("Unused elements found: 0", out)
        self.assertTrue(any("Nothing to purge" in line for line in out), out)
        self.assertEqual(1, load_workbook_rows(os.path.join(self.folder, "Empty.xlsx"), "Unused Elements").__len__())

    def test_heuristic_fallback_when_purge_api_is_missing(self):
        document = sample_document(supports_purge_api=False)
        out = self.run_report(document=document)
        summary = summary_lookup(os.path.join(self.folder, "UnusedReport.xlsx"))

        self.assertIn("Fallback heuristic scan", summary["Detection method"])
        self.assertTrue(any("Fallback heuristic" in line for line in out), out)
        rows = load_workbook_rows(os.path.join(self.folder, "UnusedReport.xlsx"), "Unused Elements")
        self.assertIn("Generic - 200mm", [row[4] for row in rows[1:]])


class ConfirmationGateTests(unittest.TestCase):
    def setUp(self):
        self.folder = tempfile.mkdtemp(prefix="purge-confirm-")
        self.document = sample_document()

    def run_with(self, confirm, keyword, show_dialog, dialog_answer="No", ui_available=True):
        FakeTaskDialog.answer = dialog_answer
        out, _ = run_script(
            self.document,
            [self.folder, "Report", confirm, keyword, show_dialog, 5],
            ui_available=ui_available,
        )
        return out

    def decision(self, out):
        return next(line for line in out if line.startswith("Decision:"))

    def test_toggle_off_blocks_purge(self):
        out = self.run_with(False, "PURGE", False)
        self.assertIn("REPORT ONLY", self.decision(out))
        self.assertEqual([], self.document.deleted_ids)

    def test_missing_keyword_blocks_purge(self):
        out = self.run_with(True, "", False)
        self.assertIn("type 'PURGE'", self.decision(out))
        self.assertEqual([], self.document.deleted_ids)

    def test_wrong_keyword_blocks_purge(self):
        out = self.run_with(True, "purge it", False)
        self.assertIn("REPORT ONLY", self.decision(out))
        self.assertEqual([], self.document.deleted_ids)

    def test_declined_dialog_blocks_purge(self):
        out = self.run_with(True, "PURGE", True, dialog_answer="No")
        self.assertIn("DECLINED", self.decision(out))
        self.assertEqual([], self.document.deleted_ids)
        self.assertEqual(1, len(FakeTaskDialog.shown))

    def test_dialog_shows_count_and_report_path(self):
        self.run_with(True, "PURGE", True, dialog_answer="No")
        dialog = FakeTaskDialog.shown[0]

        self.assertIn("8 unused element(s)", dialog.MainInstruction)
        self.assertIn("Sample Model", dialog.MainInstruction)
        self.assertIn(self.folder, dialog.MainContent)
        self.assertIn("Family Types: 2", dialog.ExpandedContent)

    def test_lowercase_keyword_is_accepted(self):
        out = self.run_with(True, " purge ", True, dialog_answer="Yes")
        self.assertIn("Approved in Revit by team.member", self.decision(out))

    def test_approval_without_dialog_is_recorded(self):
        out = self.run_with(True, "PURGE", False)
        self.assertIn("interactive dialog disabled", self.decision(out))
        self.assertTrue(self.document.deleted_ids)

    def test_purge_proceeds_when_no_dialog_can_be_shown(self):
        out = self.run_with(True, "PURGE", True, ui_available=False)
        self.assertIn("no confirmation dialog could be shown", self.decision(out))
        self.assertTrue(self.document.deleted_ids)


class PurgeExecutionTests(unittest.TestCase):
    def setUp(self):
        self.folder = tempfile.mkdtemp(prefix="purge-run-")
        FakeTaskDialog.answer = "Yes"

    def purge(self, document, inputs=None):
        inputs = inputs or [self.folder, "Report", True, "PURGE", True, 5]
        out, _ = run_script(document, inputs)
        return out

    def test_confirmed_purge_deletes_and_logs(self):
        document = sample_document()
        out = self.purge(document)
        log_path = os.path.join(self.folder, "Report_PurgeLog.xlsx")

        self.assertEqual([101, 102, 201, 301, 401, 501, 601, 701], sorted(document.deleted_ids))
        self.assertEqual([999], document.remaining_ids())
        self.assertTrue(os.path.isfile(log_path))
        self.assertTrue(any("PURGED - 8 element(s) removed" in line for line in out), out)
        self.assertTrue(any("Unused elements remaining: 0" in line for line in out), out)

        workbook = openpyxl.load_workbook(log_path)
        self.assertEqual(["Summary", "Purge Log"], workbook.sheetnames)
        log_rows = load_workbook_rows(log_path, "Purge Log")
        self.assertEqual(
            ["Pass", "Purge Group", "Category", "Family", "Name / Type", "Element Id", "Result", "Details"],
            log_rows[0],
        )
        self.assertEqual(8, len(log_rows) - 1)
        self.assertEqual({"Deleted"}, {row[6] for row in log_rows[1:]})
        self.assertEqual({1}, {row[0] for row in log_rows[1:]})

    def test_pre_purge_report_survives_and_log_cross_references_it(self):
        document = sample_document()
        self.purge(document)
        summary = summary_lookup(os.path.join(self.folder, "Report_PurgeLog.xlsx"))

        self.assertEqual(os.path.join(self.folder, "Report.xlsx"), summary["Pre-purge report"])
        self.assertEqual(8, summary["Elements removed (incl. dependents)"])
        self.assertEqual(0, summary["Elements that could not be deleted"])
        self.assertEqual(0, summary["Unused elements remaining"])
        self.assertIn("Approved in Revit by team.member", summary["Confirmation"])
        self.assertIn("PURGED", summary["Status"])

    def test_deletes_run_inside_a_committed_transaction(self):
        document = sample_document()
        self.purge(document)
        transactions = fake_revit.Transaction.log

        self.assertTrue(transactions)
        self.assertEqual({"committed"}, {transaction.state for transaction in transactions})
        self.assertEqual({"Purge Unused Elements (Dynamo)"}, {t.name for t in transactions})

    def test_undeletable_element_is_logged_without_stopping_the_pass(self):
        document = sample_document(undeletable={401})
        out = self.purge(document)
        log_rows = load_workbook_rows(os.path.join(self.folder, "Report_PurgeLog.xlsx"), "Purge Log")
        results = {row[4]: row[6] for row in log_rows[1:]}

        self.assertEqual("Failed", results["Generic - 200mm"])
        self.assertEqual("Deleted", results["600 x 900mm"])
        self.assertNotIn(401, document.deleted_ids)
        self.assertIn(101, document.deleted_ids)
        self.assertTrue(any("1 could not be deleted" in line for line in out), out)

    def test_large_model_is_deleted_in_chunks(self):
        # 260 unused elements span two delete chunks; the first chunk also takes
        # out two elements that the second chunk still expects to find.
        elements = [make_element("Material", 1000 + index, "Material %d" % index) for index in range(260)]
        document = FakeDocument(
            elements=elements,
            unused=[1000 + index for index in range(260)],
            dependents={1000: [1250, 1251]},
        )
        self.purge(document)
        log_rows = load_workbook_rows(os.path.join(self.folder, "Report_PurgeLog.xlsx"), "Purge Log")
        results = {row[4]: (row[6], row[7]) for row in log_rows[1:]}

        self.assertEqual(260, len(log_rows) - 1)
        self.assertEqual([], document.remaining_ids())
        self.assertEqual("Deleted", results["Material 0"][0])
        for carried in ("Material 250", "Material 251"):
            self.assertEqual("Skipped", results[carried][0])
            self.assertIn("dependent", results[carried][1])

    def test_second_pass_catches_newly_unused_elements(self):
        document = sample_document()
        # 902 only becomes purgeable once 901 is gone, like a family symbol whose
        # last instance was inside a deleted group.
        document._elements[901] = make_element("WallType", 901, "Cascade A", category="Walls")
        document._elements[902] = make_element("Material", 902, "Cascade B")
        document._unused = [901]
        original_delete = document.Delete

        def delete(element_ids):
            removed = original_delete(element_ids)
            if 901 in document.deleted_ids and 902 in document._elements:
                document._unused = [902]
            elif 902 not in document._elements:
                document._unused = []
            return removed

        document.Delete = delete
        self.purge(document)
        log_rows = load_workbook_rows(os.path.join(self.folder, "Report_PurgeLog.xlsx"), "Purge Log")

        self.assertEqual([1, 2], sorted({row[0] for row in log_rows[1:]}))
        self.assertEqual(["Cascade A", "Cascade B"], [row[4] for row in log_rows[1:]])
        self.assertEqual([901, 902], sorted(document.deleted_ids))

    def test_max_passes_is_respected(self):
        document = sample_document()
        document._unused = [401]
        document._undeletable = {401}  # never deletable, so passes would loop forever
        self.purge(document, inputs=[self.folder, "Report", True, "PURGE", True, 3])
        log_rows = load_workbook_rows(os.path.join(self.folder, "Report_PurgeLog.xlsx"), "Purge Log")

        self.assertEqual(1, len(log_rows) - 1)  # stops as soon as a pass deletes nothing
        self.assertEqual("Failed", log_rows[1][6])


class OutputPathTests(unittest.TestCase):
    def test_unwritable_folder_falls_back_to_temp(self):
        document = sample_document()
        handle, blocked = tempfile.mkstemp(prefix="not-a-folder-")
        os.close(handle)
        out, _ = run_script(document, [blocked, "Fallback", False, "", False, 5])
        report = next(line for line in out if line.startswith("Report: "))[len("Report: "):]

        self.assertTrue(os.path.isfile(report), out)
        self.assertEqual(tempfile.gettempdir(), os.path.dirname(report))
        os.remove(report)

    def test_xlsx_extension_in_name_is_not_doubled(self):
        folder = tempfile.mkdtemp(prefix="purge-name-")
        run_script(sample_document(), [folder, "Report.xlsx", False, "", False, 5])
        self.assertEqual(["Report.xlsx"], os.listdir(folder))

    def test_missing_document_reports_an_error(self):
        out, _ = run_script(None, ["", "", False, "", False, 5])
        self.assertEqual(1, len(out))
        self.assertIn("no Revit document", out[0])


if __name__ == "__main__":
    unittest.main(verbosity=2)
