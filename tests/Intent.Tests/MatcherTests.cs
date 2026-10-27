using System;
using System.Collections.Generic;
using System.IO;
using NahuatlIntent;
using Xunit;

namespace NahuatlIntent.Tests
{
    /// <summary>Loads data/phrases.json once for every test in the class.</summary>
    public class TableFixture
    {
        public PhraseTable Table { get; }
        public IntentMatcher Matcher { get; }

        public TableFixture()
        {
            string path = Path.Combine(AppContext.BaseDirectory, "data", "phrases.json");
            Table = PhraseTable.FromJson(File.ReadAllText(path));
            Matcher = new IntentMatcher(Table);
        }
    }

    public class MatcherTests : IClassFixture<TableFixture>
    {
        private readonly TableFixture fx;

        public MatcherTests(TableFixture fixture) { fx = fixture; }

        private MatchResult M(string text) { return fx.Matcher.Match(text); }

        // ---- table ---------------------------------------------------------

        [Fact]
        public void Table_loads_every_row_with_a_fallback()
        {
            Assert.True(fx.Table.Count >= 100, "expected the full database, got " + fx.Table.Count);
            Assert.NotNull(fx.Table.Fallback);
            Assert.Equal("fallback", fx.Table.Fallback.intent);
            Assert.Equal(fx.Table.FindById("093").clip, fx.Table.Fallback.clip);
            Assert.NotEmpty(fx.Table.StopwordsEs);
        }

        [Fact]
        public void FindByIntent_returns_the_canonical_duplicate()
        {
            PhraseRow row = fx.Table.FindByIntent("buenas_noches");
            Assert.Equal("018", row.id);
            Assert.True(row.canonical);
            Assert.True(fx.Table.RowsForIntent("buenas_noches").Count >= 3);
            Assert.Null(fx.Table.FindByIntent("no_such_intent"));
        }

        // ---- the cases the spec names ---------------------------------------

        [Fact]
        public void Perros_goes_to_095()
        {
            MatchResult r = M("¿Cuántos perros hay?");
            Assert.Equal("095", r.Row.id);
            Assert.False(r.NearMiss);
            Assert.False(r.IsFallback);
            Assert.Contains("perros", r.MatchedKeywords);
        }

        [Fact]
        public void Buenas_noches_goes_to_the_canonical_clip()
        {
            MatchResult r = M("Buenas noches");
            Assert.Equal("018", r.Row.id);
            Assert.Equal("buenas_noches", r.Intent);
        }

        [Fact]
        public void Mi_abuela_goes_to_089()
        {
            Assert.Equal("089", M("mi abuela").Row.id);
        }

        [Fact]
        public void Headache_has_no_plant_row_yet_and_falls_back()
        {
            MatchResult r = M("me duele la cabeza");
            Assert.True(r.IsFallback);
            Assert.False(r.NearMiss);
            Assert.Equal(fx.Table.Fallback, r.Row);
        }

        [Theory]
        [InlineData("el elefante toca música")]
        [InlineData("mi bicicleta es rápida")]
        [InlineData("zapato amarillo grande")]
        public void Nonsense_falls_back(string text)
        {
            MatchResult r = M(text);
            Assert.True(r.IsFallback, text + " -> " + r);
            Assert.Equal(0, r.Score);
        }

        [Fact]
        public void A_tie_between_two_intents_falls_back()
        {
            MatchResult r = M("agua y casa");
            Assert.True(r.IsFallback);
            Assert.True(r.Tie);
            Assert.Equal(1, r.Score);
        }

        [Fact]
        public void A_near_miss_is_flagged()
        {
            MatchResult r = M("cuantos peros hay");
            Assert.Equal("095", r.Row.id);
            Assert.True(r.NearMiss);
            Assert.False(r.IsFallback);
        }

        [Fact]
        public void Near_miss_ignores_short_tokens_and_stopwords()
        {
            // "pero" is 4 letters: never compared with "perro".
            MatchResult r = M("pero");
            Assert.True(r.IsFallback);
            Assert.False(r.NearMiss);
        }

        [Fact]
        public void Unapproved_rows_are_never_returned()
        {
            Assert.NotEqual("020", M("buenas noches").Row.id);
            Assert.Equal("102", M("piedras").Row.id);
            Assert.False(fx.Table.FindById("020").approved);
            Assert.False(fx.Table.FindById("065").approved);
        }

        [Fact]
        public void Empty_input_falls_back()
        {
            Assert.True(M("").IsFallback);
            Assert.True(M("¿?").IsFallback);
            Assert.True(M(null).IsFallback);
        }

        // ---- nested phrases ---------------------------------------------------

        [Theory]
        [InlineData("casa", "034")]
        [InlineData("tu casa", "058")]
        [InlineData("vende tu casa", "057")]
        [InlineData("tortilla", "061")]
        [InlineData("tortillas", "060")]
        [InlineData("duerme", "023")]
        [InlineData("duerme aquí", "070")]
        [InlineData("uno", "007")]
        [InlineData("clase uno", "088")]
        [InlineData("te amo", "111")]
        [InlineData("porque te amo", "010")]
        public void Longer_phrases_beat_the_phrases_they_contain(string text, string expectedId)
        {
            MatchResult r = M(text);
            Assert.Equal(expectedId, r.Row.id);
        }

        [Fact]
        public void Options_can_disable_the_near_miss_pass()
        {
            var strict = new IntentMatcher(fx.Table, new MatcherOptions { NearMissMaxDistance = 0 });
            Assert.True(strict.Match("cuantos peros hay").IsFallback);
        }

        // ---- editable case file -----------------------------------------------

        public static IEnumerable<object[]> Cases()
        {
            string path = Path.Combine(AppContext.BaseDirectory, "cases.csv");
            foreach (string raw in File.ReadAllLines(path))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                int sep = line.LastIndexOf(';');
                if (sep < 0) continue;
                yield return new object[] { line.Substring(0, sep).Trim(), line.Substring(sep + 1).Trim() };
            }
        }

        [Theory]
        [MemberData(nameof(Cases))]
        public void Case_file(string text, string expected)
        {
            MatchResult r = M(text);
            if (expected == "fallback")
            {
                Assert.True(r.IsFallback, "'" + text + "' should fall back but gave " + r);
            }
            else
            {
                Assert.False(r.IsFallback, "'" + text + "' fell back (" + r + ")");
                Assert.Equal(expected, r.Row.id);
            }
        }
    }
}
