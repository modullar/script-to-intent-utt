#!/usr/bin/env python3
"""Read the vocabulary document that lists every recording.

Accepted inputs
---------------
* The Word export from Drive (`Vocabulario_nahuatl.docx`): the first table
  with three columns "Número de audio | Transcripción náhuatl | Significado
  español". Parsed with the standard library only (zipfile + xml.etree).
* A TSV or CSV with the columns id, nahuatl, spanish (header optional), for
  teams that prefer a spreadsheet export.

Rows without a number (the ~60 text-only rows at the end of the document)
are kept with `id=None` so build_db.py can list them for review.
"""
from __future__ import annotations

import csv
import re
import sys
import zipfile
from dataclasses import dataclass
from pathlib import Path
from xml.etree import ElementTree as ET

W_NS = "http://schemas.openxmlformats.org/wordprocessingml/2006/main"
W = "{%s}" % W_NS

HEADER_WORDS = ("numero de audio", "transcripcion", "significado", "vocabulario nahuatl")


@dataclass
class VocabRow:
    id: str | None          # "001" .. "126", or None for unnumbered rows
    nahuatl: str
    spanish: str
    order: int              # position in the document, for stable output


def _cell_text(tc: ET.Element) -> str:
    parts: list[str] = []
    for p in tc.iter(W + "p"):
        texts = [t.text or "" for t in p.iter(W + "t")]
        if texts:
            parts.append("".join(texts))
    return " ".join(s.strip() for s in parts if s.strip()).strip()


def _looks_like_header(cells: list[str]) -> bool:
    joined = " ".join(cells).lower()
    joined = re.sub(r"[áéíóúü]", lambda m: "aeiouu"["áéíóúü".index(m.group(0))], joined)
    return any(w in joined for w in HEADER_WORDS)


def read_docx(path: str | Path) -> list[VocabRow]:
    with zipfile.ZipFile(path) as zf:
        xml = zf.read("word/document.xml")
    # A docx never declares a DOCTYPE. Refusing one closes the entity
    # expansion and external entity attacks on the stdlib parser without
    # needing defusedxml.
    if b"<!DOCTYPE" in xml[:4096] or b"<!ENTITY" in xml:
        raise ValueError(f"{path}: refusing document with a DOCTYPE/ENTITY declaration")
    root = ET.fromstring(xml)
    rows: list[VocabRow] = []
    order = 0
    for tbl in root.iter(W + "tbl"):
        for tr in tbl.iter(W + "tr"):
            cells = [_cell_text(tc) for tc in tr.findall(W + "tc")]
            cells = [c.strip() for c in cells]
            if not any(cells):
                continue
            if _looks_like_header(cells):
                continue
            first = cells[0] if cells else ""
            if re.fullmatch(r"\d{1,3}", first):
                rid = f"{int(first):03d}"
                nah = cells[1] if len(cells) > 1 else ""
                spa = cells[2] if len(cells) > 2 else ""
            else:
                rid = None
                nah = cells[0] if cells else ""
                spa = cells[1] if len(cells) > 1 else ""
            if not nah and not spa:
                continue
            order += 1
            rows.append(VocabRow(rid, nah, spa, order))
        break  # only the first table holds the vocabulary
    return rows


def read_table(path: str | Path) -> list[VocabRow]:
    p = Path(path)
    delim = "\t" if p.suffix.lower() in (".tsv", ".txt") else ","
    rows: list[VocabRow] = []
    with open(p, newline="", encoding="utf-8-sig") as fh:
        reader = csv.reader(fh, delimiter=delim)
        order = 0
        for raw in reader:
            cells = [c.strip() for c in raw]
            if not any(cells):
                continue
            if cells[0].lower() in ("id", "numero", "número") or _looks_like_header(cells):
                continue
            first = cells[0]
            if re.fullmatch(r"\d{1,3}", first):
                rid = f"{int(first):03d}"
                nah = cells[1] if len(cells) > 1 else ""
                spa = cells[2] if len(cells) > 2 else ""
            else:
                rid = None
                nah = cells[0]
                spa = cells[1] if len(cells) > 1 else ""
            order += 1
            rows.append(VocabRow(rid, nah, spa, order))
    return rows


def read_vocab(path: str | Path) -> list[VocabRow]:
    """Dispatch on the file extension."""
    p = Path(path)
    if not p.is_file():
        raise FileNotFoundError(p)
    if p.suffix.lower() == ".docx":
        return read_docx(p)
    return read_table(p)


def numbered(rows: list[VocabRow]) -> dict[str, VocabRow]:
    """First occurrence wins when a number is repeated in the document."""
    out: dict[str, VocabRow] = {}
    for r in rows:
        if r.id is not None and r.id not in out:
            out[r.id] = r
    return out


if __name__ == "__main__":  # small manual check: python3 tools/vocab.py file
    for r in read_vocab(sys.argv[1]):
        print(r.id or "---", "|", r.nahuatl, "|", r.spanish)
