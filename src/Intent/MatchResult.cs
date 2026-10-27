namespace NahuatlIntent
{
    /// <summary>What the matcher decided for one Spanish sentence.</summary>
    public sealed class MatchResult
    {
        /// <summary>Winning intent, or the fallback intent.</summary>
        public string Intent { get; }

        /// <summary>Canonical row of the winning intent (the clip to play). Null only
        /// when the table has no fallback row and nothing matched.</summary>
        public PhraseRow Row { get; }

        /// <summary>Points scored by the winner. 0 for a fallback with no hits; for a
        /// tie it is the tied score.</summary>
        public int Score { get; }

        /// <summary>Keywords that hit, in row order. Near-miss hits are written
        /// "keyword~token".</summary>
        public string[] MatchedKeywords { get; }

        /// <summary>True when the winner came from the Levenshtein pass.</summary>
        public bool NearMiss { get; }

        /// <summary>True when the fallback row was chosen.</summary>
        public bool IsFallback { get; }

        /// <summary>True when the fallback was chosen because two intents tied.</summary>
        public bool Tie { get; }

        /// <summary>The input after <see cref="Normalizer.Normalize"/>.</summary>
        public string NormalizedText { get; }

        public MatchResult(string intent, PhraseRow row, int score, string[] matchedKeywords,
                           bool nearMiss, bool isFallback, bool tie, string normalizedText)
        {
            Intent = intent;
            Row = row;
            Score = score;
            MatchedKeywords = matchedKeywords ?? new string[0];
            NearMiss = nearMiss;
            IsFallback = isFallback;
            Tie = tie;
            NormalizedText = normalizedText ?? string.Empty;
        }

        public override string ToString()
        {
            string id = Row != null ? Row.id : "-";
            return Intent + " (" + id + ") score=" + Score + (NearMiss ? " near-miss" : "") +
                   (Tie ? " tie" : "") + (IsFallback ? " fallback" : "");
        }
    }
}
