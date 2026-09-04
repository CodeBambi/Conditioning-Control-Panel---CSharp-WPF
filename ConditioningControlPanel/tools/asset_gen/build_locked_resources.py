"""
Build LockedMod/locked-resources.ccpmod — a RESOURCES-ONLY bundle for the
built-in "Locked" mode (Circe).

Unlike DroneMod/build_mod.py this writes NO mod.json: the manifest stays in
code (Models/BuiltInMods.cs -> CreateLocked()). ModService extracts this bundle
and points Locked's InstalledPath at it purely so the art + voicelines resolve
via ModResourceResolver / CompanionPhraseService instead of the baseline.

Policy: ship ONLY real Locked art. Slots we don't have fall back to the base
asset set naturally (cleaner than stamping a logo placeholder over every gap).
No giggle/pop/trigger SFX this pass (intentional base fallback).

Run:  python build_locked_resources.py
Out:  ConditioningControlPanel/LockedMod/locked-resources.ccpmod  (entries rooted at resources/...)
"""

import os
import shutil
import zipfile

ASSET_GEN = os.path.dirname(os.path.abspath(__file__))                 # .../ConditioningControlPanel/tools/asset_gen
OUTPUT = os.path.join(ASSET_GEN, "output")
CCP = os.path.normpath(os.path.join(ASSET_GEN, "..", "..", "..", "ConditioningControlPanel"))
LOCKEDMOD = os.path.join(CCP, "LockedMod")
BUNDLE = os.path.join(LOCKEDMOD, "locked-resources.ccpmod")

BUILD = os.path.join(ASSET_GEN, "build_locked")
RES = os.path.join(BUILD, "resources")

# New Circe voice (LEnmbrrxYsUYS7vsRRwD). Old voice corpus stashed at
# C:\Projects\ccp-trailer\VoicelinesKept\_flat (voice eVItLK1UvXctxuaRV2Oq).
VOICELINES_FLAT = r"C:\Projects\ccp-trailer\VoicelinesKept_LEnmbrrx\_flat"

IMG_EXTS = (".png", ".gif", ".jpg", ".jpeg")

# Root-level UI files we pull from locked_modassets (everything else there —
# bubble2*, bubble3 — is an unused experimental variant the app never requests).
MODASSET_ROOT = [
    "bubble.png", "tube.png", "tube2.png",
    "logo.png", "logo2.png", "preview.png",
    "speechbubble1.png", "speechbubble2.png",
]

# Folder -> resources/ subfolder (None = resources root). Files copied verbatim
# by filename (Locked generators already used the base app slot names).
FOLDER_DEST = {
    "locked_avatars":      None,            # avatar_pose1.png, avatar2_pose1.png, ...
    "locked_skills":       "skills",
    "locked_achievements": "achievements",
    "locked_cards":        "Cards",
    "locked_features":     "features",
}

stats = {}


def is_asset(name):
    n = name.lower()
    return n.endswith(IMG_EXTS) and "_raw" not in n


def copy_folder(src_folder, dest_sub):
    src = os.path.join(OUTPUT, src_folder)
    dst_dir = RES if dest_sub is None else os.path.join(RES, dest_sub)
    os.makedirs(dst_dir, exist_ok=True)
    n = 0
    if os.path.isdir(src):
        for f in sorted(os.listdir(src)):
            if is_asset(f):
                shutil.copy2(os.path.join(src, f), os.path.join(dst_dir, f))
                n += 1
    stats[src_folder] = n
    return n


def main():
    if os.path.exists(BUILD):
        shutil.rmtree(BUILD)
    os.makedirs(RES, exist_ok=True)
    os.makedirs(LOCKEDMOD, exist_ok=True)

    # 1. Folder-mapped art (avatars / skills / achievements / cards / features)
    for folder, sub in FOLDER_DEST.items():
        copy_folder(folder, sub)

    # 2. Core UI assets from locked_modassets (root level)
    ma = os.path.join(OUTPUT, "locked_modassets")
    ui_n = 0
    for name in MODASSET_ROOT:
        src = os.path.join(ma, name)
        if os.path.exists(src):
            shutil.copy2(src, os.path.join(RES, name))
            ui_n += 1
        else:
            print(f"  WARN: missing modasset {name}")
    stats["locked_modassets (ui root)"] = ui_n

    # 3. spiral.gif at BOTH paths the app requests (OverlayService root +
    #    SessionEngine spirals/).
    spiral_src = os.path.join(ma, "spiral.gif")
    spiral_n = 0
    if os.path.exists(spiral_src):
        shutil.copy2(spiral_src, os.path.join(RES, "spiral.gif"))
        os.makedirs(os.path.join(RES, "spirals"), exist_ok=True)
        shutil.copy2(spiral_src, os.path.join(RES, "spirals", "spiral.gif"))
        spiral_n = 2
    else:
        print("  WARN: missing spiral.gif")
    stats["spiral.gif (root + spirals/)"] = spiral_n

    # 4. Voicelines -> sounds/flashes_audio (filename stem = phrase text)
    vl_dst = os.path.join(RES, "sounds", "flashes_audio")
    os.makedirs(vl_dst, exist_ok=True)
    vl_n = 0
    if os.path.isdir(VOICELINES_FLAT):
        for f in os.listdir(VOICELINES_FLAT):
            if f.lower().endswith(".mp3"):
                shutil.copy2(os.path.join(VOICELINES_FLAT, f), os.path.join(vl_dst, f))
                vl_n += 1
    else:
        print(f"  WARN: voiceline folder not found: {VOICELINES_FLAT}")
    stats["voicelines (flashes_audio)"] = vl_n

    # 4b. Idle hums (Circe) -> sounds/ as giggle1-8.mp3, replacing the base giggle
    #     SFX the app plays for idle/preset bubbles (giggle1-4) and AI replies (5-8).
    hums_src = os.path.join(OUTPUT, "locked_sounds")
    snd_dst = os.path.join(RES, "sounds")
    os.makedirs(snd_dst, exist_ok=True)
    hum_n = 0
    if os.path.isdir(hums_src):
        for f in os.listdir(hums_src):
            if f.lower().endswith((".mp3", ".wav")):
                shutil.copy2(os.path.join(hums_src, f), os.path.join(snd_dst, f))
                hum_n += 1
    stats["idle hums (sounds/giggle*)"] = hum_n

    # 5. Zip — entries rooted at resources/... (no mod.json)
    if os.path.exists(BUNDLE):
        os.remove(BUNDLE)
    with zipfile.ZipFile(BUNDLE, "w", zipfile.ZIP_DEFLATED) as zf:
        for root, _dirs, files in os.walk(BUILD):
            for f in files:
                full = os.path.join(root, f)
                arc = os.path.relpath(full, BUILD)   # -> resources\...
                zf.write(full, arc)

    size_mb = os.path.getsize(BUNDLE) / (1024 * 1024)

    print("\n" + "=" * 52)
    print("LOCKED RESOURCES BUNDLE")
    print("=" * 52)
    for k, v in stats.items():
        print(f"  {k:34} {v}")
    total = sum(stats.values())
    print("-" * 52)
    print(f"  {'TOTAL files staged':34} {total}")
    print(f"\n  Bundle: {BUNDLE}")
    print(f"  Size:   {size_mb:.1f} MB")
    print("\n  Note: idle giggles use Circe hums (sounds/giggle1-8). Pop/trigger")
    print("  SFX still fall back to the base asset set.")


if __name__ == "__main__":
    main()
