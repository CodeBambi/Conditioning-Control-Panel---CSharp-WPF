"""
Generate ElevenLabs TTS audio for the Locked mode (Circe's voice).

Two modes:
  test  -> renders 10 fixed sample lines into OUTPUT_DIR (quick voice check)
  full  -> renders the COMPLETE Locked corpus, parsed live from
           Models/BuiltInMods.cs -> CreateLocked(), mirroring the selection
           and cleaning logic of DroneMod/generate_voicelines.py.

Usage:
  python generate_locked_voicelines.py            # full corpus (default)
  python generate_locked_voicelines.py full
  python generate_locked_voicelines.py test

The API key is read from ENV_FILE and is NEVER printed, logged, or written.
No post-processing, no audio FX. Raw ElevenLabs output only.
"""

import os
import re
import shutil
import sys
import time

# ---------------------------------------------------------------------------
# CONFIG (editable: tune and re-run)
# ---------------------------------------------------------------------------
ENV_FILE = r"C:\Projects\ccp-trailer\.env"
# Active corpus dir = the new Circe voice (LEnmbrrxYsUYS7vsRRwD). The previous
# voice's full corpus is stashed intact at C:\Projects\ccp-trailer\VoicelinesKept
# (voice eVItLK1UvXctxuaRV2Oq) — kept for easy revert.
OUTPUT_DIR = r"C:\Projects\ccp-trailer\VoicelinesKept_LEnmbrrx"
VOICE_ID = "LEnmbrrxYsUYS7vsRRwD"      # was: eVItLK1UvXctxuaRV2Oq (old, stashed)
MODEL_ID = "eleven_multilingual_v2"
STABILITY = 0.4
SIMILARITY_BOOST = 0.75
STYLE = 0.3
USE_SPEAKER_BOOST = True

# Source for the full corpus (read live so it matches the code exactly).
BUILTINMODS_CS = os.path.join(
    os.path.dirname(os.path.abspath(__file__)),
    "..", "..", "Models", "BuiltInMods.cs",
)

# Phrase categories that are NOT voiced (mirrors Drone intent: the "thinking"
# filler is internal, never spoken aloud). Locked's Thinking lines are
# parenthetical, so they would not strip to empty on their own.
EXCLUDED_PHRASE_CATEGORIES = {"Thinking"}

# Batch politeness.
CALL_DELAY_SECONDS = 0.35
MAX_429_RETRIES = 4

# Activity lines (those containing "{0}") read awkwardly when the placeholder
# becomes the literal app name. For TTS audio only we use natural wording; the
# FILENAME still derives from the "target application" substitution so the app's
# audio-by-filename resolution is unchanged. Default for any "{0}" line is "that".
# These exact source lines get bespoke wording instead (keyed by raw source text):
ACTIVITY_OVERRIDES = {
    "All that focus on {0}. Imagine giving it to me instead.":
        "All that focus on the screen. Imagine giving it to me instead.",
    "Every minute in {0} is a minute you're not sinking. Fix that.":
        "Every minute in there is a minute you're not sinking. Fix that.",
    "Lost on {0}? Let me bring you back down.":
        "Lost out there? Let me bring you back down.",
    "Shopping on {0}? Buy something I'd like to see you in.":
        "Shopping again? Buy something I'd like to see you in.",
    "{0}? Spoil yourself, then come spoil me with your attention.":
        "Shopping again? Spoil yourself, then come spoil me with your attention.",
    "Treat yourself on {0}. Then come back and be treated.":
        "Treat yourself out there. Then come back and be treated.",
    "Talking to people on {0}? They don't keep you like I do.":
        "Talking to people out there? They don't keep you like I do.",
    "{0} can have your words. I'll take everything else.":
        "They can have your words. I'll take everything else.",
    "{0} is noise. I'm the only voice you need.":
        "It's all noise. I'm the only voice you need.",
    "Finish up on {0}. I have plans for the rest of you.":
        "Finish up there. I have plans for the rest of you.",
}

# ---------------------------------------------------------------------------
# IDLE HUMS (`hums` mode) — soft contented hums/murmurs in Circe's voice that
# REPLACE the base giggle SFX for Locked. The app plays giggle1-4 for ordinary
# speech bubbles (incl. idle preset phrases) and giggle5-8 for AI replies, so we
# render all 8 slots. Written straight into the Locked asset-gen source folder;
# build_locked_resources.py copies them to resources/sounds/.
HUMS_DIR = (
    r"C:\Projects\Conditioning-Control-Panel---CSharp-WPF\ConditioningControlPanel\tools\asset_gen\output\locked_sounds"
)
HUM_LINES = [
    ("giggle1", "Mmm."),
    ("giggle2", "Mmmhmm."),
    ("giggle3", "Hmm, mmm."),
    ("giggle4", "Mm-mm."),
    ("giggle5", "Mmmm..."),
    ("giggle6", "Hmmm~"),
    ("giggle7", "Mm. Mmm."),
    ("giggle8", "Mmmmm."),
]

