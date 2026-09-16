#!/usr/bin/env python3
"""
Locked-mode ACHIEVEMENT art — re-skin the default global achievement badges into the
Locked palette. Each achievement keeps its MEANING and any numbers/stats (global unlock
conditions are fixed); ONLY the art is re-skinned and the flavor text translated.

Three render modes:
  SYMBOL  -> neon line-art icon on deep near-black + short baked neon text, NO figure.
  CREST   -> ornate magenta heraldic seal/crest (level # + rank word, lock/chain/laurel),
             NO figure (rank trophies).
  MOMMY   -> feature our Locked character; anchored to the locked_character master.

Reuses the gen_avatars + gen_locked_features pipeline (key load, nano-banana call, image
extraction, the square character reference, the painted-character style language).

Output matches EACH source file's exact dimensions + colour mode (mostly 1024x1024 RGB;
docile_cow 1871x1851 RGBA; BrainwashedSlavedoll 512 RGB; PlatinumPuppet 512 RGBA). The
RGBA sources are full-frame opaque badges, so we save full-opaque RGBA (cover-fit fill,
no transparency cutout).

Usage:
    python gen_locked_achievements.py                       # all
    python gen_locked_achievements.py --only locked_in.png,lv_10.png
    python gen_locked_achievements.py --dry-run
"""

from __future__ import annotations

import argparse
import io
import sys
import time
from pathlib import Path

from gen_avatars import load_api_key, extract_image_bytes, DEFAULT_MODEL, SCRIPT_DIR
from gen_locked_features import build_refs, SQ_REF, PREFIX_SQ, STYLE_SQ, cover_fit

OUT_DIR = SCRIPT_DIR / "output" / "locked_achievements"

SYMBOL, CREST, MOMMY = "SYMBOL", "CREST", "MOMMY"

# ---- shared style language ----
SYMBOL_STYLE = (
    " Render as a premium hypno-achievement BADGE: glowing neon-tube line-art on a deep "
    "near-black background, hot magenta-pink neon with subtle red glitch accents and a soft "
    "bloom. Centered 1:1 square composition, the icon large and clear with margin, NO "
    "character / woman / figure. No UI chrome, no app title bar, no question-mark / help "
    "icon, no watermark, no border frame around the badge."
)
CREST_STYLE = (
    " Render as an ornate circular heraldic SEAL / crest badge: a glowing magenta-pink neon "
    "ring with filigree, framing the central motif, on a deep near-black background with "
    "subtle red glitch accents and soft bloom. Premium rank-trophy look. Centered 1:1 square "
    "composition, NO character / woman / figure inside — only the emblem. No UI chrome, no "
    "watermark, no border frame outside the seal."
)


def _text_clause(text: str, spell: str = "") -> str:
    """Build a strict baked-text instruction (spelling matters)."""
    extra = f" {spell}" if spell else ""
    return (
        f' The ONLY baked text is "{text}", in bold clean neon capitals, spelled EXACTLY '
        f'as written, all correctly spelled and fully legible, not overlapping the icon. '
        f'Do NOT add, drop, or duplicate any letters, and do NOT repeat the caption. '
        f'If it needs two lines, STACK the words; never draw a slash "/" character.{extra}'
    )


