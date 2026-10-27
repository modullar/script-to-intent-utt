#!/usr/bin/env python3
"""Trim and level-match the raw recordings into audio/clean.

Usage:
    python3 tools/cleanup.py [--raw audio/raw] [--clean audio/clean] [--log cleanup.log]
                             [--only 090-105,007] [--pad-ms 150] [--target-lufs -16]
                             [--threshold-db -40] [--dry-run]

For every raw file:
1. Find speech with a windowed RMS gate (see audio_utils.silence_bounds) and
   keep it with --pad-ms of padding (150 ms) on both sides.
2. Measure the integrated loudness of that segment with ffmpeg's ebur128
   meter, apply the gain that brings it to --target-lufs (about -16 LUFS),
   with a true-peak limiter at --true-peak so nothing clips. Measure the
   result again and apply one small correction if the limiter pulled the
   level down. Clips too short or too quiet for the meter fall back to an
   RMS gain. (ffmpeg's loudnorm filter was tried first; its own report is
   several LU off on clips under three seconds, which is most of them.)
3. Write 44.1 kHz, mono, 16-bit WAV with the same stem to audio/clean.
4. Flag the clip when the trimmed length is under a third of the original
   (--flag-ratio) or when no speech was found. Listen to those.
5. Append one line per processed file to cleanup.log.

Raw files are only ever opened for reading. The tool refuses to write into
the raw folder. Exit codes: 0 ok, 1 one or more files failed, 2 bad input.
"""
from __future__ import annotations

import argparse
import datetime as dt
import math
import os
import re
import subprocess
import sys
import tempfile
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))

import audio_utils as au  # noqa: E402

ROOT = HERE.parent
AUDIO_EXT = {".mp3", ".wav", ".m4a", ".aac", ".ogg", ".flac", ".mp4", ".avi", ".mov"}
LOG_COLUMNS = [
    "timestamp", "name", "rawDurationS", "cleanDurationS", "startS", "endS",
    "inputLufs", "outputLufs", "gainDb", "method", "flag", "note",
]
EBU_I_RE = re.compile(r"^\s*I:\s*(-?[\d.]+|-inf)\s*LUFS", re.MULTILINE)
EBU_PEAK_RE = re.compile(r"^\s*Peak:\s*(-?[\d.]+|-inf)\s*dBFS", re.MULTILINE)


def build_arg_parser() -> argparse.ArgumentParser:
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--raw", default=str(ROOT / "audio" / "raw"))
    ap.add_argument("--clean", default=str(ROOT / "audio" / "clean"))
    ap.add_argument("--log", default=str(ROOT / "cleanup.log"))
    ap.add_argument("--only", default="", help="ids to process, e.g. 090-105,007 (default all)")
    ap.add_argument("--pad-ms", type=float, default=150.0)
    ap.add_argument("--target-lufs", type=float, default=-16.0)
    ap.add_argument("--true-peak", type=float, default=-1.5, help="limiter ceiling in dBTP")
    ap.add_argument("--threshold-db", type=float, default=-40.0)
    ap.add_argument("--flag-ratio", type=float, default=1.0 / 3.0)
    ap.add_argument("--sr", type=int, default=44100)
    ap.add_argument("--dry-run", action="store_true", help="measure and log, write nothing")
    return ap


# ---------------------------------------------------------------------------
# ffmpeg helpers
# ---------------------------------------------------------------------------

def _trim_filter(start: float, end: float) -> str:
    return f"atrim=start={start:.4f}:end={end:.4f},asetpts=PTS-STARTPTS"


def _ffmpeg(args: list[str]) -> str:
    cp = subprocess.run(
        ["ffmpeg", "-hide_banner", "-nostats", "-nostdin", "-loglevel", "info"] + args,
        stdout=subprocess.DEVNULL, stderr=subprocess.PIPE, check=False,
    )
    text = cp.stderr.decode("utf-8", "replace")
    if cp.returncode != 0:
        raise au.AudioToolError("ffmpeg failed: " + " ".join(args[-3:]) + "\n" + text[-400:])
    return text


def _parse_float(value: str | None) -> float | None:
    if value is None or value == "-inf":
        return None
    try:
        f = float(value)
    except ValueError:
        return None
    return None if (math.isinf(f) or math.isnan(f) or f <= -70.0) else f


def measure_lufs(path: Path, af_prefix: str = "") -> tuple[float | None, float | None]:
    """Integrated loudness (LUFS) and true peak (dBTP) of a file or segment."""
    af = (af_prefix + "," if af_prefix else "") + "ebur128=peak=true"
    text = _ffmpeg(["-i", str(path), "-af", af, "-f", "null", "-"])
    i_match = EBU_I_RE.findall(text)
    p_match = EBU_PEAK_RE.findall(text)
    lufs = _parse_float(i_match[-1]) if i_match else None
    peak = _parse_float(p_match[-1]) if p_match else None
    return lufs, peak


def render(raw: Path, out: Path, start: float, end: float, gain_db: float, ceiling_dbtp: float, sr: int) -> None:
    limit = 10.0 ** (ceiling_dbtp / 20.0)
    af = (_trim_filter(start, end) +
          f",volume={gain_db:.3f}dB" +
          f",alimiter=limit={limit:.4f}:attack=5:release=60:level=false")
    _ffmpeg(["-y", "-i", str(raw), "-af", af, "-ar", str(sr), "-ac", "1",
             "-c:a", "pcm_s16le", str(out)])


# ---------------------------------------------------------------------------
# One clip
# ---------------------------------------------------------------------------