# ---------------------------------------------------------------------------
# TEST LINES (10 fixed samples, used by `test` mode)
# ---------------------------------------------------------------------------
TEST_LINES = [
    ("greeting",   "There you are, pet. I was starting to wonder."),
    ("startup",    "Settle in. You don't have to think anymore, that's my job now."),
    ("floating",   "Mine."),
    ("floating",   "Isn't it easier when I decide?"),
    ("bubblepop",  "Good boy. Pop."),
    ("subliminal", "CIRCE OWNS YOU"),
    ("lockcard",   "GOOD BOYS DON'T DECIDE."),
    ("trigger",    "Hush now. Circe has you."),
    ("gamefailed", "Wrong, pet. Try again for me."),
    ("enginestop", "Come up slowly. Bring my voice with you."),
]


# ---------------------------------------------------------------------------
# Key handling (value never leaves this process)
# ---------------------------------------------------------------------------
def load_api_key(env_path):
    if not os.path.isfile(env_path):
        print(f"ERROR: env file not found: {env_path}")
        sys.exit(1)

    names, pairs = [], {}
    with open(env_path, "r", encoding="utf-8-sig") as fh:
        for raw in fh:
            line = raw.strip()
            if not line or line.startswith("#") or "=" not in line:
                continue
            name, value = line.split("=", 1)
            name = name.strip()
            value = value.strip().strip('"').strip("'")
            names.append(name)
            pairs[name] = value

    if pairs.get("ELEVENLABS_API_KEY"):
        return pairs["ELEVENLABS_API_KEY"], names
    for name in names:
        if "ELEVEN" in name.upper() and pairs.get(name):
            return pairs[name], names
    return None, names


# ---------------------------------------------------------------------------
# Cleaning / filename helpers (mirror DroneMod/generate_voicelines.py exactly)
# ---------------------------------------------------------------------------
def filename_clean(text):
    """Text used to derive the FILENAME. Unchanged from the original Drone logic
    ({0} -> "target application") so the app still resolves audio by this name."""
    text = re.sub(r"\[.*?\]\s*", "", text)
    text = text.replace("{0}", "target application")
    return text.strip()


def tts_clean(text):
    """Text actually SENT to ElevenLabs. For "{0}" lines, use natural wording:
    a bespoke override if one exists, otherwise "{0}" -> "that". Non-"{0}" lines
    are identical to filename_clean (just [TAGS] stripped)."""
    stripped = re.sub(r"\[.*?\]\s*", "", text)
    if "{0}" in stripped:
        if text in ACTIVITY_OVERRIDES:
            return ACTIVITY_OVERRIDES[text].strip()
        return stripped.replace("{0}", "that").strip()
    return stripped.strip()


def safe_filename(text):
    """Filesystem-safe name; the app uses the stem as display text (Drone logic)."""
    name = text.replace(":", " -").replace("?", "").replace('"', "")
    name = name.replace("<", "").replace(">", "").replace("|", "")
    name = name.replace("/", "-").replace("\\", "-").replace("*", "")
    name = re.sub(r"\s+", " ", name).strip()
    if len(name) > 120:
        name = name[:120].strip()
    return name


def slugify(text):
    words = re.sub(r"[^a-z0-9\s]", " ", text.lower()).split()
    return "-".join(words[:4]) or "line"


# ---------------------------------------------------------------------------
# Parse CreateLocked() out of BuiltInMods.cs
# ---------------------------------------------------------------------------
_LITERAL_RE = re.compile(r'"((?:[^"\\]|\\.)*)"')


def _unescape(s):
    return s.replace('\\"', '"').replace("\\n", "\n").replace("\\t", "\t").replace("\\\\", "\\")


def _literals(segment):
    return [_unescape(m.group(1)) for m in _LITERAL_RE.finditer(segment)]


def _slice(text, start_marker, end_marker):
    a = text.index(start_marker) + len(start_marker)
    b = text.index(end_marker, a)
    return text[a:b]


