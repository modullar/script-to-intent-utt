using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace NahuatlIntent.Demo
{
    /// <summary>
    /// Plays a clean WAV with whatever the operating system offers:
    /// afplay on macOS; paplay, aplay or ffplay on Linux; PowerShell's
    /// System.Media.SoundPlayer on Windows. The first command that works is
    /// kept; when none works, playback is disabled with one notice.
    /// </summary>
    public sealed class Player
    {
        private readonly string cleanDir;
        private readonly string[][] candidates;   // { exe, argument template with {0} = path }
        private int working = -1;
        private bool enabled;

        public Player(string cleanDir, bool enabled)
        {
            this.cleanDir = cleanDir;
            this.enabled = enabled;
            candidates = CandidatesForThisOs();
        }

        private static string[][] CandidatesForThisOs()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                return new[] { new[] { "afplay", "\"{0}\"" } };
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return new[] { new[] { "powershell", "-NoProfile -Command \"(New-Object System.Media.SoundPlayer '{0}').PlaySync()\"" } };
            return new[]
            {
                new[] { "paplay", "\"{0}\"" },
                new[] { "aplay", "-q \"{0}\"" },
                new[] { "ffplay", "-nodisp -autoexit -loglevel quiet \"{0}\"" },
            };
        }

        public void Play(string clip)
        {
            if (!enabled || string.IsNullOrEmpty(clip)) return;
            string path = Path.Combine(cleanDir, clip);
            if (!File.Exists(path))
            {
                Console.WriteLine("  (clip not found: " + path + ")");
                return;
            }

            if (working >= 0)
            {
                if (!TryPlay(candidates[working], path)) Disable();
                return;
            }
            for (int i = 0; i < candidates.Length; i++)
            {
                if (TryPlay(candidates[i], path))
                {
                    working = i;
                    return;
                }
            }
            Disable();
        }

        private static bool TryPlay(string[] candidate, string path)
        {
            try
            {
                var psi = new ProcessStartInfo(candidate[0], string.Format(candidate[1], path))
                {
                    UseShellExecute = false,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                };
                using (Process p = Process.Start(psi))
                {
                    if (p == null) return false;
                    p.StandardOutput.ReadToEnd();
                    p.StandardError.ReadToEnd();
                    p.WaitForExit();
                    return p.ExitCode == 0;
                }
            }
            catch (Win32Exception)
            {
                return false; // command not installed
            }
        }

        private void Disable()
        {
            Console.WriteLine("  (no audio player worked: tried " + string.Join(", ", Names()) +
                              "; playback disabled, use --no-audio to silence this)");
            enabled = false;
        }

        private string[] Names()
        {
            var names = new string[candidates.Length];
            for (int i = 0; i < candidates.Length; i++) names[i] = candidates[i][0];
            return names;
        }
    }
}
