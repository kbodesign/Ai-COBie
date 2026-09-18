# -*- coding: utf-8 -*-
"""Purge Unused Elements - Report & Confirm (Revit 2025 / 2026 / 2027).

This is the source of truth for the Python node embedded in the Dynamo graphs
under ``dynamo/``. After editing, re-run ``python tools/build_dyn.py`` so the
.dyn files pick up the change.

Only the Dynamo built-in CPython3 engine and the Python standard library are
used - no Dynamo packages, no Excel installation, no COM interop. The .xlsx is
written directly as an Office Open XML package with ``zipfile``.

Dynamo inputs
    IN[0]  Export Folder            string  "" -> folder of the model (or Documents)
    IN[1]  Report File Name         string  "" -> PurgeUnused_<model>_<timestamp>
    IN[2]  Confirm Purge            bool    False -> report only, nothing deleted
    IN[3]  Confirmation Keyword     string  must equal "PURGE" to allow deleting
    IN[4]  Show Confirmation Dialog bool    True  -> Yes/No dialog inside Revit
    IN[5]  Max Purge Passes         int     purge repeats until stable (1-10)
"""

import os
import re
import datetime
import zipfile
import traceback

import clr

clr.AddReference("RevitAPI")
clr.AddReference("RevitServices")

import Autodesk.Revit.DB as DB  # noqa: E402
from RevitServices.Persistence import DocumentManager  # noqa: E402
from RevitServices.Transactions import TransactionManager  # noqa: E402

try:  # TaskDialog lives in the UI assembly; absent in some headless contexts.
    clr.AddReference("RevitAPIUI")
    import Autodesk.Revit.UI as UI
except Exception:
    UI = None

try:
    from System.Collections.Generic import HashSet, List
except Exception:  # pragma: no cover - only if the CLR bridge misbehaves
    HashSet = None
    List = None

SCRIPT_NAME = "Purge Unused Elements - Report & Confirm"
SCRIPT_VERSION = "1.0.0"
CONFIRMATION_KEYWORD = "PURGE"
DEFAULT_MAX_PASSES = 5
DELETE_CHUNK_SIZE = 200
WORKSHARING_INFO_LIMIT = 2500

DETAIL_HEADERS = [
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
]
GROUP_HEADERS = ["Purge Group", "Unused Count"]
SUMMARY_HEADERS = ["Item", "Value"]
PURGE_LOG_HEADERS = [
    "Pass",
    "Purge Group",
    "Category",
    "Family",
    "Name / Type",
    "Element Id",
    "Result",
    "Details",
]

# Checked in order, so the more specific classes come first. Names are resolved
# at run time because the set of published classes shifts between releases.
PURGE_GROUPS = [
    ("Family", "Loadable Families"),
    ("FamilySymbol", "Family Types"),
    ("Material", "Materials"),
    ("AppearanceAssetElement", "Appearance (Render) Assets"),
    ("LinePatternElement", "Line Patterns"),
    ("FillPatternElement", "Fill Patterns"),
    ("ParameterFilterElement", "View Filters"),
    ("FilterElement", "View Filters"),
    ("GroupType", "Group Types"),
    ("ImageType", "Images"),
    ("RevitLinkType", "Revit Link Types"),
    ("CADLinkType", "CAD Link Types"),
    ("ViewFamilyType", "View Types"),
    ("TextNoteType", "Text Styles"),
    ("DimensionType", "Dimension Styles"),
    ("SpotDimensionType", "Spot Dimension Styles"),
    ("AnnotationSymbolType", "Annotation Symbol Types"),
    ("FilledRegionType", "Filled Region Types"),
    ("WallType", "Wall Types"),
    ("FloorType", "Floor Types"),
    ("RoofType", "Roof Types"),
    ("CeilingType", "Ceiling Types"),
    ("StairsType", "Stair Types"),
    ("RailingType", "Railing Types"),
    ("PanelType", "Curtain Panel Types"),
    ("MullionType", "Mullion Types"),
    ("ContinuousRailType", "Continuous Rail Types"),
    ("BuildingPadType", "Building Pad Types"),
    ("DuctType", "Duct Types"),
    ("PipeType", "Pipe Types"),
    ("ConduitType", "Conduit Types"),
    ("CableTrayType", "Cable Tray Types"),
    ("WireType", "Wire Types"),
    ("FlexDuctType", "Flex Duct Types"),
    ("FlexPipeType", "Flex Pipe Types"),
    ("InsulationType", "Insulation Types"),
    ("LinePatternElement", "Line Patterns"),
    ("ElementType", "Other Element Types"),
]


