using System;
using System.IO;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using OmniClass.Core.Configuration;

namespace OmniClass.Revit.Parameters
{
    /// <summary>
    /// Reuses Classification.Space.* and COBie.Space.Category when they already exist
    /// on rooms or in the project. Does not create definitions unless the user turns
    /// that on — creating them is what threw "GUID is already present" after a first run.
    /// Never throws; Apply can still write whatever parameters are already on the rooms.
    /// </summary>
    internal static class SharedParameterBinder
    {
        public static void EnsureBound(Document document, AddinSettings settings)
        {
            if (document == null || settings == null) return;

            TryReuseOrBind(document, settings, settings.NumberParameterName, OmniClassParameterIds.Number);
            TryReuseOrBind(document, settings, settings.TitleParameterName, OmniClassParameterIds.Title);
            TryReuseOrBind(document, settings, settings.CategoryParameterName, OmniClassParameterIds.Category);
        }

        private static void TryReuseOrBind(Document document, AddinSettings settings, string name, Guid guid)
        {
            if (string.IsNullOrEmpty(name)) return;
            if (RoomsAlreadyHaveParameter(document, name)) return;
            if (ProjectHasParameter(document, name)) return;
            if (!settings.CreateMissingParameters) return;

            try
            {
                BindNew(document, settings, name, guid);
            }
            catch (Exception)
            {
                // A GUID clash or a locked shared-parameter file must not stop Classify.
                // Rooms that already have the parameters will still be written.
            }
        }

        private static bool RoomsAlreadyHaveParameter(Document document, string name)
        {
            var collector = new FilteredElementCollector(document)
                .OfCategory(BuiltInCategory.OST_Rooms)
                .WhereElementIsNotElementType();

            foreach (var element in collector)
            {
                if (element.LookupParameter(name) != null) return true;
            }

            return false;
        }

        private static bool ProjectHasParameter(Document document, string name)
        {
            var iterator = document.ParameterBindings.ForwardIterator();
            iterator.Reset();
            while (iterator.MoveNext())
            {
                var definition = iterator.Key;
                if (definition != null && string.Equals(definition.Name, name, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private static void BindNew(Document document, AddinSettings settings, string name, Guid guid)
        {
            var application = document.Application;
            var path = settings.EffectiveSharedParameterFile();
            EnsureDefinitionFile(path);

            var previous = application.SharedParametersFilename;
            try
            {
                application.SharedParametersFilename = path;
                var file = application.OpenSharedParameterFile();
                if (file == null) return;

                var definition = FindByGuid(file, guid) ?? FindByName(file, name);
                if (definition == null)
                {
                    var group = GetOrCreateGroup(file, OmniClassParameterIds.GroupName);
                    definition = group.Definitions.Create(new ExternalDefinitionCreationOptions(name, SpecTypeId.String.Text)
                    {
                        GUID = guid,
                        UserModifiable = true,
                        Visible = true
                    });
                }

                BindToRooms(document, definition);
            }
            finally
            {
                if (!string.IsNullOrEmpty(previous) && File.Exists(previous))
                    application.SharedParametersFilename = previous;
            }
        }

        private static Definition FindByGuid(DefinitionFile file, Guid guid)
        {
            foreach (DefinitionGroup group in file.Groups)
            {
                foreach (Definition definition in group.Definitions)
                {
                    var external = definition as ExternalDefinition;
                    if (external != null && external.GUID == guid) return definition;
                }
            }

            return null;
        }

        private static Definition FindByName(DefinitionFile file, string name)
        {
            foreach (DefinitionGroup group in file.Groups)
            {
                foreach (Definition definition in group.Definitions)
                {
                    if (string.Equals(definition.Name, name, StringComparison.OrdinalIgnoreCase))
                        return definition;
                }
            }

            return null;
        }

        private static DefinitionGroup GetOrCreateGroup(DefinitionFile file, string name)
        {
            foreach (DefinitionGroup group in file.Groups)
            {
                if (string.Equals(group.Name, name, StringComparison.OrdinalIgnoreCase))
                    return group;
            }

            return file.Groups.Create(name);
        }

        private static void BindToRooms(Document document, Definition definition)
        {
            var category = document.Settings.Categories.get_Item(BuiltInCategory.OST_Rooms);
            var categories = document.Application.Create.NewCategorySet();
            categories.Insert(category);

            Binding binding = document.Application.Create.NewInstanceBinding(categories);

            if (document.ParameterBindings.Contains(definition))
                document.ParameterBindings.ReInsert(definition, binding, GroupTypeId.IdentityData);
            else
                document.ParameterBindings.Insert(definition, binding, GroupTypeId.IdentityData);
        }

        private static void EnsureDefinitionFile(string path)
        {
            if (File.Exists(path)) return;

            var folder = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);

            File.WriteAllText(path,
                "# This is a Revit shared parameter file.\r\n" +
                "*META\tVERSION\tMINVERSION\r\n" +
                "META\t2\t1\r\n" +
                "*GROUP\tID\tNAME\r\n" +
                "*PARAM\tGUID\tNAME\tDATATYPE\tDATACATEGORY\tGROUP\tVISIBLE\tDESCRIPTION\tUSERMODIFIABLE\tHIDEWHENNOVALUE\r\n");
        }
    }
}
