#!/usr/bin/env python3
"""
Locked-mode FEATURE assets — one Locked tile/banner per default CCP feature icon.

Reuses the gen_avatars pipeline (key loading, nano-banana call, image extraction) but
drives a flat per-asset list so each output matches its SOURCE file's EXACT pixel
dimensions and aspect (some square tiles, some 512 icons, some near-square, some wide
1376x768 banners). Every asset anchors to the locked_character master so she stays the
same woman; full background (NOT transparent, no rembg).

Aspect lever: Gemini follows the reference image aspect, so we build TWO references from
the master — a square waist-up crop (for square/near-square assets) and a wide banner
crop (character to one side, for the 16:9 banners) — and pass the matching one per asset.

Usage:
    python gen_locked_features.py                 # generate all
    python gen_locked_features.py --only flash,vibe
    python gen_locked_features.py --dry-run
"""

from __future__ import annotations

import argparse
import io
import sys
import time
from pathlib import Path

from gen_avatars import load_api_key, extract_image_bytes, DEFAULT_MODEL, SCRIPT_DIR

OUT_DIR = SCRIPT_DIR / "output" / "locked_features"
MASTER = SCRIPT_DIR / "output" / "locked_character" / "locked_character_master.png"

# Reference images built from the master (square + wide) so output aspect matches.
SQ_REF = SCRIPT_DIR / "output" / "locked_character" / "_ref_waistup_sq.png"
WIDE_REF = SCRIPT_DIR / "output" / "locked_character" / "_ref_waistup_wide.png"

# ---- shared character + style language (the wording that worked on the first 4) ----
CHAR_KEEP = (
    "Using the provided reference image of this EXACT character, keep the SAME woman — the "
    "SAME blonde, long, sleek side-swept hair with the single hot-magenta streak, the SAME "
    "mature face, the SAME glossy black-and-magenta latex domme outfit (structured corset / "
    "bodysuit with magenta trim, long latex gloves, distinct choker), and the SAME glossy "
    "anime rendering and colour palette. Do NOT turn her into a generic anime girl, do NOT "
    "restyle her hair or outfit. "
)
PREFIX_SQ = (
    CHAR_KEEP + "Frame her WAIST-UP / three-quarter crop (NOT full body, do not show her "
    "legs or feet) so she fits the tile. Change ONLY her pose / action and the scene: "
)
PREFIX_WIDE = (
    CHAR_KEEP + "Place her to ONE SIDE of a WIDE horizontal banner, waist-up / three-quarter, "
    "drawn SMALL and VERTICALLY CENTERED so her WHOLE head stays well inside the frame; the "
    "feature art and any text fill the rest of the width, also vertically centered. Change "
    "ONLY her pose / action and the scene: "
)
STYLE_SQ = (
    " Glossy anime illustration finish (NOT 3D, NOT photorealistic). Compose to fill the "
    "frame edge to edge with NO empty bars; keep her head, hands and the feature element "
    "inside the frame with a little margin. Deep near-black background with a hot "
    "magenta-pink neon glow and subtle red glitch accents. Premium, dramatic. Fully clothed, "
    "suggestive but SFW, no nudity. Keep any in-art text SHORT, bold and legible. NO app "
    "title bar, NO question-mark / help icon, no UI chrome, no watermark, no logo, no border "
    "frame around the tile."
)
STYLE_WIDE = (
    " Glossy anime illustration finish (NOT 3D, NOT photorealistic), composed as a WIDE "
    "horizontal banner: a neon-sign look on a dark circuit-board background with a hot "
    "magenta-pink neon glow and subtle red glitch accents, filling the full width. IMPORTANT: "
    "this banner is shown CROPPED to a short strip that reveals only its vertical MIDDLE — so "
    "keep her FULL head, hands, the feature icon and ALL text inside the central horizontal "
    "band (vertically centered), and leave roughly the top 22% and bottom 22% as empty dark "
    "background / glow ONLY, with NO heads, faces, icons or text in those top/bottom margins. "
    "Keep in-art text MEDIUM-sized (not oversized), bold and legible. Premium, dramatic. Fully "
    "clothed, suggestive but SFW, no nudity. NO app title bar, NO question-mark / help icon, "
    "no UI chrome, no watermark, no logo."
)

