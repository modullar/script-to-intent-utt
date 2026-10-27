using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Whisper.net;
using Whisper.net.Ggml;

namespace NahuatlIntent.Demo
{
    /// <summary>
    /// whisper.net wrapper: loads ggml-small (downloading it once from
    /// Hugging Face when absent) and transcribes 16 kHz mono WAV files in
    /// Spanish. In Unity this class is the one replaced by whisper.unity.
    /// </summary>
    public sealed class Transcriber : IDisposable
    {
        // ggml-small.bin is about 488 MB; anything far below that is a partial download.
        private const long MinPlausibleModelBytes = 400L * 1024 * 1024;

        private readonly WhisperFactory factory;
        private readonly WhisperProcessor processor;

        private Transcriber(WhisperFactory factory, WhisperProcessor processor)
        {
            this.factory = factory;
            this.processor = processor;
        }

        public static string DefaultModelPath()
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, ".cache", "script-to-intent-utt", "ggml-small.bin");
        }

        /// <summary>
        /// True when the file is large enough and starts with the ggml magic
        /// ("lmgg" on disk). A download interrupted with Ctrl-C fails both.
        /// </summary>
        public static bool LooksLikeGgmlModel(string path)
        {
            if (!File.Exists(path)) return false;
            if (new FileInfo(path).Length < MinPlausibleModelBytes) return false;
            var magic = new byte[4];
            using (FileStream fs = File.OpenRead(path))
            {
                if (fs.Read(magic, 0, 4) != 4) return false;
            }
            string tag = Encoding.ASCII.GetString(magic);
            return tag == "lmgg" || tag == "ggml";
        }

        public static async Task<Transcriber> CreateAsync(string modelPath)
        {
            modelPath = modelPath ?? DefaultModelPath();

            if (File.Exists(modelPath) && !LooksLikeGgmlModel(modelPath))
            {
                Console.WriteLine("Model file looks incomplete (" + new FileInfo(modelPath).Length / (1024 * 1024) +
                                  " MB), probably an interrupted download. Removing it and downloading again.");
                File.Delete(modelPath);
            }

            if (!File.Exists(modelPath))
            {
                await DownloadModelAsync(modelPath);
            }

            try
            {
                WhisperFactory factory = WhisperFactory.FromPath(modelPath);
                WhisperProcessor processor = factory.CreateBuilder()
                    .WithLanguage("es")
                    .Build();
                return new Transcriber(factory, processor);
            }
            catch (WhisperModelLoadException ex)
            {
                throw new InvalidOperationException(
                    "whisper.net could not load " + modelPath + " (" + new FileInfo(modelPath).Length / (1024 * 1024) +
                    " MB). Delete that file and run again to download it afresh.", ex);
            }
        }

        /// <summary>
        /// Downloads ggml-small to a temporary file next to the target and
        /// renames it only when complete, so an interrupted run leaves no
        /// half file behind. Prints progress because the download takes a while.
        /// </summary>
        private static async Task DownloadModelAsync(string modelPath)
        {
            Console.WriteLine("Downloading ggml-small to " + modelPath + " (about 488 MB, once). Do not interrupt.");
            Directory.CreateDirectory(Path.GetDirectoryName(modelPath));
            string partPath = modelPath + ".part";
            try
            {
                using (Stream model = await WhisperGgmlDownloader.Default.GetGgmlModelAsync(GgmlType.Small))
                using (FileStream file = File.Create(partPath))
                {
                    var buffer = new byte[1 << 20];
                    long total = 0;
                    long nextReport = 50L * 1024 * 1024;
                    int read;
                    while ((read = await model.ReadAsync(buffer, 0, buffer.Length)) > 0)
                    {
                        await file.WriteAsync(buffer, 0, read);
                        total += read;
                        if (total >= nextReport)
                        {
                            Console.WriteLine("  " + total / (1024 * 1024) + " MB");
                            nextReport += 50L * 1024 * 1024;
                        }
                    }
                }
                if (!LooksLikeGgmlModel(partPath))
                {
                    File.Delete(partPath);
                    throw new InvalidOperationException("The downloaded model is incomplete or not a ggml file; run again.");
                }
                File.Move(partPath, modelPath, true);
                Console.WriteLine("Model ready.");
            }
            catch
            {
                if (File.Exists(partPath)) File.Delete(partPath);
                throw;
            }
        }

        public async Task<string> TranscribeAsync(string wavPath)
        {
            var sb = new StringBuilder();
            using (FileStream stream = File.OpenRead(wavPath))
            {
                await foreach (SegmentData segment in processor.ProcessAsync(stream))
                {
                    sb.Append(segment.Text);
                }
            }
            return sb.ToString().Trim();
        }

        public void Dispose()
        {
            processor.Dispose();
            factory.Dispose();
        }
    }
}
