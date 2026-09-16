#!/usr/bin/env python3
"""
CCP asset generator — avatar sets (and reusable for future batches).

Generates a consistent 4-pose character avatar set with Gemini 2.5 Flash Image
("nano banana"), then post-processes each frame to match the Conditioning Control
Panel sprite format exactly:

    PNG, RGBA, transparent background, tightly cropped to subject, HEIGHT = 194 px.

Consistency strategy (per task spec):
  * POSE 1 is generated text->image and becomes the MASTER character image.
  * POSES 2-4 are generated image->image, passing POSE 1 back as a reference with
    an edit instruction so the woman/outfit/palette/lighting stay identical and
    only the pose + expression change. Nano banana is strong at this.

This script makes NO changes to the app, mechanics, or any mod folder. It only
writes generated images to its own output/ directory for review.

Usage:
    python gen_avatars.py --batch kept            # generate the Keeper set
    python gen_avatars.py --batch kept --dry-run  # build prompts, no API calls
    python gen_avatars.py --batch kept --no-rembg # skip rembg, use PIL fallback

Key loading order (never printed):
    1. --env-file PATH (if given)
    2. .env files probed at the locations in ENV_CANDIDATES below
    3. process environment: GEMINI_API_KEY / GOOGLE_API_KEY / GOOGLE_GENAI_API_KEY
"""

from __future__ import annotations

import argparse
import io
import os
import sys
import time
from pathlib import Path

# ----------------------------------------------------------------------------
# CONFIG
# ----------------------------------------------------------------------------

# Project root = two levels up from this file (tools/asset_gen/gen_avatars.py).
PROJECT_ROOT = Path(__file__).resolve().parents[3]
SCRIPT_DIR = Path(__file__).resolve().parent
OUTPUT_ROOT = SCRIPT_DIR / "output"

# Model id — "nano banana". Public GA id is `gemini-2.5-flash-image`; the older
# preview id was `gemini-2.5-flash-image-preview`. Override with --model if the
# installed SDK / your project exposes a different id.
DEFAULT_MODEL = "gemini-2.5-flash-image"

# Target sprite format, learned from DroneMod/Avatars (sets 1-5) + the packaged
# drone-mode.ccpmod resources/avatarN_poseM.png entries:
#   RGBA, transparent bg, tightly cropped, every frame exactly 194 px tall.
TARGET_HEIGHT = 194           # px — the load-bearing invariant across all real sets
TARGET_FORMAT = "PNG"
ALPHA_THRESHOLD = 10          # px considered transparent below this alpha when cropping

# Feature-tile format, learned from Resources/features/*.png (the base tiles the
# FeatureCard control consumes): 1024x1024 RGB square, opaque. The card displays
# them full-bleed with UniformToFill + vertical-centre crop, and renders the
# feature title bar + "?" help icon as UI on top — so the art has NO title/"?".
TILE_SIZE = 1024

# .env probe locations (first existing file wins). *.env is gitignored in this repo.
ENV_CANDIDATES = [
    PROJECT_ROOT / ".env",
    PROJECT_ROOT / "ConditioningControlPanel" / ".env",
    PROJECT_ROOT / "tools" / ".env",
    SCRIPT_DIR / ".env",
    Path(r"C:\Projects\ccp-trailer\.env"),  # external project .env (user-provided)
]

KEY_NAMES = ["GEMINI_API_KEY", "GOOGLE_API_KEY", "GOOGLE_GENAI_API_KEY"]

# Shared style/quality suffix appended to every prompt so future batches stay
# visually coherent with the Drone art direction.
STYLE_SUFFIX = (
    "Replicate the exact art style of the existing CCP 'Drone' avatars: a single "
    "isolated stylized character, illustrated/rendered, centered, consistent framing "
    "and scale, on a clean flat solid background for an easy clean cutout. "
    "No nudity. No text, no logo, no watermark, no UI. No extra limbs or distortions. "
    "Full figure occupies the frame vertically."
)

# ---- BATCHES ---------------------------------------------------------------
# Each batch: an output subfolder, a filename pattern, a shared CHARACTER block,
# and 4 pose deltas. Add new batches here to reuse the pipeline (other avatar
# sets, achievements, etc.).

KEEPER_CHARACTER = (
    "A striking, beautiful, confident adult dominant woman, \"the Keeper,\" shown as a "
    "FULL-BODY standing figure, head to feet, a tall slender vertical silhouette that "
    "fills the frame top to bottom (to match the tall narrow Drone avatar sprites). "
    "Alluring and composed, a knowing seductive look, sleek dark hair, flawless features. "
    "She wears a glossy black latex corset with a high collar and long gloves over "
    "sharp tailored high-cut latex, thigh-high boots, sensual but elegant. A small "
    "brushed-brass key on a fine chain is the focal accent. Palette: deep charcoal and "
    "black, oxblood wine-red accents, warm brushed-brass hardware. Warm sultry low-key "
    "lighting with a soft rim light, intimate and dramatic (boudoir, not clinical). Low "
    "camera angle, viewer looking up at her."
)

DRONE_AVATARS = (
    PROJECT_ROOT / "ConditioningControlPanel" / "DroneMod" / "Avatars"
)

# Exact style-anchor instruction (prepended to every prompt when style_ref is set).
STYLE_REF_INSTRUCTION = (
    "Use the attached image ONLY as an art-style reference. Match its line weight, "
    "flat/cel shading, proportions, and level of detail exactly. Do NOT copy its "
    "character, outfit, or pose. Render the described character in that style. "
)

# Batch-4 style-anchor instruction — new anime art direction (user-supplied anchor).
STYLE_REF_INSTRUCTION_ANIME = (
    "Use the attached image ONLY as an art-style reference. Match its rendering style, "
    "finish, shading, line quality, and anime proportions exactly. Do NOT copy its "
    "character, face, pose, outfit, or background. Render the described NEW character "
    "in that exact style. "
)

# Drone feature tile used as a COMPOSITION/FORMAT reference for the Locked tiles
# (square shape, single centered hero, glow-on-dark layout) — NOT a palette ref.
DRONE_FEATURES = PROJECT_ROOT / "ConditioningControlPanel" / "DroneMod" / "Features"

# DEFAULT (app base) feature icons — used as the FORMAT reference for style samples.
DEFAULT_FEATURES = PROJECT_ROOT / "ConditioningControlPanel" / "Resources" / "features"

# Format/composition instruction for tile 1 (paired with the green Drone tile ref).
TILE_FORMAT_INSTRUCTION = (
    "Use the attached image ONLY as a COMPOSITION and FORMAT reference: match its "
    "square 1:1 tile shape, its single centered hero object, its glow-on-dark layout "
    "and the subtle background texture filling the frame edge to edge. Do NOT copy its "
    "green colour palette and do NOT copy its objects or symbols. "
)

# Palette/finish instruction for tiles 2-3 (paired image->image with tile 1).
TILE_EDIT_PREFIX = (
    "Use the attached image as the PALETTE and FINISH reference for a matching set: keep "
    "the SAME deep near-black background, the SAME crimson-red glow, the SAME glossy "
    "rendering with brushed-brass hardware, the SAME square 1:1 tile format and the SAME "
    "edge-to-edge centered composition. Render this NEW tile instead: "
)

# ---- LOCKED-MODE STYLE SAMPLES --------------------------------------------
# 3 subjects x 3 distinct styles, each an independent text->image render so the
# styles stay cleanly separated (no image->image cross-contamination). A default
# CCP base tile is attached as a FORMAT-only reference.

SAMPLE_FORMAT_INSTRUCTION = (
    "Use the attached image ONLY as a FORMAT reference: match its square 1:1 tile "
    "dimensions, aspect ratio and basic centered full-bleed tile framing. Do NOT copy "
    "its content, objects, colour palette or art style. "
)

# Global rules appended to every sample (palette + medium + no-UI guardrails).
SAMPLE_GLOBAL = (
    "GLOBAL RULES: the palette is BLACK with brushed BRASS / GOLD as the lead colours; "
    "RED appears only as an ACCENT glow and is never the dominant colour. A single "
    "centered subject fills the square 1:1 tile edge to edge. NO app title text, NO "
    "question-mark or help icon, no UI chrome, no border watermark, no logo."
)