# ---------------------------------------------------------------------------
# small helpers
# ---------------------------------------------------------------------------
def _inputs():
    return globals().get("IN", []) or []


def _arg(index, default=None):
    values = _inputs()
    try:
        value = values[index]
    except Exception:
        return default
    return default if value is None else value


def _as_bool(value, default=False):
    if isinstance(value, bool):
        return value
    if value is None:
        return default
    if isinstance(value, (int, float)):
        return value != 0
    text = str(value).strip().lower()
    if text in ("true", "yes", "y", "1", "on"):
        return True
    if text in ("false", "no", "n", "0", "off"):
        return False
    return default


def _as_int(value, default=0):
    try:
        return int(round(float(value)))
    except Exception:
        return default


def _as_text(value, default=""):
    if value is None:
        return default
    try:
        return str(value)
    except Exception:
        return default


def _safe(getter, default=""):
    """Revit properties throw more often than they should - swallow and default."""
    try:
        value = getter()
    except Exception:
        return default
    return default if value is None else value


def _id_value(element_id):
    if element_id is None:
        return None
    for attribute in ("Value", "IntegerValue"):
        raw = getattr(element_id, attribute, None)
        if raw is None:
            continue
        try:
            return int(raw)
        except Exception:
            continue
    try:
        return int(str(element_id))
    except Exception:
        return None


def _sanitize_file_name(name, fallback="PurgeUnused"):
    cleaned = re.sub(r'[\\/:*?"<>|\r\n\t]+', "_", _as_text(name)).strip(" ._")
    return cleaned or fallback


def _timestamp():
    return datetime.datetime.now().strftime("%Y%m%d_%H%M%S")


# ---------------------------------------------------------------------------
# minimal .xlsx writer (Office Open XML, standard library only)
# ---------------------------------------------------------------------------
_ILLEGAL_XML = re.compile("[\x00-\x08\x0b\x0c\x0e-\x1f]")
_SHEET_NAME_BAD = re.compile(r"[\[\]\*/\\\?:]")


def _xml_escape(value):
    text = _ILLEGAL_XML.sub("", _as_text(value))
    text = text.replace("&", "&amp;").replace("<", "&lt;").replace(">", "&gt;")
    return text.replace('"', "&quot;")


def _column_letter(index):
    """1 -> A, 27 -> AA."""
    letters = ""
    while index > 0:
        index, remainder = divmod(index - 1, 26)
        letters = chr(65 + remainder) + letters
    return letters


def _cell_xml(reference, value, style=None):
    style_attribute = ' s="%d"' % style if style else ""
    if isinstance(value, bool):
        value = "Yes" if value else "No"
    if isinstance(value, (int, float)):
        return '<c r="%s"%s><v>%s</v></c>' % (reference, style_attribute, value)
    text = _as_text(value)
    if not text:
        return '<c r="%s"%s/>' % (reference, style_attribute)
    return '<c r="%s"%s t="inlineStr"><is><t xml:space="preserve">%s</t></is></c>' % (
        reference,
        style_attribute,
        _xml_escape(text),
    )


def _column_widths(headers, rows, minimum=9, maximum=60):
    widths = []
    for column in range(len(headers)):
        longest = len(_as_text(headers[column]))
        for row in rows:
            if column < len(row):
                longest = max(longest, len(_as_text(row[column])))
        widths.append(max(minimum, min(maximum, longest + 2)))
    return widths


def _sheet_xml(headers, rows):
    column_count = max(1, len(headers))
    for row in rows:
        column_count = max(column_count, len(row))
    last_column = _column_letter(column_count)
    row_count = len(rows) + 1

    parts = [
        '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>',
        '<worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">',
        '<dimension ref="A1:%s%d"/>' % (last_column, row_count),
        '<sheetViews><sheetView workbookViewId="0">'
        '<pane ySplit="1" topLeftCell="A2" activePane="bottomLeft" state="frozen"/>'
        "</sheetView></sheetViews>",
        '<sheetFormatPr defaultRowHeight="15"/>',
    ]

    widths = _column_widths(headers, rows)
    if widths:
        parts.append("<cols>")
        for index, width in enumerate(widths, start=1):
            parts.append('<col min="%d" max="%d" width="%d" customWidth="1"/>' % (index, index, width))
        parts.append("</cols>")

    parts.append("<sheetData>")
    header_cells = [
        _cell_xml("%s1" % _column_letter(index), header, style=1)
        for index, header in enumerate(headers, start=1)
    ]
    parts.append('<row r="1" ht="18" customHeight="1">%s</row>' % "".join(header_cells))
    for row_index, row in enumerate(rows, start=2):
        cells = [
            _cell_xml("%s%d" % (_column_letter(index), row_index), value)
            for index, value in enumerate(row, start=1)
        ]
        parts.append('<row r="%d">%s</row>' % (row_index, "".join(cells)))
    parts.append("</sheetData>")
    parts.append('<autoFilter ref="A1:%s%d"/>' % (last_column, row_count))
    parts.append("</worksheet>")
    return "".join(parts)


