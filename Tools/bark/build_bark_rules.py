#!/usr/bin/env python3
"""
build_bark_rules.py — re-runnable transform: voiceline manifests -> per-mod bark_rules.json

The authored spec (`ccp_barks_<mod>_v*_spoken.md`) defines, per bark id, the
trigger / condition / priority / cooldown / scope / class / mood. That metadata is
IDENTICAL across all mods (only the lines/audio differ), so it lives here once in
RULE_TABLE. Each mod's `<mod>_manifest.json` supplies the per-variant text (`display`)
and audio (`file`). This script joins them into each mod's bark_rules.json that the
app's BarkRuleLoader picks up from Resources/sounds/companion_audio/mods/<modId>/.

Usage:
  python build_bark_rules.py --spec-dir "C:/.../New Voicelines" \
      --repo-dir "C:/.../ConditioningControlPanel" [--copy-audio]

  --copy-audio also copies each referenced mp3 into the per-mod output folder
  (85MB total across 3 mods — see the PR for the delivery decision).

Re-run after editing a manifest, adding a mod, or tweaking RULE_TABLE.
"""
import argparse, json, os, shutil, sys

# content-folder name -> built-in mod id (where the app resolves it)
MODS = {
    "bambi": "builtin-bambisleep",
    "sissy": "builtin-sissyhypno",
    "circe": "builtin-locked",   # circe content = the Locked / "Circe's Lock" built-in
    # "drone": "drone-mode",     # 4th mod — enable when its content arrives
}

# kw_reinforce + kw_offlimits are MERGED into one keyword bark (per decision).
KW_MERGE_INTO = "kw"
KW_SOURCE_IDS = ["kw_reinforce", "kw_offlimits"]

# note_reaction is NOT a bark rule — it's the logo-egg recorded clip (MainWindow.ShowEasterEgg
# -> AvatarTubeWindow.PlayNoteClip). Skip it here.
SKIP_IDS = {"note_reaction"}

# Authored from the _spoken.md tag line: id · trigger · condition · priority · cooldown · scope · mood · class
# Fields: trigger (engine key), conditions (dict, engine operator suffixes), priority,
# cooldown_ms, repeatable, scope (session|tier|lifetime), cls, mood.
# scope/repeatable encode the spec's once/session, once/life, once/tier, repeat, per-X.
R = lambda trigger, conditions, priority, cooldown_ms, repeatable, scope, cls, mood: dict(
    trigger=trigger, conditions=conditions, priority=priority, cooldown_ms=cooldown_ms,
    repeatable=repeatable, scope=scope, cls=cls, mood=mood)

