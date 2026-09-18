#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Build the Revit 2025/2026/2027 Dynamo graphs from scripts/purge_unused_report.py.

    python tools/build_dyn.py            # write dynamo/*.dyn
    python tools/build_dyn.py --check    # fail if the .dyn files are stale

Node/port GUIDs are derived from stable names so rebuilding a graph produces a
byte-identical file and diffs stay reviewable.
"""

import argparse
import json
import os
import sys
import uuid

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SCRIPT_PATH = os.path.join(REPO_ROOT, "scripts", "purge_unused_report.py")
OUTPUT_DIR = os.path.join(REPO_ROOT, "dynamo")

# Dynamo 3.0 is the version shipped with Revit 2025; later Dynamo releases open
# and migrate this schema silently, while stamping a newer version would make
# Revit 2025 complain about a file "from a newer version".
DYNAMO_VERSION = "3.0.3.8360"

TARGETS = [
    ("2025", "Revit 2025 (Dynamo 3.0+)"),
    ("2026", "Revit 2026 (Dynamo 3.x)"),
    ("2027", "Revit 2027 (Dynamo 3.x)"),
]

GRAPH_DESCRIPTION = (
    "Reports every element Revit's 'Purge Unused' command would remove, exports it to a "
    "formatted Excel workbook with headers, and only performs the full purge after the team "
    "confirms it (toggle + typed keyword + in-Revit Yes/No dialog). Uses the built-in CPython3 "
    "engine only - no Dynamo packages and no Excel installation required."
)

NAMESPACE = uuid.UUID("3f1c9e2a-8d54-4b7a-9f2e-6c0b5a71d3e4")


def guid(*parts):
    return uuid.uuid5(NAMESPACE, "|".join(parts)).hex


def port(node, kind, index, name, description, uses_default=False):
    return {
        "Id": guid(node, kind, str(index), name),
        "Name": name,
        "Description": description,
        "UsingDefaultValue": uses_default,
        "Level": 2,
        "UseLevels": False,
        "KeepListStructure": False,
    }


def string_node(key, value, description):
    out = port(key, "out", 0, "", "String")
    return {
        "ConcreteType": "CoreNodeModels.Input.StringInput, CoreNodeModels",
        "Id": guid(key, "node"),
        "NodeType": "StringInputNode",
        "Inputs": [],
        "Outputs": [out],
        "Replication": "Disabled",
        "Description": description,
        "InputValue": value,
    }


def boolean_node(key, value, description):
    out = port(key, "out", 0, "", "Boolean")
    return {
        "ConcreteType": "CoreNodeModels.Input.BoolSelector, CoreNodeModels",
        "Id": guid(key, "node"),
        "NodeType": "BooleanInputNode",
        "Inputs": [],
        "Outputs": [out],
        "Replication": "Disabled",
        "Description": description,
        "InputValue": value,
    }


def code_block_node(key, code, description):
    out = port(key, "out", 0, "", "Line 1")
    return {
        "ConcreteType": "Dynamo.Graph.Nodes.CodeBlockNodeModel, DynamoCore",
        "Id": guid(key, "node"),
        "NodeType": "CodeBlockNode",
        "Inputs": [],
        "Outputs": [out],
        "Replication": "Disabled",
        "Description": description,
        "Code": code,
    }


def python_node(key, code, input_count):
    inputs = [
        port(key, "in", index, "IN[%d]" % index, "Input #%d" % index) for index in range(input_count)
    ]
    return {
        "ConcreteType": "PythonNodeModels.PythonNode, PythonNodeModels",
        "Id": guid(key, "node"),
        "NodeType": "PythonScriptNode",
        "Inputs": inputs,
        "Outputs": [port(key, "out", 0, "OUT", "Result of the python script")],
        "Replication": "Disabled",
        "Description": "Runs an embedded Python script.",
        "Code": code,
        "Engine": "CPython3",
        "EngineName": "CPython3",
        "VariableInputPorts": True,
    }


def watch_node(key):
    return {
        "ConcreteType": "CoreNodeModels.Watch, CoreNodeModels",
        "WatchWidth": 460.0,
        "WatchHeight": 260.0,
        "Id": guid(key, "node"),
        "NodeType": "ExtensionNode",
        "Inputs": [port(key, "in", 0, "", "Node to evaluate")],
        "Outputs": [port(key, "out", 0, "", "Watch contents")],
        "Replication": "Disabled",
        "Description": "Visualizes a node's output",
    }


def connector(source_node, target_node, target_input=0):
    start = source_node["Outputs"][0]["Id"]
    end = target_node["Inputs"][target_input]["Id"]
    return {"Start": start, "End": end, "Id": guid("connector", start, end), "IsHidden": "False"}


def node_view(node, name, x, y, is_input=False, is_output=False):
    return {
        "Id": node["Id"],
        "Name": name,
        "IsSetAsInput": is_input,
        "IsSetAsOutput": is_output,
        "Excluded": False,
        "ShowGeometry": True,
        "X": float(x),
        "Y": float(y),
    }


def annotation(key, title, nodes, left, top, width, height, background):
    return {
        "Id": guid("annotation", key),
        "Title": title,
        "DescriptionText": None,
        "IsExpanded": True,
        "WidthAdjustment": 0.0,
        "HeightAdjustment": 0.0,
        "Nodes": [node["Id"] for node in nodes],
        "HasNestedGroups": False,
        "Left": float(left),
        "Top": float(top),
        "Width": float(width),
        "Height": float(height),
        "FontSize": 30.0,
        "GroupStyleId": "00000000-0000-0000-0000-000000000000",
        "InitialTop": float(top + 46),
        "InitialHeight": float(height),
        "TextblockHeight": 46.0,
        "Background": background,
    }


def embedded_code(script_source, revit_label):
    header = [
        "# %s" % ("-" * 74),
        "# Purge Unused Elements - Report & Confirm | target: %s" % revit_label,
        "# GENERATED FILE - do not edit inside Dynamo.",
        "# Source of truth: scripts/purge_unused_report.py",
        "# Rebuild with:    python tools/build_dyn.py",
        "# %s" % ("-" * 74),
        "",
    ]
    return "\n".join(header) + script_source


def build_graph(revit_year, revit_label, script_source):
    settings = [
        (
            "export_folder",
            string_node(
                "export_folder",
                "",
                "Folder for the Excel report. Leave blank to use the folder of the model "
                "(or Documents when the model is unsaved).",
            ),
            "Export Folder (blank = model folder)",
        ),
        (
            "report_name",
            string_node(
                "report_name",
                "",
                "Report file name without extension. Leave blank for "
                "PurgeUnused_<model>_<timestamp>.xlsx.",
            ),
            "Report File Name (blank = auto)",
        ),
        (
            "confirm_purge",
            boolean_node(
                "confirm_purge",
                False,
                "False = report only. True = the team authorises deleting the unused elements.",
            ),
            "Confirm Purge (False = report only)",
        ),
        (
            "keyword",
            string_node(
                "keyword",
                "",
                "Second authorisation factor: type PURGE to allow deletion.",
            ),
            "Confirmation Keyword (type PURGE)",
        ),
        (
            "show_dialog",
            boolean_node(
                "show_dialog",
                True,
                "True = show a Yes/No confirmation dialog inside Revit before purging.",
            ),
            "Show Confirmation Dialog in Revit",
        ),
        (
            "max_passes",
            code_block_node(
                "max_passes",
                "5;",
                "How many detect-and-delete passes to run; elements that only become unused "
                "after their users are gone are picked up by later passes.",
            ),
            "Max Purge Passes",
        ),
    ]

    script = python_node("main", embedded_code(script_source, revit_label), len(settings))
    watch = watch_node("results")

    nodes = [item[1] for item in settings] + [script, watch]
    connectors = [connector(item[1], script, index) for index, item in enumerate(settings)]
    connectors.append(connector(script, watch))

    node_views = []
    for index, (_, node, label) in enumerate(settings):
        node_views.append(node_view(node, label, 0, 140 + index * 130, is_input=True))
    node_views.append(node_view(script, "Report Unused Elements & Purge On Confirmation", 640, 300))
    node_views.append(node_view(watch, "Results", 1060, 300, is_output=True))

    annotations = [
        annotation(
            "settings",
            "1. Settings - review before running (Dynamo Player inputs)",
            [item[1] for item in settings],
            -30,
            60,
            420,
            880,
            "#FFC1D676",
        ),
        annotation(
            "engine",
            "2. Report to Excel, then purge only once confirmed",
            [script],
            610,
            230,
            360,
            340,
            "#FFFFC999",
        ),
        annotation("results", "3. Results", [watch], 1030, 230, 500, 340, "#FFB5B5B5"),
    ]

    return {
        "Uuid": str(uuid.uuid5(NAMESPACE, "graph|%s" % revit_year)),
        "IsCustomNode": False,
        "Description": GRAPH_DESCRIPTION,
        "Name": "PurgeUnusedElements_ReportAndConfirm_Revit%s" % revit_year,
        "ElementResolver": {"ResolutionMap": {}},
        "Inputs": [],
        "Outputs": [],
        "Nodes": nodes,
        "Connectors": connectors,
        "Dependencies": [],
        "NodeLibraryDependencies": [],
        "EnableLegacyPolyCurveBehavior": True,
        "Thumbnail": "",
        "GraphDocumentationURL": None,
        "ExtensionWorkspaceData": [],
        "Author": "",
        "Linting": {
            "activeLinter": "None",
            "activeLinterId": "7b75fb44-43fd-4631-a878-29f4d5d8399a",
            "warningCount": 0,
            "errorCount": 0,
        },
        "Bindings": [],
        "View": {
            "Dynamo": {
                "ScaleFactor": 1.0,
                "HasRunWithoutCrash": True,
                "IsVisibleInDynamoLibrary": True,
                "Version": DYNAMO_VERSION,
                "RunType": "Manual",
                "RunPeriod": "1000",
            },
            "Camera": {
                "Name": "_Background Preview",
                "EyeX": -17.0,
                "EyeY": 24.0,
                "EyeZ": 50.0,
                "LookX": 12.0,
                "LookY": -13.0,
                "LookZ": -58.0,
                "UpX": 0.0,
                "UpY": 1.0,
                "UpZ": 0.0,
            },
            "ConnectorPins": [],
            "NodeViews": node_views,
            "Annotations": annotations,
        },
    }


def graph_path(revit_year):
    return os.path.join(OUTPUT_DIR, "PurgeUnusedElements_ReportAndConfirm_Revit%s.dyn" % revit_year)


def render(revit_year, revit_label, script_source):
    return json.dumps(build_graph(revit_year, revit_label, script_source), indent=2) + "\n"


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--check", action="store_true", help="verify the .dyn files are up to date")
    args = parser.parse_args(argv)

    with open(SCRIPT_PATH, "r", encoding="utf-8") as handle:
        script_source = handle.read()

    if not os.path.isdir(OUTPUT_DIR):
        os.makedirs(OUTPUT_DIR)

    stale = []
    for revit_year, revit_label in TARGETS:
        path = graph_path(revit_year)
        content = render(revit_year, revit_label, script_source)
        if args.check:
            existing = ""
            if os.path.isfile(path):
                with open(path, "r", encoding="utf-8") as handle:
                    existing = handle.read()
            if existing != content:
                stale.append(os.path.relpath(path, REPO_ROOT))
            continue
        with open(path, "w", encoding="utf-8", newline="\n") as handle:
            handle.write(content)
        print("wrote %s" % os.path.relpath(path, REPO_ROOT))

    if args.check:
        if stale:
            print("stale graph(s): %s\nrun: python tools/build_dyn.py" % ", ".join(stale))
            return 1
        print("all graphs are up to date")
    return 0


if __name__ == "__main__":
    sys.exit(main())
