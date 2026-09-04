#!/usr/bin/env python3
"""
Locked-mode AVATAR POSES (Bucket 2) — generate poses 2-4 for all 5 level-up stages.

Each stage already has POSE 1 (the master / avatarN_pose1). Poses 2-4 are generated
image->image, anchored to THAT STAGE'S OWN pose1 so the same woman / outfit / hair /
colour is preserved and only the pose + expression change. This is the same charref
pipeline gen_avatars uses, but with a per-stage anchor instead of one shared ref.

Stages & anchors (in output/locked_avatars/):
    stage 1  -> locked_character_master.png   (filenames avatar_pose{2,3,4}.png)
    stage 2  -> avatar2_pose1.png             (avatar2_pose{2,3,4}.png)
    stage 3  -> avatar3_pose1.png             (avatar3_pose{2,3,4}.png)
    stage 4  -> avatar4_pose1.png             (avatar4_pose{2,3,4}.png)
    stage 5  -> avatar5_pose1.png             (avatar5_pose{2,3,4}.png)

Also copies the master to avatar_pose1.png (set 1's pose 1) so the base set is complete.

Format (matches the pose-1 sprites): RGBA, transparent (rembg cutout), 960 px tall,
tight crop, both hands visible, feet in frame.

Usage:
    python gen_locked_poses.py [--only avatar2,avatar3] [--poses 2,3,4] [--dry-run] [--no-rembg]
"""

from __future__ import annotations

import argparse
import shutil
import sys
import time
from pathlib import Path

from gen_avatars import (
    load_api_key, extract_image_bytes, post_process, DEFAULT_MODEL, SCRIPT_DIR,
)

OUT_DIR = SCRIPT_DIR / "output" / "locked_avatars"
MASTER = SCRIPT_DIR / "output" / "locked_character" / "locked_character_master.png"
TARGET_HEIGHT = 960

# Identity-lock prefix: keep EVERYTHING from this stage's own pose-1 reference, change
# only pose + expression. (We do NOT want outfit/hair/colour drift between poses of the
# same stage, so this is stricter than the cross-stage "v2 distinct" prefix.)
CHARREF_PREFIX = (
    "Using the provided reference image of this EXACT character, keep the SAME woman — the "
    "SAME face and expression-identity, the SAME hairstyle and hair colour (blonde with the "
    "single hot-magenta streak), the SAME outfit design and the SAME dominant colour, the "
    "SAME hardware/props, and the SAME glossy anime rendering and palette as the reference. "
    "Do NOT redesign her outfit, hair or colour — this is the same stage, only a different "
    "frame. She is ALWAYS the DOMINANT one — sharp-eyed, composed, in total control, NEVER "
    "dizzy, drained or entranced herself. Show her as a COMPLETE FULL-BODY standing figure "
    "seen HEAD TO FEET, her legs and footwear fully in frame, filling the frame top to "
    "bottom. BOTH of her hands must be FULLY VISIBLE and well-formed with correct fingers. "
    "Change ONLY her pose and expression as described: "
)

STYLE = (
    "Single isolated character, centered, the ENTIRE figure visible from the top of her head "
    "down to her FEET — do NOT crop at the hips, thighs or feet — filling the frame "
    "vertically, on a PLAIN FLAT UNIFORM light-grey background (NO gradient, NO vignette, NO "
    "shadow, NO floor or scenery) for an easy clean cutout. Keep any glow, aura, spiral, "
    "chains and props CLOSE around her body so her silhouette cuts out neatly. Glossy anime "
    "illustration finish (NOT 3D, NOT photorealistic), vivid neon glow with subtle red glitch "
    "accents. Two arms, two legs, BOTH hands fully visible with correct fingers, no extra or "
    "duplicated limbs, no distortion. Fully clothed, suggestive but SFW, no nudity. NO text, "
    "NO logo, NO watermark, NO UI, no other characters."
)

# Pose deltas 2-4 (shared across all stages).
POSE_DELTAS = {
    2: ("APPROVING",
        "POSE 2, APPROVING — a warm, satisfied, genuinely pleased smile, pleased-but-superior, "
        "as if praising the viewer with a soft \"good boy\". Relaxed approving posture, maybe a "
        "gloved hand raised in a light approving gesture or resting at her chest. Warm and "
        "rewarding while still dominant."),
    3: ("COMMANDING",
        "POSE 3, COMMANDING — stern and denying, chin raised, looking DOWN at the viewer with a "
        "cool \"not yet\" authority. One hand may be raised palm-out or a single finger up in a "
        "firm \"stop / wait\" gesture. Strict, composed, fully in command."),
    4: ("INTENSE",
        "POSE 4, INTENSE — leaning toward the viewer, a hypnotic half-lidded gaze drawing you "
        "in, lips slightly parted in an entrancing look. Close, magnetic, predatory-calm. Her "
        "aura/glow intensifies close around her. Both hands still visible."),
}

