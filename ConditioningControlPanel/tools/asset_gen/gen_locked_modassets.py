#!/usr/bin/env python3
"""
Locked-mode MOD per-mode assets — bubble, the two tubes (attached/detached), and the
control-panel logo/hub — themed Locked and matched to the Drone mod's asset formats
(exact dimensions, transparency).

Reuses the gen_avatars pipeline (key loading, nano-banana call, image extraction).

Methods chosen per asset:
  * bubble  -> text->image on solid black, then LUMINANCE-KEY to alpha (NOT rembg) so the
              orb stays SEE-THROUGH like the original Resources/bubble.png (translucent).
  * tubes   -> image->image EDIT of the Drone tube (keep exact geometry / cavity position
              & cables so the app's avatar compositing offsets still line up; recolor
              green->magenta, circuit->lock/chain). rembg to a clean transparent cutout.
  * logo    -> the Drone logo as a LAYOUT ref + the locked_character master as the centre
              figure; recolored magenta, "LOCKED" + "CONDITIONING CONTROL PANEL". Opaque.

Usage:
    python gen_locked_modassets.py [--only bubble.png,tube.png,...] [--dry-run]
"""

from __future__ import annotations

import argparse
import io
import sys
import time
from pathlib import Path

from gen_avatars import load_api_key, extract_image_bytes, DEFAULT_MODEL, SCRIPT_DIR

RES = Path(r"C:\Projects\Conditioning-Control-Panel---CSharp-WPF\ConditioningControlPanel\Resources")
DRONE = RES / "Modassets" / "drone"
MASTER = SCRIPT_DIR / "output" / "locked_character" / "locked_character_master.png"
# Square waist-up crop of the master (built by gen_locked_features) — used for the logo so
# its ~square aspect doesn't drag the HUD portrait, and she integrates into the ring.
MASTER_SQ = SCRIPT_DIR / "output" / "locked_character" / "_ref_waistup_sq.png"
OUT_DIR = SCRIPT_DIR / "output" / "locked_modassets"

# Exact source formats (verified): bubble original 1024 RGBA translucent (alpha max 249);
# drone tube/tube2 2048x2048 RGBA; drone logo 2076x2048 RGB opaque.
BUBBLE_DIMS = (1024, 1024)
TUBE_DIMS = (2048, 2048)
LOGO_DIMS = (2076, 2048)
BUBBLE_ALPHA_MAX = 249

def _bubble_prompt(flavor):
    return (
        "A single isolated glossy translucent SEE-THROUGH soap-bubble orb, perfectly centred, "
        "floating, with a glowing hot-magenta PADLOCK suspended inside it. The padlock is "
        "fairly LARGE — a clear focal point — and GLOWS strongly with a bright magenta neon "
        "halo and soft light bloom radiating around it. The bubble is clear glass — you can "
        "see straight through its centre — with bright thin rim highlights, a soft iridescent "
        "magenta sheen and a gentle magenta glow. " + flavor + " Premium, elegant, delicate. "
        "Pure solid BLACK background, nothing else: NO hand, NO scene, NO text, NO other "
        "objects. The orb fills most of the square frame."
    )

BUBBLE_PROMPT = _bubble_prompt("")
# Two extra variants — same concept, distinct finish, bigger/glowier lock.
BUBBLE_PROMPT_V2 = _bubble_prompt(
    "Variant: a richer iridescent rainbow-magenta sheen swirling across the glass with a few "
    "tiny sparkles / bokeh glints on the surface."
)
BUBBLE_PROMPT_V3 = _bubble_prompt(
    "Variant: a cleaner crystalline glass orb with a stronger bright rim light and a denser, "
    "wider magenta glow halo around the whole bubble."
)

# Alternates of the liked bubble2 (iridescent rainbow-magenta sheen + sparkles), changing
# ONLY the reflection pattern so each reads slightly different.
_V2_BASE = ("Variant: a richer iridescent rainbow-magenta sheen swirling across the glass "
            "with a few tiny sparkles / bokeh glints on the surface. ")
BUBBLE_V2_ALTS = {
    "bubble2_alt1.png": _bubble_prompt(
        _V2_BASE + "Reflections: one long soft diagonal highlight streak across the "
        "upper-left, with a small bright catch-light beside it."),
    "bubble2_alt2.png": _bubble_prompt(
        _V2_BASE + "Reflections: several small scattered bright glints spread around the "
        "rim, no single dominant streak."),
    "bubble2_alt3.png": _bubble_prompt(
        _V2_BASE + "Reflections: one broad soft window-style reflection on the upper third "
        "with subtle rainbow-fringed edges."),
}