# ---- the asset list: (out_name, (W,H), kind, save_mode, concept, baked_text) ----
ASSETS = [
    # ---------------- SYMBOLS (1024 RGB unless noted) ----------------
    ("10_hours_pink.png", (1024, 1024), SYMBOL, "RGB",
     "A glowing magenta screen / lens with a clock face on it, marking long hours bathed in pink", "10 HOURS CRIMSON"),
    ("best_friends.png", (1024, 1024), SYMBOL, "RGB",
     "Two neon hearts joined together by a single chain link between them", "BEST FRIENDS"),
    ("blink_and_youll_miss_it.png", (1024, 1024), SYMBOL, "RGB",
     "A single neon eye with long lashes, mid-blink", "BLINK AND YOU'LL MISS IT / 100"),
    ("clean_slate.png", (1024, 1024), SYMBOL, "RGB",
     "A pair of windshield wipers sweeping clean across a glowing screen, leaving it blank", "CLEAN SLATE"),
    ("community_supported.png", (1024, 1024), SYMBOL, "RGB",
     "Two cupped neon hands holding up a glowing heart together", "COMMUNITY SUPPORTED"),
    ("corner_hit.png", (1024, 1024), SYMBOL, "RGB",
     "A bright magenta star/burst popping in the corner of a screen frame (a DVD-logo corner hit)", "CORNER HIT!"),
    ("curator.png", (1024, 1024), SYMBOL, "RGB",
     "A neon shelf holding a row of small framed mod thumbnails", "CURATOR / 10 MODS"),
    ("daily_maintenance.png", (1024, 1024), SYMBOL, "RGB",
     "A neon calendar page with a small padlock icon on it", "DAILY MAINTENANCE"),
    ("deep_sleep.png", (1024, 1024), SYMBOL, "RGB",
     "A crescent moon with floating Z-Z-Z and a small clock, deep restful sleep", "DEEP SLEEP / 3+ HRS"),
    ("directors_cut.png", (1024, 1024), SYMBOL, "RGB",
     "A neon film clapperboard with a star above it", "DIRECTOR'S CUT"),
    ("dont_look_away.png", (1024, 1024), SYMBOL, "RGB",
     "A neon eye centered inside a targeting reticle / crosshair", "DON'T LOOK AWAY"),
    ("down_the_rabbit_hole.png", (1024, 1024), SYMBOL, "RGB",
     "A deep glowing magenta spiral tunnel with a small clock, pulling downward", "DOWN THE RABBIT HOLE / 25"),
    ("going_deeper.png", (1024, 1024), SYMBOL, "RGB",
     "A single bold downward-pulling magenta hypnotic spiral with a down-arrow", "GOING DEEPER"),
    ("hand_over_control.png", (1024, 1024), SYMBOL, "RGB",
     "A neon gloved hand offering / handing over a glowing key", "HAND OVER CONTROL"),
    ("hands_free.png", (1024, 1024), SYMBOL, "RGB",
     "A single neon eye set inside an ornate decorative frame", "HANDS-FREE / 50"),
    ("held_back.png", (1024, 1024), SYMBOL, "RGB",
     "A neon report card showing a big letter grade F with a DENIED stamp feel", "HELD BACK / F"),
    ("honor_roll.png", (1024, 1024), SYMBOL, "RGB",
     "A neon rosette award medal with ribbons and a star at its center", "HONOR ROLL"),
    ("how_many.png", (1024, 1024), SYMBOL, "RGB",
     "Several floating glowing magenta padlocks of different sizes", "HOW MANY? / x5"),
    ("locked_in.png", (1024, 1024), SYMBOL, "RGB",
     "A bold neon padlock with a keyhole, closed and glowing", "LOCKED IN"),
    ("look_but_dont_touch.png", (1024, 1024), SYMBOL, "RGB",
     "A glowing neon cage / barred enclosure, nothing reachable inside", "LOOK, DON'T TOUCH / STRICT LOCK"),
    ("mad_scientist.png", (1024, 1024), SYMBOL, "RGB",
     "A neon laboratory flask / beaker bubbling with glowing magenta liquid", "MAD SCIENTIST / 5 TRIGGERS"),
    ("magic_word.png", (1024, 1024), SYMBOL, "RGB",
     "A neon speech bubble with a sparkling star inside it", "MAGIC WORD"),
    ("Mathematician's_nightmare.png", (1024, 1024), SYMBOL, "RGB",
     "Floating neon numbers 1 2 3 with a small padlock and a check-mark", "COUNTER'S NIGHTMARE / 1 2 3"),
    ("mercy_beggar.png", (1024, 1024), SYMBOL, "RGB",
     "A glowing key crossed out with a big DENIED stamp", "MERCY BEGGAR / DENIED"),
    ("modder.png", (1024, 1024), SYMBOL, "RGB",
     "A neon gear/cog with a magnifying glass over it", "MODDER"),
    ("morning_glory.png", (1024, 1024), SYMBOL, "RGB",
     "A neon ringing alarm clock", "MORNING GLORY / 6:00AM"),
    ("Neon_obsession.png", (1024, 1024), SYMBOL, "RGB",
     "A neon mouse cursor/arrow with a click burst around it", "OBSESSION / x20"),
    ("needy_doll.png", (1024, 1024), SYMBOL, "RGB",
     "A glowing neon rag-doll / marionette figure hugging a bright heart, surrounded by many "
     "small click-cursor arrows and tap ripples all reaching toward it", "NEEDY DOLL / 150"),
    ("not_a_video_editor.png", (1024, 1024), SYMBOL, "RGB",
     "A neon film clapperboard with a play triangle", "NOT A VIDEO EDITOR"),
    ("obedience_reflex.png", (1024, 1024), SYMBOL, "RGB",
     "A neon stopwatch beside a small speech bubble", "OBEDIENCE REFLEX / 'I OBEY'"),
    ("on_rails.png", (1024, 1024), SYMBOL, "RGB",
     "Neon train tracks receding to a glowing vanishing point", "ON RAILS"),
    ("on_the_shelf.png", (1024, 1024), SYMBOL, "RGB",
     "A neon laptop / screen glowing with an upgrade arrow", "ON THE SHELF / ENHANCE"),
    ("pavlov.png", (1024, 1024), SYMBOL, "RGB",
     "A single neon hand-bell, ringing", "PAVLOV / 500"),
    ("permanent_resident.png", (1024, 1024), SYMBOL, "RGB",
     "A small neon barred cell with a padlock and a clock", "PERMANENT RESIDENT / 10 HRS"),
    ("pillow_talk.png", (1024, 1024), SYMBOL, "RGB",
     "Two overlapping neon chat speech bubbles", "PILLOW TALK / 100"),
    ("player_2_disconnected.png", (1024, 1024), SYMBOL, "RGB",
     "A neon keyboard with the Alt and Tab keys crossed out", "PLAYER 2 DISCONNECTED / NO ALT+TAB"),
    ("pop_the_Thought.png", (1024, 1024), SYMBOL, "RGB",
     "Glowing magenta padlock-bubbles, one of them bursting/popping", "POP THE THOUGHT / 1000"),
    ("bubble_pop.png", (1024, 1024), SYMBOL, "RGB",
     "Glowing magenta soap bubbles floating upward, the largest one in the center bursting "
     "into a bright sparkle pop burst", "POP!"),
    ("puppet_strings.png", (1024, 1024), SYMBOL, "RGB",
     "A neon marionette control bar with strings dangling down", "PUPPET STRINGS / 100"),
    ("relapse.png", (1024, 1024), SYMBOL, "RGB",
     "A big red-magenta panic button with a small timer beside it", "RELAPSE / PANIC (ESC)"),
    ("retinal_burn.png", (1024, 1024), SYMBOL, "RGB",
     "A neon eye with a bright flash burst in front of it", "RETINAL BURN / 5000 FLASHES"),
    ("Sofa_decor.png", (1024, 1024), SYMBOL, "RGB",
     "A neon sofa/couch with a small padlock on it (you are the furniture)", "SOFA DECOR"),
    ("spiral_eyes.png", (1024, 1024), SYMBOL, "RGB",
     "A pair of hypnotic magenta spiral eyes motif", "SPIRAL EYES / 20 MIN"),
    ("system_overload.png", (1024, 1024), SYMBOL, "RGB",
     "A glowing magenta glitch / overload burst, fracturing energy", "SYSTEM OVERLOAD"),
    ("teachers_pet.png", (1024, 1024), SYMBOL, "RGB",
     "A neon apple sitting on a small stack of books", "TEACHER'S PET / 25"),
    ("throw_away_the_key.png", (1024, 1024), SYMBOL, "RGB",
     "A neon padlock with its key tossed away, arcing off to the side", "THROW AWAY THE KEY"),
    ("top_of_the_class.png", (1024, 1024), SYMBOL, "RGB",
     "A neon graduation cap with a glowing A+", "TOP OF THE CLASS / A+ 100%"),
    ("total_lockdown.png", (1024, 1024), SYMBOL, "RGB",
     "A fully sealed glowing neon cage, locked tight, nothing inside", "TOTAL LOCKDOWN"),
    ("typing_tutor.png", (1024, 1024), SYMBOL, "RGB",
     "A neon keyboard with a small speech bubble above it", "TYPING TUTOR / 'I OBEY' 0 TYPOS"),
    ("What_panic_button.png", (1024, 1024), SYMBOL, "RGB",
     "A neon ESC key / panic button crossed out", "WHAT PANIC BUTTON? / ESC"),
    ("wired_in.png", (1024, 1024), SYMBOL, "RGB",
     "A neon webcam emitting glowing connection / signal waves", "WIRED IN"),

    # ---------------- CRESTS (rank seals, NO figure) ----------------
    ("BrainwashedSlavedoll.png", (512, 512), CREST, "RGB",
     "Central motif: a hypnotic spiral fused with a padlock", "LVL 125 / BRAINWASHED GOON"),
    ("docile_cow.png", (1871, 1851), CREST, "RGBA",
     "Central motif: a collar with a hanging chain", "LVL 75 / KEPT PET"),
    ("Dumb_Bimbo.png", (1024, 1024), CREST, "RGB",
     "Central motif: a drained, emptied hypnotic spiral", "LVL 20 / EMPTY BETA"),
    ("lv_10.png", (1024, 1024), CREST, "RGB",
     "Central motif: a closed padlock", "LVL 10 / KEPT"),
    ("lv_50.png", (1024, 1024), CREST, "RGB",
     "Central motif: a branded padlock with a glowing brand mark", "LVL 50 / OWNED"),
    ("perfect_plastic_puppet.png", (1024, 1024), CREST, "RGB",
     "Central motif: marionette strings descending onto a padlock", "LVL 100 / PERFECT KEPT TOY"),
    ("PlatinumPuppet.png", (512, 512), CREST, "RGBA",
     "Central motif: a marionette control bar with strings", "LVL 150 / PLATINUM PUPPET"),

    # ---------------- MOMMY (our Locked character) ----------------
    ("pleased_to_meet_you.png", (1024, 1024), MOMMY, "RGB",
     "She faces the viewer with a warm, knowing half-smile and gives a small inviting wave / "
     "greeting gesture with one gloved hand, welcoming you. Two glowing neon speech bubbles "
     "with a little heart float beside her.", "PLEASED TO MEET YOU"),
    ("she_remembers.png", (1024, 1024), MOMMY, "RGB",
     "She taps one gloved fingertip to her temple with a knowing, slightly smug look — she "
     "remembers everything about you. A glowing neon brain-with-heart and a small chat bubble "
     "float beside her head.", "SHE REMEMBERS"),
]


