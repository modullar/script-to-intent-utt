#!/usr/bin/env python3
"""Shared audio helpers for the script-to-intent-utt tools.

ffmpeg and ffprobe do all decoding; numpy does the arithmetic. Nothing in
this module writes a file, so it can be used freely on the raw recordings.

Functions
---------
parse_id(name)          -> "011" from "Audio 011-2026.mp3" or "Audio-011-2026.mp3"
dash_name(name)         -> "Audio-011-2026.mp3" (the canonical file name)
probe(path)             -> ProbeInfo (duration, sample rate, channels, codec)
decode_mono(path, sr)   -> numpy int16 samples, mono, resampled to sr
peak_dbfs(samples)      -> peak level in dBFS
rms_dbfs(samples)       -> RMS level in dBFS
silence_bounds(samples, sr, threshold_db, window_ms)
                        -> (leading_silence_s, trailing_silence_s)
"""
from __future__ import annotations

import json
import math
import re
import subprocess
import sys
from dataclasses import dataclass
from pathlib import Path

try:
    import numpy as np
except ImportError:  # pragma: no cover
    print("audio_utils: numpy is required (pip install numpy)", file=sys.stderr)
    raise SystemExit(2)

FFMPEG = "ffmpeg"
FFPROBE = "ffprobe"

# "Audio-011-2026.mp3", "Audio 011-2026.mp3", "audio_011.mp3" all give "011".
ID_RE = re.compile(r"Audio[ _-]?(\d{3})", re.IGNORECASE)
SPACE_NAME_RE = re.compile(r"^Audio[ _]+(\d{3})", re.IGNORECASE)

SILENCE_FLOOR_DB = -120.0


# ---------------------------------------------------------------------------
# Names
# ---------------------------------------------------------------------------

def parse_id(name: str) -> str | None:
    """Return the three-digit clip id embedded in a file name, or None."""
    m = ID_RE.search(Path(name).name)
    return m.group(1) if m else None


def dash_name(name: str) -> str:
    """Return the dash form of a clip file name.

    'Audio 011-2026.mp3' -> 'Audio-011-2026.mp3'. Names that are already in
    dash form come back unchanged.
    """
    return SPACE_NAME_RE.sub(r"Audio-\1", Path(name).name)


# ---------------------------------------------------------------------------
# Probing and decoding
# ---------------------------------------------------------------------------

@dataclass
class ProbeInfo:
    duration_s: float
    sample_rate: int
    channels: int
    codec: str
    container: str
    has_audio: bool


class AudioToolError(RuntimeError):
    pass


def _run(cmd: list[str], *, capture_stderr: bool = False) -> subprocess.CompletedProcess:
    try:
        return subprocess.run(
            cmd,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE if capture_stderr else subprocess.DEVNULL,
            check=False,
        )
    except FileNotFoundError as exc:  # pragma: no cover
        raise AudioToolError(f"{cmd[0]} not found on PATH; install ffmpeg") from exc


def probe(path: str | Path) -> ProbeInfo:
    """Read stream and container facts with ffprobe. Never decodes audio."""
    cp = _run([
        FFPROBE, "-v", "error", "-print_format", "json",
        "-show_streams", "-show_format", str(path),
    ])
    if cp.returncode != 0 or not cp.stdout:
        raise AudioToolError(f"ffprobe failed on {path}")
    info = json.loads(cp.stdout.decode("utf-8", "replace"))
    fmt = info.get("format", {})
    audio = [s for s in info.get("streams", []) if s.get("codec_type") == "audio"]
    if not audio:
        return ProbeInfo(0.0, 0, 0, "", fmt.get("format_name", ""), False)
    s = audio[0]
    duration = fmt.get("duration") or s.get("duration") or 0.0
    return ProbeInfo(
        duration_s=float(duration),
        sample_rate=int(s.get("sample_rate") or 0),
        channels=int(s.get("channels") or 0),
        codec=s.get("codec_name", ""),
        container=fmt.get("format_name", ""),
        has_audio=True,
    )


def decode_mono(path: str | Path, sr: int = 44100) -> np.ndarray:
    """Decode any file ffmpeg understands to mono int16 samples at `sr` Hz.

    The file is only ever opened for reading. Raises AudioToolError when
    ffmpeg cannot decode it.
    """
    cp = _run([
        FFMPEG, "-v", "error", "-nostdin", "-i", str(path),
        "-f", "s16le", "-ac", "1", "-ar", str(sr), "-",
    ])
    if cp.returncode != 0:
        raise AudioToolError(f"ffmpeg could not decode {path}")
    return np.frombuffer(cp.stdout, dtype="<i2")


# ---------------------------------------------------------------------------
# Levels and silence
# ---------------------------------------------------------------------------

def _to_dbfs(linear: float) -> float:
    if linear <= 0.0:
        return SILENCE_FLOOR_DB
    return max(SILENCE_FLOOR_DB, 20.0 * math.log10(linear))


def peak_dbfs(samples: np.ndarray) -> float:
    if samples.size == 0:
        return SILENCE_FLOOR_DB
    peak = float(np.max(np.abs(samples.astype(np.float64)))) / 32768.0
    return _to_dbfs(peak)


def rms_dbfs(samples: np.ndarray) -> float:
    if samples.size == 0:
        return SILENCE_FLOOR_DB
    x = samples.astype(np.float64) / 32768.0
    return _to_dbfs(float(np.sqrt(np.mean(x * x))))


def window_rms_db(samples: np.ndarray, sr: int, window_ms: float = 20.0) -> np.ndarray:
    """RMS level in dBFS for consecutive non-overlapping windows."""
    win = max(1, int(sr * window_ms / 1000.0))
    n = samples.size // win
    if n == 0:
        return np.array([rms_dbfs(samples)])
    x = samples[: n * win].astype(np.float64) / 32768.0
    x = x.reshape(n, win)
    rms = np.sqrt(np.mean(x * x, axis=1))
    with np.errstate(divide="ignore"):
        db = 20.0 * np.log10(rms)
    db[~np.isfinite(db)] = SILENCE_FLOOR_DB
    return np.maximum(db, SILENCE_FLOOR_DB)


def silence_bounds(
    samples: np.ndarray,
    sr: int,
    threshold_db: float = -40.0,
    window_ms: float = 20.0,
) -> tuple[float, float]:
    """Seconds of silence at the start and at the end of the clip.

    A window counts as speech when its RMS is above `threshold_db`. When no
    window is above the threshold the whole clip is reported as leading
    silence and the trailing silence is 0, so callers can detect "no speech"
    with leading >= duration.
    """
    if samples.size == 0:
        return 0.0, 0.0
    win = max(1, int(sr * window_ms / 1000.0))
    db = window_rms_db(samples, sr, window_ms)
    above = np.where(db > threshold_db)[0]
    duration = samples.size / float(sr)
    if above.size == 0:
        return duration, 0.0
    first = int(above[0])
    last = int(above[-1])
    lead = first * win / float(sr)
    trail = duration - (last + 1) * win / float(sr)
    return max(0.0, lead), max(0.0, trail)


def parse_id_selection(spec: str | None) -> set[str] | None:
    """'090-105,007' -> {'007', '090', ..., '105'}; None or '' -> None (all)."""
    if not spec:
        return None
    out: set[str] = set()
    for part in spec.split(","):
        part = part.strip()
        if not part:
            continue
        if "-" in part:
            a, b = part.split("-", 1)
            for i in range(int(a), int(b) + 1):
                out.add(f"{i:03d}")
        else:
            out.add(f"{int(part):03d}")
    return out