_STYLES_XML = (
    '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>'
    '<styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">'
    '<fonts count="2">'
    '<font><sz val="11"/><name val="Calibri"/></font>'
    '<font><b/><sz val="11"/><color rgb="FFFFFFFF"/><name val="Calibri"/></font>'
    "</fonts>"
    '<fills count="3">'
    '<fill><patternFill patternType="none"/></fill>'
    '<fill><patternFill patternType="gray125"/></fill>'
    '<fill><patternFill patternType="solid"><fgColor rgb="FF1F4E79"/><bgColor indexed="64"/></patternFill></fill>'
    "</fills>"
    '<borders count="1"><border/></borders>'
    '<cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs>'
    '<cellXfs count="2">'
    '<xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/>'
    '<xf numFmtId="0" fontId="1" fillId="2" borderId="0" xfId="0" applyFont="1" applyFill="1" applyAlignment="1">'
    '<alignment vertical="center"/></xf>'
    "</cellXfs>"
    '<cellStyles count="1"><cellStyle name="Normal" xfId="0" builtinId="0"/></cellStyles>'
    "</styleSheet>"
)


def _sheet_names(sheets):
    names = []
    for index, sheet in enumerate(sheets, start=1):
        name = _SHEET_NAME_BAD.sub("-", _as_text(sheet[0]) or ("Sheet%d" % index))[:31]
        candidate = name or ("Sheet%d" % index)
        suffix = 2
        while candidate.lower() in [existing.lower() for existing in names]:
            candidate = "%s_%d" % (name[:28], suffix)
            suffix += 1
        names.append(candidate)
    return names


def write_workbook(path, sheets, creator=SCRIPT_NAME):
    """Write ``sheets`` (name, headers, rows) to an .xlsx package at ``path``."""
    names = _sheet_names(sheets)
    created = datetime.datetime.now().strftime("%Y-%m-%dT%H:%M:%SZ")

    content_types = [
        '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>',
        '<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">',
        '<Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>',
        '<Default Extension="xml" ContentType="application/xml"/>',
        '<Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>',
        '<Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/>',
        '<Override PartName="/docProps/core.xml" ContentType="application/vnd.openxmlformats-package.core-properties+xml"/>',
        '<Override PartName="/docProps/app.xml" ContentType="application/vnd.openxmlformats-officedocument.extended-properties+xml"/>',
    ]
    for index in range(len(sheets)):
        content_types.append(
            '<Override PartName="/xl/worksheets/sheet%d.xml" '
            'ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>'
            % (index + 1)
        )
    content_types.append("</Types>")

    package_rels = (
        '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>'
        '<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">'
        '<Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>'
        '<Relationship Id="rId2" Type="http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties" Target="docProps/core.xml"/>'
        '<Relationship Id="rId3" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/extended-properties" Target="docProps/app.xml"/>'
        "</Relationships>"
    )

    workbook_sheets = "".join(
        '<sheet name="%s" sheetId="%d" r:id="rId%d"/>' % (_xml_escape(name), index, index)
        for index, name in enumerate(names, start=1)
    )
    workbook = (
        '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>'
        '<workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" '
        'xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">'
        "<sheets>%s</sheets></workbook>" % workbook_sheets
    )

    workbook_rels = [
        '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>',
        '<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">',
    ]
    for index in range(len(sheets)):
        workbook_rels.append(
            '<Relationship Id="rId%d" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" '
            'Target="worksheets/sheet%d.xml"/>' % (index + 1, index + 1)
        )
    workbook_rels.append(
        '<Relationship Id="rId%d" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" '
        'Target="styles.xml"/>' % (len(sheets) + 1)
    )
    workbook_rels.append("</Relationships>")

    core = (
        '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>'
        '<cp:coreProperties xmlns:cp="http://schemas.openxmlformats.org/package/2006/metadata/core-properties" '
        'xmlns:dc="http://purl.org/dc/elements/1.1/" xmlns:dcterms="http://purl.org/dc/terms/" '
        'xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">'
        "<dc:creator>%s</dc:creator><cp:lastModifiedBy>%s</cp:lastModifiedBy>"
        '<dcterms:created xsi:type="dcterms:W3CDTF">%s</dcterms:created>'
        '<dcterms:modified xsi:type="dcterms:W3CDTF">%s</dcterms:modified>'
        "</cp:coreProperties>" % (_xml_escape(creator), _xml_escape(creator), created, created)
    )
    app = (
        '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>'
        '<Properties xmlns="http://schemas.openxmlformats.org/officeDocument/2006/extended-properties" '
        'xmlns:vt="http://schemas.openxmlformats.org/officeDocument/2006/docPropsVTypes">'
        "<Application>%s</Application></Properties>" % _xml_escape(creator)
    )

    folder = os.path.dirname(path)
    if folder and not os.path.isdir(folder):
        os.makedirs(folder)

    with zipfile.ZipFile(path, "w", zipfile.ZIP_DEFLATED) as package:
        package.writestr("[Content_Types].xml", "".join(content_types))
        package.writestr("_rels/.rels", package_rels)
        package.writestr("docProps/core.xml", core)
        package.writestr("docProps/app.xml", app)
        package.writestr("xl/workbook.xml", workbook)
        package.writestr("xl/_rels/workbook.xml.rels", "".join(workbook_rels))
        package.writestr("xl/styles.xml", _STYLES_XML)
        for index, sheet in enumerate(sheets, start=1):
            package.writestr("xl/worksheets/sheet%d.xml" % index, _sheet_xml(sheet[1], sheet[2]))
    return path


