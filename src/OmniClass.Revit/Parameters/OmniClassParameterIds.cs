using System;

namespace OmniClass.Revit.Parameters
{
    /// <summary>
    /// Fixed GUIDs so OmniClass Number and Title are the same shared parameters in every
    /// project this add-in touches. If each project generated its own GUID, schedules and
    /// COBie exports would see a different parameter per job.
    /// </summary>
    public static class OmniClassParameterIds
    {
        public static readonly Guid Number = new Guid("a7e4c2b1-5d8f-4a3e-9c12-6b8d0e4f1a73");
        public static readonly Guid Title = new Guid("b8f5d3c2-6e90-4b4f-8d23-7c9e1f5a2b84");
        public const string GroupName = "OmniClass";
    }
}