RULE_TABLE = {
    # --- session lifecycle ---
    "session_start_recent":      R("SessionStarted", {"days_away_lt":3,"total_sessions_gt":1}, 400, 0, False, "session", "normal", "warm, controlling"),
    "session_start_first":       R("SessionStarted", {"total_sessions_lte":1}, 450, 0, False, "lifetime", "normal", "curious, inviting"),
    "session_start_absence":     R("SessionStarted", {"days_away_gte":3,"days_away_lt":7}, 420, 0, False, "session", "normal", "warm, knowing"),
    "session_start_long_absence":R("SessionStarted", {"days_away_gte":7}, 430, 0, False, "session", "normal", "warm, pointed"),
    "session_start_latenight":   R("SessionStarted", {"local_hour_lt":4}, 380, 0, False, "session", "normal", "soft, intimate"),
    "phase_deepen":              R("SessionPhaseChanged", {"phase_is_deepener":True}, 300, 60000, True, "session", "normal", "soft, deepening"),
    "session_complete":          R("SessionCompleted", {}, 350, 0, False, "session", "normal", "afterglow"),
    "session_stopped_early":     R("SessionStopped", {}, 200, 0, False, "session", "normal", "teasing"),  # not-panic handled by safety-hold
    # --- webcam / gaze ---
    "blink_t1":   R("Blink", {"blink_count":1}, 150, 0, False, "session", "normal", "playful"),
    "blink_t2":   R("Blink", {"blink_count":2}, 150, 0, False, "session", "normal", "playful"),
    "blink_t3":   R("Blink", {"blink_count_gte":3,"blink_count_lte":5}, 150, 0, False, "session", "normal", "teasing"),
    "blink_t4":   R("Blink", {"blink_count_gte":6,"blink_count_lte":10}, 150, 0, False, "session", "normal", "teasing, smug"),
    "blink_t5":   R("Blink", {"blink_count_gt":10}, 150, 0, False, "session", "normal", "smug, pointed"),
    "hold_success": R("LongStare", {}, 180, 15000, True, "session", "normal", "approving, deepening"),
    "gaze_reward":  R("GazePopped", {}, 160, 15000, True, "session", "normal", "approving"),
    "attention_lost":      R("FaceLost", {}, 220, 30000, True, "session", "normal", "coaxing, mild scold"),
    "attention_lost_deep": R("FaceLost", {"phase_is_deepener":True}, 320, 60000, True, "session", "normal", "sharp"),
    "mouth_open":  R("MouthOpen", {}, 140, 30000, True, "session", "normal", "teasing"),
    "tongue_out":  R("TongueOut", {}, 140, 30000, True, "session", "normal", "playful"),
    "face_found":  R("FaceFound", {}, 200, 0, True, "session", "normal", "warm"),
    "cam_denied":  R("TrackingStateChanged", {"state":"CameraDenied"}, 180, 0, False, "session", "normal", "coaxing"),
    # --- video ---
    "video_pre":   R("VideoAboutToStart", {}, 240, 0, True, "session", "normal", "anticipatory"),
    "video_start": R("VideoStarted", {}, 200, 0, True, "session", "normal", "controlling"),
    "video_check_pass": R("AttentionCheckPass", {"video_playing":True}, 220, 20000, True, "session", "normal", "approving"),
    "video_check_fail": R("AttentionCheckFail", {"video_playing":True}, 240, 20000, True, "session", "normal", "scolding-sweet"),
    "video_fail_3x":    R("AttentionCheckFail", {"video_playing":True,"fail_count_gte":3}, 260, 0, True, "session", "normal", "mock-pity"),
    "video_end":   R("VideoEnded", {}, 180, 0, True, "session", "normal", "soft"),
    # --- minigames ---
    "bubble_pop":  R("BubblePopped", {}, 100, 8000, True, "session", "normal", "sweet, rewarding"),
    "bubble_miss": R("BubbleMissed", {}, 90, 8000, True, "session", "normal", "teasing"),
    "bubble_game_done": R("BubbleCountCompleted", {}, 160, 0, True, "session", "normal", "pleased"),
    "bubble_game_fail": R("BubbleCountFailed", {}, 150, 0, True, "session", "normal", "mock-sympathy"),
    "lockcard_clean":   R("LockCardCompleted", {"mistakes":0}, 240, 0, True, "session", "normal", "proud, possessive"),
    "lockcard_sloppy":  R("LockCardCompleted", {"mistakes_gt":0}, 240, 0, True, "session", "normal", "teasing"),
    # --- visual fx ---
    "flash":      R("FlashDisplayed", {}, 110, 20000, True, "session", "normal", "light"),
    "subliminal": R("SubliminalDisplayed", {}, 110, 30000, True, "session", "normal", "soft"),
    "braindrain": R("BrainDrainTriggered", {}, 130, 45000, True, "session", "normal", "soft, smug"),
    "mindwipe":   R("MindWipeTriggered", {}, 130, 45000, True, "session", "normal", "soft, possessive"),
    # --- keywords (merged) ---
    KW_MERGE_INTO: R("KeywordTriggerFired", {}, 190, 60000, True, "session", "normal", "pleased, possessive"),
    # --- awareness / progression ---
    "still_5":  R("StillOnActivity", {"still_minutes_gte":5,"still_minutes_lt":10}, 120, 0, True, "session", "normal", "ambient"),
    "still_10": R("StillOnActivity", {"still_minutes_gte":10}, 120, 0, True, "session", "normal", "ambient, knowing"),
    "activity_change": R("ActivityChanged", {}, 100, 30000, True, "session", "normal", "knowing"),
    "xp_gain":  R("XPChanged", {}, 80, 60000, True, "session", "normal", "light"),
    "level_up": R("LevelUp", {}, 220, 0, True, "session", "normal", "possessive, playful"),
    "companion_level": R("CompanionLevelUp", {}, 200, 0, True, "session", "normal", "warm"),
    "skill_unlock": R("SkillUnlocked", {}, 190, 0, True, "session", "normal", "pleased"),
    "lucky_proc": R("LuckyProc", {}, 170, 30000, True, "session", "normal", "playful"),
    "pinkrush":   R("PinkRushStarted", {}, 180, 0, True, "session", "normal", "indulgent"),
    "achievement": R("AchievementUnlocked", {}, 200, 0, True, "session", "normal", "proud, teasing"),
    "roadmap":    R("RoadmapStepCompleted", {}, 190, 0, True, "session", "normal", "encouraging"),
    "quest_done": R("QuestCompleted", {}, 210, 0, True, "session", "normal", "pleased"),
    "quest_progress": R("QuestProgressChanged", {}, 90, 90000, True, "session", "normal", "light"),
    "quiz_done":  R("QuizCompleted", {}, 190, 0, True, "session", "normal", "pleased"),
    "mantra_done": R("MantraCompleted", {}, 200, 0, True, "session", "normal", "soft, possessive"),
    "streak_up":  R("MantraStreakChanged", {}, 210, 0, True, "session", "normal", "possessive, pleased"),
    "streak_break": R("MantraStreakBroken", {}, 220, 0, True, "session", "normal", "disappointed, coaxing"),
    # --- control / system ---
    "lockdown_on":  R("LockdownActivated", {}, 280, 0, True, "session", "normal", "possessive"),
    "lockdown_off": R("LockdownDeactivated", {}, 250, 0, True, "session", "normal", "soft"),
    "lockdown_tick":R("LockdownCountdownTick", {}, 150, 300000, True, "session", "normal", "teasing"),  # cooldown=5min so ticks don't spam
    "remote_cmd":   R("RemoteCommandReceived", {}, 200, 5000, True, "session", "normal", "responsive"),
    "remote_connect": R("ControllerConnectedChanged", {}, 220, 0, True, "session", "normal", "knowing"),
    "user_msg":     R("UserMessageSent", {}, 130, 30000, True, "session", "normal", "warm"),  # NOTE: usually gated by chat-suppression — see PR
    "memory_recall":R("PersistentMemoryRecalled", {}, 160, 60000, True, "session", "normal", "knowing"),
    "mod_change":   R("ModChanged", {}, 140, 0, True, "session", "normal", "light"),
    "idle":         R("IdleStateChanged", {"idle":True}, 110, 0, True, "session", "normal", "soft"),
    "idle_resume":  R("IdleStateChanged", {"idle":False}, 120, 0, True, "session", "normal", "warm"),
    # --- safety ---
    "safety_panic": R("Panic", {}, 1000, 0, True, "session", "safety", "gentle, caring"),
    # --- easter eggs ---
    "egg_mute":        R("FlashDisplayed", {"mute":True}, 550, 600000, False, "session", "easter_egg", "meta"),
    "egg_streaming":   R("TrackingStateChanged", {"state":"CameraInUse"}, 540, 0, False, "session", "easter_egg", "meta"),
    "egg_empty_chair": R("FaceFound", {"face_lost_sec_gte":300}, 530, 0, False, "session", "easter_egg", "meta"),
    "egg_mod_spam":    R("ModChanged", {"mod_switches_60s_gte":3}, 520, 0, False, "session", "easter_egg", "meta"),
    "egg_corner":      R("BouncingTextCorner", {}, 560, 0, False, "session", "easter_egg", "meta"),
    "egg_relaunch":    R("SessionStarted", {"instant_relaunch":True}, 540, 0, False, "session", "easter_egg", "meta"),
    "egg_marathon":    R("SessionProgress", {"session_elapsed_sec_gte":10800}, 530, 0, False, "session", "easter_egg", "meta, caring"),
    "egg_update":      R("UpdateAvailable", {}, 510, 0, False, "session", "easter_egg", "meta"),       # once/version ~= once/session
    "egg_patreon_up":  R("PatreonTierChanged", {"tier_up":True}, 540, 0, False, "tier", "easter_egg", "meta"),
    "egg_100pct":      R("AchievementUnlocked", {"achievements_all_unlocked":True}, 570, 0, False, "lifetime", "easter_egg", "meta"),
    "egg_maxlevel":    R("LevelUp", {"player_level_gte":100}, 570, 0, False, "lifetime", "easter_egg", "meta"),  # no hard cap; 100 = "max" proxy
    "egg_allskills":   R("SkillUnlocked", {"all_skills_unlocked":True}, 560, 0, False, "lifetime", "easter_egg", "meta"),
    "egg_tutorial":    R("TutorialCompleted", {}, 510, 0, False, "lifetime", "easter_egg", "meta"),
    "egg_long_long":   R("SessionStarted", {"days_away_gte":60}, 560, 0, False, "session", "easter_egg", "meta"),
    "egg_discord":     R("DiscordAuthChanged", {"authenticated":True}, 520, 0, False, "lifetime", "easter_egg", "meta"),
    "egg_tray_wake":   R("WakeBambiRequested", {}, 510, 0, False, "session", "easter_egg", "meta"),
    "egg_nye":         R("SessionStarted", {"is_nye":True}, 550, 0, False, "session", "easter_egg", "meta, soft"),
}


