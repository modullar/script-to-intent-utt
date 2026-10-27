#!/usr/bin/env python3
"""pytest for the Python tools. Run: python3 -m pytest tools -q

No ffmpeg calls here; the audio tests use synthetic numpy signals.
"""
from __future__ import annotations

import io
import json
import sys
import zipfile
from pathlib import Path

import numpy as np
import pytest

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))

import audio_utils as au  # noqa: E402
import build_db  # noqa: E402
import vocab  # noqa: E402


# -- names -------------------------------------------------------------------

@pytest.mark.parametrize("name,expected", [
    ("Audio-011-2026.mp3", "011"),
    ("Audio 011-2026.mp3", "011"),
    ("audio_095.wav", "095"),
    ("notes.txt", None),
])
def test_parse_id(name, expected):
    assert au.parse_id(name) == expected


def test_dash_name():
    assert au.dash_name("Audio 011-2026.mp3") == "Audio-011-2026.mp3"
    assert au.dash_name("Audio-011-2026.mp3") == "Audio-011-2026.mp3"
    assert au.dash_name("Audio  012-2026.mp3") == "Audio-012-2026.mp3"


def test_parse_id_selection():
    assert au.parse_id_selection("090-092,7") == {"090", "091", "092", "007"}
    assert au.parse_id_selection("") is None


# -- silence -----------------------------------------------------------------

def _tone(seconds: float, sr: int, amp: float) -> np.ndarray:
    t = np.arange(int(seconds * sr)) / sr
    return (np.sin(2 * np.pi * 440 * t) * amp * 32767).astype(np.int16)


def test_silence_bounds_on_synthetic_clip():
    sr = 16000
    clip = np.concatenate([
        np.zeros(int(0.5 * sr), dtype=np.int16),
        _tone(1.0, sr, 0.3),
        np.zeros(int(0.25 * sr), dtype=np.int16),
    ])
    lead, trail = au.silence_bounds(clip, sr, threshold_db=-40.0, window_ms=20)
    assert abs(lead - 0.5) < 0.03
    assert abs(trail - 0.25) < 0.03


def test_silence_bounds_all_silent():
    sr = 16000
    clip = np.zeros(sr, dtype=np.int16)
    lead, trail = au.silence_bounds(clip, sr)
    assert lead >= 1.0 - 1e-6 and trail == 0.0


def test_levels():
    sr = 16000
    assert abs(au.peak_dbfs(_tone(0.1, sr, 0.5)) - (-6.0)) < 0.1
    assert au.rms_dbfs(np.zeros(10, dtype=np.int16)) == au.SILENCE_FLOOR_DB


# -- normalisation and keywords ---------------------------------------------

def test_normalize():
    assert build_db.normalize("¿Cuántos perros hay?") == "cuantos perros hay"
    assert build_db.normalize("  Niño,  ¡Sí! ") == "nino si"
    assert build_db.slug("Buenas noches") == "buenas_noches"


def _build(rows: dict[str, tuple[str, str]], overrides: dict | None = None):
    numbered = {rid: vocab.VocabRow(rid, nah, spa, i) for i, (rid, (nah, spa)) in enumerate(rows.items())}
    stop = build_db.load_stopwords(HERE / "stopwords_es.txt")
    b = build_db.Builder(stop, overrides or {})
    out, review = b.build(numbered, None, {}, {})
    return {r["id"]: r for r in out}, review


def test_ownership_and_variants():
    out, _ = _build({
        "034": ("Kali", "Casa"),
        "035": ("Xi K chigua kan se kali", "Hagan una casa"),
        "058": ("Mokal", "Tu casa"),
        "060": ("Tlaxcaltin", "Tortillas"),
        "061": ("Tlaxcali", "Tortilla"),
        "063": ("Xikpoua kaltin", "Cuenta casas"),
    })
    assert out["034"]["keywordsEs"] == ["casa", "casas"]
    assert out["035"]["keywordsEs"] == ["hagan una casa", "hagan casa", "hagan"]
    assert out["058"]["keywordsEs"] == ["tu casa"]
    assert "tortilla" not in out["060"]["keywordsEs"]
    assert "tortillas" not in out["061"]["keywordsEs"]
    assert "casas" not in out["063"]["keywordsEs"]