def write_csv(path, headers, rows):
    """CSV twin of the detail sheet, used when the .xlsx cannot be written."""
    def escape(value):
        text = _as_text(value).replace('"', '""')
        return '"%s"' % text

    lines = [",".join(escape(header) for header in headers)]
    for row in rows:
        lines.append(",".join(escape(value) for value in row))
    with open(path, "w", encoding="utf-8-sig", newline="") as handle:
        handle.write("\r\n".join(lines))
    return path


# ---------------------------------------------------------------------------
# Revit document helpers
# ---------------------------------------------------------------------------
def current_document():
    document = None
    manager = getattr(DocumentManager, "Instance", None)
    if manager is not None:
        document = getattr(manager, "CurrentDBDocument", None)
    return document


def document_info(document):
    application = _safe(lambda: document.Application, None)
    title = _safe(lambda: document.Title, "")
    path = _safe(lambda: document.PathName, "")
    return {
        "title": title or (os.path.basename(path) if path else "Untitled"),
        "path": path or "(not saved)",
        "is_family": bool(_safe(lambda: document.IsFamilyDocument, False)),
        "is_workshared": bool(_safe(lambda: document.IsWorkshared, False)),
        "revit_version": _safe(lambda: application.VersionName, "") or _safe(lambda: application.VersionNumber, ""),
        "revit_build": _safe(lambda: application.VersionBuild, ""),
        "user": _safe(lambda: application.Username, "") or _safe(lambda: os.environ.get("USERNAME", ""), ""),
    }


def _element_id_set(element_ids=None):
    if HashSet is None:
        return None
    collection = HashSet[DB.ElementId]()
    for element_id in element_ids or []:
        collection.Add(element_id)
    return collection


def _element_id_list(element_ids):
    if List is None:
        return list(element_ids)
    collection = List[DB.ElementId]()
    for element_id in element_ids:
        collection.Add(element_id)
    return collection


def element_name(element):
    for getter in (
        lambda: element.Name,
        lambda: DB.Element.Name.GetValue(element),
        lambda: element.get_Parameter(DB.BuiltInParameter.SYMBOL_NAME_PARAM).AsString(),
    ):
        name = _safe(getter, "")
        if name:
            return _as_text(name)
    return ""


def purge_group(element):
    for class_name, label in PURGE_GROUPS:
        revit_class = getattr(DB, class_name, None)
        if revit_class is None:
            continue
        try:
            if isinstance(element, revit_class):
                return label
        except Exception:
            continue
    class_name = _as_text(_safe(lambda: type(element).__name__, "Element"))
    spaced = re.sub(r"(?<!^)(?=[A-Z])", " ", class_name)
    return spaced or "Element"


