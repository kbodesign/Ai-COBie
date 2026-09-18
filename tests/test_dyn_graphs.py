# -*- coding: utf-8 -*-
"""Structural checks on the generated Dynamo graphs."""

import json
import os
import subprocess
import sys
import unittest

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.join(REPO_ROOT, "tools"))

import build_dyn  # noqa: E402

EXPECTED_INPUT_LABELS = [
    "Export Folder (blank = model folder)",
    "Report File Name (blank = auto)",
    "Confirm Purge (False = report only)",
    "Confirmation Keyword (type PURGE)",
    "Show Confirmation Dialog in Revit",
    "Max Purge Passes (1-10)",
]


def load(year):
    with open(build_dyn.graph_path(year), "r", encoding="utf-8") as handle:
        return json.load(handle)


def node_of_type(graph, node_type):
    return next(node for node in graph["Nodes"] if node["NodeType"] == node_type)


class GraphFileTests(unittest.TestCase):
    years = ["2025", "2026", "2027"]

    def test_one_graph_per_supported_release(self):
        for year in self.years:
            path = build_dyn.graph_path(year)
            self.assertTrue(os.path.isfile(path), path)
            self.assertEqual("PurgeUnusedElements_ReportAndConfirm_Revit%s" % year, load(year)["Name"])

    def test_graphs_are_up_to_date_with_the_script(self):
        result = subprocess.run(
            [sys.executable, os.path.join("tools", "build_dyn.py"), "--check"],
            cwd=REPO_ROOT,
            capture_output=True,
            text=True,
        )
        self.assertEqual(0, result.returncode, result.stdout + result.stderr)

    def test_no_package_dependencies(self):
        for year in self.years:
            graph = load(year)
            self.assertEqual([], graph["NodeLibraryDependencies"])
            self.assertEqual([], graph["Dependencies"])

    def test_graphs_open_in_manual_run_mode(self):
        # Automatic mode would let a graph act on the model as soon as it opens.
        for year in self.years:
            self.assertEqual("Manual", load(year)["View"]["Dynamo"]["RunType"])

    def test_graphs_target_the_dynamo_version_shipped_with_revit_2025(self):
        for year in self.years:
            self.assertEqual("3.0.3.8360", load(year)["View"]["Dynamo"]["Version"])

    def test_each_graph_has_a_unique_uuid(self):
        uuids = [load(year)["Uuid"] for year in self.years]
        self.assertEqual(len(uuids), len(set(uuids)))


class GraphWiringTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.graph = load("2026")
        cls.python = node_of_type(cls.graph, "PythonScriptNode")

    def test_identifiers_are_unique(self):
        node_ids = [node["Id"] for node in self.graph["Nodes"]]
        port_ids = [
            port["Id"]
            for node in self.graph["Nodes"]
            for port in node["Inputs"] + node["Outputs"]
        ]
        self.assertEqual(len(node_ids), len(set(node_ids)))
        self.assertEqual(len(port_ids), len(set(port_ids)))

    def test_every_python_input_is_fed_exactly_once(self):
        ends = [connector["End"] for connector in self.graph["Connectors"]]
        self.assertEqual(6, len(self.python["Inputs"]))
        for index, port in enumerate(self.python["Inputs"]):
            self.assertEqual("IN[%d]" % index, port["Name"])
            self.assertEqual(1, ends.count(port["Id"]), port["Name"])

    def test_connectors_reference_real_ports(self):
        outputs = {port["Id"] for node in self.graph["Nodes"] for port in node["Outputs"]}
        inputs = {port["Id"] for node in self.graph["Nodes"] for port in node["Inputs"]}
        for connector in self.graph["Connectors"]:
            self.assertIn(connector["Start"], outputs)
            self.assertIn(connector["End"], inputs)

    def test_python_output_reaches_the_watch_node(self):
        watch = node_of_type(self.graph, "ExtensionNode")
        pairs = {(connector["Start"], connector["End"]) for connector in self.graph["Connectors"]}
        self.assertIn((self.python["Outputs"][0]["Id"], watch["Inputs"][0]["Id"]), pairs)

    def test_node_views_and_groups_cover_every_node(self):
        node_ids = {node["Id"] for node in self.graph["Nodes"]}
        view_ids = {view["Id"] for view in self.graph["View"]["NodeViews"]}
        grouped = {node_id for group in self.graph["View"]["Annotations"] for node_id in group["Nodes"]}
        self.assertEqual(node_ids, view_ids)
        self.assertEqual(node_ids, grouped)

    def test_inputs_are_exposed_to_dynamo_player_in_order(self):
        labels = [
            view["Name"] for view in self.graph["View"]["NodeViews"] if view["IsSetAsInput"]
        ]
        self.assertEqual(EXPECTED_INPUT_LABELS, labels)
        outputs = [view["Name"] for view in self.graph["View"]["NodeViews"] if view["IsSetAsOutput"]]
        self.assertEqual(["Results"], outputs)

    def test_defaults_cannot_purge_anything(self):
        booleans = [node["InputValue"] for node in self.graph["Nodes"] if node["NodeType"] == "BooleanInputNode"]
        strings = [node["InputValue"] for node in self.graph["Nodes"] if node["NodeType"] == "StringInputNode"]

        self.assertEqual([False, True], booleans)  # Confirm Purge off, dialog on
        # folder, file name and the confirmation keyword all blank; only the pass count is set
        self.assertEqual(["", "", "", "5"], strings)

    def test_only_node_types_present_in_every_dynamo_3_release_are_used(self):
        # Sliders and other input nodes have changed concrete type between
        # releases; these four have not.
        allowed = {"StringInputNode", "BooleanInputNode", "PythonScriptNode", "ExtensionNode"}
        self.assertEqual(allowed, {node["NodeType"] for node in self.graph["Nodes"]})


class EmbeddedScriptTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        with open(build_dyn.SCRIPT_PATH, "r", encoding="utf-8") as handle:
            cls.source = handle.read()

    def test_uses_the_builtin_cpython3_engine(self):
        for year in ["2025", "2026", "2027"]:
            python = node_of_type(load(year), "PythonScriptNode")
            self.assertEqual("CPython3", python["Engine"])
            self.assertEqual("CPython3", python["EngineName"])

    def test_embedded_code_matches_the_source_of_truth(self):
        for year, label in build_dyn.TARGETS:
            code = node_of_type(load(year), "PythonScriptNode")["Code"]
            self.assertTrue(code.endswith(self.source), year)
            self.assertIn("target: %s" % label, code.split("\n\n")[0])
            self.assertIn("Rebuild with:    python tools/build_dyn.py", code)

    def test_embedded_code_compiles(self):
        for year in ["2025", "2026", "2027"]:
            code = node_of_type(load(year), "PythonScriptNode")["Code"]
            compile(code, "embedded-%s" % year, "exec")

    def test_graphs_differ_only_by_release_metadata(self):
        base = json.dumps(load("2025"))
        for year in ["2026", "2027"]:
            other = json.dumps(load(year))
            self.assertEqual(len(base.split("\n")), len(other.split("\n")))
            self.assertNotEqual(base, other)


if __name__ == "__main__":
    unittest.main(verbosity=2)
