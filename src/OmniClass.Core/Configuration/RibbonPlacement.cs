namespace OmniClass.Core.Configuration
{
    /// <summary>
    /// Where the add-in lands in Revit. Kept here so the placement is a tested contract,
    /// not a string that only exists inside the Revit project.
    /// </summary>
    public static class RibbonPlacement
    {
        public const string Tab = "Arch Tools";
        public const string Panel = "Room Data";
    }
}