def element_info(document, element_id, with_worksharing=False):
    element = _safe(lambda: document.GetElement(element_id), None)
    if element is None:
        return {
            "group": "Already removed",
            "category": "",
            "family": "",
            "name": "",
            "id": _id_value(element_id),
            "unique_id": "",
            "api_class": "",
            "workset": "",
            "changed_by": "",
            "notes": "Element no longer present in the model",
        }

    family = _safe(lambda: element.Family.Name, "") or _safe(lambda: element.FamilyName, "")
    workset = ""
    changed_by = ""
    if with_worksharing:
        workset = _as_text(
            _safe(lambda: document.GetWorksetTable().GetWorkset(element.WorksetId).Name, "")
        )
        changed_by = _as_text(
            _safe(
                lambda: DB.WorksharingUtils.GetWorksharingTooltipInfo(document, element_id).LastChangedBy,
                "",
            )
        )

    return {
        "group": purge_group(element),
        "category": _as_text(_safe(lambda: element.Category.Name, "")),
        "family": _as_text(family),
        "name": element_name(element),
        "id": _id_value(element_id),
        "unique_id": _as_text(_safe(lambda: element.UniqueId, "")),
        "api_class": _as_text(_safe(lambda: type(element).__name__, "")),
        "workset": workset,
        "changed_by": changed_by,
        "notes": "",
    }


def detail_rows(document, element_ids, with_worksharing=False):
    rows = []
    infos = []
    for index, element_id in enumerate(element_ids, start=1):
        info = element_info(document, element_id, with_worksharing)
        infos.append(info)
        rows.append(
            [
                index,
                info["group"],
                info["category"],
                info["family"],
                info["name"],
                info["id"],
                info["unique_id"],
                info["api_class"],
                info["workset"],
                info["changed_by"],
                info["notes"],
            ]
        )
    rows.sort(key=lambda row: (_as_text(row[1]), _as_text(row[2]), _as_text(row[4])))
    for index, row in enumerate(rows, start=1):
        row[0] = index
    return rows, infos


def group_counts(infos):
    counts = {}
    for info in infos:
        counts[info["group"]] = counts.get(info["group"], 0) + 1
    return sorted(counts.items(), key=lambda item: (-item[1], item[0]))


# ---------------------------------------------------------------------------
# detection of unused elements
# ---------------------------------------------------------------------------
def detect_unused(document):
    """Return (element_ids, method_label).

    Revit 2024+ exposes the engine behind "Purge Unused" through
    Document.GetAllUnusedElements / GetUnusedElements. Method names are probed
    at run time so a rename in a future release degrades to the heuristic scan
    instead of failing the graph.
    """
    empty = _element_id_set()
    for method_name, label in (
        ("GetAllUnusedElements", "Revit API - Document.GetAllUnusedElements (matches Purge Unused)"),
        ("GetUnusedElements", "Revit API - Document.GetUnusedElements"),
    ):
        method = getattr(document, method_name, None)
        if method is None:
            continue
        for arguments in ((empty,), ()):
            if arguments and arguments[0] is None:
                continue
            try:
                result = method(*arguments)
            except Exception:
                continue
            if result is None:
                continue
            try:
                return [element_id for element_id in result], label
            except Exception:
                continue
    return heuristic_unused(document), "Fallback heuristic scan (purge API unavailable in this release)"


def heuristic_unused(document):
    """Approximate the purge set without the dedicated API.

    Collects everything referenced by placed instances (and, transitively, by
    the types those instances use), then reports the remaining candidates.
    """
    used = set()

    def mark(element_id):
        value = _id_value(element_id)
        if value is not None and value > 0:
            used.add(value)

    def references(element):
        for parameter in _safe(lambda: element.Parameters, []) or []:
            try:
                if parameter.StorageType == DB.StorageType.ElementId:
                    mark(parameter.AsElementId())
            except Exception:
                continue
        for material_id in _safe(lambda: element.GetMaterialIds(False), []) or []:
            mark(material_id)

    instances = list(
        _safe(lambda: DB.FilteredElementCollector(document).WhereElementIsNotElementType(), []) or []
    )
    frontier = []
    for element in instances:
        type_id = _safe(lambda: element.GetTypeId(), None)
        mark(type_id)
        references(element)
        for view_template_id in (_safe(lambda: element.ViewTemplateId, None),):
            mark(view_template_id)
        for filter_id in _safe(lambda: element.GetFilters(), []) or []:
            mark(filter_id)
        if type_id is not None:
            frontier.append(type_id)

    seen = set()
    while frontier:
        next_frontier = []
        for element_id in frontier:
            value = _id_value(element_id)
            if value is None or value in seen:
                continue
            seen.add(value)
            element = _safe(lambda: document.GetElement(element_id), None)
            if element is None:
                continue
            before = set(used)
            references(element)
            family_id = _safe(lambda: element.Family.Id, None)
            mark(family_id)
            next_frontier.extend(
                DB.ElementId(new_value) for new_value in used - before if new_value > 0
            )
        frontier = next_frontier

    candidates = []
    types = _safe(lambda: DB.FilteredElementCollector(document).WhereElementIsElementType(), []) or []
    candidates.extend(list(types))
    for class_name in ("Material", "AppearanceAssetElement", "LinePatternElement", "FillPatternElement",
                       "ParameterFilterElement", "GroupType", "Family"):
        revit_class = getattr(DB, class_name, None)
        if revit_class is None:
            continue
        collected = _safe(
            lambda: DB.FilteredElementCollector(document).OfClass(revit_class).ToElements(), []
        )
        candidates.extend(list(collected or []))

    unused = []
    reported = set()
    for element in candidates:
        element_id = _safe(lambda: element.Id, None)
        value = _id_value(element_id)
        if value is None or value in used or value in reported:
            continue
        reported.add(value)
        unused.append(element_id)
    return unused


