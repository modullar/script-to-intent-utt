using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace NahuatlIntent.Demo
{
    /// <summary>
    /// Records from the default microphone with sox, 16 kHz mono 16-bit.
    /// On macOS and Linux it records until Enter is pressed (sox is stopped
    /// with SIGINT so it closes the WAV properly). Windows has no SIGINT for
    /// another process, so there it records a fixed number of seconds;
    /// --seconds forces that mode everywhere.
    /// </summary>
    public sealed class Recorder
    {
        private readonly int fixedSeconds;

        public Recorder(int fixedSeconds)
        {
            if (fixedSeconds <= 0 && RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                fixedSeconds = 5;
            this.fixedSeconds = fixedSeconds;
        }

        public bool FixedLength { get { return fixedSeconds > 0; } }
        public int Seconds { get { return fixedSeconds; } }

        public string Record(string outPath)
        {
            if (File.Exists(outPath)) File.Delete(outPath);
            string args = "-d -r 16000 -c 1 -b 16 \"" + outPath + "\"";
            if (fixedSeconds > 0) args += " trim 0 " + fixedSeconds;

            Process sox;
            try
            {
                sox = Process.Start(new ProcessStartInfo("sox", args) { UseShellExecute = false });
            }
            catch (Win32Exception)
            {
                Console.Error.WriteLine("sox not found. macOS: brew install sox. Linux: apt install sox. " +
                                        "Windows: choco install sox.portable (or sourceforge.net/projects/sox).");
                return null;
            }
            if (sox == null) return null;

            if (fixedSeconds > 0)
            {
                Console.WriteLine("Recording " + fixedSeconds + " seconds... speak now.");
                sox.WaitForExit();
            }
            else
            {
                Console.WriteLine("Recording... press Enter to stop.");
                Console.ReadLine();
                // sox finalises the WAV header on SIGINT; Kill() would leave it broken.
                try
                {
                    using (Process kill = Process.Start(new ProcessStartInfo("kill", "-INT " + sox.Id) { UseShellExecute = false }))
                    {
                        kill?.WaitForExit(2000);
                    }
                }
                catch (Win32Exception) { }
                if (!sox.WaitForExit(5000)) sox.Kill();
            }
            sox.Dispose();

            if (!File.Exists(outPath) || new FileInfo(outPath).Length < 1000)
            {
                Console.Error.WriteLine("Recording failed or too short. Is a microphone connected and allowed?");
                return null;
            }
            return outPath;
        }
    }
}