STYLE_A_INKED = (
    "STYLE A — INKED OCCULT: a FLAT 2D illustration in an engraved tarot-card / woodcut "
    "style — bold black linework with fine engraving hatching, a decorative ornamental "
    "ritualistic border framing the tile, colours limited to black and aged parchment / "
    "brass with a single blood-red accent. Arcane and ritualistic. NOT 3D, NOT "
    "photorealistic, NOT glossy."
)
STYLE_B_NEON = (
    "STYLE B — NEON-NOIR: a FLAT 2D refined line-art illustration — thin precise glowing "
    "red and brushed brass/gold lines on a deep matte-black background, sharp and elegant "
    "(NOT thick, NOT bubbly, NOT cartoonish), a dark atmospheric goon-cave mood with subtle "
    "haze. NOT 3D, NOT photorealistic, NOT a glossy product render."
)
STYLE_C_CEL = (
    "STYLE C — CEL COMIC: a BOLD FLAT cel-shaded manga / comic illustration — hard-edged "
    "flat colour fills, hard cast shadows, halftone / screentone dot accents, thick "
    "confident inked outlines, punchy and graphic. Black with brushed brass/gold leads and "
    "a red accent only. NOT 3D, NOT photorealistic, NOT glossy."
)

# Subjects are described as ISOLATED OBJECTS (no people/bodies) to stay shippable.
SUBJECT_LOCK = (
    "Subject: an isolated metal CHASTITY CAGE device (an object only, no body, no person) "
    "held shut by a brushed-brass padlock, with a small day-counter display reading "
    "\"DAY 14\" beside it and a hanging tag that reads \"LOCKED\"."
)
SUBJECT_VIDEO = (
    "Subject: a screen / monitor displaying a pair of entranced hypnotised eyes with "
    "swirling spiral pupils and a faint hint of drool, with in-art text reading "
    "\"DON'T LOOK AWAY\"."
)
SUBJECT_SPIRAL = (
    "Subject: a hypnotic spiral with a swinging pendulum on a chain crossing in front of "
    "it. No text."
)

def _sample_poses(subject):
    """Three style variants (A/B/C) of one subject, as (label, prompt, filename)."""
    return [
        ("A) Inked Occult", f"{STYLE_A_INKED} {subject}", "A_inked.png"),
        ("B) Neon-Noir",    f"{STYLE_B_NEON} {subject}",  "B_neon.png"),
        ("C) Cel Comic",    f"{STYLE_C_CEL} {subject}",   "C_cel.png"),
    ]

# User-supplied anime style anchor for batch 4 (copied into style_ref/).
# Use the head/upper-body crop: it conveys the rendering style/line/shading while
# dropping the skin-heavy region that trips Gemini's IMAGE_SAFETY on the full frame.
STYLE_ANCHOR_ANIME = SCRIPT_DIR / "style_ref" / "style_anchor_crop.png"

# Character reference for the Keeper — the liked render (originally kept/_raw_pose4.png),
# copied to a stable style_ref/ location so it survives archiving of the draft outputs.
# Passed as an image->image reference for EVERY pose so the same woman/outfit/style
# is kept and only the pose + expression change.
CHAR_REF_KEEPER = SCRIPT_DIR / "style_ref" / "keeper_char_ref.png"

# Prepended to every charref-mode prompt.
CHARREF_PREFIX = (
    "Using the provided reference image of this EXACT character, keep the SAME woman, "
    "SAME face and short dark hair, SAME glossy black latex outfit with red garter "
    "accents and long gloves, SAME brass key on a chain, SAME color palette, SAME anime "
    "art style and rendering, SAME full-body framing and scale, SAME clean flat "
    "background. Change ONLY her pose and expression: "
)

# ---- LOCKED V2: figure-forward "hypno-mommy" tiles -------------------------
LOCKED_V2_ANCHOR = SCRIPT_DIR / "style_ref" / "locked_style.png"

LOCKED_V2_STYLE_INSTRUCTION = (
    "Use the attached reference image ONLY as a STYLE and PALETTE anchor: match its "
    "glossy anime illustration finish, its hot magenta/pink with deep black and red-streak "
    "accents, its neon glow and glitchy cyber atmosphere, and its playful-menacing "
    "'hypno-mommy' character design (platinum-blonde twin-tails, glowing magenta spiral "
    "hypno-eyes, glossy pink and black latex). Do NOT copy its exact pose, text, or "
    "composition. "
)
LOCKED_V2_EDIT_PREFIX = (
    "Use the attached image as the CHARACTER and STYLE reference: keep the SAME glossy "
    "magenta-anime hypno-mommy figure (same face, platinum twin-tails, glowing magenta "
    "spiral eyes, pink/black latex), the SAME palette, neon glow and finish, the SAME deep "
    "black background and square 1:1 tile format. Render her in this NEW scene instead: "
)
LOCKED_V2_STYLE = (
    "Deep black background with a hot magenta/pink neon glow and subtle red-streak accents, "
    "glitchy cyber atmosphere. A single glossy magenta-anime hypno-mommy figure, "
    "figure-forward and filling a square 1:1 tile. Fully clothed in glossy pink/black latex, "
    "suggestive but SFW, NO nudity, no exposed breasts or buttocks. NO app title bar, NO "
    "question-mark / help icon, no UI chrome, no watermark, no logo."
)


# ---- LOCKED CHARACTER: the "Locked" hypno-mommy master sheet -------------------
# A single full-body master render of an ORIGINAL hypno-mommy character. Uses
# locked_style.png ONLY as a style/palette anchor (NOT a character ref) so the
# rendering matches but the character is brand new. Avatar format (RGBA, transparent,
# tight full-figure crop) at master-sheet height 960 to match Resources/avatarN_pose1
# (the canonical 540x960 set) rather than the tiny 194px packaged DroneMod sprites.

LOCKED_CHAR_STYLE_INSTRUCTION = (
    "Use the attached image ONLY as an ART-STYLE and PALETTE reference: match its glossy "
    "anime illustration finish, its hot magenta-pink + deep black palette with red glitch "
    "accents and neon glow, and its playful-menacing finish. Do NOT copy the reference "
    "character in any way — NOT her hair, NOT her eyes, NOT her face, NOT her outfit, NOT "
    "her pose. Render the described NEW, original character in that style. "
)

LOCKED_CHAR_CHARACTER = (
    "A confident, mature, dominant adult woman — a 'hypno-mommy' with warm-dangerous, "
    "commanding presence — shown as a COMPLETE FULL-BODY standing figure seen HEAD TO FEET, "
    "her legs and footwear fully visible, a tall vertical composition that fills the frame "
    "top to bottom. Hair: BLONDE, long, sleek and elegant, worn side-swept (or a high "
    "half-up) — explicitly NOT twin-tails, a grown elegant style — with a single hot-magenta "
    "streak. Eyes: striking and commanding with NORMAL round pupils (absolutely NO hypnotic "
    "spiral eyes), and a knowing half-smile. Face: mature and sharp, clearly a few years "
    "older than a teen-idol look. Outfit: glossy black latex in a dominant domme cut that is "
    "clearly its OWN distinct design — a structured boned latex CORSET with hot-magenta "
    "piping over a high-cut latex bodysuit, long latex opera gloves, and thigh-high latex "
    "boots. There is absolutely NO front-zipper down the chest and it is NOT a plain "
    "zip-front catsuit. A collar/choker of a distinct sculptural shape. Suggestive but FULLY "
    "CLOTHED, no nudity. Palette: black + hot magenta-pink. Standing tall, commanding, in "
    "total control."
)

LOCKED_CHAR_STYLE = (
    "Single isolated original character, centered, the ENTIRE figure visible from the top of "
    "her head down to her FEET — do NOT crop at the hips or thighs, show the full standing "
    "body including legs and boots — filling the frame vertically, on a CLEAN FLAT SOLID "
    "background for an easy clean cutout, with subtle red glitch accents in the BACKGROUND "
    "ONLY. Suitable as a master character sheet. Two arms, two legs, correct hands, no extra "
    "limbs, no distortion. Fully clothed, no nudity. NO text, NO logo, NO watermark, NO UI, "
    "NO hypnotic spiral, no other characters."
)


# ---- LOCKED TILES (character) ----------------------------------------------
# Feature tiles that mirror the Sissy/Bambi build: OUR locked-in character performs
# each feature's action in-frame, deep black bg + magenta neon glow, figure-forward.
# Every tile anchors to the locked_character master (charref mode) so she stays the
# same woman and never drifts to a generic anime girl. kind=tile -> 1024x1024 RGB.

LOCKED_CHAR_MASTER = (
    SCRIPT_DIR / "output" / "locked_character" / "locked_character_master.png"
)
# Square, waist-up crop of the master — used as the char-ref for SQUARE tiles so the
# model outputs a square waist-up figure (the tall master ref biased output to full-body
# portrait, which left black bars / cropped heads in a 1:1 tile). Same character.
LOCKED_CHAR_MASTER_SQ = (
    SCRIPT_DIR / "output" / "locked_character" / "locked_character_waistup_sq.png"
)

