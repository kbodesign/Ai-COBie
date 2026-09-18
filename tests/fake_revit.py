# -*- coding: utf-8 -*-
"""Minimal stand-ins for the Revit / Dynamo runtime.

Enough of ``clr``, ``Autodesk.Revit.DB``, ``Autodesk.Revit.UI``, ``RevitServices``
and ``System.Collections.Generic`` to execute scripts/purge_unused_report.py on a
plain CPython interpreter, so the reporting, confirmation and purge logic can be
tested without Revit.
"""

import os
import sys
import types

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SCRIPT_PATH = os.path.join(REPO_ROOT, "scripts", "purge_unused_report.py")

# ---------------------------------------------------------------------------
# Revit DB doubles
# ---------------------------------------------------------------------------
TYPE_CLASSES = [
    "ElementType",
    "FamilySymbol",
    "WallType",
    "FloorType",
    "TextNoteType",
    "DimensionType",
    "FilledRegionType",
    "ViewFamilyType",
    "DuctType",
    "PipeType",
]
OTHER_CLASSES = [
    "Family",
    "Material",
    "AppearanceAssetElement",
    "LinePatternElement",
    "FillPatternElement",
    "ParameterFilterElement",
    "FilterElement",
    "GroupType",
    "ImageType",
    "RevitLinkType",
    "CADLinkType",
    "View",
]


class ElementId(object):
    def __init__(self, value):
        self.Value = int(value)

    def __repr__(self):
        return "ElementId(%d)" % self.Value

    def __eq__(self, other):
        return isinstance(other, ElementId) and other.Value == self.Value

    def __hash__(self):
        return hash(self.Value)


ElementId.InvalidElementId = ElementId(-1)


class Category(object):
    def __init__(self, name):
        self.Name = name


class Element(object):
    def __init__(self, element_id, name, category=None, family=None, materials=None):
        self.Id = ElementId(element_id)
        self.Name = name
        self.Category = Category(category) if category else None
        self.UniqueId = "fake-unique-%04d" % element_id
        self.WorksetId = 0
        self.Parameters = []
        self.FamilyName = family or ""
        self.Family = types.SimpleNamespace(Name=family, Id=None) if family else None
        self._materials = materials or []
        self._type_id = ElementId(-1)

    def GetTypeId(self):
        return self._type_id

    def GetMaterialIds(self, paint):
        return list(self._materials)


class StorageType(object):
    ElementId = "ElementId"
    String = "String"


class BuiltInParameter(object):
    SYMBOL_NAME_PARAM = "SYMBOL_NAME_PARAM"


class FailureProcessingResult(object):
    Continue = "Continue"


class IFailuresPreprocessor(object):
    pass


class WorksharingTooltipInfo(object):
    def __init__(self, last_changed_by):
        self.LastChangedBy = last_changed_by


class WorksharingUtils(object):
    @staticmethod
    def GetWorksharingTooltipInfo(document, element_id):
        return WorksharingTooltipInfo("fake.user")


class FailureHandlingOptions(object):
    def __init__(self):
        self.preprocessor = None
        self.clear_after_rollback = False

    def SetFailuresPreprocessor(self, preprocessor):
        self.preprocessor = preprocessor
        return self

    def SetClearAfterRollback(self, value):
        self.clear_after_rollback = value
        return self


class Transaction(object):
    log = []

    def __init__(self, document, name):
        self.document = document
        self.name = name
        self.state = "created"
        Transaction.log.append(self)

    def Start(self):
        self.state = "started"

    def Commit(self):
        self.state = "committed"

    def RollBack(self):
        self.state = "rolled back"

    def GetFailureHandlingOptions(self):
        return FailureHandlingOptions()

    def SetFailureHandlingOptions(self, options):
        self.options = options


