# script-to-intent-utt

A Spanish sentence goes in, one of the 122 Náhuatl recordings comes out.

* `src/Intent/` — the matcher, a C# library (netstandard2.1, no packages, no
  LINQ/reflection/dynamic) that is copied into the Unity project as files.
* `data/phrases.json` — the phrase database, one row per recording, built
  from the vocabulary document. Ships to Unity.
* `audio/clean/` — trimmed, level-matched 44.1 kHz mono WAVs. Ships to Unity.
* `tools/` — Python + ffmpeg, run once: inventory, cleanup, build_db.
* `src/Demo/` — terminal demo for macOS: whisper.net (ggml-small, Spanish)
  + sox microphone + afplay.
* `tests/Intent.Tests/` — xUnit, strings only, under five seconds.
* `tests/Integration/` — Whisper on your own recordings, run by hand.

## Install

macOS:

```sh
brew install --cask dotnet-sdk
brew install sox ffmpeg
pip3 install -r tools/requirements.txt      # numpy, pytest
```

Linux (Ubuntu 24.04): `apt install dotnet-sdk-8.0 ffmpeg`, then the pip line.

## 1. Audio: inventory, listen, cleanup

Download the Drive folder `Audios_náhuatl` into `audio/raw/` (122 MP3s,
`Audio-NNN-2026.mp3`). Then, from the repository root:

```sh
python3 tools/inventory.py      # renames "Audio 011"/"Audio 012" to the dash form,
                                # writes inventory.csv and missing.csv (expect 123-126)
python3 tools/cleanup.py        # audio/raw -> audio/clean, appends to cleanup.log,
                                # prints the clips to listen to
python3 tools/cleanup.py --only 090-105   # just the suspects, if you prefer
```

`cleanup.py` trims silence at both ends with 150 ms padding, measures the
integrated loudness of what is left with ffmpeg's `ebur128` meter, applies
the gain that brings it to -16 LUFS behind a -1.5 dBTP limiter, and writes
16-bit mono WAV at 44.1 kHz. A clip is flagged `LISTEN` when the trimmed
length is under a third of the original or when no speech was found. Raw
files are only ever opened for reading; the tool refuses to write into
`audio/raw`.

Finding worth knowing: the Session 3 deck said clips 090 to 105 "run 7 to
12 seconds". They do not. They are 1.2 to 2.5 s each, encoded at 320 kbps
stereo instead of 64 kbps mono, which is why their files are large. Two of
them (100 "Hijo", 102 "Piedra") still trim to under a third and are flagged.

## 2. Database

```sh
python3 tools/build_db.py       # data/phrases.json + review.csv
python3 -m pytest tools -q      # the Python tests
```

`build_db.py` reads the Word export (`data/source/Vocabulario_nahuatl.docx`,
parsed with the standard library; a TSV/CSV with id, nahuatl, spanish works
too), `inventory.csv` (which clips exist, raw duration), `audio/clean/`
(clean duration), `tools/overrides.json` and `tools/stopwords_es.txt`.

Each row of `phrases.json`:

| field        | meaning                                                                 |
|--------------|-------------------------------------------------------------------------|
| `id`         | `"001"` … `"122"`, or `"fallback"`                                      |
| `intent`     | slug of the Spanish gloss (`buenas_noches`); duplicates share one       |
| `keywordsEs` | normalised keywords; a multi-word entry is a phrase worth one point per word |
| `nahuatl`, `spanish` | the two columns of the document                                 |
| `clip`       | WAV name in `audio/clean` (StreamingAssets in Unity)                    |
| `durationS`  | clean duration, or raw duration until cleanup has run                  |
| `canonical`  | the row to play when several rows share the intent                      |
| `approved`   | `false` for rows whose label looks wrong; the matcher skips them        |
| `notes`      | why it is a duplicate, suspect, re-recorded, etc.                       |

The file also carries `fallbackIntent` and `stopwordsEs`. The fallback row
points at clip 093 (*Okzepa*, "otra vez"). There are no plant rows yet; add
them to the document, give them keywords in `overrides.json`, rerun.

### Keyword rules

1. The whole normalised gloss is a keyword. The matcher scores an n-word
   keyword n points, so "vende tu casa" (3) beats "tu casa" (2) beats
   "casa" (1).
2. The gloss without stopwords is a second phrase keyword when two or more
   words remain ("hagan una casa" → "hagan casa"), for children who drop
   articles.
3. Each content word is a keyword. Single-word glosses also get a simple
   plural/singular variant (perro/perros).