LOCKED_TILE_CHARREF_PREFIX = (
    "Using the provided reference image of this EXACT character, keep the SAME woman — the "
    "SAME blonde, long, sleek side-swept hair with the single hot-magenta streak, the SAME "
    "mature face, the SAME glossy black-and-magenta latex domme outfit (structured corset / "
    "bodysuit with magenta trim, long latex gloves, distinct choker), and the SAME glossy "
    "anime rendering and colour palette. Do NOT turn her into a generic anime girl, do NOT "
    "restyle her hair or outfit. Change ONLY her pose / action and the scene around her: "
)

LOCKED_TILE_STYLE = (
    "Glossy anime illustration finish (NOT 3D, NOT photorealistic), figure-forward. "
    "COMPOSE FOR A SQUARE 1:1 FRAME: place the character and the scene element SIDE BY SIDE "
    "so together they fill the width of the square; frame her from roughly the thighs up (a "
    "three-quarter crop is fine) so the composition reads as a balanced square, and keep her "
    "head, hands and any held object comfortably inside the frame with margin — nothing "
    "cropped at the top, bottom or sides. Deep near-black background with a hot magenta-pink "
    "neon glow and subtle red glitch accents. Premium, dramatic. Fully clothed, suggestive "
    "but SFW, no nudity. NO app title bar, NO question-mark / help icon, no UI chrome, no "
    "watermark, no logo, no border frame around the tile."
)


# ---- LOCKED TILES SQUARE (character, dashboard grid) -----------------------
# Same 4 locked tiles, but composed WAIST-UP / three-quarter so the character fills
# a SQUARE 1:1 tile (the tall full-body version left black bars). Figure to one side,
# feature element filling the rest — mirrors the default Sissy tile layout. Anchored
# to the locked_character master so she stays on-model. kind=tile -> 1024x1024 RGB,
# fit=cover so the square is filled edge to edge (no padding bars).

LOCKED_SQ_CHARREF_PREFIX = (
    "Using the provided reference image of this EXACT character, keep the SAME woman — the "
    "SAME blonde, long, sleek side-swept hair with the single hot-magenta streak, the SAME "
    "mature face, the SAME glossy black-and-magenta latex domme outfit (structured corset / "
    "bodysuit with magenta trim, long latex gloves, distinct choker), and the SAME glossy "
    "anime rendering and colour palette. Do NOT turn her into a generic anime girl, do NOT "
    "restyle her hair or outfit. Frame her WAIST-UP / three-quarter crop (NOT full body, do "
    "NOT show her legs or feet) so she fits a SQUARE tile. Change ONLY her pose / action and "
    "the scene around her: "
)

LOCKED_SQ_STYLE = (
    "Glossy anime illustration finish (NOT 3D, NOT photorealistic). COMPOSE FOR A SQUARE 1:1 "
    "FRAME, filled edge to edge with NO empty bars: the character (waist-up / three-quarter) "
    "sits to ONE SIDE occupying roughly half the tile, and the feature element fills the "
    "OTHER half — a balanced left/right composition like a poster. Keep her head, hands and "
    "the feature element comfortably inside the frame with a little margin so nothing is "
    "cropped at the edges. Deep near-black background with a hot magenta-pink neon glow and "
    "subtle red glitch accents. Premium, dramatic. Fully clothed, suggestive but SFW, no "
    "nudity. NO app title bar, NO question-mark / help icon, no UI chrome, no watermark, no "
    "logo, no border frame around the tile."
)


# ---- LOCKED LEVEL-UP AVATARS (stages 2-5) ----------------------------------
# Four escalating full-body companion sprites of the SAME Locked hypno-mommy,
# each anchored (charref) to the stage-1 master so she stays the same woman while
# her outfit / pose / props / surrounding intensity escalate per stage. Avatar
# format: RGBA transparent, rembg cutout, 960px tall, tight crop — matches stage 1
# (locked_character_master.png) and the canonical Resources/avatarN_pose1 sprites.
# Filenames follow the per-set convention: stage2=avatar2 ... stage5=avatar5.

LOCKED_AVATAR_CHARREF_PREFIX = (
    "Using the provided reference image of this EXACT character, keep the SAME woman — the "
    "SAME face and mature sharp features, the SAME blonde long sleek side-swept hair with the "
    "single hot-magenta streak, the SAME knowing half-smile, the SAME glossy anime rendering "
    "and hot magenta-pink + deep black palette. She is ALWAYS the DOMINANT one — sharp-eyed, "
    "commanding, in total control, NEVER dizzy, drained or entranced herself. Show her as a "
    "COMPLETE FULL-BODY standing figure seen HEAD TO FEET, her legs and footwear fully "
    "visible, filling the frame top to bottom. You MAY escalate and elaborate her outfit, "
    "pose, props, hardware and the intensity around her as described below, but she stays the "
    "same recognizable woman. Escalation for this stage: "
)

LOCKED_AVATAR_STYLE = (
    "Single isolated character, centered, the ENTIRE figure visible from the top of her head "
    "down to her FEET — do NOT crop at the hips or thighs, show the full standing body "
    "including legs and boots — filling the frame vertically, on a CLEAN FLAT SOLID background "
    "for an easy clean cutout. Keep all glow, spiral energy, chains, locks and props CLOSE "
    "around her body so her silhouette stays clean and cuts out neatly (no far-flung "
    "background scenery). Glossy anime illustration finish (NOT 3D, NOT photorealistic), hot "
    "magenta-pink neon glow with subtle red glitch accents. Two arms, two legs, correct hands, "
    "no extra or duplicated limbs, no distortion. Fully clothed, suggestive but SFW, no "
    "nudity. NO text, NO logo, NO watermark, NO UI, no other characters."
)


# ---- LOCKED LEVEL-UP AVATARS v2 (stages 2-5, VISUALLY DISTINCT) ------------
# Same Locked hypno-mommy FACE, but each stage is a deliberately distinct look:
# different hairstyle, different outfit design, different DOMINANT colour. The
# anchor is FACE-ONLY (keep her face + the signature blonde-with-magenta-streak
# identity), so outfit/hair/palette are free to change per stage. Avatar format:
# RGBA transparent, rembg cutout, 960px tall, tight crop.

LOCKED_AVATAR_V2_CHARREF_PREFIX = (
    "Using the provided reference image, keep ONLY her FACE and her identity as a blonde "
    "woman with a single signature hot-magenta streak in her hair — the SAME facial features "
    "and the SAME sharp, knowing dominant expression. CHANGE everything else for this stage "
    "exactly as described: give her a DIFFERENT hairstyle, a DIFFERENT outfit design, and a "
    "DIFFERENT dominant colour — do NOT reuse a previous outfit and do NOT default to a plain "
    "black latex bodysuit. She is ALWAYS dominant, composed and sharp-eyed, NEVER dizzy, "
    "drained or submissive. Show her as a COMPLETE FULL-BODY standing figure seen HEAD TO "
    "FEET, legs and footwear fully visible, filling the frame top to bottom. BOTH of her hands "
    "must be FULLY VISIBLE and well-formed with correct fingers — do NOT crop, hide, tuck away "
    "or cut off her hands or arms. THIS STAGE: "
)

LOCKED_AVATAR_V2_STYLE = (
    "Single isolated character, centered, the ENTIRE figure visible from the very top of her "
    "head (including any hair or slim headpiece) down to her FEET — her FEET must be FULLY in "
    "shot, do NOT crop the feet, hips or thighs — filling the frame vertically, on a PLAIN "
    "FLAT UNIFORM light-grey background (NO gradient, NO vignette, NO shadow, NO floor or "
    "scenery) for an easy clean cutout. Keep any glow, aura, spiral, chains and props CLOSE "
    "around her body so her silhouette cuts out neatly. Softer, FEMININE physique — slim and "
    "gently curvy, NOT muscular, NOT broad-shouldered. Sleek MODERN LATEX-DOMME aesthetic with "
    "'Locked' motifs only (locks, keys, chains, hypno-spiral) — absolutely NO fantasy "
    "costumes: NO military uniforms, NO fussy ornate gowns, NO RPG-queen or armored-fantasy "
    "looks. Glossy anime illustration finish (NOT 3D, NOT photorealistic), vivid neon glow "
    "with subtle red glitch accents. Two arms, two legs, BOTH hands fully visible with correct "
    "fingers, no extra or duplicated limbs, no distortion. Fully clothed, suggestive but SFW, "
    "no nudity. NO text, NO logo, NO watermark, NO UI, no other characters."
)


