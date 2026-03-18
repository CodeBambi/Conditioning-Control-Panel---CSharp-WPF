"""Build drone-mode.ccpmod from DroneMod staging folder."""
import os, shutil, zipfile

dm = os.path.dirname(os.path.abspath(__file__))
out = os.path.join(dm, "build")
pkg = os.path.join(out, "resources")

# Clean build dir
if os.path.exists(out):
    shutil.rmtree(out)
for d in ["resources/achievements", "resources/features", "resources/skills",
          "resources/Cards"]:
    os.makedirs(os.path.join(out, d), exist_ok=True)

placeholder = os.path.join(dm, "UI", "droneos_logo.png")
count = {"real": 0, "placeholder": 0}

def copy_or_ph(src, dst):
    if src and os.path.exists(src):
        shutil.copy2(src, dst)
        count["real"] += 1
    else:
        shutil.copy2(placeholder, dst)
        count["placeholder"] += 1

# ── mod.json ──
shutil.copy2(os.path.join(dm, "mod.json"), os.path.join(out, "mod.json"))

# ── ACHIEVEMENTS (29 slots) ──
ach_map = {
    "initiation_sequence.png":      "lv_10.png",
    "blank_slate.png":              "Dumb_Bimbo.png",
    "synthetic_perfection.png":     "lv_50.png",
    "hive_node.png":                "docile_cow.png",
    "fully_assimilated.png":        "perfect_plastic_puppet.png",
    "format_c.png":                 "BrainwashedSlavedoll.png",
    "filtered_perception.png":      "PlatinumPuppet.png",
    "standby_mode.png":             "10_hours_pink.png",
    "daily_synchronization.png":    "daily_maintenance.png",
    "data_overload.png":            "retinal_burn.png",
    "boot_sequence.png":            "morning_glory.png",
    "task_failed_successfully.png": "player_2_disconnected.png",
    "display_unit.png":             "Sofa_decor.png",
    "access_denied.png":            "look_but_dont_touch.png",
    "hypno_sync.png":               "spiral_eyes.png",
    "processing_error.png":         "Mathematician's_nightmare.png",
    "defragmentation.png":          "pop_the_Thought.png",
    "transcription_unit.png":       "typing_tutor.png",
    "overclocked.png":              "obedience_reflex.png",
    "glitch_in_the_system.png":     "Neon_obsession.png",
    "memory_wiped.png":             "clean_slate.png",
    "perfect_alignment.png":        "corner_hit.png",
    "haptic_feedback.png":          "deep_sleep.png",
    "terminal_lock.png":            "total_lockdown.png",
    "reboot_loop.png":              "relapse.png",
    "absolute_override.png":        "mercy_beggar.png",
    "fatal_exception.png":          "system_overload.png",
    "unit_online.png":              "What_panic_button.png",
}
for src_name, slot in ach_map.items():
    copy_or_ph(os.path.join(dm, "Achievements", src_name),
               os.path.join(pkg, "achievements", slot))
copy_or_ph(None, os.path.join(pkg, "achievements", "how_many.png"))
print(f"Achievements: 29 slots ({len(ach_map)} real + 1 placeholder)")

# ── FEATURES (17 slots) ──
feat_map = {
    "data_injection.png":       "flash.png",
    "mandatory_playback.png":   "mandatory_videos.png",
    "subliminal_protocol.png":  "subliminal.png",
    "floating_directive.png":   "bouncing_text.png",
    "green_filter.png":         "Pink_filter.png",
    "hypno_vortex.png":         "spiral_overlay.png",
    "memory_flush.png":         "brain_drain.png",
    "data_purge.png":           "Bubble_pop.png",
    "protocol_lock.png":        "Phrase_Lock.png",
    "enumeration_task.png":     "Bubble_count.png",
    "peripheral_stimulus.png":  "corner_gif.png",
    "audio_uplink.png":         "audio_whispers.png",
    "sector_wipe.png":          "Mind_Wipers.png",
    "system_override.png":      "bambi takeover.png",
    "override_terminal.png":    "takeover.png",
    "haptic_signal.png":        "vibe.png",
}
for src_name, slot in feat_map.items():
    copy_or_ph(os.path.join(dm, "Features", src_name),
               os.path.join(pkg, "features", slot))
copy_or_ph(None, os.path.join(pkg, "features", "4new.png"))
print(f"Features: 17 slots ({len(feat_map)} real + 1 placeholder)")