def process_clip(raw: Path, clean_dir: Path, args) -> dict:
    sr = args.sr
    samples = au.decode_mono(raw, sr)
    if samples.size == 0:
        raise au.AudioToolError(f"{raw.name}: decoded to zero samples")
    duration = samples.size / float(sr)
    lead, trail = au.silence_bounds(samples, sr, args.threshold_db)
    pad = args.pad_ms / 1000.0
    notes: list[str] = []
    if lead >= duration:  # no window above the gate: keep everything
        start, end = 0.0, duration
        notes.append("no speech above gate; kept whole clip")
    else:
        start = max(0.0, lead - pad)
        end = min(duration, duration - trail + pad)
    clean_duration = end - start
    flagged = clean_duration < args.flag_ratio * duration or bool(notes)

    in_lufs, in_peak = measure_lufs(raw, _trim_filter(start, end))
    if in_lufs is not None:
        gain = args.target_lufs - in_lufs
        method = "ebur128-gain"
        if in_peak is not None and in_peak + gain > args.true_peak:
            method = "ebur128-gain+limiter"
    else:
        seg = samples[int(start * sr): int(end * sr)]
        rms = au.rms_dbfs(seg)
        gain = args.target_lufs - rms
        method = "rms-gain"
        notes.append("meter gave no integrated loudness; RMS gain used")

    rec = {
        "timestamp": dt.datetime.now(dt.timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"),
        "name": raw.name,
        "rawDurationS": f"{duration:.3f}",
        "cleanDurationS": f"{clean_duration:.3f}",
        "startS": f"{start:.3f}",
        "endS": f"{end:.3f}",
        "inputLufs": f"{in_lufs:.1f}" if in_lufs is not None else "",
        "outputLufs": "",
        "gainDb": f"{gain:.1f}",
        "method": method if not args.dry_run else "dry-run",
        "flag": "LISTEN" if flagged else "",
        "note": "",
    }
    if args.dry_run:
        rec["note"] = "; ".join(notes)
        return rec

    out = clean_dir / (raw.stem + ".wav")
    with tempfile.NamedTemporaryFile(prefix=".tmp-", suffix=".wav", dir=clean_dir, delete=False) as tf:
        tmp = Path(tf.name)
    try:
        render(raw, tmp, start, end, gain, args.true_peak, sr)
        out_lufs, _ = measure_lufs(tmp)
        # The limiter pulls peaky clips below the target; correct up to three
        # times while the result is still more than 0.5 LU off.
        passes = 0
        while (out_lufs is not None and in_lufs is not None
               and abs(out_lufs - args.target_lufs) > 0.5 and passes < 3):
            gain += args.target_lufs - out_lufs
            render(raw, tmp, start, end, gain, args.true_peak, sr)
            out_lufs, _ = measure_lufs(tmp)
            passes += 1
        if passes:
            notes.append(f"{passes} correction pass(es) after limiter")
            rec["gainDb"] = f"{gain:.1f}"
        rec["outputLufs"] = f"{out_lufs:.1f}" if out_lufs is not None else ""
        os.replace(tmp, out)
    finally:
        if tmp.exists():
            tmp.unlink()
    rec["note"] = "; ".join(notes)
    return rec


# ---------------------------------------------------------------------------
# Driver
# ---------------------------------------------------------------------------

def append_log(log_path: Path, records: list[dict]) -> None:
    new_file = not log_path.exists() or log_path.stat().st_size == 0
    with open(log_path, "a", encoding="utf-8") as fh:
        if new_file:
            fh.write("\t".join(LOG_COLUMNS) + "\n")
        for r in records:
            fh.write("\t".join(str(r.get(c, "")) for c in LOG_COLUMNS) + "\n")


def main(argv=None) -> int:
    args = build_arg_parser().parse_args(argv)
    raw_dir = Path(args.raw).resolve()
    clean_dir = Path(args.clean).resolve()
    if not raw_dir.is_dir():
        print(f"cleanup: raw folder not found: {raw_dir}", file=sys.stderr)
        return 2
    if clean_dir == raw_dir or raw_dir in clean_dir.parents:
        print("cleanup: refusing to write inside the raw folder", file=sys.stderr)
        return 2
    clean_dir.mkdir(parents=True, exist_ok=True)

    selection = au.parse_id_selection(args.only)
    files = [p for p in sorted(raw_dir.iterdir())
             if p.is_file() and not p.name.startswith(".") and p.suffix.lower() in AUDIO_EXT]
    if selection is not None:
        files = [p for p in files if au.parse_id(p.name) in selection]
    if not files:
        print("cleanup: nothing to do", file=sys.stderr)
        return 2

    records: list[dict] = []
    failures = 0
    for p in files:
        try:
            rec = process_clip(p, clean_dir, args)
        except au.AudioToolError as exc:
            failures += 1
            print(f"cleanup: FAILED {p.name}: {exc}", file=sys.stderr)
            continue
        records.append(rec)
        flag = " LISTEN" if rec["flag"] else ""
        print(f"{p.name}: {rec['rawDurationS']}s -> {rec['cleanDurationS']}s "
              f"[{rec['startS']}..{rec['endS']}] {rec['method']} "
              f"in={rec['inputLufs'] or '?'} out={rec['outputLufs'] or '?'} LUFS{flag}")

    if records:
        append_log(Path(args.log), records)
    flagged = [r["name"] for r in records if r["flag"]]
    print(f"cleanup: {len(records)} written, {failures} failed, {len(flagged)} flagged -> {args.log}")
    if flagged:
        print("listen to these (trimmed to under a third of the original, or no speech found):")
        for name in flagged:
            print("  " + name)
    return 1 if failures else 0


if __name__ == "__main__":
    raise SystemExit(main())