def _sample_batch(subject_poses):
    """Shared config for a style-sample batch (one subject, 3 styles)."""
    return {
        "kind": "tile",
        "tile_size": TILE_SIZE,
        "consistency": "independent",            # each style is its own text->image
        "style_ref": DEFAULT_FEATURES / "Phrase_Lock.png",  # FORMAT only
        "style_instruction": SAMPLE_FORMAT_INSTRUCTION,
        "style": SAMPLE_GLOBAL,
        "poses": subject_poses,
    }


BATCHES = {
    # Locked CHARACTER — ONE full-body master render of an ORIGINAL hypno-mommy.
    # Style/palette anchored to locked_style.png (style ref ONLY, new character).
    # Avatar format: RGBA transparent, tight full-figure crop, 960px tall master.
    "locked_character": {
        "subfolder": "locked_character",
        "filename_pattern": "locked_character_master.png",
        "consistency": "independent",
        "style_ref": LOCKED_V2_ANCHOR,
        "style_instruction": LOCKED_CHAR_STYLE_INSTRUCTION,
        "target_height": 960,
        "character": LOCKED_CHAR_CHARACTER,
        "style": LOCKED_CHAR_STYLE,
        "poses": [
            ("Master",
             "She stands tall and commanding at full height, facing the viewer, both legs and "
             "thigh-high boots fully in frame, weight settled, one gloved hand resting on her "
             "hip, chin level, a knowing half-smile — a poised hypno-mommy in total control. "
             "Frame the whole body head to feet with a little headroom and floor below the "
             "boots.",
             "locked_character_master.png"),
        ],
    },
    # Locked LEVEL-UP AVATARS (stages 2-5) — escalating full-body companion sprites,
    # each charref'd to the stage-1 master. Avatar format: RGBA, rembg, 960px tall.
    # Filenames: avatar2_pose1.png .. avatar5_pose1.png (one POSE 1 per stage).
    "locked_avatars": {
        "subfolder": "locked_avatars",
        "consistency": "charref",
        "char_ref": LOCKED_CHAR_MASTER,
        "charref_prefix": LOCKED_AVATAR_CHARREF_PREFIX,
        "target_height": 960,
        "style": LOCKED_AVATAR_STYLE,
        "poses": [
            ("Stage 2 - The Claim",
             "STAGE 2, THE CLAIM — a more assertive, possessive stance with a knowing claiming "
             "look; she holds a glossy magenta-and-black COLLAR with a leash (or a single ornate "
             "magenta key) out toward the viewer, as if about to claim you. Her latex domme "
             "outfit is slightly MORE elaborate than the reference — a little more magenta "
             "piping and hardware. Confident and owning.",
             "avatar2_pose1.png"),
            ("Stage 3 - The Grip",
             "STAGE 3, THE GRIP — a more commanding, regal stance, chin slightly raised, one "
             "gloved hand GRIPPING a glowing magenta CHAIN that extends down toward the viewer "
             "like she holds your leash. Magenta spiral energy and faint red glitch sparks curl "
             "CLOSE around her. Her outfit is more regal with more black-and-magenta hardware, "
             "buckles and straps. Powerful, in command.",
             "avatar3_pose1.png"),
            ("Stage 4 - The Descent",
             "STAGE 4, THE DESCENT — she LOOMS and looks DOWN at the viewer from a slightly "
             "high, dominant angle, triumphant and possessive with controlled aggression. "
             "Glowing magenta hypnotic spirals, padlocks and chains wrap CLOSE around her body "
             "and trail from her gloved hands. Her outfit is darker and more ornate — heavier "
             "latex with magenta-glowing hardware. Overwhelming presence.",
             "avatar4_pose1.png"),
            ("Stage 5 - The Apex",
             "STAGE 5, THE APEX — MAXIMUM dominance and her most elaborate, regal look: a sleek "
             "magenta-glowing CROWN / headpiece, draped in chains and padlocks, with a glowing "
             "magenta hypnotic-spiral aura enveloping her held CLOSE like radiant energy and a "
             "subtle hint of a dark ornate throne framing her directly behind. Imperious, "
             "absolute, towering in total control. She is STANDING and is the clear subject.",
             "avatar5_pose1.png"),
        ],
    },
    # Locked LEVEL-UP AVATARS v2 — VISUALLY DISTINCT stages (different hair/outfit/colour
    # each), FACE-ONLY anchor to the master. Avatar format: RGBA, rembg, 960px tall.
    # Filenames: avatar2_pose1.png .. avatar5_pose1.png (one POSE 1 per stage).
    "locked_avatars_v2": {
        "subfolder": "locked_avatars",
        "consistency": "charref",
        "char_ref": LOCKED_CHAR_MASTER,
        "charref_prefix": LOCKED_AVATAR_V2_CHARREF_PREFIX,
        "target_height": 960,
        "style": LOCKED_AVATAR_V2_STYLE,
        "poses": [
            ("Stage 2 - The Claim",
             "THE CLAIM — dominant colour HOT MAGENTA / PINK, playful and confident. A glossy "
             "PINK LATEX TWO-PIECE: a fitted pink latex CROP TOP with SEPARATE pink latex "
             "high-waisted SHORTS and a bare midriff between them (NOT a catsuit, NOT a dress). "
             "HIGH SLEEK PONYTAIL (blonde with the magenta streak). One hand rests on her hip; "
             "the OTHER hand holds up a single small GOLD KEY, clearly displayed. Soft feminine "
             "build, playful-confident smile. NO chains and NO padlocks — just the one key.",
             "avatar2_pose1.png"),
            ("Stage 3 - The Pull",
             "THE PULL — colours BLACK + MAGENTA, a sleek dominatrix-HYPNOTIST. A sleek BLACK "
             "LATEX catsuit / bodysuit with a strappy HARNESS and a subtle magenta HYPNO-SPIRAL "
             "motif worked into the latex; a magenta spiral AURA glows close around her. Hair "
             "worn DOWN, long and sleek (blonde with the magenta streak). She HOLDS UP a "
             "pocket-watch on a chain (or a glowing magenta spiral orb) in one hand, swinging it "
             "to draw the viewer under; the OTHER hand is open and visible. Sharp, entrancing, "
             "in control. NO chains and NO padlocks.",
             "avatar3_pose1.png"),
            ("Stage 4 - The Bind",
             "THE BIND — DEEP BLACK latex with an intense MAGENTA / RED glow, possessive. A "
             "sleek black latex bodysuit and a prominent COLLAR. Sleek glowing-magenta CHAINS "
             "begin to WRAP around her body and REACH OUT toward the viewer; she may hold a "
             "glowing magenta LEASH in one hand. Hair slicked back or in a SEVERE HIGH BUN "
             "(blonde with the magenta streak). Looming posture, chin lowered, looking DOWN at "
             "the viewer, possessive and predatory. The chains are SLEEK and glowing, NOT "
             "fussy. Both hands fully visible.",
             "avatar4_pose1.png"),
            ("Stage 5 - The Apex",
             "THE APEX — sleek BLACK + MAGENTA, minimal-luxe and distilled. Sleek refined black "
             "latex bodysuit. She CLEARLY WEARS a SLIM, THIN, understated GOLD CROWN / halo "
             "band resting on her head (small and elegant, plainly visible but NOT large, NOT "
             "ornate, NOT spiky) — this thin gold crown is the ONE gold accent. ONE statement "
             "piece at her CHEST: a single glowing magenta KEYHOLE emblem. Refined thin chains "
             "only as a whisper of an accent. Hair sleek and elegant (blonde with the magenta "
             "streak). Quietly powerful, commanding, minimal. NOT an ornate fantasy gown, NOT "
             "armored regalia, NOT busy. Both hands fully visible.",
             "avatar5_pose1.png"),
        ],
    },
    # Locked TILES (character) — OUR locked-in character performs each feature action,
    # anchored to the locked_character master so she stays consistent across all 4.
    # kind=tile -> 1024x1024 RGB, no bg removal. Filenames match the default tiles.
    "locked_tiles_char": {
        "subfolder": "locked_tiles",
        "kind": "tile",
        "tile_size": TILE_SIZE,
        "consistency": "charref",
        "char_ref": LOCKED_CHAR_MASTER,
        "charref_prefix": LOCKED_TILE_CHARREF_PREFIX,
        "tile_fit": "contain",
        "style": LOCKED_TILE_STYLE,
        "poses": [
            ("Spiral Overlay",
             "She stands to one side of the tile, her full upper body and face clearly "
             "visible, and with one gloved hand presents a glowing magenta hypnotic spiral "
             "disc held off to the OTHER side (like a magician presenting an orb). The spiral "
             "must NOT cover her face or body — she is the clear focal character beside it. "
             "NO text anywhere in the image.",
             "spiral_overlay.png"),
            ("Mandatory Video",
             "She stands beside a large glowing screen / monitor and gestures 'watch this' "
             "toward it with a commanding look; in-art neon text on or around the screen "
             "reads \"DON'T LOOK AWAY\" in a clean bold neon font.",
             "mandatory_videos.png"),
            ("Bouncing Text",
             "She perches sideways on the TOP edge of a big glowing neon sign, legs swung "
             "together to one side and leaning back on one hand, relaxed and playful. The "
             "neon sign sits below her and its in-art text reads \"GOOD BOY\" in bold glowing "
             "neon letters — the COMPLETE two words must be fully legible and NOT blocked or "
             "overlapped by her body, legs or boots.",
             "bouncing_text.png"),
            ("Lock Card",
             "She holds up a small card / tag toward the viewer with a knowing half-smile; "
             "the in-art text on the card reads \"LOCKED\" in a clean bold neon font.",
             "Phrase_Lock.png"),
        ],
    },
    # Locked TILES SQUARE — same 4 tiles, waist-up so they fill a 1:1 dashboard tile.
    "locked_tiles_square": {
        "subfolder": "locked_tiles_square",
        "kind": "tile",
        "tile_size": TILE_SIZE,
        "consistency": "charref",
        "char_ref": LOCKED_CHAR_MASTER_SQ,
        "charref_prefix": LOCKED_SQ_CHARREF_PREFIX,
        "tile_fit": "cover",
        "style": LOCKED_SQ_STYLE,
        "poses": [
            ("Spiral Overlay",
             "A HUGE glowing magenta hypnotic spiral dominates the tile — an enormous "
             "spiral disc that fills most of the square, its outer rings running right to "
             "the edges. She is pushed to one side, waist-up, smaller, one gloved hand "
             "raised presenting / conjuring the giant spiral. The spiral is the clear focal "
             "point and must NOT cover her face. NO text anywhere in the image.",
             "spiral_overlay.png"),
            ("Mandatory Video",
             "She is on one side of the square, waist-up, gesturing 'watch this' toward a "
             "large glowing screen / monitor that fills the other half of the tile; in-art "
             "neon text on the screen reads \"DON'T LOOK AWAY\" in a clean bold neon font.",
             "mandatory_videos.png"),
            ("Bouncing Text",
             "She leans in from one side, waist-up, resting on a big glowing neon sign that "
             "fills the lower / other half of the square; the neon sign's in-art text reads "
             "\"GOOD BOY\" in bold glowing neon letters — the COMPLETE two words fully "
             "legible and NOT blocked by her body.",
             "bouncing_text.png"),
            ("Lock Card",
             "She is on one side of the square, waist-up, holding up a card / tag toward the "
             "viewer with a knowing half-smile; the in-art text on the card reads \"LOCKED\" "
             "in a clean bold neon font, the card large enough to read clearly.",
             "Phrase_Lock.png"),
        ],
    },
    # Locked V2 — figure-forward hypno-mommy tiles, anchored to style_ref/locked_style.png.
    # Tile 1 (Spiral) is the text+style-ref master; tiles 2-3 are image->image from it so
    # the figure stays consistent. kind="tile" -> 1024x1024 RGB square.
    "locked_v2": {
        "subfolder": "locked_v2",
        "kind": "tile",
        "tile_size": TILE_SIZE,
        "consistency": "reference",
        "style_ref": LOCKED_V2_ANCHOR,
        "style_instruction": LOCKED_V2_STYLE_INSTRUCTION,
        "edit_prefix": LOCKED_V2_EDIT_PREFIX,
        "style": LOCKED_V2_STYLE,
        "poses": [
            ("Spiral Overlay",
             "She presents and holds up a glowing magenta hypnotic spiral disc toward the "
             "viewer, drawing the eye into it. No text.",
             "spiral_overlay.png"),
            ("Mandatory Video",
             "She stands beside a glowing screen / monitor, gesturing 'watch this' toward it; "
             "in-art text reads \"DON'T LOOK AWAY\" in a clean bold font.",
             "mandatory_videos.png"),
            ("Lock Card",
             "She holds up a small card / tag toward the viewer with a playful-menacing "
             "smile; the in-art text on the card reads \"GOOD BOY\" in a clean bold font.",
             "Phrase_Lock.png"),
        ],
    },
    # Locked-mode STYLE SAMPLES: one subject per batch, 3 distinct styles each.
    "locked_sample_lock":   {"subfolder": "locked_style_samples/lock",
                             **_sample_batch(_sample_poses(SUBJECT_LOCK))},
    "locked_sample_video":  {"subfolder": "locked_style_samples/video",
                             **_sample_batch(_sample_poses(SUBJECT_VIDEO))},
    "locked_sample_spiral": {"subfolder": "locked_style_samples/spiral",
                             **_sample_batch(_sample_poses(SUBJECT_SPIRAL))},
    # Locked-mode FEATURE TILES (not avatars). kind="tile" -> 1024x1024 RGB square,
    # no background removal. Tile 1 (Lock Card) is the text+format-ref master; tiles
    # 2-3 are image->image from tile 1 so the palette/finish stays consistent.
    "locked_tiles": {
        "subfolder": "locked_tiles",
        "kind": "tile",
        "tile_size": TILE_SIZE,
        "consistency": "reference",
        "style_ref": DRONE_FEATURES / "protocol_lock.png",   # composition/format only
        "style_instruction": TILE_FORMAT_INSTRUCTION,
        "edit_prefix": TILE_EDIT_PREFIX,
        "style": (
            "Deep near-black background with a crimson-red glow and a subtle brushed-metal "
            "and chain texture filling the whole square frame edge to edge. Objects rendered "
            "glossy with a red rim-light and brushed-brass hardware. Premium, expensive, "
            "dramatic, a single centered hero composition. Any in-art text uses a sharp "
            "elegant serif or a stamped-brass look — NOT neon, NOT terminal monospace. NO "
            "feature title bar, NO question-mark / help icon, no UI chrome, no watermark, no "
            "logo, no border frame around the tile. Square 1:1 tile."
        ),
        # (label, prompt, output filename) — filenames match the base tiles a mod overrides.
        "poses": [
            ("Lock Card",
             "A glossy brushed-brass padlock, heavy and premium, front and centre, glowing "
             "crimson red. A small tag/card hangs from the padlock's shackle reading "
             "\"I AM KEPT\" in elegant serif lettering. The hero tile of the set — make it "
             "look expensive.",
             "Phrase_Lock.png"),
            ("Mandatory Video",
             "A glossy black monitor / screen with a crimson-red screen-glow, set in a "
             "brushed-brass frame. On the screen, a single commanding red-glowing female eye "
             "staring straight out at the viewer. Short in-art text reads \"EYES ON ME\" in "
             "elegant serif lettering.",
             "mandatory_videos.png"),
            ("Spiral Overlay",
             "A deep crimson-red glossy hypnotic spiral filling the tile, with a brushed-brass "
             "keyhole at its dead centre; the spiral arms pull the eye inward toward the "
             "keyhole. Red glow, brass accent. Clean, NO text at all.",
             "spiral_overlay.png"),
        ],
    },
    # Batch 5: keep the liked Keeper character (kept/_raw_pose4.png) and produce a
    # 4-pose set in the SAME anime style via image->image. Only pose/expression vary.
    "kept5": {
        "subfolder": "kept_v5",
        "filename_pattern": "avatar8_pose{n}.png",
        "consistency": "charref",
        "char_ref": CHAR_REF_KEEPER,
        "style": (
            "Single isolated character, centered, the full figure standing and occupying "
            "the frame vertically head to feet, on a clean flat solid background for an "
            "easy clean cutout. Fully clothed, no nudity, no text, no logo, no watermark, "
            "no other characters. Two arms, two legs, correct hands, no extra limbs, no "
            "distortion."
        ),
        "poses": [
            ("Composed",
             "standing composed and in total control, hands resting easily at her front, "
             "a faint knowing superior smile, eyes on the viewer."),
            ("Amused",
             "an amused, entertained expression — a soft genuine smirk, head tilted "
             "slightly, eyes bright with amusement, one gloved hand raised near her lips. "
             "Playful and pleased."),
            ("Alluring",
             "an alluring, seductive pose — hip cocked, chin lowered, a sultry inviting "
             "half-lidded gaze drawing the viewer in, one hand trailing the key on its "
             "chain. Confident and enticing."),
            ("Teasing",
             "a teasing, playful pose — a sly tongue-in-cheek smirk, one gloved finger "
             "raised in a coy \"not yet\" gesture, mischievous and in control. Playful, "
             "not cruel."),
        ],
    },
    # Batch 6: same liked Keeper character as kept5, but STRONGER pose variation —
    # distinct full-body posture, weight shifts, turns and camera angles (not just
    # face/hands). Same character/outfit/style preserved via image->image.
    "kept6": {
        "subfolder": "kept_v6",
        "filename_pattern": "avatar8_pose{n}.png",
        "consistency": "charref",
        "char_ref": CHAR_REF_KEEPER,
        "style": (
            "Single isolated character, centered, the WHOLE figure head to feet visible "
            "and filling the frame vertically, on a clean flat solid background for an "
            "easy clean cutout. Fully clothed, no nudity, no text, no logo, no watermark, "
            "no other characters. Two arms, two legs, correct hands, no extra limbs, no "
            "distortion."
        ),
        "poses": [
            ("Composed",
             "Change her WHOLE-BODY pose noticeably: standing tall in a confident "
             "three-quarter turn, weight on one leg, arms loosely crossed under the chest, "
             "chin level, a faint knowing superior smile. Clearly a different stance from "
             "the reference."),
            ("Amused",
             "Change her WHOLE-BODY pose noticeably: weight shifted onto one hip, one hand "
             "on that hip and the other raised near her mouth, shoulders relaxed and turned, "
             "head tilted back slightly in genuine amusement, a bright smirk. Dynamic, "
             "clearly different from the reference stance."),
            ("Alluring",
             "Change her WHOLE-BODY pose noticeably: a strong hip-cocked contrapposto seen "
             "from a three-quarter angle, an S-curve to the body, one gloved hand trailing "
             "down her thigh and the other lifting the key on its chain, glancing back over "
             "her shoulder at the viewer with a sultry half-lidded gaze. Sinuous and "
             "enticing, clearly different from the reference stance."),
            ("Teasing",
             "Change her WHOLE-BODY pose noticeably: facing the viewer in a playful "
             "three-quarter stance, weight on one leg with the other knee bent and leaning "
             "slightly forward, one gloved finger raised to her lips in a coy \"shh / not "
             "yet\" gesture and the other hand on her hip, a sly mischievous smirk. Playful "
             "and dynamic, clearly different from the reference stance."),
        ],
    },
    # Batch 4: NEW anime style direction (user-supplied style anchor) + NEW
    # character — the Keeper as a DOMINANT HYPNOTIST (the one who entrances,
    # never entranced). Dark hair, red/gold glowing spiral eyes, brass key as a
    # scepter, crimson power accent. Writes into output/kept/ per task spec.
    "kept4": {
        "subfolder": "kept",
        "filename_pattern": "avatar8_pose{n}.png",
        "consistency": "independent",
        "style_ref": STYLE_ANCHOR_ANIME,
        "style_instruction": STYLE_REF_INSTRUCTION_ANIME,
        "style": (
            "Single isolated character, centered, the full figure standing and occupying "
            "the frame vertically head to feet, on a clean flat solid background for an "
            "easy clean cutout. No nudity, fully clothed. No text, no logo, no watermark, "
            "no UI, NO background spiral, no other characters. Two arms, two legs, correct "
            "hands, no extra limbs, no distortion."
        ),
        "character": (
            "A dominant, confident adult woman, \"the Keeper,\" shown as a FULL-BODY "
            "standing figure, head to feet, a tall vertical composition that fills the "
            "frame top to bottom (to match the tall narrow avatar sprite format). She is "
            "the HYPNOTIST who ENTRANCES others and is NEVER entranced herself — in total "
            "control. Eyes: sharp, knowing, half-lidded, with a faint cold red/gold glow "
            "and a subtle spiral in the irises — commanding and aware, NOT blank, NOT "
            "drained. Expression: a faint superior knowing smile, looking directly at the "
            "viewer. Long dark hair. Wardrobe (hard domme, not soft lingerie): a structured "
            "glossy black latex corset or harness, a high collar, long black latex gloves, "
            "sharp architectural tailored lines — suggestive but FULLY CLOTHED, NO nudity. "
            "Signature prop: an ornate brushed-brass key held in one hand like a scepter "
            "(or at her hip on a chain), clearly visible. Palette: deep black, brushed-brass "
            "hardware, and crimson red as her POWER accent — the eye-glow and trim read as "
            "her energy, not merely outfit colour. Posture: composed, dominant, standing "
            "tall, in control."
        ),
        "poses": [
            ("Composed",
             "POSE 1 (Composed): standing tall and relaxed in total control, the brass key "
             "held like a scepter, a faint superior knowing smile, eyes on the viewer."),
            ("Approving",
             "Her pose and expression: a warm satisfied smile, pleased but superior, one "
             "gloved hand lightly turning the key."),
            ("Denying",
             "Her pose and expression: teasing denial, chin slightly up, looking down at "
             "the viewer with a small \"not yet\" smirk, the key held just out of reach. "
             "Playful, not angry."),
            ("Deepening",
             "Her pose and expression: leaning toward the viewer, intense half-lidded "
             "gaze drawing you in, the key dangled forward on its chain. Hypnotic."),
        ],
    },
    # Batch 3: cel-shaded to match the Drone avatar art style via a STYLE-ANCHOR
    # image (a real Drone avatar passed as an art-style reference, not a character
    # reference). Blonde, full-body, flat 2D illustration.
    "kept3": {
        "subfolder": "kept_v3",
        "filename_pattern": "avatar8_pose{n}.png",
        "consistency": "independent",
        "style_ref": DRONE_AVATARS / "Chassis_Gamma_Standby.png",
        "style_instruction": STYLE_REF_INSTRUCTION,
        "style": (
            "Single isolated character, centered, the full figure standing and occupying "
            "the frame vertically head to feet, on a plain flat solid background for an "
            "easy clean cutout. Flat cel shading, crisp clean outlines, limited color "
            "banding, minimal rendering. No nudity, no text, no logo, no watermark. "
            "Two arms, two legs, correct hands, no extra limbs, no distortion."
        ),
        "character": (
            "A confident adult dominant woman, \"the Keeper,\" as a FULL-BODY standing "
            "figure, head to feet, facing the viewer, a tall vertical composition that "
            "fills the frame top to bottom (to match the tall narrow avatar sprite format). "
            "FLAT CEL-SHADED 2D ILLUSTRATION, clean bold linework, stylized proportions, "
            "minimal rendering, graphic, NOT photorealistic, NOT heavily shaded, NOT 3D, "
            "NOT glossy. Long straight platinum-blonde hair parted in the centre falling "
            "past the shoulders, an oval face, full lips, light makeup, a knowing seductive "
            "expression. She wears a black latex corset with a high collar and long gloves, "
            "high-cut latex and thigh-high boots, sharp tailored lines, elegant. A small "
            "brushed-brass key on a fine chain is the focal accent. Palette: deep charcoal "
            "and black, oxblood wine-red accents, warm brushed-brass hardware."
        ),
        "poses": [
            ("Composed",
             "POSE 1 (Composed): relaxed and in control, the key resting at her throat, "
             "a faint knowing smile, eyes on the viewer."),
            ("Approving",
             "Her pose and expression: a warm satisfied smile, pleased but superior, one "
             "gloved hand lightly turning the key."),
            ("Denying",
             "Her pose and expression: teasing denial, chin slightly up, looking down at "
             "the viewer with a small \"not yet\" smirk, the key held just out of reach. "
             "Playful, not angry."),
            ("Deepening",
             "Her pose and expression: leaning toward the viewer, intense half-lidded "
             "seductive gaze drawing you in, the key dangled forward on its chain. Hypnotic."),
        ],
    },
    # Batch 2: blonde + photorealistic / "more human" (less stylized-AI look).
    # Overrides STYLE_SUFFIX with an editorial-photo style.
    "kept2": {
        "subfolder": "kept_v2",
        "filename_pattern": "avatar8_pose{n}.png",
        # Photoreal latex references trip Gemini's IMAGE_SAFETY on image->image
        # edits, so generate every pose text->image and hold consistency with a
        # detailed invariant character description instead.
        "consistency": "independent",
        "style": (
            "Editorial boudoir PHOTOGRAPH look — shot on an 85mm lens, shallow depth of "
            "field, soft natural film grain, true-to-life color, looks like a real photo of "
            "a real person, NOT a 3D render, NOT a doll, NOT a stylized illustration. "
            "A single isolated person, centered, the full figure occupying the frame "
            "vertically, standing on a clean seamless flat studio backdrop for an easy clean "
            "cutout. Realistic human anatomy: correct hands and fingers, two arms, two legs, "
            "no extra or missing limbs, no warping. No nudity, no text, no logo, no watermark."
        ),
        "character": (
            "A striking, beautiful, confident adult dominant woman, \"the Keeper,\" as a "
            "FULL-BODY standing figure, head to feet, a tall vertical composition that fills "
            "the frame top to bottom (to match the tall narrow avatar sprite format). "
            "PHOTOREALISTIC — she looks like a real human woman: natural realistic body "
            "proportions (not an exaggerated hourglass), lifelike skin with real texture, "
            "pores and subtle imperfections, NOT airbrushed, NOT plastic, NOT glossy CGI skin. "
            "ALWAYS THE SAME WOMAN: long straight sleek platinum-blonde hair parted in the "
            "centre falling past the shoulders, an oval face with high cheekbones, full lips, "
            "light makeup with a soft smoky eye, mid-twenties, slim athletic build. A knowing "
            "seductive expression. She wears a glossy black latex corset with a high collar and "
            "long gloves over high-cut latex, thigh-high boots, sharp tailored lines, sensual "
            "but elegant. A small brushed-brass key on a fine chain is the focal accent. "
            "Palette: deep charcoal and black, oxblood wine-red accents, warm brushed-brass "
            "hardware. Warm sultry low-key boudoir lighting with a soft rim light, intimate and "
            "dramatic. Low camera angle, viewer looking slightly up at her."
        ),
        "poses": [
            ("Composed",
             "POSE 1 (Composed): relaxed and in control, the key resting at her throat, "
             "a faint knowing smile, eyes on the viewer."),
            ("Approving",
             "Change ONLY the pose and expression: a warm satisfied smile, pleased but "
             "superior, one gloved hand lightly turning the key."),
            ("Denying",
             "Change ONLY the pose and expression: teasing denial, chin slightly up, "
             "looking down at the viewer with a small \"not yet\" smirk, the key held just "
             "out of reach. Playful, not angry."),
            ("Deepening",
             "Change ONLY the pose and expression: leaning toward the viewer, intense "
             "half-lidded seductive gaze drawing you in, the key dangled forward on its "
             "chain. Hypnotic."),
        ],
    },
    "kept": {
        "subfolder": "kept",
        # App-ready convention for the FIRST custom set of a new mode is set 8
        # (built-in sets are 1-7). Source descriptive pose names kept in metadata.
        "filename_pattern": "avatar8_pose{n}.png",
        "character": KEEPER_CHARACTER,
        "poses": [
            ("Composed",
             "POSE 1 (Composed): relaxed and in control, the key resting at her throat, "
             "a faint knowing smile, eyes on the viewer."),
            ("Approving",
             "Change ONLY the pose and expression: a warm satisfied smile, pleased but "
             "superior, one gloved hand lightly turning the key."),
            ("Denying",
             "Change ONLY the pose and expression: teasing denial, chin slightly up, "
             "looking down at the viewer with a small \"not yet\" smirk, the key held just "
             "out of reach. Playful, not angry."),
            ("Deepening",
             "Change ONLY the pose and expression: leaning toward the viewer, intense "
             "half-lidded seductive gaze drawing you in, the key dangled forward on its "
             "chain. Hypnotic."),
        ],
    },
}

