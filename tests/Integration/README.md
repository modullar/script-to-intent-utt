# Integration run (slow, by hand, macOS)

Whisper on your own voice, then the matcher. Target: 8 of 10 sentences
correct in a quiet room and 8 of 10 in a noisy room.

1. Install: `brew install --cask dotnet-sdk` and `brew install sox`.
2. Record the ten sentences in `sentences.csv`, once per room. Same file
   names in both folders:

   ```sh
   sox -d -r 16000 -c 1 -b 16 tests/Integration/wavs/quiet/01.wav   # Ctrl-C to stop
   sox -d -r 16000 -c 1 -b 16 tests/Integration/wavs/noisy/01.wav
   ```

   The WAVs are git-ignored; they stay on your machine.
3. Run it. The first run downloads `ggml-small` (about 480 MB) into
   `~/.cache/script-to-intent-utt/`:

   ```sh
   dotnet test tests/Integration --logger "console;verbosity=detailed"
   ```

   Each line shows what you said, what Whisper heard, and the clip chosen.
   `NAHUATL_MODEL=/path/to/ggml-small.bin` overrides the model location.

This project is not in `NahuatlIntent.sln` on purpose, so `dotnet test` at
the repository root stays fast.
