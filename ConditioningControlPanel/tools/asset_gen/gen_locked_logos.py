#!/usr/bin/env python3
"""
Locked-mode logo2.png + preview.png (Bucket 1) — the mod's secondary logo and the
mode-picker hero card.

Mirrors the mod-build convention (DroneMod): logo2 and preview are 2076x2048 RGB
(opaque), the same big square format as our existing logo.png — so they stay visually
consistent with the logo we already generated.

  * logo2  -> a SIBLING of logo.png: same magenta HUD panel composition, but with a
              glowing KEYHOLE emblem in the centre ring instead of the figure (a clean
              secondary mark). Anchored to our locked logo.png for layout/style.
  * preview -> the mode-select hero card: FEATURE the Locked character (anchored to the
              waist-up master crop) with bold "LOCKED" branding on deep black + magenta.

Output: output/locked_modassets/{logo2,preview}.png

Usage:
    python gen_locked_logos.py [--only logo2.png,preview.png] [--dry-run]
"""

from __future__ import annotations

import argparse
import io
import sys
import time
from pathlib import Path

from PIL import Image

from gen_avatars import load_api_key, extract_image_bytes, DEFAULT_MODEL, SCRIPT_DIR

OUT_DIR = SCRIPT_DIR / "output" / "locked_modassets"
LOCKED_LOGO = OUT_DIR / "logo.png"                                   # layout/style anchor
MASTER_SQ = SCRIPT_DIR / "output" / "locked_character" / "_ref_waistup_sq.png"
LOGO_DIMS = (2076, 2048)

LOGO2_PROMPT = (
    "Use the FIRST image as the exact LAYOUT and STYLE reference and KEEP its SQUARE aspect "
    "ratio and composition: the same circular HUD ring, the same round buttons at the top, "
    "the same left/right status read-outs, the same heart-rate trace lines, the same bottom "
    "title block and the same dense code-matrix background filling the whole square frame "
    "edge to edge. Recolour everything hot MAGENTA-PINK on deep BLACK — absolutely NO green "
    "anywhere. This is the SECONDARY logo variant: in the CENTRE of the HUD ring place a "
    "single large glowing magenta KEYHOLE emblem (with a subtle hint of a padlock). There is "
    "NO human figure and NO person in this version — just the keyhole emblem in the ring. "
    "Texts: \"LOCKED\" prominently at the top, and \"CONDITIONING CONTROL PANEL\" as the bold "
    "bottom title — spell both exactly, no other words. Hot magenta neon glow, glossy, "
    "premium, fully OPAQUE background filling the whole square frame, no transparency, no "
    "watermark."
)

PREVIEW_PROMPT = (
    "A premium MODE-SELECT HERO CARD, SQUARE 1:1, fully OPAQUE, filling the whole frame. Use "
    "the attached image as the CHARACTER LIKENESS: the SAME blonde woman with the single "
    "hot-magenta hair streak, the SAME face, the SAME glossy black-and-magenta latex domme "
    "look. Feature her prominently — a striking upper-body / three-quarter portrait — set to "
    "ONE side of the card against a DEEP BLACK background with a hot magenta-pink neon glow, a "
    "faint hypnotic spiral and subtle red glitch accents. On the OTHER side place bold "
    "branding: the single word \"LOCKED\" as a huge glossy magenta neon WORDMARK (spell it "
    "exactly L-O-C-K-E-D, all capitals, one word), with a smaller subtitle \"CONDITIONING "
    "MODE\" beneath it, and a glowing padlock / keyhole motif near the wordmark. Glossy anime "
    "illustration finish, dramatic and premium. EXACTLY ONE figure — no duplicate or second "
    "person anywhere. No other text, no watermark, no UI chrome, no border frame."
)

# (out_name, [ref paths], prompt)
ASSETS = [
    ("logo2.png", [LOCKED_LOGO], LOGO2_PROMPT),
    ("preview.png", [MASTER_SQ], PREVIEW_PROMPT),
]


def call(client, types, model, ref_bytes, prompt):
    contents = [types.Part.from_bytes(data=b, mime_type="image/png") for b in ref_bytes]
    contents.append(prompt)
    for attempt in range(4):
        resp = client.models.generate_content(model=model, contents=contents)
        data = extract_image_bytes(resp)
        if data:
            return data
        cand = (getattr(resp, "candidates", []) or [None])[0]
        print(f"  [retry {attempt}] no image (finish={getattr(cand,'finish_reason',None)})")
        time.sleep(2)
    return None


def cover_fit_rgb(raw, dims):
    img = Image.open(io.BytesIO(raw)).convert("RGB")
    w, h = img.size
    W, H = dims
    scale = max(W / w, H / h)
    nw, nh = max(1, round(w * scale)), max(1, round(h * scale))
    img = img.resize((nw, nh), Image.LANCZOS)
    left, top = (nw - W) // 2, (nh - H) // 2
    return img.crop((left, top, left + W, top + H))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--only", default=None)
    ap.add_argument("--model", default=DEFAULT_MODEL)
    ap.add_argument("--dry-run", action="store_true")
    args = ap.parse_args()

    only = {x.strip() for x in args.only.split(",")} if args.only else None
    assets = [a for a in ASSETS if not only or a[0] in only]

    OUT_DIR.mkdir(parents=True, exist_ok=True)
    key, source = load_api_key(None)
    print(f"[key] {'found' if key else 'NOT FOUND'} ({source})")

    for name, refs, prompt in assets:
        for r in refs:
            if not Path(r).is_file():
                print(f"[warn] {name}: ref missing {r}")

    if args.dry_run:
        for name, refs, prompt in assets:
            print(f"\n[{name}] refs={[Path(r).name for r in refs]}")
            print("  ", prompt[:120], "...")
        return
    if not key:
        sys.exit("No API key.")

    from google import genai
    from google.genai import types
    client = genai.Client(api_key=key)

    written, failed = [], []
    for name, refs, prompt in assets:
        print(f"\n[{name}] generating ...")
        ref_bytes = [Path(r).read_bytes() for r in refs if Path(r).is_file()]
        data = call(client, types, args.model, ref_bytes, prompt)
        if not data:
            failed.append(name)
            continue
        (OUT_DIR / f"_raw_{Path(name).stem}.png").write_bytes(data)
        out = cover_fit_rgb(data, LOGO_DIMS)
        out.save(OUT_DIR / name)
        written.append((name, out.size, out.mode))
        print(f"  -> {name}  {out.size} {out.mode}")
        time.sleep(1)

    print("\n=== SUMMARY ===")
    for name, size, mode in written:
        print(f"  {name:14} {size} {mode}")
    if failed:
        print("  FAILED:", ", ".join(failed))


if __name__ == "__main__":
    main()
