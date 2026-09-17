using System;
using System.IO;
using Autodesk.Revit.DB;
using OmniClass.Core.Configuration;

namespace OmniClass.Revit.Parameters
{
    /// <summary>
    /// Writes onto Classification.Space.Number, Classification.Space.Description and
    /// COBie.Space.Category. If those parameters already exist in the template they are
    /// reused; they are only created when the project does not already have them.
    /// </summary>
    internal static class SharedParameterBinder
    {
        public static void EnsureBound(Document document, AddinSettings settings)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));
            if (settings == null) throw new ArgumentNullException(nameof(settings));

            BindIfMissing(document, settings, settings.NumberParameterName, OmniClassParameterIds.Number);
            BindIfMissing(document, settings, settings.TitleParameterName, OmniClassParameterIds.Title);
            BindIfMissing(document, settings, settings.CategoryParameterName, OmniClassParameterIds.Category);
        }

        private static void BindIfMissing(Document document, AddinSettings settings, string name, Guid guid)
        {
            if (string.IsNullOrEmpty(name)) return;
            if (ProjectHasParameter(document, name)) return;

            var application = document.Application;
            var path = settings.EffectiveSharedParameterFile();
            EnsureDefinitionFile(path);

            var previous = application.SharedParametersFilename;
            try
            {
                application.SharedParametersFilename = path;
                var file = application.OpenSharedParameterFile()
                           ?? throw new InvalidOperationException(
                               "Revit could not open the shared parameter file at '" + path + "'.");

                var group = GetOrCreateGroup(file, OmniClassParameterIds.GroupName);
                var definition = GetOrCreateDefinition(group, name, guid);
                BindToRooms(document, definition);
            }
            finally
            {
                if (!string.IsNullOrEmpty(previous) && File.Exists(previous))
                    application.SharedParametersFilename = previous;
            }
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

        private static DefinitionGroup GetOrCreateGroup(DefinitionFile file, string name)
        {
            foreach (DefinitionGroup group in file.Groups)
            {
                if (string.Equals(group.Name, name, StringComparison.OrdinalIgnoreCase))
                    return group;
            }

            return file.Groups.Create(name);
        }

        private static Definition GetOrCreateDefinition(DefinitionGroup group, string name, Guid guid)
        {
            foreach (Definition existing in group.Definitions)
            {
                if (!string.Equals(existing.Name, name, StringComparison.OrdinalIgnoreCase))
                    continue;

                var external = existing as ExternalDefinition;
                if (external != null && external.GUID != guid)
                {
                    throw new InvalidOperationException(
                        "Shared parameter '" + name + "' already exists with GUID " + external.GUID +
                        ", which is not the GUID this add-in uses. Point sharedParameterFile at a " +
                        "dedicated file in OmniClass.Rooms.config, or use the parameters already " +
                        "in the project template.");
                }

                return existing;
            }

            var options = new ExternalDefinitionCreationOptions(name, SpecTypeId.String.Text)
            {
                GUID = guid,
                UserModifiable = true,
                Visible = true
            };

            return group.Definitions.Create(options);
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
