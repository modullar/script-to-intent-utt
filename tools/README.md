# tools/

Run from the repository root, in this order. All of them accept `-h`.

| script          | reads                                            | writes                                   |
|-----------------|--------------------------------------------------|------------------------------------------|
| `inventory.py`  | `audio/raw/`, the vocabulary document            | `inventory.csv`, `missing.csv`; renames the two space-named files |
| `cleanup.py`    | `audio/raw/`                                     | `audio/clean/*.wav`, appends `cleanup.log` |
| `build_db.py`   | the document, `inventory.csv`, `audio/clean/`, `overrides.json`, `stopwords_es.txt` | `data/phrases.json`, `review.csv` |

Shared modules: `audio_utils.py` (ffmpeg/ffprobe + numpy helpers, never
writes), `vocab.py` (docx/TSV reader, standard library only).

Tests: `python3 -m pytest tools -q`.

Dependencies: `ffmpeg`/`ffprobe` on PATH, `numpy`, `pytest` (see
`requirements.txt`).
