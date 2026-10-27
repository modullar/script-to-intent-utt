#!/usr/bin/env python3
"""Build the intent database (data/phrases.json) and the review sheet.

Usage:
    python3 tools/build_db.py [--vocab data/source/Vocabulario_nahuatl.docx]
                              [--inventory inventory.csv] [--clean audio/clean]
                              [--overrides tools/overrides.json]
                              [--stopwords tools/stopwords_es.txt]
                              [--out data/phrases.json] [--review review.csv]

One row per recording (numbered rows of the vocabulary document that have a
file, or all numbered rows when there is no inventory yet). Field names are
camelCase so Unity's JsonUtility maps them without attributes:

    id, intent, keywordsEs, nahuatl, spanish, clip, durationS, canonical,
    approved, notes

Keyword rules (see README "Keyword rules" for the reasoning):
  1. The whole normalised Spanish gloss is a keyword. The C# matcher scores
     an n-word keyword n points, so a longer phrase beats the shorter phrase
     it contains.
  2. The gloss with stopwords removed is a second phrase keyword when it
     still has two or more words ("hagan una casa" -> "hagan casa").
  3. Every content word (not a stopword) is a single-word keyword, plus a
     simple plural/singular variant.
  4. Ownership: a row owns its whole gloss; a single-word row also owns its
     variants unless a variant is another row's whole gloss. Nobody else may
     list an owned string as a standalone keyword. Unapproved rows own
     nothing and join no group.
  5. tools/overrides.json adds, removes and fixes on top of that.

Exit codes: 0 ok, 2 missing input.
"""
from __future__ import annotations

import argparse
import csv
import datetime as dt
import json
import re
import sys
import unicodedata
import wave
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))

import vocab  # noqa: E402
from audio_utils import parse_id_selection  # noqa: E402

ROOT = HERE.parent
CLIP_SUFFIX_DEFAULT = "-2026"


# ---------------------------------------------------------------------------
# Normalisation (must agree with src/Intent/Normalizer.cs)
# ---------------------------------------------------------------------------

def normalize(text: str) -> str:
    """lowercase, strip accents (ñ -> n), strip punctuation, collapse spaces."""
    if not text:
        return ""
    s = unicodedata.normalize("NFD", text.lower())
    s = "".join(ch for ch in s if unicodedata.category(ch) != "Mn")
    out = []
    for ch in s:
        if ch.isalnum():
            out.append(ch)
        else:
            out.append(" ")
    return " ".join("".join(out).split())


def slug(text: str) -> str:
    return normalize(text).replace(" ", "_")


def load_stopwords(path: Path) -> list[str]:
    words: list[str] = []
    if not path.is_file():
        return words
    for line in path.read_text(encoding="utf-8").splitlines():
        line = line.strip()
        if not line or line.startswith("#"):
            continue
        w = normalize(line)
        if w and w not in words:
            words.append(w)
    return words


# ---------------------------------------------------------------------------
# Keyword generation
# ---------------------------------------------------------------------------

def variants(word: str, stopwords: set[str]) -> list[str]:
    """Plural/singular forms worth listing. Kept deliberately simple."""
    out: list[str] = []
    if len(word) < 3:
        return out
    if word.endswith("es") and len(word) > 4:
        base = word[:-2]
        out.append(base)
        if base.endswith("c"):
            out.append(base[:-1] + "z")
    elif word.endswith("s") and len(word) > 3:
        out.append(word[:-1])
    elif word.endswith("z"):
        out.append(word[:-1] + "ces")
    elif word[-1] in "aeiou":
        out.append(word + "s")
    elif word[-1] in "lnrdj":
        out.append(word + "es")
    return [v for v in out if v not in stopwords and len(v) >= 3 and v != word]


def content_words(gloss_norm: str, stopwords: set[str]) -> list[str]:
    return [w for w in gloss_norm.split() if w not in stopwords]


