#!/usr/bin/env python3
"""Inventory of the raw Náhuatl recordings.

Usage:
    python3 tools/inventory.py [--raw audio/raw] [--vocab data/source/Vocabulario_nahuatl.docx]
                               [--out inventory.csv] [--missing missing.csv] [--no-rename]

What it does
------------
1. Renames the two space-named files to the dash form
   ("Audio 011-2026.mp3" -> "Audio-011-2026.mp3"). This rename is the only
   change this tool ever makes inside audio/raw; file contents are never
   touched.
2. For every file in audio/raw writes one row to inventory.csv:
   name, id, durationS, sampleRate, channels, codec, container, bytes,
   peakDbfs, rmsDbfs, leadSilenceS, trailSilenceS, decodes, renamedFrom
   `decodes` is true when ffmpeg produced samples. The three files Drive
   tags as video (064, 071, 088) are plain MP3 and should read true.
3. Writes missing.csv: every numbered row of the vocabulary document that
   has no file (expected: 123-126).

Exit codes: 0 ok, 1 some file failed to decode, 2 missing input.
"""
from __future__ import annotations

import argparse
import csv
import os
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))

import audio_utils as au  # noqa: E402
import vocab  # noqa: E402

ROOT = HERE.parent
AUDIO_EXT = {".mp3", ".wav", ".m4a", ".aac", ".ogg", ".flac", ".mp4", ".avi", ".mov"}

COLUMNS = [
    "name", "id", "durationS", "sampleRate", "channels", "codec", "container",
    "bytes", "peakDbfs", "rmsDbfs", "leadSilenceS", "trailSilenceS", "decodes",
    "renamedFrom",
]


def build_arg_parser() -> argparse.ArgumentParser:
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--raw", default=str(ROOT / "audio" / "raw"))
    ap.add_argument("--vocab", default=str(ROOT / "data" / "source" / "Vocabulario_nahuatl.docx"))
    ap.add_argument("--out", default=str(ROOT / "inventory.csv"))
    ap.add_argument("--missing", default=str(ROOT / "missing.csv"))
    ap.add_argument("--threshold-db", type=float, default=-40.0,
                    help="window RMS above this counts as speech (default -40 dBFS)")
    ap.add_argument("--no-rename", action="store_true", help="do not rename space-form files")
    return ap


def rename_space_names(raw: Path) -> dict[str, str]:
    """Rename 'Audio 011-2026.mp3' to 'Audio-011-2026.mp3'. Returns new->old."""
    renamed: dict[str, str] = {}
    for p in sorted(raw.iterdir()):
        if not p.is_file() or p.name.startswith("."):
            continue
        new = au.dash_name(p.name)
        if new == p.name:
            continue
        target = raw / new
        if target.exists():
            print(f"inventory: cannot rename {p.name}: {new} already exists", file=sys.stderr)
            continue
        os.rename(p, target)
        renamed[new] = p.name
        print(f"renamed  {p.name!r} -> {new!r}")
    return renamed


def inventory_rows(raw: Path, renamed: dict[str, str], threshold_db: float) -> list[dict]:
    rows: list[dict] = []
    for p in sorted(raw.iterdir()):
        if not p.is_file() or p.name.startswith(".") or p.suffix.lower() not in AUDIO_EXT:
            continue
        row = {c: "" for c in COLUMNS}
        row["name"] = p.name
        row["id"] = au.parse_id(p.name) or ""
        row["bytes"] = p.stat().st_size
        row["renamedFrom"] = renamed.get(p.name, "")
        try:
            info = au.probe(p)
            row["durationS"] = f"{info.duration_s:.3f}"
            row["sampleRate"] = info.sample_rate
            row["channels"] = info.channels
            row["codec"] = info.codec
            row["container"] = info.container
        except au.AudioToolError as exc:
            print(f"inventory: {exc}", file=sys.stderr)
        try:
            samples = au.decode_mono(p, 44100)
            ok = samples.size > 0
            row["decodes"] = "true" if ok else "false"
            if ok:
                lead, trail = au.silence_bounds(samples, 44100, threshold_db)
                row["peakDbfs"] = f"{au.peak_dbfs(samples):.1f}"
                row["rmsDbfs"] = f"{au.rms_dbfs(samples):.1f}"
                row["leadSilenceS"] = f"{lead:.3f}"
                row["trailSilenceS"] = f"{trail:.3f}"
                if not row["durationS"]:
                    row["durationS"] = f"{samples.size / 44100.0:.3f}"
        except au.AudioToolError as exc:
            row["decodes"] = "false"
            print(f"inventory: {exc}", file=sys.stderr)
        rows.append(row)
    rows.sort(key=lambda r: (r["id"] or "999", r["name"]))
    return rows


def write_csv(path: Path, columns: list[str], rows: list[dict]) -> None:
    with open(path, "w", newline="", encoding="utf-8") as fh:
        w = csv.DictWriter(fh, fieldnames=columns)
        w.writeheader()
        for r in rows:
            w.writerow(r)


def main(argv=None) -> int:
    args = build_arg_parser().parse_args(argv)
    raw = Path(args.raw)
    if not raw.is_dir():
        print(f"inventory: raw folder not found: {raw}", file=sys.stderr)
        return 2

    renamed = {} if args.no_rename else rename_space_names(raw)
    rows = inventory_rows(raw, renamed, args.threshold_db)
    write_csv(Path(args.out), COLUMNS, rows)

    present = {r["id"] for r in rows if r["id"]}
    missing_rows: list[dict] = []
    vocab_path = Path(args.vocab)
    if vocab_path.is_file():
        for rid, vr in sorted(vocab.numbered(vocab.read_vocab(vocab_path)).items()):
            if rid not in present:
                missing_rows.append({"id": rid, "nahuatl": vr.nahuatl, "spanish": vr.spanish})
    else:
        print(f"inventory: vocabulary not found at {vocab_path}; missing.csv uses ids 001-126",
              file=sys.stderr)
        for i in range(1, 127):
            rid = f"{i:03d}"
            if rid not in present:
                missing_rows.append({"id": rid, "nahuatl": "", "spanish": ""})
    write_csv(Path(args.missing), ["id", "nahuatl", "spanish"], missing_rows)

    failures = [r["name"] for r in rows if r["decodes"] != "true"]
    print(f"inventory: {len(rows)} files, {len(failures)} decode failures, "
          f"{len(missing_rows)} document ids without a file -> {args.out}, {args.missing}")
    for name in failures:
        print(f"  does not decode: {name}")
    return 1 if failures else 0


if __name__ == "__main__":
    raise SystemExit(main())
