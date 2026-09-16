using System;
using System.Collections.Generic;
using System.Linq;
using OmniClass.Core.Model;
using OmniClass.Core.Text;

namespace OmniClass.Core.Matching
{
    public sealed class ClassifierOptions
    {
        /// <summary>At or above this score a single candidate is offered as Probable.</summary>
        public double ProbableThreshold { get; set; } = 0.85;

        /// <summary>Below this score nothing is offered at all.</summary>
        public double ReviewThreshold { get; set; } = 0.65;

        /// <summary>
        /// Two candidates this close together are treated as a tie, unless one is a child of
        /// the other, in which case the more specific classification wins.
        /// </summary>
        public double TieMargin { get; set; } = 0.05;

        public int MaxCandidates { get; set; } = 5;

        public static ClassifierOptions Default { get; } = new ClassifierOptions();
    }

    /// <summary>
    /// Three tiers: exact normalized hit, fuzzy candidate for review, or nothing.
    /// Matching is always whole-token or whole-string; an alias is never treated as a
    /// substring, so "RR" cannot quietly classify a corridor.
    /// </summary>
    public sealed class RoomClassifier
    {
        private readonly ClassificationDictionary _dictionary;
        private readonly ClassifierOptions _options;

        public RoomClassifier(ClassificationDictionary dictionary, ClassifierOptions options = null)
        {
            _dictionary = dictionary ?? throw new ArgumentNullException(nameof(dictionary));
            _options = options ?? ClassifierOptions.Default;
        }

        public ClassificationResult Classify(string roomName)
        {
            var normalizer = _dictionary.NormalizerOptions;
            var key = RoomNameNormalizer.Key(roomName, normalizer);

            if (key.Length == 0) return ClassificationResult.Unmatched(roomName, key);

            var exact = _dictionary.FindExact(key);
            if (exact != null) return ClassificationResult.Exact(roomName, key, exact);

            var tokens = RoomNameNormalizer.Tokens(roomName, normalizer);
            var ranked = Rank(key, tokens);

            if (ranked.Count == 0 || ranked[0].Score < _options.ReviewThreshold)
            {
                return ClassificationResult.Unmatched(roomName, key);
            }

            var candidates = ranked.Take(_options.MaxCandidates).ToList();
            var status = Grade(candidates);

            return ClassificationResult.Fuzzy(roomName, key, status, candidates);
        }

        public IReadOnlyList<ClassificationResult> ClassifyAll(IEnumerable<string> roomNames)
        {
            return (roomNames ?? Enumerable.Empty<string>()).Select(Classify).ToList();
        }

        private List<MatchCandidate> Rank(string key, IReadOnlyList<string> tokens)
        {
            var scored = new List<MatchCandidate>();

            foreach (var alias in _dictionary.Aliases)
            {
                var score = Math.Max(
                    StringSimilarity.Characters(key, alias.Key),
                    StringSimilarity.Tokens(tokens, alias.Tokens));

                if (score >= _options.ReviewThreshold) scored.Add(new MatchCandidate(alias, score));
            }

            return scored
                .OrderByDescending(c => c.Score)
                // Prefer the more specific classification when scores are level.
                .ThenByDescending(c => c.Entry.Number.Depth)
                .ThenBy(c => c.Entry.Number.Canonical, StringComparer.Ordinal)
                .ToList();
        }

        private MatchStatus Grade(List<MatchCandidate> candidates)
        {
            var best = candidates[0];

            var rivals = candidates
                .Skip(1)
                .Where(c => !c.Entry.Number.Equals(best.Entry.Number))
                .Where(c => best.Score - c.Score <= _options.TieMargin)
                .ToList();

            // A parent losing to its own child is not a real conflict, it is the
            // specificity rule doing its job.
            var genuineRivals = rivals
                .Where(c => !best.Entry.Number.IsDescendantOf(c.Entry.Number))
                .ToList();

            if (genuineRivals.Count > 0) return MatchStatus.Ambiguous;

            return best.Score >= _options.ProbableThreshold ? MatchStatus.Probable : MatchStatus.Ambiguous;
        }
    }
}
