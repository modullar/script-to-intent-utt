using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;

namespace NahuatlIntent.Demo
{
    /// <summary>Records from the default microphone with sox until Enter is pressed.</summary>
    public sealed class Recorder
    {
        public string RecordUntilEnter(string outPath)
        {
            if (File.Exists(outPath)) File.Delete(outPath);
            var psi = new ProcessStartInfo("sox", "-d -r 16000 -c 1 -b 16 \"" + outPath + "\"")
            {
                UseShellExecute = false,
            };
            Process sox;
            try
            {
                sox = Process.Start(psi);
            }
            catch (Win32Exception)
            {
                Console.Error.WriteLine("sox not found. Install it with: brew install sox");
                return null;
            }
            if (sox == null) return null;

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
            sox.Dispose();

            if (!File.Exists(outPath) || new FileInfo(outPath).Length < 1000)
            {
                Console.Error.WriteLine("Recording failed or too short.");
                return null;
            }
            return outPath;
        }
    }
}
