using NahuatlIntent;
using Xunit;

namespace NahuatlIntent.Tests
{
    public class NormalizerTests
    {
        [Theory]
        [InlineData("¿Cuántos perros hay?", "cuantos perros hay")]
        [InlineData("¡Hola!", "hola")]
        [InlineData("Niño, niña", "nino nina")]
        [InlineData("  Buenas   NOCHES  ", "buenas noches")]
        [InlineData("él—dijo: \"sí\"", "el dijo si")]
        [InlineData("¿Cómo se dice buenas noches?", "como se dice buenas noches")]
        [InlineData("pingüino, açúcar", "pinguino acucar")]
        [InlineData("", "")]
        [InlineData(null, "")]
        public void Normalize_strips_accents_punctuation_and_spaces(string input, string expected)
        {
            Assert.Equal(expected, Normalizer.Normalize(input));
        }

        [Fact]
        public void Tokenize_splits_on_single_spaces()
        {
            string[] tokens = Normalizer.Tokenize("cuantos perros hay");
            Assert.Equal(new[] { "cuantos", "perros", "hay" }, tokens);
            Assert.Empty(Normalizer.Tokenize(""));
        }

        [Fact]
        public void MiniJson_parses_escapes_numbers_and_nesting()
        {
            var root = (System.Collections.Generic.Dictionary<string, object>)MiniJson.Parse(
                "{\"a\": \"caf\\u00e9 \\\"x\\\"\", \"n\": -1.5e1, \"b\": true, \"z\": null, \"l\": [1, {\"k\": \"v\"}]}");
            Assert.Equal("café \"x\"", MiniJson.GetString(root, "a", ""));
            Assert.Equal(-15.0, MiniJson.GetDouble(root, "n", 0));
            Assert.True(MiniJson.GetBool(root, "b", false));
            Assert.Null(root["z"]);
            var list = MiniJson.GetList(root, "l");
            Assert.Equal(2, list.Count);
            Assert.Equal(1.0, list[0]);
        }

        [Fact]
        public void Levenshtein_counts_edits()
        {
            Assert.Equal(0, IntentMatcher.Levenshtein("perro", "perro"));
            Assert.Equal(1, IntentMatcher.Levenshtein("peros", "perros"));
            Assert.Equal(2, IntentMatcher.Levenshtein("peros", "perro"));
            Assert.Equal(2, IntentMatcher.Levenshtein("duele", "duerme"));
            Assert.Equal(3, IntentMatcher.Levenshtein("abc", ""));
            Assert.Equal(2, IntentMatcher.Levenshtein("casa", "cosas", 1));
        }
    }
}
