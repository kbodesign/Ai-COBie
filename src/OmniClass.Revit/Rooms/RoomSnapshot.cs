namespace OmniClass.Revit.Rooms
{
    /// <summary>
    /// Everything the preview and the writer need from a room, captured before the
    /// dialog opens so the grid is not holding live Revit objects.
    /// </summary>
    public sealed class RoomSnapshot
    {
        public long Id { get; set; }
        public string Number { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string LevelName { get; set; } = string.Empty;
        public double Area { get; set; }
        public bool IsUnplaced { get; set; }
        public bool IsNotEnclosed { get; set; }
        public bool OwnedByOtherUser { get; set; }
        public string OwnerName { get; set; } = string.Empty;
        public string CurrentNumber { get; set; } = string.Empty;
        public string CurrentTitle { get; set; } = string.Empty;
        public string CurrentCategory { get; set; } = string.Empty;
        public bool HasCobieParameters { get; set; }

        public bool IsWritable => !IsUnplaced && !IsNotEnclosed && !OwnedByOtherUser;

        public bool AlreadyClassified =>
            OmniClass.Core.Matching.ApplyPolicy.HasExistingClassification(CurrentNumber, CurrentTitle)
            || !string.IsNullOrWhiteSpace(CurrentCategory);
    }
}
