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

        public static async Task<Transcriber> CreateAsync(string modelPath)
        {
            modelPath = modelPath ?? DefaultModelPath();
            if (!File.Exists(modelPath))
            {
                Console.WriteLine("Downloading ggml-small to " + modelPath + " (about 480 MB, once)...");
                Directory.CreateDirectory(Path.GetDirectoryName(modelPath));
                using (Stream model = await WhisperGgmlDownloader.Default.GetGgmlModelAsync(GgmlType.Small))
                using (FileStream file = File.Create(modelPath))
                {
                    await model.CopyToAsync(file);
                }
            }
            WhisperFactory factory = WhisperFactory.FromPath(modelPath);
            WhisperProcessor processor = factory.CreateBuilder()
                .WithLanguage("es")
                .Build();
            return new Transcriber(factory, processor);
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
