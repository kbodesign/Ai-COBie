#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Produce sample report/purge-log workbooks without Revit.

Runs scripts/purge_unused_report.py against the fake Revit runtime used by the
tests, so the Excel output can be previewed or attached to documentation:

    python tools/make_sample_report.py --out sample-output
"""

import argparse
import os
import sys

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.join(REPO_ROOT, "tests"))

from fake_revit import FakeDocument, FakeTaskDialog, make_element, run_script  # noqa: E402

SAMPLE_ELEMENTS = [
    ("FamilySymbol", "Doors", "Single-Flush", "0762 x 2032mm"),
    ("FamilySymbol", "Doors", "Double-Glass 1", "1730 x 2134mm"),
    ("FamilySymbol", "Windows", "Fixed", "0406 x 0610mm"),
    ("FamilySymbol", "Furniture", "Desk", "1525 x 762mm"),
    ("FamilySymbol", "Casework", "Counter Top", "Angled"),
    ("FamilySymbol", "Generic Annotations", "Detail Callout Head", "Standard"),
    ("FamilySymbol", "Mechanical Equipment", "Fan Coil Unit", "FCU-04 (superseded)"),
    ("WallType", "Walls", "", "Interior - 79mm Partition (1-hr)"),
    ("WallType", "Walls", "", "Exterior - CMU on Mtl. Stud"),
    ("FloorType", "Floors", "", "Wood Joist 10\" - Ceramic Tile"),
    ("Material", None, "", "Concrete - Cast-in-Place - C40"),
    ("Material", None, "", "Metal - Paint Finish - Ivory White"),
    ("Material", None, "", "Glass - Green & Blue <tint>"),
    ("Material", None, "", 'Site - Asphalt "Legacy"'),
    ("AppearanceAssetElement", None, "", "Brickwork - Reclaimed"),
    ("AppearanceAssetElement", None, "", "Carpet - Loop Pile 2018"),
    ("LinePatternElement", None, "", "Dash 3mm space 1mm"),
    ("LinePatternElement", None, "", "Hidden - Consultant Import"),
    ("FillPatternElement", None, "", "Diagonal Crosshatch 1.5mm"),
    ("TextNoteType", None, "", "2.5mm Arial - Old Standard"),
    ("DimensionType", None, "", "Linear - 2.5mm Arial (superseded)"),
    ("FilledRegionType", None, "", "Solid Black - Legacy"),
    ("ViewFamilyType", None, "", "Section - Coordination (unused)"),
    ("ParameterFilterElement", None, "", "Filter - Temporary Coordination"),
    ("ParameterFilterElement", None, "", "Filter - Phase 0 Demo"),
    ("GroupType", None, "", "Typical Office Kit - 2019"),
    ("GroupType", None, "", "Toilet Pod - Option B"),
    ("Family", None, "", "M_Casework-Base Cabinet (obsolete)"),
    ("Family", None, "", "Consultant Bracket rev0"),
    ("ImageType", None, "", "site-survey-markup.png"),
    ("CADLinkType", None, "", "SURVEY-2021-OLD.dwg"),
    ("DuctType", None, "", "Round Duct - Legacy Radius"),
    ("PipeType", None, "", "Pipe Type - Consultant Import"),
]


def sample_document():
    elements = []
    unused = []
    for index, (class_name, category, family, name) in enumerate(SAMPLE_ELEMENTS):
        element_id = 200000 + index * 7
        elements.append(
            make_element(class_name, element_id, name, category=category, family=family or None)
        )
        unused.append(element_id)
    # A type that is in use, to show it is excluded from the report.
    elements.append(make_element("WallType", 310000, "Generic - 200mm (in use)", category="Walls"))
    return FakeDocument(
        title="RVT-SAMPLE-Tower",
        path="C:\\Projects\\SAMPLE\\RVT-SAMPLE-Tower.rvt",
        elements=elements,
        unused=unused,
        workshared=True,
    )


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--out", default="sample-output", help="output folder")
    parser.add_argument("--purge", action="store_true", help="also confirm the purge and log it")
    args = parser.parse_args(argv)

    folder = os.path.abspath(args.out)
    if not os.path.isdir(folder):
        os.makedirs(folder)

    FakeTaskDialog.answer = "Yes" if args.purge else "No"
    out, _ = run_script(
        sample_document(),
        [folder, "PurgeUnused_SAMPLE", args.purge, "PURGE" if args.purge else "", True, 5],
    )
    print("\n".join(out))
    return 0


if __name__ == "__main__":
    sys.exit(main())
