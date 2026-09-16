#!/usr/bin/env python3
"""
Locked-mode SKILL ICONS (Bucket 1) — re-skin the 22 default skill-tree icons.

Same approach as the achievement SYMBOL set: a single centered neon GLYPH (NO figure)
that conveys the skill's FUNCTION, restyled to the Locked palette (glossy hot
magenta-pink + deep black, red glitch accents, soft neon bloom) with lock / key / chain /
spiral motifs woven in where natural. Text-free (tiers shown as pips, never spelled out)
to dodge the image-model misspelling problem.

The default icons (Resources/skills/*.png) are RGBA with transparent backgrounds and
VARYING per-file dimensions. We match each source's exact (W,H) and keep transparency:
generate the glyph on pure black, luminance-key to alpha (black -> transparent, glyph +
glow -> opaque, like the bubble/drone glow icons), crop to the glyph, then CONTAIN-fit
centred onto a transparent canvas of the source's exact size (no distortion, no clipping;
the transparent padding is invisible in-app).

Output: output/locked_skills/<same-filename>.png   (RGBA, exact source dims)

Usage:
    python gen_locked_skills.py [--only pink_rush,hive_mind] [--dry-run]
"""

from __future__ import annotations

import argparse
import io
import sys
import time
from pathlib import Path

import numpy as np
from PIL import Image

from gen_avatars import (
    load_api_key, extract_image_bytes, crop_to_alpha, DEFAULT_MODEL, SCRIPT_DIR,
)

SRC_DIR = Path(
    r"C:\Projects\Conditioning-Control-Panel---CSharp-WPF\ConditioningControlPanel\Resources\skills"
)
OUT_DIR = SCRIPT_DIR / "output" / "locked_skills"

STYLE = (
    "A single centered glowing neon ICON / EMBLEM, glossy HOT MAGENTA-PINK with deep BLACK and "
    "subtle RED glitch accents, soft magenta neon bloom and a thin bright rim-light. Flat "
    "vector emblem / game-skill-icon style (NOT 3D, NOT photorealistic, NOT a 3D render). The "
    "icon is clean, bold and instantly readable, filling most of the frame with a little "
    "margin. On a PURE SOLID BLACK background, nothing else. NO text, NO numbers, NO letters, "
    "NO words anywhere unless explicitly required. NO border, NO frame, NO UI, NO watermark, "
    "no human figure or face."
)