# ---------------------------------------------------------------------------
# confirmation gate
# ---------------------------------------------------------------------------
def _dialog_confirm(title, instruction, content, expanded):
    """Yes/No prompt. Returns True/False, or None when no UI is available."""
    if UI is not None:
        try:
            dialog = UI.TaskDialog(title)
            dialog.MainInstruction = instruction
            dialog.MainContent = content
            if expanded:
                dialog.ExpandedContent = expanded
            dialog.CommonButtons = UI.TaskDialogCommonButtons.Yes | UI.TaskDialogCommonButtons.No
            dialog.DefaultButton = UI.TaskDialogResult.No
            return dialog.Show() == UI.TaskDialogResult.Yes
        except Exception:
            pass
    try:
        clr.AddReference("System.Windows.Forms")
        from System.Windows.Forms import MessageBox, MessageBoxButtons, DialogResult

        answer = MessageBox.Show(
            "%s\n\n%s\n\n%s" % (instruction, content, expanded or ""), title, MessageBoxButtons.YesNo
        )
        return answer == DialogResult.Yes
    except Exception:
        return None


def confirm_purge(info, counts, total, report_path, confirm_flag, keyword, show_dialog):
    """Three gates: the toggle, the typed keyword, and the in-Revit dialog."""
    if total <= 0:
        return False, "Nothing to purge - no unused elements were found."
    if not confirm_flag:
        return False, "REPORT ONLY - 'Confirm Purge' is False, so no elements were deleted."
    if _as_text(keyword).strip().upper() != CONFIRMATION_KEYWORD:
        return False, (
            "REPORT ONLY - type '%s' into 'Confirmation Keyword' to authorise deletion."
            % CONFIRMATION_KEYWORD
        )

    if not show_dialog:
        return True, "Approved by toggle + keyword (interactive dialog disabled) by %s." % info["user"]

    top = "\n".join("  %s: %d" % (name, count) for name, count in counts[:15])
    if len(counts) > 15:
        top += "\n  ... and %d more groups" % (len(counts) - 15)
    answer = _dialog_confirm(
        "Purge Unused Elements",
        "Permanently purge %d unused element(s) from '%s'?" % (total, info["title"]),
        "This performs a full 'Purge Unused Elements' pass and cannot be undone once the model is "
        "synchronised.\nReview the report before continuing:\n%s" % report_path,
        "Unused elements by group:\n%s" % top,
    )
    if answer is True:
        return True, "Approved in Revit by %s on %s." % (
            info["user"],
            datetime.datetime.now().strftime("%Y-%m-%d %H:%M:%S"),
        )
    if answer is False:
        return False, "DECLINED - %s dismissed the confirmation dialog; nothing was deleted." % info["user"]
    return True, (
        "Approved by toggle + keyword; no confirmation dialog could be shown in this session (%s)."
        % info["user"]
    )


# ---------------------------------------------------------------------------
# purge execution
# ---------------------------------------------------------------------------
_FAILURE_PREPROCESSOR = None


def _failure_preprocessor():
    """Swallow the warnings Revit raises while deleting types, if we can."""
    global _FAILURE_PREPROCESSOR
    if _FAILURE_PREPROCESSOR is not None:
        return _FAILURE_PREPROCESSOR
    try:
        class _SwallowWarnings(DB.IFailuresPreprocessor):
            def PreprocessFailures(self, accessor):
                accessor.DeleteAllWarnings()
                return DB.FailureProcessingResult.Continue

        _FAILURE_PREPROCESSOR = _SwallowWarnings()
    except Exception:
        _FAILURE_PREPROCESSOR = False
    return _FAILURE_PREPROCESSOR