def test_groups_canonical_and_unapproved():
    out, review = _build({
        "018": ("Kuali yowal", "Buenas noches"),
        "020": ("Ken moijtos", "Buenas noches"),
        "045": ("Yohualtzino", "Buenas noches"),
        "093": ("Okzepa", "Otra vez"),
    }, {"unapproved": {"020": "wrong label"}, "fallback": "093"})
    assert out["018"]["canonical"] is True and out["045"]["canonical"] is False
    assert out["020"]["approved"] is False and out["020"]["canonical"] is False
    assert out["018"]["intent"] == out["045"]["intent"] == "buenas_noches"
    assert out["fallback"]["clip"] == out["093"]["clip"]
    assert out["fallback"]["keywordsEs"] == []
    statuses = {r["id"]: r["status"] for r in review}
    assert statuses == {"018": "ok", "020": "suspect", "045": "duplicate", "093": "ok"}


def test_stopwords_keep_no_inside_phrase_only():
    out, _ = _build({"041": ("Amo xi choca", "No llores"), "043": ("Xichoka", "Llora")})
    assert out["041"]["keywordsEs"][0] == "no llores"
    assert "no" not in out["041"]["keywordsEs"]


# -- docx parser --------------------------------------------------------------

def _mini_docx(rows: list[list[str]]) -> bytes:
    ns = "http://schemas.openxmlformats.org/wordprocessingml/2006/main"
    def cell(t):
        return f'<w:tc><w:p><w:r><w:t>{t}</w:t></w:r></w:p></w:tc>'
    body = "".join(f"<w:tr>{''.join(cell(c) for c in r)}</w:tr>" for r in rows)
    xml = f'<?xml version="1.0" encoding="UTF-8"?><w:document xmlns:w="{ns}"><w:body><w:tbl>{body}</w:tbl></w:body></w:document>'
    buf = io.BytesIO()
    with zipfile.ZipFile(buf, "w") as zf:
        zf.writestr("word/document.xml", xml)
    return buf.getvalue()


def test_read_docx(tmp_path):
    p = tmp_path / "v.docx"
    p.write_bytes(_mini_docx([
        ["Número de audio", "Vocabulario náhuatl", ""],
        ["TRANSCRIPCIÓN NÁHUATL", "SIGNIFICADO ESPAÑOL", ""],
        ["001", "¿Kenin nonkatej axxan noyolikniwan?", "¿Cómo están mis amigos?"],
        ["Tlaolli", "Maíz (desgranado).", ""],
    ]))
    rows = vocab.read_vocab(p)
    assert [(r.id, r.spanish) for r in rows] == [
        ("001", "¿Cómo están mis amigos?"), (None, "Maíz (desgranado)."),
    ]


def test_read_docx_refuses_doctype(tmp_path):
    p = tmp_path / "bad.docx"
    buf = io.BytesIO()
    with zipfile.ZipFile(buf, "w") as zf:
        zf.writestr("word/document.xml", '<!DOCTYPE x [<!ENTITY a "b">]><w:document/>')
    p.write_bytes(buf.getvalue())
    with pytest.raises(ValueError):
        vocab.read_vocab(p)


def test_real_vocab_document_if_present():
    doc = HERE.parent / "data" / "source" / "Vocabulario_nahuatl.docx"
    if not doc.is_file():
        pytest.skip("vocabulary document not present")
    numbered = vocab.numbered(vocab.read_vocab(doc))
    assert len(numbered) == 126
    assert numbered["095"].spanish.strip() == "Perro"
    assert numbered["093"].nahuatl.strip() == "Okzepa"