# Instruction prefix for image->image pose variations (poses 2-4).
EDIT_PREFIX = (
    "Using the provided reference image of this exact character, keep the SAME woman, "
    "SAME face and hair, SAME latex outfit and brass key, SAME color palette and SAME "
    "lighting. Keep the same framing, scale and flat background. "
)


# ----------------------------------------------------------------------------
# KEY LOADING (value never printed)
# ----------------------------------------------------------------------------

def _parse_env_file(path: Path) -> dict:
    out = {}
    try:
        for raw in path.read_text(encoding="utf-8", errors="ignore").splitlines():
            line = raw.strip()
            if not line or line.startswith("#") or "=" not in line:
                continue
            k, v = line.split("=", 1)
            out[k.strip()] = v.strip().strip('"').strip("'")
    except Exception:
        pass
    return out


def load_api_key(explicit_env: str | None) -> tuple[str | None, str]:
    """Return (key, source_description). Never returns/prints the key value."""
    candidates = []
    if explicit_env:
        candidates.append(Path(explicit_env))
    candidates += ENV_CANDIDATES

    for p in candidates:
        if p.is_file():
            data = _parse_env_file(p)
            for name in KEY_NAMES:
                if data.get(name):
                    return data[name], f"{name} from {p}"

    for name in KEY_NAMES:
        v = os.environ.get(name)
        if v:
            return v, f"{name} from process environment"

    return None, "not found"