# Per-asset spelling reinforcement for words nano-banana keeps mangling.
SPELL_HINTS = {
    "curator.png": 'The first word reads CURATOR — letters in order C, U, R, A, T, O, R — '
                   'seven letters total with only one U near the start.',
    "dont_look_away.png": 'Exactly the three words DON\'T LOOK AWAY, no extra words.',
    "she_remembers.png": 'The caption is the two words SHE REMEMBERS; the verb REMEMBERS ends '
                         'in the letters B-E-R-S; it is NOT "REMEMBRES" — do not drop the E '
                         'before the final R-S.',
}


def build_prompt(kind: str, concept: str, text: str, name: str = "") -> str:
    spell = SPELL_HINTS.get(name, "")
    if kind == SYMBOL:
        return f"{concept}.{SYMBOL_STYLE}{_text_clause(text, spell)}"
    if kind == CREST:
        return (f"{concept}. Also incorporate a lock / chain / laurel motif into the seal."
                f"{CREST_STYLE} The level number and rank word appear in neon on the ring / "
                f"banner of the seal." + _text_clause(text, spell))
    # MOMMY -> painted character, reuse features language
    return (f"{PREFIX_SQ}{concept}{STYLE_SQ}"
            f" A neon caption across the BOTTOM reads exactly \"{text}\", bold and fully "
            f"legible, correctly spelled, clear of her body. Do NOT add or drop any letters. "
            f"{spell}")


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
    print(f"[assets] {len(assets)} of {len(ASSETS)}")
    OUT_DIR.mkdir(parents=True, exist_ok=True)

    if args.dry_run:
        for name, dims, kind, smode, concept, text in assets:
            print(f"\n[{name}] {dims} {kind} {smode}")
            print("   ", build_prompt(kind, concept, text, name)[:160], "...")
        print(f"\n[dry-run] {len(assets)} assets. No API calls.")
        return

    if not key:
        sys.exit("No API key found.")

    # Square character reference (only needed for MOMMY assets).
    need_mommy = any(a[2] == MOMMY for a in assets)
    sq_bytes = None
    if need_mommy:
        build_refs()
        sq_bytes = SQ_REF.read_bytes()
        print(f"[refs] built {SQ_REF.name}")

    from google import genai
    from google.genai import types
    client = genai.Client(api_key=key)

    written, failed = [], []
    for name, dims, kind, smode, concept, text in assets:
        prompt = build_prompt(kind, concept, text, name)
        if kind == MOMMY:
            contents = [types.Part.from_bytes(data=sq_bytes, mime_type="image/png"), prompt]
        else:
            contents = [prompt]  # text-only -> square default, no figure
        print(f"\n[{name}] {dims} {kind} generating...")

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
        img = cover_fit(data, dims, smode)  # RGB->RGBA gives full-opaque alpha
        img.save(OUT_DIR / name)
        written.append((name, img.size, img.mode))
        print(f"  -> {name}  {img.size} {img.mode}")
        time.sleep(1)

    print("\n=== SUMMARY ===")
    for name, sz, md in written:
        print(f"  {name:34} {sz} {md}")
    if failed:
        print("  FAILED:", ", ".join(failed))


if __name__ == "__main__":
    main()