def _delete_ids(document, element_ids):
    """Delete in chunks, falling back to one-by-one so a single stubborn
    element cannot abort the whole pass. Returns (status_by_id, removed_count)."""
    status = {}
    removed_total = 0

    def record(element_id, result, details=""):
        status[_id_value(element_id)] = (result, details)

    def delete_batch(batch):
        removed = document.Delete(_element_id_list(batch))
        try:
            return len(list(removed))
        except Exception:
            return len(batch)

    def still_present(element_id):
        return _safe(lambda: document.GetElement(element_id), None) is not None

    for start in range(0, len(element_ids), DELETE_CHUNK_SIZE):
        chunk = []
        for element_id in element_ids[start : start + DELETE_CHUNK_SIZE]:
            if still_present(element_id):
                chunk.append(element_id)
            else:
                record(element_id, "Skipped", "Already removed as a dependent of another element")
        if not chunk:
            continue
        try:
            removed_total += delete_batch(chunk)
            for element_id in chunk:
                record(element_id, "Deleted")
        except Exception:
            for element_id in chunk:
                if not still_present(element_id):
                    record(element_id, "Skipped", "Already removed as a dependent of another element")
                    continue
                try:
                    removed_total += delete_batch([element_id])
                    record(element_id, "Deleted")
                except Exception as error:
                    record(element_id, "Failed", _as_text(error).split("\n")[0][:300])
    return status, removed_total


def purge(document, max_passes):
    """Repeat detect + delete until nothing new shows up (mirrors what Revit's
    own command does when elements only become unused after their users go)."""
    log_rows = []
    deleted = 0
    passes = 0

    for pass_number in range(1, max(1, max_passes) + 1):
        element_ids, _ = detect_unused(document)
        if not element_ids:
            break
        passes = pass_number
        infos = [element_info(document, element_id) for element_id in element_ids]

        TransactionManager.Instance.ForceCloseTransaction()
        transaction = DB.Transaction(document, "Purge Unused Elements (Dynamo)")
        transaction.Start()
        preprocessor = _failure_preprocessor()
        if preprocessor:
            try:
                options = transaction.GetFailureHandlingOptions()
                options.SetFailuresPreprocessor(preprocessor)
                options.SetClearAfterRollback(True)
                transaction.SetFailureHandlingOptions(options)
            except Exception:
                pass
        try:
            status, removed = _delete_ids(document, element_ids)
            transaction.Commit()
        except Exception as error:
            try:
                transaction.RollBack()
            except Exception:
                pass
            status = {info["id"]: ("Failed", _as_text(error).split("\n")[0][:300]) for info in infos}
            removed = 0

        deleted += removed
        for info in infos:
            result, details = status.get(info["id"], ("Not attempted", ""))
            log_rows.append(
                [
                    pass_number,
                    info["group"],
                    info["category"],
                    info["family"],
                    info["name"],
                    info["id"],
                    result,
                    details,
                ]
            )
        if not any(row[6] == "Deleted" for row in log_rows if row[0] == pass_number):
            break

    remaining, _ = detect_unused(document)
    return log_rows, deleted, passes, len(remaining)


# ---------------------------------------------------------------------------
# report assembly
# ---------------------------------------------------------------------------
def summary_rows(info, method, counts, total, status, extra=None):
    rows = [
        ["Report", SCRIPT_NAME],
        ["Script version", SCRIPT_VERSION],
        ["Generated", datetime.datetime.now().strftime("%Y-%m-%d %H:%M:%S")],
        ["Run by", info["user"]],
        ["Model", info["title"]],
        ["Model path", info["path"]],
        ["Model type", "Family document" if info["is_family"] else "Project document"],
        ["Worksharing", "Enabled" if info["is_workshared"] else "Not enabled"],
        ["Revit version", info["revit_version"]],
        ["Revit build", info["revit_build"]],
        ["Detection method", method],
        ["Unused elements found", total],
        ["Purge groups", len(counts)],
        ["Status", status],
    ]
    for item, value in extra or []:
        rows.append([item, value])
    for name, count in counts:
        rows.append(["Unused - %s" % name, count])
    return rows


def resolve_output_path(folder, file_name, info):
    if not _as_text(folder).strip():
        model_folder = os.path.dirname(info["path"]) if os.path.isdir(os.path.dirname(info["path"] or "")) else ""
        folder = model_folder or os.path.join(os.path.expanduser("~"), "Documents")
    folder = os.path.expandvars(os.path.expanduser(_as_text(folder).strip()))

    base = _as_text(file_name).strip()
    if not base:
        base = "PurgeUnused_%s_%s" % (_sanitize_file_name(info["title"]), _timestamp())
    if base.lower().endswith(".xlsx"):
        base = base[:-5]
    base = _sanitize_file_name(base)

    try:
        if not os.path.isdir(folder):
            os.makedirs(folder)
        probe = os.path.join(folder, ".purge_unused_write_test")
        with open(probe, "w") as handle:
            handle.write("")
        os.remove(probe)
    except Exception:
        import tempfile

        folder = tempfile.gettempdir()
    return folder, base


