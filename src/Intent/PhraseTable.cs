using System;
using System.Collections.Generic;

namespace NahuatlIntent
{
    /// <summary>
    /// The phrase database in memory: rows by id, rows by intent, and the
    /// canonical row for each intent. Build it from a <see cref="PhraseFile"/>
    /// (Unity, via JsonUtility) or straight from JSON text with
    /// <see cref="FromJson"/> (demo and tests, via MiniJson).
    /// </summary>
    public sealed class PhraseTable
    {
        private readonly PhraseRow[] rows;
        private readonly string[] stopwords;
        private readonly string fallbackIntent;
        private readonly Dictionary<string, PhraseRow> byId;
        private readonly Dictionary<string, List<PhraseRow>> byIntent;
        private readonly Dictionary<string, PhraseRow> canonicalByIntent;
        private readonly PhraseRow fallback;

        public PhraseTable(PhraseFile file)
        {
            if (file == null) throw new ArgumentNullException(nameof(file));
            rows = file.rows ?? new PhraseRow[0];
            stopwords = NormalizeAll(file.stopwordsEs);
            fallbackIntent = string.IsNullOrEmpty(file.fallbackIntent) ? "fallback" : file.fallbackIntent;
            byId = new Dictionary<string, PhraseRow>(rows.Length, StringComparer.Ordinal);
            byIntent = new Dictionary<string, List<PhraseRow>>(rows.Length, StringComparer.Ordinal);
            canonicalByIntent = new Dictionary<string, PhraseRow>(rows.Length, StringComparer.Ordinal);

            for (int i = 0; i < rows.Length; i++)
            {
                PhraseRow r = rows[i];
                if (r == null) continue;
                if (r.id == null) r.id = string.Empty;
                r.intent = r.intent == null ? string.Empty : r.intent.Trim();
                r.keywordsEs = NormalizeAll(r.keywordsEs);
                if (!byId.ContainsKey(r.id)) byId[r.id] = r;

                List<PhraseRow> list;
                if (!byIntent.TryGetValue(r.intent, out list))
                {
                    list = new List<PhraseRow>(2);
                    byIntent[r.intent] = list;
                }
                list.Add(r);
            }

            foreach (KeyValuePair<string, List<PhraseRow>> kv in byIntent)
            {
                canonicalByIntent[kv.Key] = PickCanonical(kv.Value);
            }

            PhraseRow fb;
            fallback = canonicalByIntent.TryGetValue(fallbackIntent, out fb) ? fb : null;
        }

        /// <summary>Parse phrases.json with MiniJson (no Unity needed).</summary>
        public static PhraseTable FromJson(string json)
        {
            var root = MiniJson.Parse(json) as Dictionary<string, object>;
            if (root == null) throw new FormatException("phrases.json: root is not an object");

            var file = new PhraseFile();
            file.version = MiniJson.GetInt(root, "version", 1);
            file.generatedAt = MiniJson.GetString(root, "generatedAt", string.Empty);
            file.source = MiniJson.GetString(root, "source", string.Empty);
            file.fallbackIntent = MiniJson.GetString(root, "fallbackIntent", "fallback");
            file.stopwordsEs = MiniJson.GetStringArray(root, "stopwordsEs");

            List<object> rowList = MiniJson.GetList(root, "rows");
            var parsed = new List<PhraseRow>(rowList.Count);
            for (int i = 0; i < rowList.Count; i++)
            {
                var obj = rowList[i] as Dictionary<string, object>;
                if (obj == null) continue;
                var r = new PhraseRow();
                r.id = MiniJson.GetString(obj, "id", string.Empty);
                r.intent = MiniJson.GetString(obj, "intent", string.Empty);
                r.keywordsEs = MiniJson.GetStringArray(obj, "keywordsEs");
                r.nahuatl = MiniJson.GetString(obj, "nahuatl", string.Empty);
                r.spanish = MiniJson.GetString(obj, "spanish", string.Empty);
                r.clip = MiniJson.GetString(obj, "clip", string.Empty);
                r.durationS = (float)MiniJson.GetDouble(obj, "durationS", 0.0);
                r.canonical = MiniJson.GetBool(obj, "canonical", false);
                r.approved = MiniJson.GetBool(obj, "approved", true);
                r.notes = MiniJson.GetString(obj, "notes", string.Empty);
                parsed.Add(r);
            }
            file.rows = parsed.ToArray();
            return new PhraseTable(file);
        }

        public int Count { get { return rows.Length; } }
        public PhraseRow[] Rows { get { return rows; } }
        public string[] StopwordsEs { get { return stopwords; } }
        public string FallbackIntent { get { return fallbackIntent; } }

        /// <summary>The row played when nothing matches; null if the table has none.</summary>
        public PhraseRow Fallback { get { return fallback; } }

        /// <summary>Canonical row for an intent, or null when the intent is unknown.</summary>
        public PhraseRow FindByIntent(string intent)
        {
            if (intent == null) return null;
            PhraseRow r;
            return canonicalByIntent.TryGetValue(intent, out r) ? r : null;
        }

        /// <summary>Every row that shares an intent (duplicates included), in file order.</summary>
        public IReadOnlyList<PhraseRow> RowsForIntent(string intent)
        {
            List<PhraseRow> list;
            if (intent != null && byIntent.TryGetValue(intent, out list)) return list;
            return new PhraseRow[0];
        }

        public PhraseRow FindById(string id)
        {
            if (id == null) return null;
            PhraseRow r;
            return byId.TryGetValue(id, out r) ? r : null;
        }

        public bool HasIntent(string intent)
        {
            return intent != null && byIntent.ContainsKey(intent);
        }

        private static PhraseRow PickCanonical(List<PhraseRow> group)
        {
            for (int i = 0; i < group.Count; i++)
                if (group[i].canonical && group[i].approved) return group[i];
            for (int i = 0; i < group.Count; i++)
                if (group[i].approved) return group[i];
            return group.Count > 0 ? group[0] : null;
        }

        private static string[] NormalizeAll(string[] values)
        {
            if (values == null) return new string[0];
            var result = new List<string>(values.Length);
            for (int i = 0; i < values.Length; i++)
            {
                string n = Normalizer.Normalize(values[i]);
                if (n.Length > 0) result.Add(n);
            }
            return result.ToArray();
        }
    }
}