class FilteredElementCollector(object):
    def __init__(self, document, elements=None):
        self.document = document
        self._elements = list(document.all_elements() if elements is None else elements)

    def _derive(self, elements):
        return FilteredElementCollector(self.document, elements)

    def WhereElementIsElementType(self):
        return self._derive([e for e in self._elements if isinstance(e, DB_CLASSES["ElementType"])])

    def WhereElementIsNotElementType(self):
        return self._derive([e for e in self._elements if not isinstance(e, DB_CLASSES["ElementType"])])

    def OfClass(self, revit_class):
        return self._derive([e for e in self._elements if isinstance(e, revit_class)])

    def ToElements(self):
        return list(self._elements)

    def __iter__(self):
        return iter(self._elements)


DB_CLASSES = {"Element": Element}
for _name in TYPE_CLASSES:
    base = Element if _name == "ElementType" else DB_CLASSES["ElementType"]
    DB_CLASSES[_name] = type(_name, (base,), {})
for _name in OTHER_CLASSES:
    DB_CLASSES[_name] = type(_name, (Element,), {})


# ---------------------------------------------------------------------------
# fake document
# ---------------------------------------------------------------------------
class FakeApplication(object):
    def __init__(self, version="2026"):
        self.VersionNumber = version
        self.VersionName = "Autodesk Revit %s" % version
        self.VersionBuild = "%s.0.1.234" % version
        self.Username = "team.member"


class FakeDocument(object):
    """Document double.

    ``supports_purge_api`` mirrors releases that expose
    Document.GetAllUnusedElements; when False the script must fall back to its
    heuristic scan.
    """

    def __init__(self, title="Sample Model", path="", elements=None, unused=None,
                 supports_purge_api=True, workshared=False, dependents=None,
                 undeletable=None, purge_api_needs_categories=False,
                 categories=("Walls", "Doors", "Materials")):
        self.Title = title
        self.PathName = path
        self.IsFamilyDocument = False
        self.IsWorkshared = workshared
        self.Application = FakeApplication()
        self._elements = {}
        self._unused = []
        self._dependents = dependents or {}
        self._undeletable = set(undeletable or [])
        self.deleted_ids = []
        self.supports_purge_api = supports_purge_api
        self.purge_api_needs_categories = purge_api_needs_categories
        self.purge_api_calls = []
        self.Settings = types.SimpleNamespace(
            Categories=[
                types.SimpleNamespace(Name=name, Id=ElementId(-2000000 - index))
                for index, name in enumerate(categories)
            ]
        )
        for element in elements or []:
            self._elements[element.Id.Value] = element
        for element_id in unused or []:
            self._unused.append(element_id)
        if not supports_purge_api:
            # Hide the purge API the way an unsupported release would.
            self.__dict__["GetAllUnusedElements"] = None
            self.__dict__["GetUnusedElements"] = None

    # --- API surface used by the script ---
    def GetElement(self, element_id):
        return self._elements.get(getattr(element_id, "Value", None))

    def GetAllUnusedElements(self, ids):
        if not self.supports_purge_api:
            raise AttributeError("GetAllUnusedElements")
        self.purge_api_calls.append(len(list(ids)))
        if self.purge_api_needs_categories and not len(list(ids)):
            return []
        return [ElementId(value) for value in self._unused if value in self._elements]

    def GetUnusedElements(self, ids):
        return self.GetAllUnusedElements(ids)

    def Delete(self, element_ids):
        requested = [element_id.Value for element_id in element_ids]
        # Revit's Delete is all-or-nothing, so refuse before touching anything.
        blocked = [value for value in requested if value in self._undeletable]
        if blocked:
            raise RuntimeError("Element(s) %s cannot be deleted" % blocked)

        removed = []
        for value in requested:
            for target in [value] + list(self._dependents.get(value, [])):
                if target in self._elements:
                    del self._elements[target]
                    self.deleted_ids.append(target)
                    removed.append(ElementId(target))
        return removed

    def GetWorksetTable(self):
        return types.SimpleNamespace(
            GetWorkset=lambda workset_id: types.SimpleNamespace(Name="Workset1")
        )

    # --- test helpers ---
    def all_elements(self):
        return list(self._elements.values())

    def remaining_ids(self):
        return sorted(self._elements.keys())