# ── SKILLS (22 slots) ──
skill_map = {
    "uptime_hours.png":       "pink_hours.png",
    "corrupted_data.png":     "ditzy_data.png",
    "overclock_I.png":        "sparkle_boost_1.png",
    "compliance_streak.png":  "good_girl_streak.png",
    "hive_network.png":       "hive_mind.png",
    "achievement_cache.png":  "trophy_case.png",
    "overclock_II.png":       "sparkle_boost_2.png",
    "rng_exploit.png":        "lucky_bimbo.png",
    "checkpoint_rewards.png": "milestone_rewards.png",
    "error_recovery.png":     "oopsie_insurance.png",
    "network_popularity.png": "popular_girl.png",
    "task_refresh.png":       "quest_refresh.png",
    "enhanced_directives.png":"better_quests.png",
    "overclock_III.png":      "sparkle_boost_3.png",
    "lucky_packets.png":      "lucky_bubbles.png",
    "system_surge.png":       "pink_rush.png",
    "streak_amplifier.png":   "streak_power.png",
    "recompile_addict.png":   "reroll_addict.png",
    "perfect_cycle.png":      "perfect_bimbo_week.png",
    "night_cycle.png":        "night_shift.png",
    "early_boot.png":         "early_bird_bimbo.png",
    "eternal_unit.png":       "eternal_doll.png",
}
for src_name, slot in skill_map.items():
    copy_or_ph(os.path.join(dm, "Skills", src_name),
               os.path.join(pkg, "skills", slot))
print(f"Skills: 22 slots (all real)")

# ── AVATARS (28 slots: 20 real + 8 placeholder) ──
avatar_map = {
    "Chassis_Alpha_Standby.png":  "avatar_pose1.png",
    "Chassis_Alpha_Active.png":   "avatar_pose2.png",
    "Chassis_Alpha_Alert.png":    "avatar_pose3.png",
    "Chassis_Alpha_Override.png": "avatar_pose4.png",
    "Chassis_Beta_Standby.png":   "avatar2_pose1.png",
    "Chassis_Beta_Active.png":    "avatar2_pose2.png",
    "Chassis_Beta_Alert.png":     "avatar2_pose3.png",
    "Chassis_Beta_Override.png":  "avatar2_pose4.png",
    "Chassis_Gamma_Standby.png":  "avatar3_pose1.png",
    "Chassis_Gamma_Active.png":   "avatar3_pose2.png",
    "Chassis_Gamma_Alert.png":    "avatar3_pose3.png",
    "Chassis_Gamma_Override.png": "avatar3_pose4.png",
    "Chassis_Delta_Standby.png":  "avatar4_pose1.png",
    "Chassis_Delta_Active.png":   "avatar4_pose2.png",
    "Chassis_Delta_Alert.png":    "avatar4_pose3.png",
    "Chassis_Delta_Override.png": "avatar4_pose4.png",
    "Chassis_Omega_Standby.png":  "avatar5_pose1.png",
    "Chassis_Omega_Active.png":   "avatar5_pose2.png",
    "Chassis_Omega_Alert.png":    "avatar5_pose3.png",
    "Chassis_Omega_Override.png": "avatar5_pose4.png",
}
for src_name, slot in avatar_map.items():
    copy_or_ph(os.path.join(dm, "Avatars", src_name),
               os.path.join(pkg, slot))
for s in [6, 7]:
    for p in range(1, 5):
        copy_or_ph(None, os.path.join(pkg, f"avatar{s}_pose{p}.png"))
print(f"Avatars: 28 slots (20 real + 8 placeholder)")

# ── UI ASSETS ──
ui_dir = os.path.join(dm, "UI")
ui_map = {
    "data_packet.png":       "bubble.png",
    "stasis_pod.png":        "tube.png",
    "stasis_pod_alt.png":    "tube2.png",
    "droneos_logo.png":      "logo.png",
    "terminal_bubble_1.png": "speechbubble1.png",
    "terminal_bubble_2.png": "speechbubble2.png",
}
for src_name, slot in ui_map.items():
    copy_or_ph(os.path.join(ui_dir, src_name), os.path.join(pkg, slot))
# Missing UI
copy_or_ph(None, os.path.join(pkg, "logo2.png"))
copy_or_ph(None, os.path.join(pkg, "spiral.gif"))
copy_or_ph(None, os.path.join(pkg, "Cards", "fireworks.png"))
copy_or_ph(None, os.path.join(pkg, "Cards", "hearth.png"))
copy_or_ph(None, os.path.join(pkg, "Cards", "spotlight.png"))
# Preview
copy_or_ph(None, os.path.join(pkg, "preview.png"))
print(f"UI + preview: 12 slots (6 real + 6 placeholder)")

# ── ZIP into .ccpmod ──
ccpmod_path = os.path.join(dm, "drone-mode.ccpmod")
if os.path.exists(ccpmod_path):
    os.remove(ccpmod_path)
with zipfile.ZipFile(ccpmod_path, "w", zipfile.ZIP_DEFLATED) as zf:
    for root, dirs, files in os.walk(out):
        for f in files:
            full = os.path.join(root, f)
            arc = os.path.relpath(full, out)
            zf.write(full, arc)

zip_size = os.path.getsize(ccpmod_path) / (1024*1024)

print(f"""
{'='*40}
BUILD COMPLETE
{'='*40}
Real images:    {count['real']}
Placeholders:   {count['placeholder']}
Total assets:   {count['real'] + count['placeholder']}
Package:        drone-mode.ccpmod ({zip_size:.1f} MB)
Location:       {ccpmod_path}
""")
