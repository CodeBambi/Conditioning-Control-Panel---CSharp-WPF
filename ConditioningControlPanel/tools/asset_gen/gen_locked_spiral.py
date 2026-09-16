#!/usr/bin/env python3
"""
Locked-mode spiral.gif (Bucket 3) — recolour the default hypnotic spiral, frame-by-frame.

nano banana can't make GIFs, so this is a pure-Pillow recolour of the existing
Resources/spiral.gif. We map each frame's LUMINANCE through a black -> hot-magenta ->
white-pink gradient (with a dark-red toe for the 'glitch' accent), which keeps the
exact spiral geometry, rotation and motion while restyling it to the Locked palette.

Output matches the source EXACTLY: same WxH, same frame count, same per-frame duration
and the same loop flag.

Usage:
    python gen_locked_spiral.py [--src PATH] [--out PATH]
"""

from __future__ import annotations

import argparse
from pathlib import Path

import numpy as np
from PIL import Image, ImageSequence

from gen_avatars import SCRIPT_DIR

DEFAULT_SRC = Path(
    r"C:\Projects\Conditioning-Control-Panel---CSharp-WPF\ConditioningControlPanel\Resources\spiral.gif"
)
DEFAULT_OUT = SCRIPT_DIR / "output" / "locked_modassets" / "spiral.gif"

# Locked gradient stops: (position 0..1, (R,G,B)).
STOPS = [
    (0.00, (0, 0, 0)),         # black
    (0.22, (70, 0, 28)),       # dark red toe (glitch accent)
    (0.45, (200, 12, 110)),    # deep magenta
    (0.62, (255, 20, 147)),    # hot magenta (deeppink) — the lead colour
    (0.82, (255, 95, 200)),    # bright pink
    (1.00, (255, 225, 245)),   # near-white pink highlight
]


def build_lut() -> np.ndarray:
    """256x3 uint8 lookup table from the gradient stops."""
    xs = np.array([s[0] for s in STOPS]) * 255.0
    cols = np.array([s[1] for s in STOPS], dtype=float)
    grid = np.arange(256, dtype=float)
    lut = np.stack([np.interp(grid, xs, cols[:, c]) for c in range(3)], axis=1)
    return np.clip(lut, 0, 255).astype(np.uint8)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--src", default=str(DEFAULT_SRC))
    ap.add_argument("--out", default=str(DEFAULT_OUT))
    args = ap.parse_args()

    src = Path(args.src)
    out = Path(args.out)
    out.parent.mkdir(parents=True, exist_ok=True)

    gif = Image.open(src)
    size = gif.size
    loop = gif.info.get("loop", 0)
    lut = build_lut()

    frames, durations = [], []
    for frame in ImageSequence.Iterator(gif):
        durations.append(frame.info.get("duration", gif.info.get("duration", 30)))
        lum = np.asarray(frame.convert("L"))           # luminance preserves the spiral motion
        rgb = lut[lum]                                  # (H,W,3) recoloured
        frames.append(Image.fromarray(rgb, mode="RGB"))

    print(f"[src] {src.name}  {size}  frames={len(frames)}  loop={loop}  dur={durations[0]}")
    frames[0].save(
        out,
        save_all=True,
        append_images=frames[1:],
        duration=durations,
        loop=loop if loop is not None else 0,
        disposal=2,
        optimize=False,
    )
    chk = Image.open(out)
    n = sum(1 for _ in ImageSequence.Iterator(chk))
    print(f"[out] {out}  {chk.size}  frames={n}  loop={chk.info.get('loop')}")


if __name__ == "__main__":
    main()
