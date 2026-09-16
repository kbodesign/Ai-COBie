using System.Collections.Generic;
using System.Linq;
using OmniClass.Core.Model;

namespace OmniClass.Core.Matching
{
    public enum MatchStatus
    {
        /// <summary>Normalized name is in the dictionary. Safe to apply without review.</summary>
        Exact,

        /// <summary>One strong fuzzy candidate. Needs a human to accept it.</summary>
        Probable,

        /// <summary>Several plausible candidates, or one weak one. Needs a human to choose.</summary>
        Ambiguous,

        /// <summary>Nothing close. Belongs in the harvest report so the dictionary can grow.</summary>
        Unmatched
    }

    public sealed class MatchCandidate
    {
        public MatchCandidate(RoomAlias alias, double score)
        {
            Alias = alias;
            Score = score;
        }

        public RoomAlias Alias { get; }
        public double Score { get; }

        public OmniClassEntry Entry => Alias.Entry;

        public override string ToString() =>
            $"{Entry.Number} {Entry.Title} (via '{Alias.Raw}', {Score:0.00})";
    }

    public sealed class ClassificationResult
    {
        private ClassificationResult(
            string roomName,
            string normalizedKey,
            MatchStatus status,
            MatchCandidate best,
            IReadOnlyList<MatchCandidate> candidates)
        {
            RoomName = roomName;
            NormalizedKey = normalizedKey;
            Status = status;
            Best = best;
            Candidates = candidates ?? new List<MatchCandidate>();
        }

        public string RoomName { get; }
        public string NormalizedKey { get; }
        public MatchStatus Status { get; }
        public MatchCandidate Best { get; }
        public IReadOnlyList<MatchCandidate> Candidates { get; }

        public string Number => Best?.Entry.Number.Canonical ?? string.Empty;
        public string Title => Best?.Entry.Title ?? string.Empty;
        public string MatchedAlias => Best?.Alias.Raw ?? string.Empty;
        public double Score => Best?.Score ?? 0d;

        /// <summary>
        /// Only exact dictionary hits are ever written without someone looking at them.
        /// A wrong number propagates into schedules and IFC exports where nobody rechecks it.
        /// </summary>
        public bool CanAutoApply => Status == MatchStatus.Exact;

        public bool NeedsReview => Status == MatchStatus.Probable || Status == MatchStatus.Ambiguous;

        public static ClassificationResult Exact(string roomName, string key, RoomAlias alias)
        {
            var best = new MatchCandidate(alias, 1d);
            return new ClassificationResult(roomName, key, MatchStatus.Exact, best, new[] { best });
        }

        public static ClassificationResult Fuzzy(
            string roomName,
            string key,
            MatchStatus status,
            IReadOnlyList<MatchCandidate> candidates)
        {
            return new ClassificationResult(roomName, key, status, candidates.FirstOrDefault(), candidates);
        }

        public static ClassificationResult Unmatched(string roomName, string key, IReadOnlyList<MatchCandidate> candidates = null)
        {
            return new ClassificationResult(roomName, key, MatchStatus.Unmatched, null, candidates);
        }
    }
}
