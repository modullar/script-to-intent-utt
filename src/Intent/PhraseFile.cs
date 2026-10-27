using System;

namespace NahuatlIntent
{
    /// <summary>
    /// The whole data/phrases.json document. In Unity:
    /// <code>
    ///   var file  = JsonUtility.FromJson&lt;PhraseFile&gt;(jsonText);
    ///   var table = new PhraseTable(file);
    /// </code>
    /// Outside Unity, <see cref="PhraseTable.FromJson(string)"/> uses the
    /// built-in MiniJson reader instead.
    /// </summary>
    [Serializable]
    public class PhraseFile
    {
        public int version;
        public string generatedAt;
        public string source;
        public string fallbackIntent;

        /// <summary>Spanish stopwords; the near-miss pass ignores these tokens.</summary>
        public string[] stopwordsEs;

        public PhraseRow[] rows;
    }
}
