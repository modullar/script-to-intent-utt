using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;

namespace NahuatlIntent.Demo
{
    /// <summary>Plays a clean WAV with afplay (macOS). Silently skips elsewhere.</summary>
    public sealed class Player
    {
        private readonly string cleanDir;
        private bool enabled;

        public Player(string cleanDir, bool enabled)
        {
            this.cleanDir = cleanDir;
            this.enabled = enabled;
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
            try
            {
                using (Process p = Process.Start(new ProcessStartInfo("afplay", "\"" + path + "\"") { UseShellExecute = false }))
                {
                    p?.WaitForExit();
                }
            }
            catch (Win32Exception)
            {
                Console.WriteLine("  (afplay not found; playback disabled)");
                enabled = false;
            }
        }
    }
}