# (key, anchor_path, out_pattern)
STAGES = [
    ("avatar",  MASTER,                       "avatar_pose{n}.png"),
    ("avatar2", OUT_DIR / "avatar2_pose1.png", "avatar2_pose{n}.png"),
    ("avatar3", OUT_DIR / "avatar3_pose1.png", "avatar3_pose{n}.png"),
    ("avatar4", OUT_DIR / "avatar4_pose1.png", "avatar4_pose{n}.png"),
    ("avatar5", OUT_DIR / "avatar5_pose1.png", "avatar5_pose{n}.png"),
]


def call(client, types, model, anchor_bytes, prompt):
    contents = [
        types.Part.from_bytes(data=anchor_bytes, mime_type="image/png"),
        prompt,
    ]
    for attempt in range(4):
        resp = client.models.generate_content(model=model, contents=contents)
        data = extract_image_bytes(resp)
        if data:
            return data
        cand = (getattr(resp, "candidates", []) or [None])[0]
        print(f"    [retry {attempt}] no image (finish={getattr(cand,'finish_reason',None)})")
        time.sleep(2)
    return None


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--only", default=None, help="comma list of stage keys, e.g. avatar2,avatar5")
    ap.add_argument("--poses", default="2,3,4")
    ap.add_argument("--model", default=DEFAULT_MODEL)
    ap.add_argument("--no-rembg", action="store_true")
    ap.add_argument("--dry-run", action="store_true")
    args = ap.parse_args()

    only = {x.strip() for x in args.only.split(",")} if args.only else None
    poses = [int(x) for x in args.poses.split(",") if x.strip()]
    stages = [s for s in STAGES if not only or s[0] in only]

    OUT_DIR.mkdir(parents=True, exist_ok=True)

    # Ensure set-1 pose-1 exists (copy of the master) so the base set is complete.
    base_pose1 = OUT_DIR / "avatar_pose1.png"
    if not base_pose1.exists() and MASTER.exists():
        if args.dry_run:
            print(f"[would copy] master -> {base_pose1.name}")
        else:
            shutil.copyfile(MASTER, base_pose1)
            print(f"[copied] master -> {base_pose1.name}")

    key, source = load_api_key(None)
    print(f"[key] {'found' if key else 'NOT FOUND'} ({source})")

    if args.dry_run:
        for skey, anchor, pat in stages:
            print(f"\n=== {skey}  anchor={Path(anchor).name}  exists={Path(anchor).is_file()} ===")
            for n in poses:
                lbl, delta = POSE_DELTAS[n]
                print(f"  pose{n} {lbl}: {pat.format(n=n)}")
        return
    if not key:
        sys.exit("No API key.")

    from google import genai
    from google.genai import types
    client = genai.Client(api_key=key)

    written, failed = [], []
    for skey, anchor, pat in stages:
        anchor = Path(anchor)
        if not anchor.is_file():
            print(f"\n[skip] {skey}: anchor missing {anchor}")
            failed.append(f"{skey}(anchor)")
            continue
        anchor_bytes = anchor.read_bytes()
        print(f"\n=== {skey}  (anchor {anchor.name}) ===")
        for n in poses:
            lbl, delta = POSE_DELTAS[n]
            out_name = pat.format(n=n)
            print(f"  [{out_name}] {lbl} ...")
            data = call(client, types, args.model, anchor_bytes, f"{CHARREF_PREFIX}{delta} {STYLE}")
            if not data:
                print(f"    [error] no image for {out_name}")
                failed.append(out_name)
                continue
            (OUT_DIR / f"_raw_{Path(out_name).stem}.png").write_bytes(data)
            img = post_process(data, use_rembg=not args.no_rembg, target_height=TARGET_HEIGHT)
            img.save(OUT_DIR / out_name)
            written.append((out_name, img.size))
            print(f"    -> {out_name}  {img.size} {img.mode}")
            time.sleep(1)

    print("\n=== SUMMARY ===")
    for name, size in written:
        print(f"  {name:20} {size}")
    if failed:
        print("  FAILED:", ", ".join(failed))


if __name__ == "__main__":
    main()
