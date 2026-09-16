using System;

namespace OmniClass.Core.Matching
{
    /// <summary>
    /// Which preview rows are checked by default. Kept here so the policy can be tested
    /// without loading the Revit API: only exact hits are pre-selected, and an existing
    /// value is left alone unless overwrite is on.
    /// </summary>
    public static class ApplyPolicy
    {
        public static bool HasExistingClassification(string number, string title)
        {
            return !string.IsNullOrWhiteSpace(number) || !string.IsNullOrWhiteSpace(title);
        }

        public static bool DefaultChecked(
            bool canAutoApply,
            bool alreadyClassified,
            bool overwriteExisting,
            bool isWritable)
        {
            if (!isWritable) return false;
            if (!canAutoApply) return false;
            if (alreadyClassified && !overwriteExisting) return false;
            return true;
        }

        public static string Reason(
            bool isUnplaced,
            bool isNotEnclosed,
            bool ownedByOther,
            string ownerName,
            bool alreadyClassified,
            bool overwriteExisting,
            MatchStatus status)
        {
            if (isUnplaced) return "Unplaced";
            if (isNotEnclosed) return "Not enclosed";
            if (ownedByOther)
            {
                return string.IsNullOrEmpty(ownerName)
                    ? "Owned by another user"
                    : "Owned by " + ownerName;
            }

            switch (status)
            {
                case MatchStatus.Unmatched:
                    return "No match";
                case MatchStatus.Probable:
                    return "Needs review";
                case MatchStatus.Ambiguous:
                    return "Ambiguous";
                case MatchStatus.Exact:
                    if (alreadyClassified && !overwriteExisting) return "Already classified";
                    return "Exact match";
                default:
                    return status.ToString();
            }
        }
    }
}
