#!/usr/bin/env python3
"""
Locked-mode CARDS (Bucket 1) — re-skin the 3 default level-up celebration cards.

The default Cards/{fireworks,hearth,spotlight}.png are ~513x513 RGBA (opaque) neon
celebration graphics shown behind level-up / reward popups. We re-skin to the Locked
palette (glossy hot magenta-pink + deep black, red glitch accents) keeping each card's
MEANING. Symbolic, NO human figure.

Format match: cover-fit to each source's exact (W,H), output RGBA opaque.

Output: output/locked_cards/<same-filename>.png

Usage:
    python gen_locked_cards.py [--only fireworks] [--dry-run]
"""

from __future__ import annotations

import argparse
import io
import sys
import time
from pathlib import Path

from PIL import Image

from gen_avatars import load_api_key, extract_image_bytes, DEFAULT_MODEL, SCRIPT_DIR

SRC_DIR = Path(
    r"C:\Projects\Conditioning-Control-Panel---CSharp-WPF\ConditioningControlPanel\Resources\Cards"
)
OUT_DIR = SCRIPT_DIR / "output" / "locked_cards"

STYLE = (
    "Glossy NEON illustration on a DEEP BLACK background, hot MAGENTA-PINK as the lead colour "
    "with subtle RED glitch accents and bright neon bloom — a premium celebration / reward "
    "card graphic. Centered symbolic composition that fills the whole SQUARE frame edge to "
    "edge. NO human figure, NO face, NO text, NO words, NO numbers, NO UI chrome, NO border "
    "frame, no watermark, no logo."
)

CARDS = {
    "fireworks.png":
        "an exuberant burst of magenta and pink NEON FIREWORKS exploding across the black sky, "
        "with a few bursting sparks shaped like tiny KEYS and glints; celebratory and "
        "triumphant.",
    "hearth.png":
        "a large warm glowing magenta HEART with a KEYHOLE at its centre (a heart-shaped "
        "padlock), radiating a cozy magenta hearth-glow and soft embers; warm, intimate, "
        "rewarding.",
    "spotlight.png":
        "a dramatic magenta SPOTLIGHT beam shining straight down from above onto a small raised "
        "pedestal that bears a single glowing magenta KEYHOLE emblem, with a slim magenta crown "
        "of light above it; a moment-in-the-spotlight reward, NO person on the pedestal.",
}


def call(client, types, model, prompt):
    for attempt in range(4):
        resp = client.models.generate_content(model=model, contents=[prompt])
        data = extract_image_bytes(resp)
        if data:
            return data
        cand = (getattr(resp, "candidates", []) or [None])[0]
        print(f"    [retry {attempt}] no image (finish={getattr(cand,'finish_reason',None)})")
        time.sleep(2)
    return None


def cover_fit(raw, dims):
    img = Image.open(io.BytesIO(raw)).convert("RGB")
    w, h = img.size
    W, H = dims
    scale = max(W / w, H / h)
    nw, nh = max(1, round(w * scale)), max(1, round(h * scale))
    img = img.resize((nw, nh), Image.LANCZOS)
    left, top = (nw - W) // 2, (nh - H) // 2
    return img.crop((left, top, left + W, top + H)).convert("RGBA")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--only", default=None)
    ap.add_argument("--model", default=DEFAULT_MODEL)
    ap.add_argument("--dry-run", action="store_true")
    args = ap.parse_args()

    only = {x.strip() for x in args.only.split(",")} if args.only else None
    items = []
    for name, concept in CARDS.items():
        if only and name not in only and Path(name).stem not in only:
            continue
        src = SRC_DIR / name
        if not src.is_file():
            print(f"[warn] source missing: {name}")
            continue
        items.append((name, Image.open(src).size, concept))

    OUT_DIR.mkdir(parents=True, exist_ok=True)
    key, source = load_api_key(None)
    print(f"[key] {'found' if key else 'NOT FOUND'} ({source})  | {len(items)} cards")

    if args.dry_run:
        for name, dims, concept in items:
            print(f"  {name:16} {dims} :: {concept[:70]}")
        return
    if not key:
        sys.exit("No API key.")

    from google import genai
    from google.genai import types
    client = genai.Client(api_key=key)

    written, failed = [], []
    for name, dims, concept in items:
        print(f"\n[{name}] {dims} ...")
        data = call(client, types, args.model, f"{STYLE} The card depicts: {concept}")
        if not data:
            print(f"  [error] no image for {name}")
            failed.append(name)
            continue
        (OUT_DIR / f"_raw_{Path(name).stem}.png").write_bytes(data)
        out = cover_fit(data, dims)
        out.save(OUT_DIR / name)
        written.append((name, out.size))
        print(f"  -> {name}  {out.size} {out.mode}")
        time.sleep(1)

    print("\n=== SUMMARY ===")
    for name, size in written:
        print(f"  {name:16} {size}")
    if failed:
        print("  FAILED:", ", ".join(failed))


if __name__ == "__main__":
    main()
