using System;
using System.IO;
using Autodesk.Revit.DB;
using OmniClass.Revit.Settings;

namespace OmniClass.Revit.Parameters
{
    /// <summary>
    /// Binds OmniClass Number and Title as instance shared parameters on the Room
    /// category. Shared, not project: they can be scheduled and they survive IFC and
    /// ODBC export, which is what the COBie side of this work needs.
    /// </summary>
    internal static class SharedParameterBinder
    {
        public static void EnsureBound(Document document, AddinSettings settings)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));
            if (settings == null) throw new ArgumentNullException(nameof(settings));

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
                var number = GetOrCreateDefinition(group, settings.NumberParameterName, OmniClassParameterIds.Number);
                var title = GetOrCreateDefinition(group, settings.TitleParameterName, OmniClassParameterIds.Title);

                BindToRooms(document, number);
                BindToRooms(document, title);
            }
            finally
            {
                // Put the user's shared parameter file back. Leaving ours in place would
                // surprise every other tool they use in the same session.
                if (!string.IsNullOrEmpty(previous) && File.Exists(previous))
                    application.SharedParametersFilename = previous;
            }
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
                        ", which is not the GUID this add-in uses. Rename the existing parameter or " +
                        "point sharedParameterFile at a dedicated file in OmniClass.Rooms.config.");
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

            // Revit will not open a truly empty file as a shared parameter file.
            File.WriteAllText(path,
                "# This is a Revit shared parameter file.\r\n" +
                "*META\tVERSION\tMINVERSION\r\n" +
                "META\t2\t1\r\n" +
                "*GROUP\tID\tNAME\r\n" +
                "*PARAM\tGUID\tNAME\tDATATYPE\tDATACATEGORY\tGROUP\tVISIBLE\tDESCRIPTION\tUSERMODIFIABLE\tHIDEWHENNOVALUE\r\n");
        }
    }
}
