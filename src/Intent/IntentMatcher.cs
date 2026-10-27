using System;
using System.Collections.Generic;

namespace NahuatlIntent
{
    public sealed class MatcherOptions
    {
        /// <summary>Levenshtein distance allowed in the near-miss pass; 0 disables it.
        /// Keep it at 1: at 2, "duele" reaches "duerme" and "gato" reaches "plato".</summary>
        public int NearMissMaxDistance = 1;

        /// <summary>Input tokens and keywords shorter than this are not compared
        /// in the near-miss pass ("pero" must not reach "perro").</summary>
        public int NearMissMinTokenLength = 5;

        /// <summary>Match rows flagged approved=false as well (off by default).</summary>
        public bool IncludeUnapproved = false;
    }

    /// <summary>
    /// Keyword matcher. Scoring: a single-word keyword scores 1 when it equals a
    /// token of the input; an n-word keyword scores n when it appears as a
    /// contiguous phrase. Rows sharing an intent are one candidate (best row
    /// wins). The highest intent wins; a score of 0 or a tie between two
    /// intents returns the fallback row. When nothing hits exactly, an
    /// optional Levenshtein pass looks for near misses and flags the result.
    /// Plain loops only: no LINQ, no reflection, no dynamic.
    /// </summary>
    public sealed class IntentMatcher
    {
        private sealed class Keyword
        {
            public string Text;
            public string[] Tokens;
            public int Points;
        }

        private sealed class Candidate
        {
            public PhraseRow Row;
            public Keyword[] Keywords;
        }

        private readonly PhraseTable table;
        private readonly MatcherOptions options;
        private readonly Candidate[] candidates;
        private readonly HashSet<string> stopwords;

        public IntentMatcher(PhraseTable table) : this(table, null) { }

        public IntentMatcher(PhraseTable table, MatcherOptions options)
        {
            if (table == null) throw new ArgumentNullException(nameof(table));
            this.table = table;
            this.options = options ?? new MatcherOptions();

            stopwords = new HashSet<string>(StringComparer.Ordinal);
            string[] sw = table.StopwordsEs;
            for (int i = 0; i < sw.Length; i++) stopwords.Add(sw[i]);

            var list = new List<Candidate>(table.Count);
            PhraseRow[] rows = table.Rows;
            for (int i = 0; i < rows.Length; i++)
            {
                PhraseRow r = rows[i];
                if (r == null) continue;
                if (!r.approved && !this.options.IncludeUnapproved) continue;
                if (r.intent == table.FallbackIntent) continue;
                string[] kws = r.keywordsEs ?? new string[0];
                var compiled = new List<Keyword>(kws.Length);
                for (int k = 0; k < kws.Length; k++)
                {
                    string[] toks = Normalizer.Tokenize(kws[k]);
                    if (toks.Length == 0) continue;
                    var kw = new Keyword();
                    kw.Text = kws[k];
                    kw.Tokens = toks;
                    kw.Points = toks.Length;
                    compiled.Add(kw);
                }
                if (compiled.Count == 0) continue;
                var c = new Candidate();
                c.Row = r;
                c.Keywords = compiled.ToArray();
                list.Add(c);
            }
            candidates = list.ToArray();
        }

        public PhraseTable Table { get { return table; } }
        public MatcherOptions Options { get { return options; } }

        public MatchResult Match(string spanishText)
        {
            string normalized = Normalizer.Normalize(spanishText);
            string[] tokens = Normalizer.Tokenize(normalized);
            if (tokens.Length == 0) return Fallback(0, false, false, normalized);

            MatchResult exact = Pass(tokens, normalized, false);
            if (exact != null) return exact;

            if (options.NearMissMaxDistance > 0)
            {
                MatchResult near = Pass(tokens, normalized, true);
                if (near != null) return near;
            }
            return Fallback(0, false, false, normalized);
        }

        // One scoring pass. Returns null when the pass produced no hit at all;
        // returns a fallback result when it produced a cross-intent tie.
        private MatchResult Pass(string[] tokens, string normalized, bool nearMissPass)
        {
            string bestIntent = null;
            int bestScore = 0;
            PhraseRow bestRow = null;
            List<string> bestMatched = null;
            bool tie = false;

            var intentScore = new Dictionary<string, int>(candidates.Length, StringComparer.Ordinal);
            var intentRow = new Dictionary<string, PhraseRow>(candidates.Length, StringComparer.Ordinal);
            var intentMatched = new Dictionary<string, List<string>>(candidates.Length, StringComparer.Ordinal);

            for (int c = 0; c < candidates.Length; c++)
            {
                Candidate cand = candidates[c];
                List<string> matched = null;
                int score = nearMissPass
                    ? ScoreNearMiss(cand, tokens, ref matched)
                    : ScoreExact(cand, tokens, ref matched);
                if (score <= 0) continue;

                string intent = cand.Row.intent;
                int prev;
                if (!intentScore.TryGetValue(intent, out prev) || score > prev)
                {
                    intentScore[intent] = score;
                    intentRow[intent] = cand.Row;
                    intentMatched[intent] = matched;
                }
            }

            foreach (KeyValuePair<string, int> kv in intentScore)
            {
                if (kv.Value > bestScore)
                {
                    bestScore = kv.Value;
                    bestIntent = kv.Key;
                    bestRow = intentRow[kv.Key];
                    bestMatched = intentMatched[kv.Key];
                    tie = false;
                }
                else if (kv.Value == bestScore && bestIntent != null && kv.Key != bestIntent)
                {
                    tie = true;
                }
            }

            if (bestIntent == null) return null;
            if (tie) return Fallback(bestScore, nearMissPass, true, normalized);

            PhraseRow canonical = table.FindByIntent(bestIntent) ?? bestRow;
            return new MatchResult(bestIntent, canonical, bestScore, bestMatched.ToArray(),
                                   nearMissPass, false, false, normalized);
        }