def parse_locked_corpus(cs_path):
    """Return (phrases: dict[str, list[str]], extra_lines: list[(folder, text)])."""
    with open(cs_path, "r", encoding="utf-8") as fh:
        full = fh.read()

    body = full[full.index("private static ModManifest CreateLocked()"):]

    # --- Phrases ---
    phrases_region = _slice(
        body,
        "Phrases = new Dictionary<string, string[]>",
        "TextReplacements = new Dictionary<string, string>",
    )
    headers = list(re.finditer(r'\["(\w+)"\]\s*=\s*new\[\]', phrases_region))
    phrases = {}
    for i, h in enumerate(headers):
        name = h.group(1)
        chunk_end = headers[i + 1].start() if i + 1 < len(headers) else len(phrases_region)
        chunk = phrases_region[h.end():chunk_end]
        phrases[name] = _literals(chunk)

    # --- Extra lines (Drone order: Triggers, Messages, Subliminals, LockCard) ---
    extra = []

    triggers_region = _slice(body, "Triggers = new ModTriggers", "Messages = new ModMessages")
    for text in _literals(triggers_region):
        extra.append(("Triggers", text))

    messages_region = _slice(body, "Messages = new ModMessages", "Browser = new ModBrowser")
    for text in _literals(messages_region):
        extra.append(("Messages", text.split("\n")[0]))  # first line only

    subliminal_region = _slice(
        body, "SubliminalPool = new Dictionary<string, bool>",
        "LockCardPhrases = new Dictionary<string, bool>",
    )
    for text in _literals(subliminal_region):
        extra.append(("Subliminals", text))

    lockcard_region = _slice(
        body, "LockCardPhrases = new Dictionary<string, bool>",
        "CustomTriggers = new List<string>",
    )
    for text in _literals(lockcard_region):
        extra.append(("LockCard", text))

    return phrases, extra


# ---------------------------------------------------------------------------
# TTS request (shared by both modes)
# ---------------------------------------------------------------------------
def synthesize(requests, key, line):
    """Return (bytes, None) on success or (None, 'HTTP NNN') on failure."""
    url = f"https://api.elevenlabs.io/v1/text-to-speech/{VOICE_ID}"
    headers = {"xi-api-key": key, "Content-Type": "application/json"}
    body = {
        "text": line,
        "model_id": MODEL_ID,
        "voice_settings": {
            "stability": STABILITY,
            "similarity_boost": SIMILARITY_BOOST,
            "style": STYLE,
            "use_speaker_boost": USE_SPEAKER_BOOST,
        },
    }
    for attempt in range(MAX_429_RETRIES):
        try:
            resp = requests.post(url, headers=headers, json=body, timeout=90)
        except Exception as exc:
            return None, f"request error ({type(exc).__name__})"
        if resp.status_code == 200 and resp.content:
            return resp.content, None
        if resp.status_code == 429:
            time.sleep(2 * (attempt + 1))
            continue
        return None, f"HTTP {resp.status_code}"
    return None, "HTTP 429 (retries exhausted)"


# ---------------------------------------------------------------------------
# Modes
# ---------------------------------------------------------------------------
def run_test(requests, key):
    os.makedirs(OUTPUT_DIR, exist_ok=True)
    written, errors = 0, []
    for i, (category, line) in enumerate(TEST_LINES, start=1):
        nn = f"{i:02d}"
        fname = f"{nn}_{category}_{slugify(line)}.mp3"
        fpath = os.path.join(OUTPUT_DIR, fname)
        if os.path.exists(fpath):
            print(f"[{nn}] SKIP (exists) {fname}")
            written += 1
            continue
        content, err = synthesize(requests, key, line)
        if content:
            with open(fpath, "wb") as out:
                out.write(content)
            written += 1
            print(f"[{nn}] wrote {fname}")
        else:
            errors.append(f"{category}: {err}")
            print(f"[{nn}] FAILED {fname} ({err})")
        time.sleep(CALL_DELAY_SECONDS)
    print(f"\nDone: {written}/{len(TEST_LINES)} files. Output: {OUTPUT_DIR}")
    if errors:
        print("Errors:")
        for e in errors:
            print(f"  - {e}")


def run_hums(requests, key):
    """Render the 8 idle hums to HUMS_DIR as giggle1..giggle8.mp3 (overwrites)."""
    os.makedirs(HUMS_DIR, exist_ok=True)
    written, errors = 0, []
    for name, line in HUM_LINES:
        fpath = os.path.join(HUMS_DIR, name + ".mp3")
        content, err = synthesize(requests, key, line)
        if content:
            with open(fpath, "wb") as out:
                out.write(content)
            written += 1
            print(f"[hum] wrote {name}.mp3   \"{line}\"")
        else:
            errors.append(f"{name}: {err}")
            print(f"[hum] FAILED {name} ({err})")
        time.sleep(CALL_DELAY_SECONDS)
    print(f"\nDone: {written}/{len(HUM_LINES)} hums. Output: {HUMS_DIR}")
    if errors:
        print("Errors:")
        for e in errors:
            print(f"  - {e}")