# ----------------------------------------------------------------------------
# POST-PROCESSING
# ----------------------------------------------------------------------------

def cutout_with_rembg(img):
    from rembg import remove
    return remove(img)  # returns RGBA with subject isolated


def cutout_with_pil(img):
    """Fallback cutout: flood-fill transparency from the 4 corners assuming a
    near-uniform background. Good enough for the clean flat bg we prompt for."""
    from PIL import Image, ImageDraw
    img = img.convert("RGBA")
    w, h = img.size
    px = img.load()
    # Sample corner color as background reference.
    bg = px[0, 0]
    tol = 32

    def close(a, b):
        return all(abs(a[i] - b[i]) <= tol for i in range(3))

    # BFS flood fill from all border pixels matching bg -> alpha 0.
    from collections import deque
    seen = [[False] * w for _ in range(h)]
    dq = deque()
    for x in range(w):
        for y in (0, h - 1):
            dq.append((x, y))
    for y in range(h):
        for x in (0, w - 1):
            dq.append((x, y))
    while dq:
        x, y = dq.popleft()
        if x < 0 or y < 0 or x >= w or y >= h or seen[y][x]:
            continue
        seen[y][x] = True
        r, g, b, a = px[x, y]
        if not close((r, g, b), bg):
            continue
        px[x, y] = (r, g, b, 0)
        dq.extend([(x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1)])
    return img


