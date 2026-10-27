using System.Globalization;
using System.Text;

namespace NahuatlIntent
{
    /// <summary>
    /// Text normalisation shared by the matcher and the database builder
    /// (tools/build_db.py implements the same steps in Python):
    /// lowercase, strip accents through Unicode decomposition (ñ becomes n),
    /// replace punctuation with spaces, collapse runs of spaces.
    /// </summary>
    public static class Normalizer
    {
        // Spanish (and the odd Portuguese/French) accented letters, mapped by
        // hand as well. Unicode decomposition does the same job, but a build
        // with globalization-invariant mode, or an IL2CPP build with stripped
        // normalization tables, leaves "cómo" untouched and every accented
        // keyword would stop matching. The table makes that impossible.
        private const string Accented = "áéíóúüñàèìòùâêîôûäëïöç";
        private const string Plain    = "aeiouunaeiouaeiouaeioc";

        public static string Normalize(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            string lower = text.ToLowerInvariant();
            string decomposed;
            try
            {
                decomposed = lower.Normalize(NormalizationForm.FormD);
            }
            catch (System.Exception)
            {
                decomposed = lower; // invariant globalization mode: the table below still applies
            }
            var sb = new StringBuilder(decomposed.Length);
            bool pendingSpace = false;
            for (int i = 0; i < decomposed.Length; i++)
            {
                char c = decomposed[i];
                UnicodeCategory cat = CharUnicodeInfo.GetUnicodeCategory(c);
                if (cat == UnicodeCategory.NonSpacingMark) continue;
                int mapped = Accented.IndexOf(c);
                if (mapped >= 0) c = Plain[mapped];
                if (char.IsLetterOrDigit(c))
                {
                    if (pendingSpace && sb.Length > 0) sb.Append(' ');
                    pendingSpace = false;
                    sb.Append(c);
                }
                else
                {
                    pendingSpace = true;
                }
            }
            return sb.ToString();
        }

        /// <summary>Split an already normalised string on single spaces.</summary>
        public static string[] Tokenize(string normalized)
        {
            if (string.IsNullOrEmpty(normalized)) return new string[0];
            int count = 1;
            for (int i = 0; i < normalized.Length; i++) if (normalized[i] == ' ') count++;
            var tokens = new string[count];
            int n = 0;
            int start = 0;
            for (int i = 0; i <= normalized.Length; i++)
            {
                if (i == normalized.Length || normalized[i] == ' ')
                {
                    if (i > start) tokens[n++] = normalized.Substring(start, i - start);
                    start = i + 1;
                }
            }
            if (n == count) return tokens;
            var trimmed = new string[n];
            for (int i = 0; i < n; i++) trimmed[i] = tokens[i];
            return trimmed;
        }
    }
}