# (filename, glyph concept). Function preserved from the default skill; art -> Locked.
SKILLS = {
    "better_quests.png":
        "an upgraded TASK SCROLL / checklist with a glowing magenta up-arrow badge, signifying "
        "improved quests; a tiny keyhole worked into the scroll seal.",
    "ditzy_data.png":
        "a glowing brain-chip / data crystal radiating magenta sparkles and little data motes, "
        "signifying knowledge & data; faint spiral lines in the glow.",
    "early_bird_bimbo.png":
        "a magenta neon SUNRISE cresting a horizon line with a small key as the sun's glint, "
        "signifying a morning / early bonus.",
    "eternal_doll.png":
        "a glowing magenta INFINITY symbol entwined with a fine chain and a tiny padlock at its "
        "crossing, signifying a permanent / eternal prestige reward.",
    "good_girl_streak.png":
        "a rising magenta FLAME shaped like a teardrop with a small heart and keyhole in its "
        "core, signifying an ongoing good-behaviour streak.",
    "hive_mind.png":
        "a glowing network of linked NODES (connected dots) forming a neat web, magenta lines, "
        "signifying a shared hive mind / collective.",
    "lucky_bimbo.png":
        "a glowing magenta four-leaf CLOVER with a tiny keyhole at its centre and a sparkle, "
        "signifying luck.",
    "lucky_bubbles.png":
        "a glossy magenta translucent BUBBLE with a small four-leaf clover or sparkle floating "
        "inside it, signifying lucky bubbles.",
    "milestone_rewards.png":
        "a glowing magenta TROPHY with a small flag/banner and a keyhole on its cup, signifying "
        "milestone rewards.",
    "night_shift.png":
        "a magenta crescent MOON with small stars and a tiny padlock hanging from the crescent, "
        "signifying a night / late-hours bonus.",
    "oopsie_insurance.png":
        "a glowing magenta SHIELD with a keyhole at its centre, signifying protection / "
        "insurance against a slip-up.",
    "perfect_bimbo_week.png":
        "a glowing magenta CALENDAR grid with seven cells, each marked with a tiny check / "
        "sparkle and a star above, signifying a perfect week.",
    "pink_hours.png":
        "a glowing magenta CLOCK / stopwatch face with a small key as the hour hand, signifying "
        "bonus 'happy hours'.",
    "pink_rush.png":
        "a bold glowing magenta LIGHTNING BOLT with motion streaks, signifying a speed / rush "
        "boost.",
    "popular_girl.png":
        "a glowing magenta HEART haloed by small sparkles and tiny orbiting hearts, signifying "
        "popularity / social charm.",
    "quest_refresh.png":
        "two glowing magenta REFRESH arrows forming a circular loop around a small task scroll, "
        "signifying refreshing the quests.",
    "reroll_addict.png":
        "a glowing magenta pair of DICE mid-tumble with sparkle trails, signifying re-rolling, "
        "a tiny keyhole pip on one die.",
    "sparkle_boost_1.png":
        "a glowing magenta upward CHEVRON / up-arrow with ONE bright star, signifying a tier-1 "
        "XP boost.",
    "sparkle_boost_2.png":
        "a glowing magenta upward double-CHEVRON / up-arrow with TWO bright stars, signifying a "
        "tier-2 XP boost.",
    "sparkle_boost_3.png":
        "a glowing magenta upward triple-CHEVRON / up-arrow with THREE bright stars, signifying "
        "a tier-3 XP boost.",
    "streak_power.png":
        "a powerful glowing magenta FLAME with a small lightning bolt or '×' multiplier mark in "
        "its core, signifying a streak power multiplier.",
    "trophy_case.png":
        "a glowing magenta display CABINET / case holding a small trophy and a padlock, "
        "signifying a locked trophy collection.",
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


def luma_key(raw):
    """Neon-on-black -> RGBA with alpha = brightness (keeps the glow), cropped to glyph."""
    img = Image.open(io.BytesIO(raw)).convert("RGB")
    arr = np.asarray(img, dtype=np.uint8)
    alpha = arr.max(axis=2).astype(np.uint8)          # brightest channel = neon presence
    out = img.convert("RGBA")
    out.putalpha(Image.fromarray(alpha, mode="L"))
    return crop_to_alpha(out, threshold=12)


def fit_contain(img, dims):
    """Centre the glyph on a transparent canvas of exactly dims (no distortion/clip)."""
    W, H = dims
    w, h = img.size
    scale = min(W / w, H / h)
    nw, nh = max(1, round(w * scale)), max(1, round(h * scale))
    g = img.resize((nw, nh), Image.LANCZOS)
    canvas = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    canvas.paste(g, ((W - nw) // 2, (H - nh) // 2), g)
    return canvas


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--only", default=None)
    ap.add_argument("--model", default=DEFAULT_MODEL)
    ap.add_argument("--dry-run", action="store_true")
    args = ap.parse_args()

    only = {x.strip() for x in args.only.split(",")} if args.only else None
    items = []
    for name, concept in SKILLS.items():
        if only and name not in only and Path(name).stem not in only:
            continue
        src = SRC_DIR / name
        if not src.is_file():
            print(f"[warn] source missing, skipping: {name}")
            continue
        items.append((name, Image.open(src).size, concept))

    OUT_DIR.mkdir(parents=True, exist_ok=True)
    key, source = load_api_key(None)
    print(f"[key] {'found' if key else 'NOT FOUND'} ({source})  | {len(items)} icons")

    if args.dry_run:
        for name, dims, concept in items:
            print(f"  {name:24} {dims}  :: {concept[:70]}")
        return
    if not key:
        sys.exit("No API key.")

    from google import genai
    from google.genai import types
    client = genai.Client(api_key=key)

    written, failed = [], []
    for name, dims, concept in items:
        print(f"\n[{name}] {dims} ...")
        data = call(client, types, args.model, f"{STYLE} The icon depicts: {concept}")
        if not data:
            print(f"  [error] no image for {name}")
            failed.append(name)
            continue
        (OUT_DIR / f"_raw_{Path(name).stem}.png").write_bytes(data)
        glyph = luma_key(data)
        out = fit_contain(glyph, dims)
        out.save(OUT_DIR / name)
        written.append((name, out.size))
        print(f"  -> {name}  {out.size} {out.mode}")
        time.sleep(1)

    print("\n=== SUMMARY ===")
    for name, size in written:
        print(f"  {name:24} {size}")
    if failed:
        print("  FAILED:", ", ".join(failed))


if __name__ == "__main__":
    main()