def crop_to_alpha(img, threshold=ALPHA_THRESHOLD):
    from PIL import Image
    img = img.convert("RGBA")
    alpha = img.getchannel("A")
    # Treat near-transparent as transparent for a tight bbox.
    mask = alpha.point(lambda a: 255 if a > threshold else 0)
    bbox = mask.getbbox()
    return img.crop(bbox) if bbox else img


def resize_to_height(img, height=TARGET_HEIGHT):
    from PIL import Image
    w, h = img.size
    if h == 0:
        return img
    new_w = max(1, round(w * height / h))
    return img.resize((new_w, height), Image.LANCZOS)


def post_process(raw_bytes, use_rembg=True, target_height=TARGET_HEIGHT):
    from PIL import Image
    img = Image.open(io.BytesIO(raw_bytes)).convert("RGBA")
    if use_rembg:
        try:
            img = cutout_with_rembg(img)
        except Exception as e:
            print(f"  [warn] rembg failed ({e}); using PIL flood-fill fallback")
            img = cutout_with_pil(img)
    else:
        img = cutout_with_pil(img)
    img = crop_to_alpha(img)
    img = resize_to_height(img, target_height)
    return img


def post_process_tile(raw_bytes, size=TILE_SIZE, fit="cover"):
    """Feature-tile post-process: NO background removal (the dark bg IS the tile).
    Output an exact size x size square, flattened to RGB to match the base
    Resources/features/*.png tiles (1024x1024 RGB, opaque).

    fit="cover" (default): cover-fit then centre-crop (fills the square, may crop edges).
    fit="contain": fit the WHOLE image inside the square and pad with black — used when
    the generated art is a tall figure that must not be cropped at head/feet. The pad is
    invisible against the deep-black tile background.
    """
    from PIL import Image
    img = Image.open(io.BytesIO(raw_bytes)).convert("RGB")
    w, h = img.size
    if fit == "contain":
        scale = min(size / w, size / h)
        nw, nh = max(1, round(w * scale)), max(1, round(h * scale))
        img = img.resize((nw, nh), Image.LANCZOS)
        canvas = Image.new("RGB", (size, size), (0, 0, 0))
        canvas.paste(img, ((size - nw) // 2, (size - nh) // 2))
        return canvas
    scale = max(size / w, size / h)
    nw, nh = max(1, round(w * scale)), max(1, round(h * scale))
    img = img.resize((nw, nh), Image.LANCZOS)
    left, top = (nw - size) // 2, (nh - size) // 2
    return img.crop((left, top, left + size, top + size))


# ----------------------------------------------------------------------------
# GENERATION
# ----------------------------------------------------------------------------

def extract_image_bytes(response):
    """Pull the first inline image payload out of a google-genai response."""
    for cand in getattr(response, "candidates", []) or []:
        content = getattr(cand, "content", None)
        if not content:
            continue
        for part in getattr(content, "parts", []) or []:
            inline = getattr(part, "inline_data", None)
            if inline and getattr(inline, "data", None):
                return inline.data
    return None


def generate_batch(batch_key, model, api_key, use_rembg, dry_run, poses_filter=None):
    if batch_key not in BATCHES:
        sys.exit(f"Unknown batch '{batch_key}'. Available: {', '.join(BATCHES)}")
    cfg = BATCHES[batch_key]
    out_dir = OUTPUT_ROOT / cfg["subfolder"]
    out_dir.mkdir(parents=True, exist_ok=True)

    # Optional art-style anchor image, prepended to every generation call.
    style_ref_path = cfg.get("style_ref")
    style_instruction = cfg.get("style_instruction", "") if style_ref_path else ""
    style_ref_bytes = None
    if style_ref_path:
        sp = Path(style_ref_path)
        if not sp.is_file():
            sys.exit(f"style_ref image not found: {sp}")
        style_ref_bytes = sp.read_bytes()

    # Optional character reference (charref mode): an existing render passed back
    # image->image for EVERY pose so the same character+style is preserved.
    char_ref_path = cfg.get("char_ref")
    char_ref_bytes = None
    if char_ref_path:
        cp = Path(char_ref_path)
        if not cp.is_file():
            sys.exit(f"char_ref image not found: {cp}")
        char_ref_bytes = cp.read_bytes()

    # Build the 4 full prompts up front (useful for --dry-run review).
    char = cfg.get("character", "")  # not needed in charref mode
    style = cfg.get("style", STYLE_SUFFIX)  # per-batch style override
    # "reference" (default): pose1 text->image master, poses 2-4 image->image.
    # "independent": every pose text->image with the full invariant character
    #                description (use when image->image is blocked/unreliable).
    mode = cfg.get("consistency", "reference")
    edit_prefix = cfg.get("edit_prefix", EDIT_PREFIX)  # image->image instruction (i>1)
    prompts = []
    filenames = []
    for i, entry in enumerate(cfg["poses"], start=1):
        label, delta = entry[0], entry[1]
        fname = entry[2] if len(entry) > 2 else cfg.get("filename_pattern", "out{n}.png").format(n=i)
        filenames.append(fname)
        if mode == "charref":
            # Every pose is image->image from the fixed character reference.
            charref_prefix = cfg.get("charref_prefix", CHARREF_PREFIX)
            prompts.append((label, f"{charref_prefix}{delta} {style}"))
        elif mode == "independent":
            # Normalize edit-style deltas into standalone descriptions.
            d = delta.replace("Change ONLY the pose and expression:", "Her pose and expression:")
            prompts.append((label, f"{style_instruction}{char} {d} {style}"))
        elif i == 1:
            prompts.append((label, f"{style_instruction}{char} {delta} {style}"))
        else:
            # image->image from the master: use the batch edit prefix. Do NOT re-attach
            # style_instruction — it's tied to the (text->image) master's format ref.
            prompts.append((label, f"{edit_prefix}{delta} {style}"))

    def call_kind(idx):
        if mode == "charref":
            return "charref->image"
        if mode == "independent":
            return "text+styleref->image" if style_ref_bytes else "text->image"
        if idx == 1:
            return "text+styleref->image (MASTER)" if style_ref_bytes else "text->image (MASTER)"
        return "image->image from pose1"

    print(f"\n=== Batch '{batch_key}' -> {out_dir} ===")
    if style_ref_path:
        print(f"style anchor: {Path(style_ref_path).name}")
    if poses_filter:
        print(f"poses: {sorted(poses_filter)} only")
    for i, (label, p) in enumerate(prompts, start=1):
        print(f"\n[pose {i}: {label}] ({call_kind(i)})")
        print("  prompt:", p[:180] + ("..." if len(p) > 180 else ""))

    if dry_run:
        print("\n[dry-run] No API calls made. Prompts above are final.")
        return []

    from google import genai
    from google.genai import types

    client = genai.Client(api_key=api_key)
    written = []
    master_png = None  # post-processed RGBA of pose1, reused as reference

    style_ref_part = (
        types.Part.from_bytes(data=style_ref_bytes, mime_type="image/png")
        if style_ref_bytes else None
    )
    char_ref_part = (
        types.Part.from_bytes(data=char_ref_bytes, mime_type="image/png")
        if char_ref_bytes else None
    )

    for i, (label, prompt) in enumerate(prompts, start=1):
        if poses_filter and i not in poses_filter:
            continue
        print(f"\n[pose {i}: {label}] generating...")
        contents = []
        # In reference mode the style_ref seeds ONLY the master (i==1); later frames
        # are anchored to the master itself, so don't re-attach the format ref.
        if style_ref_part is not None and (mode != "reference" or i == 1):
            contents.append(style_ref_part)  # art-style / format anchor (first)
        if char_ref_part is not None:
            contents.append(char_ref_part)   # fixed character reference (charref mode)
        if mode == "reference" and i != 1:
            # Pass the master pose-1 image back as a reference (image->image).
            buf = io.BytesIO()
            master_png.save(buf, format="PNG")
            contents.append(types.Part.from_bytes(data=buf.getvalue(), mime_type="image/png"))
        contents.append(prompt)

        # Retry a few times — IMAGE_OTHER is often transient.
        data = None
        for attempt in range(3):
            resp = client.models.generate_content(model=model, contents=contents)
            data = extract_image_bytes(resp)
            if data:
                break
            cand = (getattr(resp, "candidates", []) or [None])[0]
            fr = getattr(cand, "finish_reason", None)
            print(f"  [retry {attempt}] no image (finish={fr})")
            time.sleep(2)
        if not data:
            print(f"  [error] no image returned for pose {i}; skipping")
            continue

        # Save the raw generation for audit, then post-process.
        raw_path = out_dir / f"_raw_pose{i}.png"
        raw_path.write_bytes(data)

        if cfg.get("kind") == "tile":
            processed = post_process_tile(data, cfg.get("tile_size", TILE_SIZE),
                                          fit=cfg.get("tile_fit", "cover"))
        else:
            processed = post_process(data, use_rembg=use_rembg,
                                     target_height=cfg.get("target_height", TARGET_HEIGHT))
        if i == 1:
            master_png = processed  # the reference for image->image frames

        final_name = filenames[i - 1]
        final_path = out_dir / final_name
        processed.save(final_path, TARGET_FORMAT)
        written.append((label, final_path, processed.size))
        print(f"  -> {final_path.name}  {processed.size}  (raw kept as {raw_path.name})")
        time.sleep(1)  # gentle pacing

    return written


# ----------------------------------------------------------------------------
# MAIN
# ----------------------------------------------------------------------------

def main():
    ap = argparse.ArgumentParser(description="CCP avatar/asset generator (nano banana).")
    ap.add_argument("--batch", default="kept", help="batch key (default: kept)")
    ap.add_argument("--model", default=DEFAULT_MODEL, help="Gemini image model id")
    ap.add_argument("--env-file", default=None, help="explicit .env path")
    ap.add_argument("--dry-run", action="store_true", help="build prompts, no API calls")
    ap.add_argument("--no-rembg", action="store_true", help="skip rembg; use PIL fallback")
    ap.add_argument("--poses", default=None,
                    help="comma list of pose numbers to generate, e.g. '1' or '1,2,3,4' (default: all)")
    args = ap.parse_args()

    poses_filter = None
    if args.poses:
        poses_filter = {int(x) for x in args.poses.split(",") if x.strip()}

    key, source = load_api_key(args.env_file)
    if args.dry_run:
        print(f"[key] {('found (' + source + ')') if key else 'NOT FOUND — dry-run ok'}")
        generate_batch(args.batch, args.model, key, not args.no_rembg, dry_run=True,
                       poses_filter=poses_filter)
        return

    if not key:
        sys.exit(
            "ERROR: No Gemini API key found.\n"
            f"  Looked for {', '.join(KEY_NAMES)} in: "
            + "; ".join(str(p) for p in ENV_CANDIDATES)
            + " and the process environment.\n"
            "  Create a .env with GEMINI_API_KEY=... or pass --env-file PATH, "
            "or set the env var, then re-run."
        )
    print(f"[key] found ({source})")
    print(f"[model] {args.model}")

    written = generate_batch(args.batch, args.model, key, not args.no_rembg, dry_run=False,
                             poses_filter=poses_filter)

    print("\n=== SUMMARY ===")
    print(f"Format matched: {TARGET_FORMAT} RGBA, transparent bg, height={TARGET_HEIGHT}px, tight width crop")
    for label, path, size in written:
        print(f"  {label:10} {path}  {size}")
    if not written:
        print("  (no files written)")


if __name__ == "__main__":
    main()