class Builder:
    def __init__(self, stopwords: list[str], overrides: dict):
        self.stopwords = stopwords
        self.stop = set(stopwords)
        self.ov = overrides

    # -- rows ------------------------------------------------------------
    def build(self, numbered: dict[str, vocab.VocabRow], ids_with_clip: set[str] | None,
              clip_name: dict[str, str], duration: dict[str, float]) -> tuple[list[dict], list[dict]]:
        ov = self.ov
        unapproved = ov.get("unapproved", {})
        intent_ov = ov.get("intent", {})
        rows: list[dict] = []
        review: list[dict] = []

        # pass 1: basic fields
        for rid in sorted(numbered):
            vr = numbered[rid]
            gloss = normalize(vr.spanish)
            approved = rid not in unapproved and bool(gloss)
            intent = intent_ov.get(rid) or (slug(gloss) if gloss else f"row_{rid}")
            has_clip = ids_with_clip is None or rid in ids_with_clip
            rows.append({
                "id": rid, "intent": intent, "gloss": gloss, "approved": approved,
                "nahuatl": vr.nahuatl.strip(), "spanish": vr.spanish.strip(),
                "hasClip": has_clip, "notes": [], "keywords": [],
            })

        # pass 2: groups and ownership (approved rows with a clip only)
        active = [r for r in rows if r["approved"] and r["hasClip"]]
        gloss_intent: dict[str, str] = {r["gloss"]: r["intent"] for r in active}
        owner: dict[str, str] = {}          # string -> intent that owns it
        for r in active:
            owner.setdefault(r["gloss"], r["intent"])
        for r in active:
            words = r["gloss"].split()
            if len(words) == 1:
                for v in variants(words[0], self.stop):
                    if v in gloss_intent and gloss_intent[v] != r["intent"]:
                        continue
                    owner.setdefault(v, r["intent"])

        def may_use(s: str, intent: str) -> bool:
            o = owner.get(s)
            return o is None or o == intent

        # pass 3: keywords
        add_kw = ov.get("addKeywords", {})
        rem_kw = ov.get("removeKeywords", {})
        rem_gen = set(ov.get("removeGenerated", []))
        add_after = ov.get("addKeywordsAfterRemove", {})
        for r in rows:
            gloss, intent, rid = r["gloss"], r["intent"], r["id"]
            kws: list[str] = []
            if gloss:
                kws.append(gloss)
                cw = content_words(gloss, self.stop)
                stripped = " ".join(cw)
                if rid not in rem_gen:
                    if stripped != gloss and len(cw) >= 2 and may_use(stripped, intent):
                        kws.append(stripped)
                    # Variants only for single-word glosses (nouns such as
                    # perro/perros). Content words of phrases get none: the
                    # forms would be junk ("tardes" -> "tard").
                    single = len(gloss.split()) == 1
                    for w in cw:
                        if w != gloss and may_use(w, intent):
                            kws.append(w)
                        if single:
                            for v in variants(w, self.stop):
                                if may_use(v, intent):
                                    kws.append(v)
            for k in add_after.get(rid, []) + add_kw.get(rid, []):
                kws.append(normalize(k))
            removed = {normalize(k) for k in rem_kw.get(rid, [])}
            seen: set[str] = set()
            final: list[str] = []
            for k in kws:
                if k and k not in seen and k not in removed:
                    seen.add(k)
                    final.append(k)
            r["keywords"] = final

        # pass 4: canonical per intent, duplicate notes
        canon_ov = ov.get("canonical", {})
        by_intent: dict[str, list[dict]] = {}
        for r in active:
            by_intent.setdefault(r["intent"], []).append(r)
        for intent, group in by_intent.items():
            ids = [g["id"] for g in group]
            want = canon_ov.get(intent)
            canon = want if want in ids else ids[0]
            for g in group:
                g["canonical"] = g["id"] == canon
                if len(group) > 1:
                    others = [i for i in ids if i != g["id"]]
                    if g["canonical"]:
                        g["notes"].append(f"canonical for '{intent}'; duplicates: {', '.join(others)}")
                    else:
                        g["notes"].append(f"duplicate of {canon} ('{intent}')")
        for r in rows:
            r.setdefault("canonical", False)

        # pass 5: notes from overrides and state
        notes_ov = ov.get("notes", {})
        rr = ov.get("reRecorded", {})
        rr_ids = set(rr.get("ids", []))
        for r in rows:
            rid = r["id"]
            if rid in unapproved:
                r["notes"].insert(0, "SUSPECT: " + unapproved[rid])
            if not r["gloss"]:
                r["notes"].append("no Spanish gloss in the document")
            if rid in notes_ov:
                r["notes"].append(notes_ov[rid])
            if rid in rr_ids:
                r["notes"].append(rr.get("note", "re-recorded"))
            if not r["hasClip"]:
                r["notes"].append("no recording in audio/raw")

        # shared words across intents (honest ties) for the review sheet
        word_owners: dict[str, set[str]] = {}
        for r in active:
            for k in r["keywords"]:
                if " " not in k:
                    word_owners.setdefault(k, set()).add(r["intent"])
        ties = {w: sorted(s) for w, s in word_owners.items() if len(s) > 1}

        # output rows
        out_rows: list[dict] = []
        for r in rows:
            if not r["hasClip"]:
                continue
            rid = r["id"]
            clip = clip_name.get(rid, f"Audio-{rid}{CLIP_SUFFIX_DEFAULT}.wav")
            out_rows.append({
                "id": rid,
                "intent": r["intent"],
                "keywordsEs": r["keywords"],
                "nahuatl": r["nahuatl"],
                "spanish": r["spanish"],
                "clip": clip,
                "durationS": round(duration.get(rid, 0.0), 3),
                "canonical": bool(r["canonical"]),
                "approved": bool(r["approved"]),
                "notes": "; ".join(r["notes"]),
            })

        # fallback row
        fb_id = ov.get("fallback", "093")
        fb = next((o for o in out_rows if o["id"] == fb_id), None)
        if fb is None:
            print(f"build_db: fallback clip {fb_id} has no row; fallback row omitted", file=sys.stderr)
        else:
            out_rows.append({
                "id": "fallback", "intent": "fallback", "keywordsEs": [],
                "nahuatl": fb["nahuatl"], "spanish": fb["spanish"], "clip": fb["clip"],
                "durationS": fb["durationS"], "canonical": True, "approved": True,
                "notes": f"Played when nothing matches. Same recording as {fb_id}.",
            })

        # review sheet rows
        for r in rows:
            if not r["hasClip"]:
                status = "missing-clip"
            elif not r["approved"]:
                status = "suspect"
            elif r["intent"] in by_intent and len(by_intent[r["intent"]]) > 1 and not r["canonical"]:
                status = "duplicate"
            else:
                status = "ok"
            review.append({
                "id": r["id"], "status": status, "intent": r["intent"],
                "canonical": "yes" if r["canonical"] else "",
                "approved": "yes" if r["approved"] else "no",
                "nahuatl": r["nahuatl"], "spanish": r["spanish"],
                "keywordsEs": " | ".join(r["keywords"]),
                "clip": clip_name.get(r["id"], "") if r["hasClip"] else "",
                "durationS": f"{duration.get(r['id'], 0.0):.3f}" if r["hasClip"] else "",
                "notes": "; ".join(r["notes"]),
            })
        self.ties = ties
        return out_rows, review


