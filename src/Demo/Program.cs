using System;
using System.IO;
using System.Threading.Tasks;
using NahuatlIntent;

namespace NahuatlIntent.Demo
{
    internal sealed class DemoOptions
    {
        public string Text;
        public string Wav;
        public string Model;
        public string Data;
        public string Clean;
        public bool NoAudio;
        public int Threshold = 1;

        public static DemoOptions Parse(string[] args)
        {
            var o = new DemoOptions();
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                string next = i + 1 < args.Length ? args[i + 1] : null;
                switch (a)
                {
                    case "--text": o.Text = Require(next, a); i++; break;
                    case "--wav": o.Wav = Require(next, a); i++; break;
                    case "--model": o.Model = Require(next, a); i++; break;
                    case "--data": o.Data = Require(next, a); i++; break;
                    case "--clean": o.Clean = Require(next, a); i++; break;
                    case "--threshold": o.Threshold = int.Parse(Require(next, a)); i++; break;
                    case "--no-audio": o.NoAudio = true; break;
                    case "-h": case "--help": return null;
                    default:
                        Console.Error.WriteLine("unknown argument: " + a);
                        return null;
                }
            }
            return o;
        }

        private static string Require(string value, string flag)
        {
            if (value == null) throw new ArgumentException(flag + " needs a value");
            return value;
        }
    }

    internal static class Program
    {
        private static void PrintUsage()
        {
            Console.WriteLine(@"NahuatlIntentDemo: Spanish in, Náhuatl clip out.

  dotnet run --project src/Demo                      record from the microphone (sox), Enter to stop
  dotnet run --project src/Demo -- --wav file.wav     transcribe a 16 kHz mono WAV instead
  dotnet run --project src/Demo -- --text ""hola""      skip Whisper, match the text

Options:
  --model PATH     ggml model (default ~/.cache/script-to-intent-utt/ggml-small.bin, downloaded on first use)
  --data PATH      phrases.json (default <repo>/data/phrases.json)
  --clean DIR      clean WAV folder (default <repo>/audio/clean)
  --threshold N    Levenshtein distance for near misses (default 1, 0 disables)
  --no-audio       do not play clips (also skips the greeting)");
        }

        private static async Task<int> Main(string[] args)
        {
            DemoOptions opts;
            try { opts = DemoOptions.Parse(args); }
            catch (Exception ex) { Console.Error.WriteLine(ex.Message); return 2; }
            if (opts == null) { PrintUsage(); return 2; }

            string root = FindRepoRoot();
            string dataPath = opts.Data ?? Path.Combine(root, "data", "phrases.json");
            string cleanDir = opts.Clean ?? Path.Combine(root, "audio", "clean");
            if (!File.Exists(dataPath))
            {
                Console.Error.WriteLine("phrases.json not found: " + dataPath);
                return 2;
            }

            PhraseTable table = PhraseTable.FromJson(File.ReadAllText(dataPath));
            var matcher = new IntentMatcher(table, new MatcherOptions { NearMissMaxDistance = opts.Threshold });
            var player = new Player(cleanDir, !opts.NoAudio);
            Console.WriteLine("Loaded " + table.Count + " rows from " + dataPath);

            PhraseRow greeting = table.FindById("001");
            if (greeting != null)
            {
                Console.WriteLine("NPC: " + greeting.nahuatl + "   (" + greeting.spanish + ")");
                player.Play(greeting.clip);
            }

            if (opts.Text != null)
            {
                Handle(matcher, player, opts.Text, "typed");
                return 0;
            }

            Transcriber transcriber;
            try
            {
                transcriber = await Transcriber.CreateAsync(opts.Model);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("Whisper could not start: " + ex.Message);
                return 3;
            }
            using (transcriber)
            {
                if (opts.Wav != null)
                {
                    string text = await transcriber.TranscribeAsync(opts.Wav);
                    Handle(matcher, player, text, "whisper");
                    return 0;
                }

                var recorder = new Recorder();
                string wavPath = Path.Combine(Path.GetTempPath(), "script-to-intent-utt-rec.wav");
                while (true)
                {
                    Console.Write("\nEnter to record, q to quit: ");
                    string line = Console.ReadLine();
                    if (line == null || line.Trim().Equals("q", StringComparison.OrdinalIgnoreCase)) break;
                    string wav = recorder.RecordUntilEnter(wavPath);
                    if (wav == null) continue;
                    string text = await transcriber.TranscribeAsync(wav);
                    Handle(matcher, player, text, "whisper");
                }
            }
            return 0;
        }

        private static void Handle(IntentMatcher matcher, Player player, string text, string source)
        {
            MatchResult r = matcher.Match(text);
            Console.WriteLine();
            Console.WriteLine("transcript  (" + source + "): " + text);
            Console.WriteLine("normalised  : " + r.NormalizedText);
            Console.WriteLine("keywords hit: " + (r.MatchedKeywords.Length == 0 ? "-" : string.Join(", ", r.MatchedKeywords)));
            Console.WriteLine("intent      : " + r.Intent + "  score=" + r.Score +
                              (r.NearMiss ? "  near-miss" : "") + (r.Tie ? "  tie" : "") +
                              (r.IsFallback ? "  (fallback)" : ""));
            if (r.Row == null)
            {
                Console.WriteLine("clip        : (none)");
                return;
            }
            Console.WriteLine("clip        : " + r.Row.clip + "  [" + r.Row.id + "]");
            Console.WriteLine("nahuatl     : " + r.Row.nahuatl);
            Console.WriteLine("spanish     : " + r.Row.spanish);
            player.Play(r.Row.clip);
        }

        /// <summary>Walk up from the executable until data/phrases.json is found.</summary>
        private static string FindRepoRoot()
        {
            string dir = AppContext.BaseDirectory;
            for (int i = 0; i < 8 && dir != null; i++)
            {
                if (File.Exists(Path.Combine(dir, "data", "phrases.json"))) return dir;
                dir = Path.GetDirectoryName(dir.TrimEnd(Path.DirectorySeparatorChar));
            }
            return Directory.GetCurrentDirectory();
        }
    }
}