4. **Ownership.** A row owns its whole gloss (and, for single-word glosses,
   its variants). Nobody else may list an owned string on its own: 034
   "Casa" owns `casa`, so 035 "Hagan una casa" lists `hagan una casa`,
   `hagan casa`, `hagan`, but not `casa`. That is what makes "casa" reach
   034 instead of a four-way tie. Unapproved rows own nothing.
5. `tools/overrides.json` adds the rest: which rows are unapproved and why
   (020, 065), extra keywords (`hola` → 002, `buenos dias` → 055, `mama` →
   098 …), canonical choices, notes. Everything in it is echoed into
   `review.csv`.

Words that belong to several intents on purpose (`flor`: 011 and 021,
`hagan`: 035/059/071, `amo`: 010/109/111) tie and return the fallback;
`build_db.py` prints the list.

### Matching

`IntentMatcher.Match(text)`:

1. `Normalizer.Normalize`: lowercase, accents stripped (ñ → n),
   punctuation out, spaces collapsed. `build_db.py` applies the same steps
   to the keywords.
2. Exact pass: every keyword of every approved row is tested; rows sharing
   an intent count as one candidate; the best intent wins. Score 0 or a tie
   between two intents → fallback.
3. Near-miss pass, only when the exact pass found nothing: Levenshtein
   distance ≤ 1 between input tokens and single-word keywords, both at
   least five letters, stopwords skipped. The result is flagged
   `NearMiss`. (Distance 2 is too loose: "duele" would reach "duerme".)

`MatchResult` gives `Intent`, `Row` (the canonical row, i.e. the clip to
play), `Score`, `MatchedKeywords`, `NearMiss`, `IsFallback`, `Tie`.

## 3. Library and tests

```sh
dotnet test                      # Intent.Tests, runs in a few seconds
```

`tests/Intent.Tests/cases.csv` is the table-driven part: one line per case,
`text ; expected id` (or `fallback`). Add lines, rerun `dotnet test`.

## 4. Demo (macOS)

```sh
dotnet run --project src/Demo                        # microphone: Enter to start, Enter to stop
dotnet run --project src/Demo -- --wav my.wav        # a 16 kHz mono WAV
dotnet run --project src/Demo -- --text "cuántos perros hay"
```

It greets with clip 001, prints transcript, normalised text, keywords hit,
intent, clip, Náhuatl and Spanish, then plays the WAV with `afplay`. The
first run downloads `ggml-small` (about 480 MB) to
`~/.cache/script-to-intent-utt/`. `--no-audio` skips playback; `--threshold 0`
turns the near-miss pass off.

## 5. Integration run

See `tests/Integration/README.md`: record the ten sentences in a quiet and
a noisy room, run `dotnet test tests/Integration`, 8 of 10 must be right.

## 6. Handover to Unity

1. Copy `src/Intent/*.cs` into `Assets/Scripts/Phase3`. Nothing in them
   depends on .NET beyond netstandard2.1; IL2CPP on Android compiles them
   unchanged.
2. Copy `data/phrases.json` and `audio/clean/*.wav` into `StreamingAssets`.
3. Load the table:

   ```csharp
   string json  = File.ReadAllText(Path.Combine(Application.streamingAssetsPath, "phrases.json"));
   var file     = JsonUtility.FromJson<PhraseFile>(json);   // or PhraseTable.FromJson(json)
   var table    = new PhraseTable(file);
   var matcher  = new IntentMatcher(table);
   MatchResult r = matcher.Match(transcript);               // r.Row.clip is the WAV to play
   ```

4. Replace `src/Demo/Transcriber.cs` (whisper.net) with whisper.unity. The
   matcher only needs the transcript string.

Nothing else changes.

## Review with Sonia

* `review.csv` — every row of the document with a status: `ok`,
  `duplicate`, `suspect`, `missing-clip`, `unnumbered`; the generated
  keywords; the notes.
* `missing.csv` — document numbers without a recording (123–126).
* `cleanup.log` — what cleanup did to each clip, with the `LISTEN` flags.
* To change a keyword, a canonical choice or an approval: edit
  `tools/overrides.json`, run `build_db.py`, run `dotnet test`.

Known judgment calls, all marked in `review.csv`: 018 is the canonical
"buenas noches" (not 045/054); 055 answers "buenos días"; 049 answers
"bien gracias"; 041 also takes "no llora"; `buho` reaches 103. Twenty-two
clips (001–006, 016, 017, 026, 032, 045–055) were replaced on Drive on
2026-10-14 and match the *Toño* vocabulary list, i.e. a second speaker.
