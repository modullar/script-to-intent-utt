using System;

namespace NahuatlIntent
{
    /// <summary>
    /// One recording. Field names match data/phrases.json exactly so Unity's
    /// JsonUtility maps them without attributes. Plain public fields, no
    /// properties, no constructor logic.
    /// </summary>
    [Serializable]
    public class PhraseRow
    {
        /// <summary>"001" .. "122", or "fallback".</summary>
        public string id;

        /// <summary>Slug of the Spanish gloss, e.g. "buenas_noches". Duplicate
        /// recordings share one intent; one of them is canonical.</summary>
        public string intent;

        /// <summary>Normalised Spanish keywords. A multi-word entry is matched
        /// as a phrase and scores one point per word.</summary>
        public string[] keywordsEs;

        public string nahuatl;
        public string spanish;

        /// <summary>WAV file name in audio/clean (StreamingAssets in Unity).</summary>
        public string clip;

        public float durationS;

        /// <summary>The row to play for its intent when several share it.</summary>
        public bool canonical;

        /// <summary>False for rows whose label looks wrong; the matcher skips them.</summary>
        public bool approved;

        public string notes;
    }
}