# ---- the asset list: (out_name, (W,H), ref, save_mode, delta) ----
# ref: "sq" -> square waist-up reference; "wide" -> wide banner reference.
SQ, WIDE = "sq", "wide"
ASSETS = [
    # --- square 1024 tiles ---
    ("Pink_filter.png", (1024, 1024), SQ, "RGB",
     "She sweeps one gloved hand across the scene casting a magenta colour-wash / lens, so "
     "the whole tile is washed in a dreamy hot-magenta tint with soft haze. No text."),
    ("Mind_Wipers.png", (1024, 1024), SQ, "RGB",
     "Beside a blank faceless silhouette head, a glowing magenta spiral is draining out of "
     "it as she gestures, wiping the mind empty. Short neon text reads \"EMPTY\"."),
    ("Bubble_pop.png", (1024, 1024), SQ, "RGB",
     "She floats among several glowing magenta padlock bubbles and reaches out to POP one "
     "with a gloved fingertip, a little neon burst at the contact. No text."),
    ("Bubble_count.png", (1024, 1024), SQ, "RGB",
     "She gestures invitingly at a cluster of many floating glowing magenta padlocks. Short "
     "neon text reads \"HOW MANY?\"."),
    # --- small 512 square icons ---
    ("flash.png", (512, 512), SQ, "RGB",
     "She gestures toward a glowing screen bursting with magenta hypnotic FLASH patterns — a "
     "spiral, an eye, a mandala and a zig-zag in a grid on the screen. No text."),
    ("subliminal.png", (512, 512), SQ, "RGB",
     "She stands beside a big glowing neon sign that stacks commands; the sign's in-art text "
     "reads \"OBEY\" / \"MINE\" / \"GOOD BOY\" on three stacked neon lines."),
    ("audio_whispers.png", (512, 512), SQ, "RGB",
     "She wears glossy headphones with one gloved finger to her lips whispering; magenta "
     "sound-waves and a few music notes curl around her. Short neon text reads \"LISTEN\"."),
    ("corner_gif.png", (512, 512), SQ, "RGB",
     "She sits at a glowing desktop computer with a keyboard, a small screen glowing magenta "
     "in the corner of the frame. No text."),
    ("mandatory_videos.png", (1024, 1024), SQ, "RGB",
     "She gestures toward a large glowing magenta screen / TV playing a hypnotic video, a "
     "bright neon play-button triangle glowing on it. Short neon text reads \"WATCH\"."),
    ("bouncing_text.png", (1024, 1024), SQ, "RGB",
     "Several glowing magenta neon command words bounce and float around her like drifting "
     "on-screen captions — \"OBEY\", \"SINK\", \"GOOD BOY\" — short, bold and legible."),
    ("Phrase_Lock.png", (1024, 1024), SQ, "RGB",
     "She holds up a single glowing magenta affirmation lock-card with a neon PADLOCK and a "
     "short phrase on it; the card's in-art text reads \"GOOD BOY\"."),
    ("spiral_overlay.png", (1024, 1024), SQ, "RGB",
     "A huge glowing magenta hypnotic SPIRAL fills the scene as an overlay, her waist-up "
     "figure set against / drawn into it, pulling the eye to the centre. No text."),
    # --- near-square RGBA sources (exact dims, opaque full bg) ---
    ("brain_drain.png", (771, 807), SQ, "RGBA",
     "She holds the sides of her head with both gloved hands as a glowing magenta spiral "
     "drains upward out of her head, mind emptying. Short neon text reads \"BRAIN DRAIN\"."),
    ("bambi takeover.png", (834, 835), SQ, "RGBA",
     "She stands as a smiling puppeteer holding glowing magenta marionette strings, with "
     "small puppet figures dangling below her hands. A neon caption in the LOWER THIRD (with a "
     "clear margin ABOVE the bottom edge — the text must NOT touch or run off the bottom) reads "
     "exactly \"LOCKED TAKEOVER\" — two words, spelled L-O-C-K-E-D space T-A-K-E-O-V-E-R, "
     "correctly and fully legible, not overlapping her body."),
    ("takeover.png", (882, 973), SQ, "RGBA",
     "She stands as a commanding puppeteer holding glowing magenta marionette strings, small "
     "puppet figures dangling below. A neon caption in the LOWER THIRD (with a clear margin "
     "ABOVE the bottom edge — the text must NOT touch or run off the bottom) reads exactly "
     "\"LOCKED TAKEOVER\" — two words, spelled L-O-C-K-E-D space T-A-K-E-O-V-E-R, correctly "
     "and fully legible, clear of her arms and body."),
    ("vibe.png", (910, 884), SQ, "RGBA",
     "She is a DJ leaning over glowing magenta turntables / a mixer, wearing glossy "
     "headphones, with music notes and a neon waveform around her. Short neon text \"VIBE\"."),
    # --- wide 1376x768 banners ---
    ("awareness.png", (1376, 768), WIDE, "RGB",
     "She stands on the LEFT, one gloved hand raised pointing to the right, the other relaxed "
     "at her side; large bold neon text fills the right side reading \"TRIGGER AWARENESS\" "
     "with a small neon bell icon. Correct anatomy: EXACTLY two arms and two hands, no extra "
     "or duplicated limbs."),
    ("blink_trainer.png", (1376, 768), WIDE, "RGB",
     "She sits at a glowing screen on the left; large bold neon text on the right reads "
     "\"BLINK TRAINER\" with a couple of neon EYE icons."),
    ("remote_control.png", (1376, 768), WIDE, "RGB",
     "Large bold neon text \"REMOTE CONTROL\" fills the left; on the right she stands "
     "confidently facing the viewer, holding up a glossy magenta-and-black TV remote in one "
     "gloved hand and pressing its button with the other, a knowing half-smile, like she is "
     "controlling YOU. Correct anatomy: EXACTLY two arms and two hands, no extra or "
     "duplicated limbs."),
    ("lab_aimemory_hero.png", (1376, 768), WIDE, "RGB",
     "She stands on one side with her arms relaxed, gesturing with one gloved hand toward a "
     "single glowing magenta data disc floating beside her; a neon brain icon and a couple of "
     "chat bubbles float nearby (AI-memory lab). Short neon text reads \"AI MEMORY\". "
     "CORRECT HUMAN ANATOMY IS CRITICAL: she has EXACTLY ONE head, TWO arms and TWO hands and "
     "no more — absolutely NO third/fourth arm, NO duplicated, mirrored, floating or "
     "disembodied limbs or hands. Draw a single normal pair of arms."),
    ("lab_focusgaze_hero.png", (1376, 768), WIDE, "RGB",
     "She stands on one side with her arms relaxed at her sides, her gaze locked onto a bright "
     "glowing magenta focus orb / target hovering to the other side (focus-gaze lab). Short "
     "neon text reads \"FOCUS\". CORRECT HUMAN ANATOMY IS CRITICAL: she has EXACTLY ONE head, "
     "TWO arms and TWO hands and no more — absolutely NO third/fourth arm, NO duplicated, "
     "mirrored, floating or disembodied limbs or hands. Draw a single normal pair of arms."),
    ("lab_gaze_hero.png", (1376, 768), WIDE, "RGB",
     "She sits centred, flanked by two glowing monitors — the left showing a green check, the "
     "right a red cross — with a small neon webcam above (gaze-tracking lab). No long text."),
    ("lab_quiz_hero.png", (1376, 768), WIDE, "RGB",
     "She on one side points confidently; glowing magenta award medals and a check-mark float "
     "nearby (quiz lab). Short neon text reads \"QUIZ\"."),
]


