#!/usr/bin/env python3
"""
Locked-mode SPEECH BUBBLES (Bucket 1) — re-skin speechbubble1/2.png.

The app composites the avatar's spoken text ON TOP of these bubble shapes, and the
exact silhouette + tail position are what the text layout is tuned against. So rather
than regenerate the shape with an image model (which can't reproduce the precise alpha
tail), we RECOLOUR the existing bubbles programmatically: keep the EXACT alpha channel
(shape + tail + dims), and remap the RGB through a Locked luminance ramp.

The ramp keeps the bubble INTERIOR light (so dark spoken text stays readable) and turns
the rim / outline into hot magenta with a near-black red-magenta edge — the Locked look.

Source: Resources/speechbubble{1,2}.png (2048x2048 RGBA).
Output: output/locked_modassets/speechbubble{1,2}.png (identical dims + alpha).

Usage:
    python gen_locked_bubbles.py
"""

from __future__ import annotations

import argparse
from pathlib import Path

import numpy as np
from PIL import Image

from gen_avatars import SCRIPT_DIR

RES = Path(r"C:\Projects\Conditioning-Control-Panel---CSharp-WPF\ConditioningControlPanel\Resources")
OUT_DIR = SCRIPT_DIR / "output" / "locked_modassets"

# Luminance ramp: interior stays bright (text-readable), rim -> hot magenta, edge -> dark.
STOPS = [
    (0.00, (40, 0, 25)),       # darkest outline -> near-black red-magenta
    (0.22, (150, 10, 90)),     # deep magenta shadow
    (0.40, (235, 30, 150)),    # hot magenta outline
    (0.58, (255, 120, 205)),   # magenta inner rim
    (0.78, (255, 205, 235)),   # light pink interior
    (1.00, (255, 242, 250)),   # near-white-pink highlight
]


def build_lut() -> np.ndarray:
    xs = np.array([s[0] for s in STOPS]) * 255.0
    cols = np.array([s[1] for s in STOPS], dtype=float)
    grid = np.arange(256, dtype=float)
    lut = np.stack([np.interp(grid, xs, cols[:, c]) for c in range(3)], axis=1)
    return np.clip(lut, 0, 255).astype(np.uint8)


def recolor(src: Path, out: Path, lut):
    img = Image.open(src).convert("RGBA")
    arr = np.asarray(img)
    rgb, a = arr[:, :, :3], arr[:, :, 3]
    lum = (0.299 * rgb[:, :, 0] + 0.587 * rgb[:, :, 1] + 0.114 * rgb[:, :, 2]).astype(np.uint8)
    new_rgb = lut[lum]                                  # recolour by brightness
    out_arr = np.dstack([new_rgb, a]).astype(np.uint8)  # keep EXACT original alpha
    Image.fromarray(out_arr, mode="RGBA").save(out)
    print(f"  -> {out.name}  {img.size}  (alpha preserved)")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--only", default=None)
    args = ap.parse_args()
    OUT_DIR.mkdir(parents=True, exist_ok=True)
    lut = build_lut()
    names = ["speechbubble1.png", "speechbubble2.png"]
    if args.only:
        want = {x.strip() for x in args.only.split(",")}
        names = [n for n in names if n in want or Path(n).stem in want]
    for name in names:
        src = RES / name
        if not src.is_file():
            print(f"[warn] missing {src}")
            continue
        print(f"[{name}]")
        recolor(src, OUT_DIR / name, lut)


if __name__ == "__main__":
    main()
