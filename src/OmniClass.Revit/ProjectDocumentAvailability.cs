using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace OmniClass.Revit
{
    /// <summary>Commands only make sense in a project document that can actually have rooms.</summary>
    public sealed class ProjectDocumentAvailability : IExternalCommandAvailability
    {
        public bool IsCommandAvailable(UIApplication applicationData, CategorySet selectedCategories)
        {
            var document = applicationData?.ActiveUIDocument?.Document;
            return document != null && !document.IsFamilyDocument;
        }
    }
}