TUBE_DETACHED_EDIT = (
    "Edit this image. Keep the EXACT same tube shape, size, position and orientation within "
    "the frame, and the SAME cable layout coming off the top and the cable bundle at the "
    "bottom. Change ONLY the styling: recolour everything from green to hot MAGENTA-PINK on "
    "deep black — the glass tint, the smoke inside, the glow at the base and the cables all "
    "become magenta. Replace the green circuit-board patterns on the metal collars with "
    "magenta-and-black lock-and-chain / glitch accents. Keep the glossy translucent glass "
    "look. The vessel is EMPTY inside (just magenta smoke, no figure or person). Fully "
    "transparent background. Premium and elegant."
)
TUBE_ATTACHED_EDIT = (
    "Edit this image. Keep the EXACT same tube shape, size, position and orientation within "
    "the frame, and keep the side CONNECTOR TAB in the same place. This is the attached "
    "version: NO cables. Change ONLY the styling: recolour everything from green to hot "
    "MAGENTA-PINK on deep black — the glass tint, the smoke inside and the glow at the base "
    "all become magenta. Replace the green circuit-board patterns on the metal collars with "
    "magenta-and-black lock-and-chain / glitch accents. Keep the glossy translucent glass "
    "look. The vessel is EMPTY inside (just magenta smoke, no figure or person). Fully "
    "transparent background. Premium and elegant."
)
LOGO_PROMPT = (
    "Use the FIRST image as the exact LAYOUT / COMPOSITION reference and KEEP its SQUARE "
    "aspect ratio: the same circular HUD ring, the same round buttons at the top, the same "
    "left/right status read-outs, the same heart-rate trace lines, the same bottom title "
    "block and the same dense code-matrix background, same overall square composition that "
    "fills the whole square frame top to bottom. Use the SECOND image only as the CHARACTER "
    "likeness. Create a conditioning control-panel HUD recoloured to hot MAGENTA-PINK and "
    "deep BLACK — replace ALL the green with magenta. "
    "There must be EXACTLY ONE figure: the SECOND-image character (same blonde side-swept "
    "hair with a magenta streak, same face, same black-and-magenta latex outfit) rendered as "
    "a glowing magenta figure standing INSIDE the centre of the HUD ring. Do NOT add any "
    "second character, portrait, bust or duplicate figure anywhere. "
    "Replace ALL text with Locked wording — there must be NO 'DRONE', '0xDR0N3' or "
    "'PORTS-SECURE' text anywhere. The only texts are: \"LOCKED\" prominently at the top, "
    "\"CONDITIONING CONTROL PANEL\" as the big bold bottom title, \"Mind State: [LOCKED]\" "
    "and \"Connection: [SECURE]\" as the small side read-outs. "
    "Hot magenta neon glow, glossy, premium. Fully OPAQUE background filling the whole "
    "square frame, no transparency. "
    "IMPORTANT: absolutely NO green anywhere — the OUTERMOST border ring and every glow must "
    "be MAGENTA, not green. Replace every small code label (e.g. anything like "
    "'0xDR0N3_INIT') with \"0xL0CK3D_INIT\", and the connection status must read exactly "
    "\"Connection: [SECURE]\" (not PORTS-SECURE)."
)

# (out_name, kind, dims, save_mode, [ref files], prompt)
ASSETS = [
    ("bubble.png", "bubble", BUBBLE_DIMS, "RGBA", [], BUBBLE_PROMPT),
    ("bubble2.png", "bubble", BUBBLE_DIMS, "RGBA", [], BUBBLE_PROMPT_V2),
    ("bubble3.png", "bubble", BUBBLE_DIMS, "RGBA", [], BUBBLE_PROMPT_V3),
    ("bubble2_alt1.png", "bubble", BUBBLE_DIMS, "RGBA", [], BUBBLE_V2_ALTS["bubble2_alt1.png"]),
    ("bubble2_alt2.png", "bubble", BUBBLE_DIMS, "RGBA", [], BUBBLE_V2_ALTS["bubble2_alt2.png"]),
    ("bubble2_alt3.png", "bubble", BUBBLE_DIMS, "RGBA", [], BUBBLE_V2_ALTS["bubble2_alt3.png"]),
    ("tube.png", "tube", TUBE_DIMS, "RGBA", [DRONE / "tube.png"], TUBE_DETACHED_EDIT),
    ("tube2.png", "tube", TUBE_DIMS, "RGBA", [DRONE / "tube2.png"], TUBE_ATTACHED_EDIT),
    ("logo.png", "logo", LOGO_DIMS, "RGB", [DRONE / "logo.png", MASTER_SQ], LOGO_PROMPT),
]