def make_element(class_name, element_id, name, category=None, family=None, materials=None):
    return DB_CLASSES[class_name](element_id, name, category=category, family=family, materials=materials)


# ---------------------------------------------------------------------------
# module installation
# ---------------------------------------------------------------------------
class _Generic(object):
    """Stands in for an open generic type: ``List[ElementId]()``."""

    def __init__(self, factory):
        self.factory = factory

    def __getitem__(self, item):
        return self.factory


class _Collection(object):
    def __init__(self, items=None):
        self.items = list(items or [])

    def Add(self, item):
        self.items.append(item)

    def __iter__(self):
        return iter(self.items)

    def __len__(self):
        return len(self.items)


class FakeTaskDialog(object):
    """Records the confirmation prompt and answers with ``answer``."""

    answer = "No"
    shown = []

    def __init__(self, title):
        self.title = title
        self.MainInstruction = ""
        self.MainContent = ""
        self.ExpandedContent = ""
        self.CommonButtons = 0
        self.DefaultButton = 0

    def Show(self):
        FakeTaskDialog.shown.append(self)
        return FakeTaskDialog.answer


def _module(name, **attributes):
    module = types.ModuleType(name)
    for key, value in attributes.items():
        setattr(module, key, value)
    sys.modules[name] = module
    return module


def install(ui_available=True):
    """Register the fake modules and return the DB / UI module pair."""
    Transaction.log = []
    FakeTaskDialog.shown = []

    db_attributes = dict(DB_CLASSES)
    db_attributes.update(
        {
            "ElementId": ElementId,
            "StorageType": StorageType,
            "BuiltInParameter": BuiltInParameter,
            "FilteredElementCollector": FilteredElementCollector,
            "Transaction": Transaction,
            "IFailuresPreprocessor": IFailuresPreprocessor,
            "FailureProcessingResult": FailureProcessingResult,
            "WorksharingUtils": WorksharingUtils,
        }
    )

    _module("clr", AddReference=lambda name: None)
    autodesk = _module("Autodesk")
    revit = _module("Autodesk.Revit")
    db = _module("Autodesk.Revit.DB", **db_attributes)
    autodesk.Revit = revit
    revit.DB = db

    ui = None
    if ui_available:
        ui = _module(
            "Autodesk.Revit.UI",
            TaskDialog=FakeTaskDialog,
            TaskDialogCommonButtons=types.SimpleNamespace(Yes=1, No=2),
            TaskDialogResult=types.SimpleNamespace(Yes="Yes", No="No"),
        )
        revit.UI = ui
    else:
        sys.modules.pop("Autodesk.Revit.UI", None)

    revit_services = _module("RevitServices")
    persistence = _module("RevitServices.Persistence")
    transactions = _module("RevitServices.Transactions")
    revit_services.Persistence = persistence
    revit_services.Transactions = transactions
    persistence.DocumentManager = types.SimpleNamespace(
        Instance=types.SimpleNamespace(CurrentDBDocument=None)
    )
    transactions.TransactionManager = types.SimpleNamespace(
        Instance=types.SimpleNamespace(
            ForceCloseTransaction=lambda: None,
            EnsureInTransaction=lambda document: None,
            TransactionTaskDone=lambda: None,
        )
    )

    system = _module("System")
    collections = _module("System.Collections")
    generic = _module(
        "System.Collections.Generic",
        HashSet=_Generic(_Collection),
        List=_Generic(_Collection),
    )
    system.Collections = collections
    collections.Generic = generic

    return db, ui


def run_script(document, inputs, ui_available=True):
    """Execute the Dynamo script against ``document`` and return its OUT value."""
    db, _ = install(ui_available=ui_available)
    sys.modules["RevitServices.Persistence"].DocumentManager.Instance.CurrentDBDocument = document
    with open(SCRIPT_PATH, "r", encoding="utf-8") as handle:
        source = handle.read()
    namespace = {"IN": list(inputs), "__name__": "dynamo_python_node"}
    exec(compile(source, SCRIPT_PATH, "exec"), namespace)
    return namespace["OUT"], namespace