        private static int ScoreExact(Candidate cand, string[] tokens, ref List<string> matched)
        {
            int score = 0;
            Keyword[] kws = cand.Keywords;
            for (int k = 0; k < kws.Length; k++)
            {
                Keyword kw = kws[k];
                bool hit = kw.Tokens.Length == 1
                    ? ContainsToken(tokens, kw.Tokens[0])
                    : ContainsPhrase(tokens, kw.Tokens);
                if (!hit) continue;
                score += kw.Points;
                if (matched == null) matched = new List<string>(4);
                matched.Add(kw.Text);
            }
            return score;
        }

        private int ScoreNearMiss(Candidate cand, string[] tokens, ref List<string> matched)
        {
            int score = 0;
            int maxDist = options.NearMissMaxDistance;
            int minLen = options.NearMissMinTokenLength;
            Keyword[] kws = cand.Keywords;
            for (int k = 0; k < kws.Length; k++)
            {
                Keyword kw = kws[k];
                if (kw.Tokens.Length != 1) continue;
                string word = kw.Tokens[0];
                if (word.Length < minLen) continue;
                for (int t = 0; t < tokens.Length; t++)
                {
                    string tok = tokens[t];
                    if (tok.Length < minLen || stopwords.Contains(tok)) continue;
                    if (Levenshtein(tok, word, maxDist) > maxDist) continue;
                    score += 1;
                    if (matched == null) matched = new List<string>(4);
                    matched.Add(word + "~" + tok);
                    break;
                }
            }
            return score;
        }

        private static bool ContainsToken(string[] tokens, string word)
        {
            for (int i = 0; i < tokens.Length; i++)
                if (string.Equals(tokens[i], word, StringComparison.Ordinal)) return true;
            return false;
        }

        private static bool ContainsPhrase(string[] tokens, string[] phrase)
        {
            int n = phrase.Length;
            for (int start = 0; start + n <= tokens.Length; start++)
            {
                bool ok = true;
                for (int j = 0; j < n; j++)
                {
                    if (!string.Equals(tokens[start + j], phrase[j], StringComparison.Ordinal))
                    {
                        ok = false;
                        break;
                    }
                }
                if (ok) return true;
            }
            return false;
        }

        private MatchResult Fallback(int score, bool nearMiss, bool tie, string normalized)
        {
            return new MatchResult(table.FallbackIntent, table.Fallback, score, new string[0],
                                   nearMiss, true, tie, normalized);
        }

        /// <summary>
        /// Classic two-row Levenshtein distance. When the length difference
        /// already exceeds <paramref name="limit"/> it returns limit + 1 at once.
        /// </summary>
        public static int Levenshtein(string a, string b, int limit)
        {
            if (a == null) a = string.Empty;
            if (b == null) b = string.Empty;
            int la = a.Length, lb = b.Length;
            if (Math.Abs(la - lb) > limit) return limit + 1;
            if (la == 0) return lb;
            if (lb == 0) return la;

            var prev = new int[lb + 1];
            var cur = new int[lb + 1];
            for (int j = 0; j <= lb; j++) prev[j] = j;
            for (int i = 1; i <= la; i++)
            {
                cur[0] = i;
                int rowMin = cur[0];
                char ca = a[i - 1];
                for (int j = 1; j <= lb; j++)
                {
                    int cost = ca == b[j - 1] ? 0 : 1;
                    int del = prev[j] + 1;
                    int ins = cur[j - 1] + 1;
                    int sub = prev[j - 1] + cost;
                    int v = del < ins ? del : ins;
                    if (sub < v) v = sub;
                    cur[j] = v;
                    if (v < rowMin) rowMin = v;
                }
                if (rowMin > limit) return limit + 1;
                int[] tmp = prev; prev = cur; cur = tmp;
            }
            return prev[lb];
        }

        public static int Levenshtein(string a, string b)
        {
            int la = a == null ? 0 : a.Length;
            int lb = b == null ? 0 : b.Length;
            return Levenshtein(a, b, la > lb ? la : lb);
        }
    }
}