def load_manifest(path):
    """Return {bark_id: [ {text, audio}, ... ]} ordered by variant n."""
    with open(path, encoding="utf-8") as f:
        entries = json.load(f)
    by_id = {}
    for e in entries:
        by_id.setdefault(e["id"], []).append((e.get("n", 0), e.get("display", ""), e.get("file")))
    out = {}
    for k, vs in by_id.items():
        vs.sort(key=lambda t: t[0])
        out[k] = [{"text": d, "audio": f} for (_, d, f) in vs]
    return out


def variants_for(rule_id, manifest):
    """Variant pool for a rule id, applying the kw merge."""
    if rule_id == KW_MERGE_INTO:
        pool = []
        for sid in KW_SOURCE_IDS:
            pool.extend(manifest.get(sid, []))
        return pool
    return manifest.get(rule_id, [])


def build_rules(manifest, warn):
    rules = []
    for rule_id, meta in RULE_TABLE.items():
        pool = variants_for(rule_id, manifest)
        if not pool:
            warn(f"  no variants in manifest for rule '{rule_id}' (skipped)")
            continue
        rule = {
            "id": rule_id,
            "trigger": meta["trigger"],
            "priority": meta["priority"],
            "cooldown_ms": meta["cooldown_ms"],
            "repeatable": meta["repeatable"],
            "scope": meta["scope"],
            "mood": meta["mood"],
            "class": meta["cls"],
            "variant_pool": pool,
        }
        if meta["conditions"]:
            # insert conditions right after trigger for readability
            rule = {**{k: rule[k] for k in ("id", "trigger")},
                    "conditions": meta["conditions"],
                    **{k: rule[k] for k in ("priority", "cooldown_ms", "repeatable", "scope", "mood", "class", "variant_pool")}}
        rules.append(rule)
    return rules


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--spec-dir", required=True, help="folder with <mod>_manifest.json + <mod>/ audio")
    ap.add_argument("--repo-dir", required=True, help="ConditioningControlPanel project dir")
    ap.add_argument("--copy-audio", action="store_true", help="also copy referenced mp3s into each mod's output folder")
    args = ap.parse_args()

    known_ids = set(RULE_TABLE.keys()) | set(KW_SOURCE_IDS) | SKIP_IDS
    total_rules = 0
    for mod_name, mod_id in MODS.items():
        manifest_path = os.path.join(args.spec_dir, f"{mod_name}_manifest.json")
        audio_src = os.path.join(args.spec_dir, mod_name)
        if not os.path.exists(manifest_path):
            print(f"[skip] {mod_name}: no manifest at {manifest_path}")
            continue

        manifest = load_manifest(manifest_path)
        # surface any manifest ids we don't have a rule for (spec drift)
        for mid in manifest:
            if mid not in known_ids:
                print(f"[warn] {mod_name}: manifest id '{mid}' has no RULE_TABLE entry")

        warnings = []
        rules = build_rules(manifest, warnings.append)
        for w in warnings:
            print(f"[warn] {mod_name}:{w}")

        out_dir = os.path.join(args.repo_dir, "Resources", "sounds", "companion_audio", "mods", mod_id)
        os.makedirs(out_dir, exist_ok=True)
        out_path = os.path.join(out_dir, "bark_rules.json")
        with open(out_path, "w", encoding="utf-8", newline="\n") as f:
            json.dump(rules, f, ensure_ascii=False, indent=2)
            f.write("\n")
        print(f"[ok] {mod_name} -> {out_path}  ({len(rules)} rules)")
        total_rules += len(rules)

        if args.copy_audio:
            if not os.path.isdir(audio_src):
                print(f"[warn] {mod_name}: audio source missing at {audio_src}")
            else:
                n = 0
                referenced = {v["audio"] for r in rules for v in r["variant_pool"] if v.get("audio")}
                for fn in referenced:
                    src = os.path.join(audio_src, fn)
                    if os.path.exists(src):
                        shutil.copy2(src, os.path.join(out_dir, fn))
                        n += 1
                    else:
                        print(f"[warn] {mod_name}: missing audio file {fn}")
                print(f"[ok] {mod_name}: copied {n} mp3s -> {out_dir}")

    print(f"done. {total_rules} rules across {len(MODS)} mods.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