# ---------------------------------------------------------------------------
# Inputs
# ---------------------------------------------------------------------------

def read_inventory(path: Path) -> dict[str, dict]:
    out: dict[str, dict] = {}
    if not path.is_file():
        return out
    with open(path, newline="", encoding="utf-8") as fh:
        for row in csv.DictReader(fh):
            rid = row.get("id") or ""
            if rid and rid not in out:
                out[rid] = row
    return out


def wav_duration(path: Path) -> float | None:
    try:
        with wave.open(str(path), "rb") as w:
            frames = w.getnframes()
            rate = w.getframerate()
            return frames / float(rate) if rate else None
    except (wave.Error, OSError):
        return None


def build_arg_parser() -> argparse.ArgumentParser:
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--vocab", default=str(ROOT / "data" / "source" / "Vocabulario_nahuatl.docx"))
    ap.add_argument("--inventory", default=str(ROOT / "inventory.csv"))
    ap.add_argument("--clean", default=str(ROOT / "audio" / "clean"))
    ap.add_argument("--overrides", default=str(HERE / "overrides.json"))
    ap.add_argument("--stopwords", default=str(HERE / "stopwords_es.txt"))
    ap.add_argument("--out", default=str(ROOT / "data" / "phrases.json"))
    ap.add_argument("--review", default=str(ROOT / "review.csv"))
    ap.add_argument("--all-numbered", action="store_true",
                    help="emit every numbered row even when inventory.csv lacks its file")
    ap.add_argument("--ids", default="",
                    help="with --all-numbered: ids to emit, e.g. 001-122 (default all numbered)")
    return ap


