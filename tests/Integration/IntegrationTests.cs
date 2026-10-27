using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using NahuatlIntent;
using NahuatlIntent.Demo;
using Xunit;
using Xunit.Abstractions;

namespace NahuatlIntent.Integration
{
    /// <summary>
    /// Whisper (ggml-small, Spanish) on your own recordings, then the matcher.
    /// Passes when at least 8 of the 10 sentences in a room reach the expected
    /// clip. Run by hand: dotnet test tests/Integration
    /// Set NAHUATL_MODEL to point at a ggml model if it is not in the default
    /// cache location.
    /// </summary>
    public class IntegrationTests
    {
        private readonly ITestOutputHelper output;

        public IntegrationTests(ITestOutputHelper output) { this.output = output; }

        [Theory]
        [InlineData("quiet")]
        [InlineData("noisy")]
        public async Task Eight_of_ten_sentences_reach_the_right_clip(string room)
        {
            string baseDir = AppContext.BaseDirectory;
            string wavDir = Path.Combine(baseDir, "wavs", room);
            List<(string file, string said, string expected)> cases = ReadSentences(Path.Combine(baseDir, "sentences.csv"));
            Assert.True(cases.Count >= 10, "sentences.csv needs at least ten rows");

            var missing = new List<string>();
            foreach (var c in cases)
                if (!File.Exists(Path.Combine(wavDir, c.file))) missing.Add(c.file);
            Assert.True(missing.Count == 0,
                "Record these first (see sentences.csv): " + string.Join(", ", missing) + " in " + wavDir);

            PhraseTable table = PhraseTable.FromJson(File.ReadAllText(Path.Combine(baseDir, "data", "phrases.json")));
            var matcher = new IntentMatcher(table);
            string model = Environment.GetEnvironmentVariable("NAHUATL_MODEL");

            int correct = 0;
            using (Transcriber transcriber = await Transcriber.CreateAsync(model))
            {
                foreach (var c in cases)
                {
                    string text = await transcriber.TranscribeAsync(Path.Combine(wavDir, c.file));
                    MatchResult r = matcher.Match(text);
                    bool ok = c.expected == "fallback" ? r.IsFallback : (!r.IsFallback && r.Row.id == c.expected);
                    if (ok) correct++;
                    output.WriteLine((ok ? "ok   " : "MISS ") + c.file + "  said='" + c.said + "'  heard='" + text +
                                     "'  -> " + r + "  expected " + c.expected);
                }
            }
            output.WriteLine(room + ": " + correct + "/" + cases.Count);
            Assert.True(correct * 10 >= cases.Count * 8, room + " room: " + correct + "/" + cases.Count + " correct, need 8/10");
        }

        private static List<(string, string, string)> ReadSentences(string path)
        {
            var list = new List<(string, string, string)>();
            foreach (string raw in File.ReadAllLines(path))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                string[] parts = line.Split(';');
                if (parts.Length < 3) continue;
                list.Add((parts[0].Trim(), parts[1].Trim(), parts[2].Trim()));
            }
            return list;
        }
    }
}