def call(client, types, model, ref_bytes, prompt):
    contents = [types.Part.from_bytes(data=b, mime_type="image/png") for b in ref_bytes]
    contents.append(prompt)
    for attempt in range(3):
        resp = client.models.generate_content(model=model, contents=contents)
        data = extract_image_bytes(resp)
        if data:
            return data
        cand = (getattr(resp, "candidates", []) or [None])[0]
        print(f"  [retry {attempt}] no image (finish={getattr(cand,'finish_reason',None)})")
        time.sleep(2)
    return None


def post_bubble(raw, dims):
    """Luminance-key on black: alpha = per-pixel brightness (capped to match the original's
    translucent max alpha), so the orb stays see-through. Then resize to exact dims."""
    from PIL import Image
    img = Image.open(io.BytesIO(raw)).convert("RGB")
    # square-crop centre first so the orb stays centred at the target square
    w, h = img.size
    s = min(w, h)
    img = img.crop(((w - s) // 2, (h - s) // 2, (w - s) // 2 + s, (h - s) // 2 + s))
    rgb = img.resize(dims, Image.LANCZOS)
    # alpha from per-pixel max channel (keeps coloured glow), capped to the original's
    # translucent max so the orb reads see-through rather than a hard opaque disc.
    import numpy as np
    arr = np.asarray(rgb, dtype=np.uint8)
    alpha = arr.max(axis=2)
    alpha = np.minimum(alpha, BUBBLE_ALPHA_MAX).astype(np.uint8)
    out = rgb.convert("RGBA")
    out.putalpha(Image.fromarray(alpha, mode="L"))
    return out


def post_tube(raw, dims):
    """rembg cutout to transparent, resize to exact dims."""
    from PIL import Image
    from rembg import remove
    img = Image.open(io.BytesIO(raw)).convert("RGBA")
    img = remove(img)
    return img.resize(dims, Image.LANCZOS)


def post_logo(raw, dims):
    """Cover-fit to exact dims, opaque RGB."""
    from PIL import Image
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

    key, source = load_api_key(None)
    print(f"[key] {'found' if key else 'NOT FOUND'} ({source})")
    OUT_DIR.mkdir(parents=True, exist_ok=True)

    if args.dry_run:
        for name, kind, dims, mode, refs, prompt in assets:
            print(f"\n[{name}] kind={kind} dims={dims} mode={mode} refs={[Path(r).name for r in refs]}")
            print("  ", prompt[:120], "...")
        print(f"\n[dry-run] {len(assets)} assets.")
        return
    if not key:
        sys.exit("No API key found.")

    from google import genai
    from google.genai import types
    client = genai.Client(api_key=key)

    written, failed = [], []
    for name, kind, dims, mode, refs, prompt in assets:
        print(f"\n[{name}] ({kind}) generating...")
        ref_bytes = [Path(r).read_bytes() for r in refs]
        data = call(client, types, args.model, ref_bytes, prompt)
        if not data:
            print(f"  [error] no image for {name}")
            failed.append(name)
            continue
        (OUT_DIR / f"_raw_{Path(name).stem}.png").write_bytes(data)
        if kind == "bubble":
            img = post_bubble(data, dims)
        elif kind == "tube":
            img = post_tube(data, dims)
        else:
            img = post_logo(data, dims)
        img.save(OUT_DIR / name)
        written.append((name, img.size, img.mode))
        print(f"  -> {name}  {img.size} {img.mode}")
        time.sleep(1)

    print("\n=== SUMMARY ===")
    for name, size, mode in written:
        print(f"  {name:16} {size} {mode}")
    if failed:
        print("  FAILED:", ", ".join(failed))


if __name__ == "__main__":
    main()