def build_refs():
    """Build a square waist-up reference and a wide (character-left) reference from the
    master, so generated assets inherit the right aspect."""
    from PIL import Image
    master = Image.open(MASTER).convert("RGBA")
    bbox = master.getchannel("A").point(lambda a: 255 if a > 10 else 0).getbbox()
    fig = master.crop(bbox)
    fw, fh = fig.size
    waist = fig.crop((0, 0, fw, int(fh * 0.52)))  # head -> hips

    # square 1024 ref: centred on black
    sq = Image.new("RGBA", (1024, 1024), (0, 0, 0, 255))
    s = int(1024 * 0.94) / waist.size[1]
    nw, nh = int(waist.size[0] * s), int(waist.size[1] * s)
    sq.alpha_composite(waist.resize((nw, nh), Image.LANCZOS),
                       ((1024 - nw) // 2, (1024 - nh) // 2))
    sq.convert("RGB").save(SQ_REF)

    # wide 1376x768 ref: character on the left, SMALL + vertically centered so her head
    # lands in the central band the app actually shows (it crops to the vertical middle).
    wide = Image.new("RGBA", (1376, 768), (0, 0, 0, 255))
    s = int(768 * 0.56) / waist.size[1]
    nw, nh = int(waist.size[0] * s), int(waist.size[1] * s)
    wide.alpha_composite(waist.resize((nw, nh), Image.LANCZOS), (64, (768 - nh) // 2))
    wide.convert("RGB").save(WIDE_REF)
    return SQ_REF, WIDE_REF


def cover_fit(raw_bytes, target, mode):
    """Cover-fit (fill + centre-crop) the generated image to exact target WxH."""
    from PIL import Image
    W, H = target
    img = Image.open(io.BytesIO(raw_bytes)).convert("RGB")
    w, h = img.size
    scale = max(W / w, H / h)
    nw, nh = max(1, round(w * scale)), max(1, round(h * scale))
    img = img.resize((nw, nh), Image.LANCZOS)
    left, top = (nw - W) // 2, (nh - H) // 2
    img = img.crop((left, top, left + W, top + H))
    return img.convert(mode)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--only", default=None, help="comma list of output filenames to do")
    ap.add_argument("--model", default=DEFAULT_MODEL)
    ap.add_argument("--dry-run", action="store_true")
    args = ap.parse_args()

    only = {x.strip() for x in args.only.split(",")} if args.only else None
    assets = [a for a in ASSETS if not only or a[0] in only]

    key, source = load_api_key(None)
    print(f"[key] {'found' if key else 'NOT FOUND'} ({source})")
    print(f"[model] {args.model}")
    OUT_DIR.mkdir(parents=True, exist_ok=True)

    if args.dry_run:
        for name, dims, ref, smode, delta in assets:
            print(f"\n[{name}] {dims} ref={ref} mode={smode}")
            print("  ", (PREFIX_WIDE if ref == WIDE else PREFIX_SQ)[:60] + "... " + delta[:90])
        print(f"\n[dry-run] {len(assets)} assets. No API calls.")
        return

    if not key:
        sys.exit("No API key found.")

    sq_ref, wide_ref = build_refs()
    print(f"[refs] built {sq_ref.name}, {wide_ref.name}")

    from google import genai
    from google.genai import types
    client = genai.Client(api_key=key)
    sq_bytes = sq_ref.read_bytes()
    wide_bytes = wide_ref.read_bytes()

    written, failed = [], []
    for name, dims, ref, smode, delta in assets:
        prefix = PREFIX_WIDE if ref == WIDE else PREFIX_SQ
        style = STYLE_WIDE if ref == WIDE else STYLE_SQ
        prompt = f"{prefix}{delta}{style}"
        ref_bytes = wide_bytes if ref == WIDE else sq_bytes
        contents = [types.Part.from_bytes(data=ref_bytes, mime_type="image/png"), prompt]
        print(f"\n[{name}] {dims} ({ref}) generating...")

        data = None
        for attempt in range(3):
            resp = client.models.generate_content(model=args.model, contents=contents)
            data = extract_image_bytes(resp)
            if data:
                break
            cand = (getattr(resp, "candidates", []) or [None])[0]
            print(f"  [retry {attempt}] no image (finish={getattr(cand,'finish_reason',None)})")
            time.sleep(2)
        if not data:
            print(f"  [error] no image for {name}")
            failed.append(name)
            continue

        (OUT_DIR / f"_raw_{Path(name).stem}.png").write_bytes(data)
        img = cover_fit(data, dims, smode)
        img.save(OUT_DIR / name)
        written.append((name, dims))
        print(f"  -> {name}  {img.size} {img.mode}")
        time.sleep(1)

    print("\n=== SUMMARY ===")
    for name, dims in written:
        print(f"  {name:24} {dims}")
    if failed:
        print("  FAILED:", ", ".join(failed))


if __name__ == "__main__":
    main()