def main(argv=None) -> int:
    args = build_arg_parser().parse_args(argv)
    vocab_path = Path(args.vocab)
    if not vocab_path.is_file():
        print(f"build_db: vocabulary not found: {vocab_path}", file=sys.stderr)
        return 2
    overrides = json.loads(Path(args.overrides).read_text(encoding="utf-8")) if Path(args.overrides).is_file() else {}
    stopwords = load_stopwords(Path(args.stopwords))

    all_rows = vocab.read_vocab(vocab_path)
    numbered = vocab.numbered(all_rows)
    unnumbered = [r for r in all_rows if r.id is None]

    inv = read_inventory(Path(args.inventory))
    clean_dir = Path(args.clean)
    ids_with_clip: set[str] | None
    clip_name: dict[str, str] = {}
    duration: dict[str, float] = {}
    if inv:
        ids_with_clip = set()
        for rid, row in inv.items():
            if row.get("decodes", "true") == "false":
                continue
            ids_with_clip.add(rid)
            stem = Path(row["name"]).stem
            clip_name[rid] = stem + ".wav"
            raw_dur = float(row.get("durationS") or 0.0)
            d = wav_duration(clean_dir / (stem + ".wav"))
            duration[rid] = d if d is not None else raw_dur
        if args.all_numbered:
            ids_with_clip = None
    else:
        print("build_db: no inventory.csv; assuming every numbered row has a clip and durationS=0",
              file=sys.stderr)
        ids_with_clip = None
    if ids_with_clip is None and args.ids:
        wanted = parse_id_selection(args.ids)
        ids_with_clip = {rid for rid in numbered if rid in wanted}
    for rid in numbered:
        clip_name.setdefault(rid, f"Audio-{rid}{CLIP_SUFFIX_DEFAULT}.wav")

    builder = Builder(stopwords, overrides)
    out_rows, review = builder.build(numbered, ids_with_clip, clip_name, duration)

    for r in unnumbered:
        review.append({
            "id": "", "status": "unnumbered", "intent": "", "canonical": "", "approved": "",
            "nahuatl": r.nahuatl, "spanish": r.spanish, "keywordsEs": "", "clip": "",
            "durationS": "", "notes": "text-only row in the document, no clip number",
        })

    payload = {
        "version": 1,
        "generatedAt": dt.datetime.now(dt.timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"),
        "source": vocab_path.name,
        "fallbackIntent": "fallback",
        "stopwordsEs": stopwords,
        "rows": out_rows,
    }
    out_path = Path(args.out)
    out_path.parent.mkdir(parents=True, exist_ok=True)
    out_path.write_text(json.dumps(payload, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")

    cols = ["id", "status", "intent", "canonical", "approved", "nahuatl", "spanish",
            "keywordsEs", "clip", "durationS", "notes"]
    with open(args.review, "w", newline="", encoding="utf-8") as fh:
        w = csv.DictWriter(fh, fieldnames=cols)
        w.writeheader()
        for r in review:
            w.writerow(r)

    n_rows = len(out_rows)
    n_unapproved = sum(1 for r in out_rows if not r["approved"])
    n_intents = len({r["intent"] for r in out_rows})
    print(f"build_db: {n_rows} rows ({n_intents} intents, {n_unapproved} unapproved) -> {args.out}")
    print(f"build_db: {len(review)} review rows -> {args.review}")
    if builder.ties:
        print("build_db: single words shared by several intents (an input of just that word "
              "returns the fallback):")
        for w, intents in sorted(builder.ties.items()):
            print(f"  {w}: {', '.join(intents)}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