def run_full(requests, key):
    phrases, extra = parse_locked_corpus(BUILTINMODS_CS)

    # Build the ordered work list: phrase categories first, then extras.
    # We keep the RAW source line so we can derive filename vs. TTS text
    # separately and detect "{0}" lines (which are force-re-rendered).
    work = []  # (folder, raw)
    for category, lines in phrases.items():
        if category in EXCLUDED_PHRASE_CATEGORIES:
            continue
        for raw in lines:
            if filename_clean(raw):
                work.append((category, raw))
    for folder, raw in extra:
        if filename_clean(raw):
            work.append((folder, raw))

    total = len(work)
    os.makedirs(OUTPUT_DIR, exist_ok=True)
    flat_dir = os.path.join(OUTPUT_DIR, "_flat")
    os.makedirs(flat_dir, exist_ok=True)
    print(f"Locked corpus: {total} lines")
    print(f"Voice: {VOICE_ID} | model: {MODEL_ID}")
    print(f"Excluded categories: {', '.join(sorted(EXCLUDED_PHRASE_CATEGORIES))}")
    print(f"Output: {OUTPUT_DIR}/  (force re-render: lines containing '{{0}}')")
    print("=" * 60)

    generated, rerendered, skipped, errors = 0, 0, 0, []
    done = 0
    for folder, raw in work:
        done += 1
        cat_dir = os.path.join(OUTPUT_DIR, folder)
        os.makedirs(cat_dir, exist_ok=True)

        fname = safe_filename(filename_clean(raw)) + ".mp3"   # NEVER changes
        fpath = os.path.join(cat_dir, fname)
        force = "{0}" in raw                                   # activity lines only

        if os.path.exists(fpath) and not force:
            skipped += 1
            continue

        tts = tts_clean(raw)
        content, err = synthesize(requests, key, tts)
        if content:
            with open(fpath, "wb") as out:
                out.write(content)
            # Keep the _flat mirror in lockstep (overwrite on re-render).
            shutil.copy2(fpath, os.path.join(flat_dir, fname))
            if force:
                rerendered += 1
                print(f"  [{done}/{total}] RE-RENDER {folder}: {tts[:55]}")
            else:
                generated += 1
                print(f"  [{done}/{total}] {folder}: {tts[:55]}")
        else:
            errors.append((folder, err, tts[:50]))
            print(f"  [{done}/{total}] FAILED {folder}: {err}")
        time.sleep(CALL_DELAY_SECONDS)

    # Flat mirror safety net (copy any category file missing from _flat; never
    # overwrites — forced re-renders were already mirrored in the loop above).
    flat_count = 0
    for root, _dirs, files in os.walk(OUTPUT_DIR):
        if "_flat" in root or os.path.abspath(root) == os.path.abspath(OUTPUT_DIR):
            continue
        for f in files:
            if f.endswith(".mp3"):
                dst = os.path.join(flat_dir, f)
                if not os.path.exists(dst):
                    shutil.copy2(os.path.join(root, f), dst)
                    flat_count += 1

    # Per-category counts on disk.
    print("\n" + "=" * 60)
    print("PER-CATEGORY FILE COUNTS")
    grand = 0
    for folder in sorted(
        {d for d in os.listdir(OUTPUT_DIR)
         if os.path.isdir(os.path.join(OUTPUT_DIR, d)) and d != "_flat"}
    ):
        n = len([f for f in os.listdir(os.path.join(OUTPUT_DIR, folder)) if f.endswith(".mp3")])
        grand += n
        print(f"  {folder:24} {n}")
    flat_total = len([f for f in os.listdir(flat_dir) if f.endswith(".mp3")])
    print("-" * 60)
    print(f"  {'GRAND TOTAL':24} {grand}")
    print(f"  {'_flat mirror':24} {flat_total}")
    print(f"\nNew: {generated} | Re-rendered ('{{0}}' lines): {rerendered} | "
          f"Skipped (existing): {skipped} | Errors: {len(errors)} | "
          f"Newly flattened: {flat_count}")
    if errors:
        print("Failures:")
        for folder, err, preview in errors:
            print(f"  - {folder}: {err}  ({preview})")


# ---------------------------------------------------------------------------
def main():
    try:
        import requests
    except ImportError:
        os.system(f'"{sys.executable}" -m pip install requests --break-system-packages')
        import requests

    key, names = load_api_key(ENV_FILE)
    if not key:
        print("No ElevenLabs key found in env file. Variable names present:")
        for n in names:
            print(f"  - {n}")
        sys.exit(1)

    mode = sys.argv[1].lower() if len(sys.argv) > 1 else "full"
    if mode == "test":
        run_test(requests, key)
    elif mode == "hums":
        run_hums(requests, key)
    else:
        run_full(requests, key)


if __name__ == "__main__":
    main()