def main():
    document = current_document()
    if document is None:
        return ["ERROR: no Revit document is available to this Dynamo session."]

    folder_input = _arg(0, "")
    name_input = _arg(1, "")
    confirm_flag = _as_bool(_arg(2, False), False)
    keyword = _as_text(_arg(3, ""))
    show_dialog = _as_bool(_arg(4, True), True)
    max_passes = min(10, max(1, _as_int(_arg(5, DEFAULT_MAX_PASSES), DEFAULT_MAX_PASSES)))

    info = document_info(document)
    folder, base_name = resolve_output_path(folder_input, name_input, info)
    report_path = os.path.join(folder, "%s.xlsx" % base_name)

    element_ids, method = detect_unused(document)
    with_worksharing = info["is_workshared"] and len(element_ids) <= WORKSHARING_INFO_LIMIT
    rows, infos = detail_rows(document, element_ids, with_worksharing)
    counts = group_counts(infos)
    total = len(rows)

    pending_status = (
        "%d unused element(s) found - awaiting team confirmation before purging." % total
        if total
        else "No unused elements found - nothing to purge."
    )
    sheets = [
        ("Summary", SUMMARY_HEADERS, summary_rows(info, method, counts, total, pending_status)),
        ("Unused Elements", DETAIL_HEADERS, rows),
        ("By Purge Group", GROUP_HEADERS, [[name, count] for name, count in counts]),
    ]

    written = []
    try:
        written.append(write_workbook(report_path, sheets))
    except Exception as error:
        report_path = os.path.join(folder, "%s.csv" % base_name)
        written.append(write_csv(report_path, DETAIL_HEADERS, rows))
        method += " | Excel write failed (%s) - CSV written instead" % _as_text(error).split("\n")[0][:160]

    approved, decision = confirm_purge(
        info, counts, total, report_path, confirm_flag, keyword, show_dialog
    )

    result = [
        "%s v%s" % (SCRIPT_NAME, SCRIPT_VERSION),
        "Model: %s" % info["title"],
        "Detection: %s" % method,
        "Unused elements found: %d" % total,
        "Report: %s" % report_path,
        "Decision: %s" % decision,
    ]

    if not approved:
        return result

    log_rows, deleted, passes, remaining = purge(document, max_passes)
    # A later pass can succeed where an earlier one failed, so count elements
    # rather than log rows.
    failed_ids = set(row[5] for row in log_rows if row[6] == "Failed")
    deleted_ids = set(row[5] for row in log_rows if row[6] == "Deleted")
    failed = len(failed_ids - deleted_ids)
    purged_status = "PURGED - %d element(s) removed in %d pass(es); %d could not be deleted." % (
        deleted,
        passes,
        failed,
    )
    log_path = os.path.join(folder, "%s_PurgeLog.xlsx" % base_name)
    log_sheets = [
        (
            "Summary",
            SUMMARY_HEADERS,
            summary_rows(
                info,
                method,
                counts,
                total,
                purged_status,
                extra=[
                    ["Confirmation", decision],
                    ["Purge passes run", passes],
                    ["Elements removed (incl. dependents)", deleted],
                    ["Elements that could not be deleted", failed],
                    ["Unused elements remaining", remaining],
                    ["Pre-purge report", report_path],
                ],
            ),
        ),
        ("Purge Log", PURGE_LOG_HEADERS, log_rows),
    ]
    try:
        written.append(write_workbook(log_path, log_sheets))
    except Exception as error:
        log_path = os.path.join(folder, "%s_PurgeLog.csv" % base_name)
        written.append(write_csv(log_path, PURGE_LOG_HEADERS, log_rows))
        result.append("Excel log write failed (%s) - CSV written instead" % _as_text(error).split("\n")[0][:160])

    result.extend(
        [
            "Purge result: %s" % purged_status,
            "Unused elements remaining: %d" % remaining,
            "Purge log: %s" % log_path,
        ]
    )
    return result


try:
    OUT = main()
except Exception:
    OUT = ["ERROR in %s v%s" % (SCRIPT_NAME, SCRIPT_VERSION), traceback.format_exc()]
