namespace OmniClass.Core.Model
{
    public enum ValidationSeverity
    {
        /// <summary>Worth knowing, costs nothing: redundant cells, collapsed duplicates.</summary>
        Info,

        /// <summary>Usable but suspicious. The dictionary still loads.</summary>
        Warning,

        /// <summary>The row is dropped. Fix before the dictionary is published.</summary>
        Error
    }

    public sealed class ValidationMessage
    {
        public ValidationMessage(ValidationSeverity severity, string code, string message, int sourceRow = 0, string sourceColumn = null)
        {
            Severity = severity;
            Code = code;
            Message = message;
            SourceRow = sourceRow;
            SourceColumn = sourceColumn;
        }

        public ValidationSeverity Severity { get; }

        /// <summary>Stable identifier so reports can be filtered without string matching.</summary>
        public string Code { get; }

        public string Message { get; }

        /// <summary>1-based spreadsheet row, matching what the author sees in Excel.</summary>
        public int SourceRow { get; }

        public string SourceColumn { get; }

        public string Location
        {
            get
            {
                if (SourceRow <= 0) return string.Empty;
                return string.IsNullOrEmpty(SourceColumn) ? "row " + SourceRow : SourceColumn + SourceRow;
            }
        }

        public override string ToString()
        {
            var location = Location;
            var prefix = location.Length == 0 ? string.Empty : location + ": ";
            return Severity.ToString().ToUpperInvariant() + " " + Code + " - " + prefix + Message;
        }
    }
}
