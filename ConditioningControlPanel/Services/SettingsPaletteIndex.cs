using System;
using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// One row of the Ctrl+K palette: a place in the app, described by where it lives rather
    /// than by how to get there.
    ///
    /// <para><b>Nothing here holds a resolved string.</b> Every caption is a localization key
    /// read through <see cref="Loc"/> at display time, so switching language mid-session and
    /// reopening the palette yields fresh text with no cache to invalidate (rule 4 of the
    /// palette brief). <see cref="Aliases"/> is the one deliberate exception: it is never shown,
    /// only searched, and it stays English so that a user typing "volume" or "webcam" finds the
    /// row whatever UI language they picked.</para>
    ///
    /// <para><b><see cref="ElementNames"/> is a candidate list, not a promise.</b> Phase 2 moves
    /// controls between files while this index is being written, and a settings control may end
    /// up under a different (or additional) x:Name than the one the audit recorded. The palette
    /// tries each candidate in order and simply skips the highlight when none resolve - the
    /// navigation to the right page always happens. A stale name therefore degrades to "took you
    /// to the right section", never to a dead entry or a throw.</para>
    /// </summary>
    public sealed class SettingsPaletteEntry
    {
        /// <summary>Stable id for logs and tests. Never shown to the user.</summary>
        public string Id { get; init; } = string.Empty;

        /// <summary>Localization key for the row's caption.</summary>
        public string LabelKey { get; init; } = string.Empty;

        /// <summary>Small glyph shown left of the caption. Purely decorative.</summary>
        public string Glyph { get; init; } = "•";

        /// <summary>ShowTab key. Load-bearing API - see MainWindow.TabNavigation.cs.</summary>
        public string TabKey { get; init; } = string.Empty;

        /// <summary>
        /// AppSettingsTabView section key (general/audio/devices/...), when <see cref="TabKey"/>
        /// is <c>appsettings</c>. Null for every other tab.
        /// </summary>
        public string? SectionKey { get; init; }

        /// <summary>
        /// x:Name candidates for the control to highlight once the page is up. First one that
        /// resolves wins; an empty array means "navigate only".
        /// </summary>
        public string[] ElementNames { get; init; } = Array.Empty<string>();

        /// <summary>
        /// Localization keys for the breadcrumb under the caption, joined with a chevron
        /// ("Settings > Audio"). Resolved at display time like everything else.
        /// </summary>
        public string[] ContextKeys { get; init; } = Array.Empty<string>();

        /// <summary>Untranslated search-only synonyms. Never displayed.</summary>
        public string Aliases { get; init; } = string.Empty;

        // ---- display-time resolution (deliberately not cached) ----

        public string Label => Loc.Get(LabelKey);

        public string Context =>
            ContextKeys.Length == 0
                ? string.Empty
                : string.Join(" › ", ContextKeys.Select(Loc.Get));
    }

    /// <summary>
    /// The Ctrl+K palette's static registry: every door, every tab, and the high-traffic settings
    /// controls, each pointing at the ONE surface that owns it after the Phase 2 dedup.
    ///
    /// <para>Deliberately a hand-written list rather than a reflection sweep over the visual tree.
    /// A tree walk would index whatever happened to exist and would silently resurrect duplicates
    /// in search results. Curating by hand is what makes the palette agree with the "exactly one
    /// editor per property" contract.</para>
    ///
    /// <para>Phase 8 removed the last ambiguity this guarded against: <c>ChkPerformanceMode</c>,
    /// <c>CmbMotionLevel</c> and <c>BtnCheckUpdates</c> each existed TWICE (live editor + a
    /// Collapsed LegacyDashboardHost / ProgressionTabView twin), so a <c>FindName</c> from the
    /// palette could land on an invisible control. Those twins are deleted; every element name
    /// below now resolves to exactly one control.</para>
    /// </summary>
    public static class SettingsPaletteIndex
    {
        // Group captions (also used as the first breadcrumb crumb).
        private const string GroupNav = "set2_palette_group_go_to";
        private const string GroupDoors = "set2_palette_group_doors";
        private const string GroupSettings = "nav_door_settings";

        private static readonly SettingsPaletteEntry[] _all = BuildEntries();

        /// <summary>Every entry, in declaration order (which is also the empty-query order).</summary>
        public static IReadOnlyList<SettingsPaletteEntry> All => _all;

        /// <summary>
        /// Ranked filter. Empty query returns the list as authored, capped - so the palette opens
        /// showing the doors first rather than an empty box.
        ///
        /// Ranking is deliberately boring: a label that starts with the query beats a label whose
        /// word starts with it, which beats a substring, which beats a hit that only matched an
        /// English alias or the breadcrumb. Ties keep declaration order, which puts navigation
        /// above individual settings for a one-letter query.
        /// </summary>
        public static IReadOnlyList<SettingsPaletteEntry> Search(string? query, int max = 60)
        {
            if (max <= 0) return Array.Empty<SettingsPaletteEntry>();

            var q = (query ?? string.Empty).Trim();
            if (q.Length == 0) return _all.Take(max).ToList();

            var scored = new List<(int Score, int Order, SettingsPaletteEntry Entry)>();
            for (int i = 0; i < _all.Length; i++)
            {
                var score = Score(_all[i], q);
                if (score > 0) scored.Add((score, i, _all[i]));
            }

            return scored
                .OrderByDescending(t => t.Score)
                .ThenBy(t => t.Order)
                .Take(max)
                .Select(t => t.Entry)
                .ToList();
        }

        private static int Score(SettingsPaletteEntry e, string q)
        {
            var label = e.Label;
            if (Starts(label, q)) return 100;
            if (WordStarts(label, q)) return 80;
            if (Contains(label, q)) return 60;
            if (Contains(e.Aliases, q)) return 40;
            if (Contains(e.Context, q)) return 20;
            return 0;
        }

        private static bool Starts(string haystack, string q) =>
            haystack.StartsWith(q, StringComparison.CurrentCultureIgnoreCase);

        private static bool Contains(string haystack, string q) =>
            haystack.Length > 0 && haystack.IndexOf(q, StringComparison.CurrentCultureIgnoreCase) >= 0;

        private static bool WordStarts(string haystack, string q)
        {
            // Cheap word-boundary test: a space, chevron or punctuation immediately before a match.
            int idx = 0;
            while (idx < haystack.Length)
            {
                int hit = haystack.IndexOf(q, idx, StringComparison.CurrentCultureIgnoreCase);
                if (hit < 0) return false;
                if (hit == 0 || !char.IsLetterOrDigit(haystack[hit - 1])) return true;
                idx = hit + 1;
            }
            return false;
        }

        // =================================================================================
        //  the registry
        // =================================================================================

        private static SettingsPaletteEntry[] BuildEntries()
        {
            var list = new List<SettingsPaletteEntry>();

            // ---- doors (7) -------------------------------------------------------------
            // A door's entry navigates to that door's DEFAULT tab, exactly like clicking its
            // header does; ShowTab's ExpandDoorForTab then opens the accordion for free.
            void Door(string id, string labelKey, string glyph, string tab, string aliases) =>
                list.Add(new SettingsPaletteEntry
                {
                    Id = "door." + id,
                    LabelKey = labelKey,
                    Glyph = glyph,
                    TabKey = tab,
                    ContextKeys = new[] { GroupDoors },
                    Aliases = aliases,
                });

            Door("home", "nav_door_home", "🏠", "settings", "home dashboard start");
            // Phase 4 gave the Studio door a first entry of its own - the effects rack - so its
            // default tab is "studio", not "presets". Clicking DoorStudio lands on the rack; this
            // row has to land in the same place or the palette and the rail disagree.
            Door("studio", "nav_door_studio", "🎛️", "studio", "studio effects rack presets");
            Door("companion", "nav_door_companion", "🤖", "companion", "companion ai avatar");
            // Phase 6: the Play door's default destination is the card wall's own key. "lab" is
            // still a working alias but is no longer the name of anything that exists.
            Door("play", "nav_door_play", "🎮", "play", "play games lab");
            Door("you", "nav_door_you", "👤", "discord", "you profile progress");
            Door("library", "nav_door_library", "📚", "assets", "library assets media");
            Door("settings", "nav_door_settings", "⚙️", "appsettings", "settings options preferences config");

            // ---- tabs (every live ShowTab key) -----------------------------------------
            void Tab(string id, string labelKey, string glyph, string tab, string aliases) =>
                list.Add(new SettingsPaletteEntry
                {
                    Id = "tab." + id,
                    LabelKey = labelKey,
                    Glyph = glyph,
                    TabKey = tab,
                    ContextKeys = new[] { GroupNav },
                    Aliases = aliases,
                });

            Tab("settings", "tab_dashboard", "📊", "settings", "dashboard home start engine");
            Tab("presets", "tab_presets", "📋", "presets", "presets sessions catalogue share");
            // Phase 4: the effects rack. Reuses the door's own loc key as the caption, the same way
            // the Play row does. Every rack module is in the aliases so a feature is findable by its
            // own name ("brain drain", "spiral") and not only by the room it now lives in - which is
            // also where "scheduler"/"ramp" resolve to now that they are rack modules.
            Tab("studio", "nav_door_studio", "🎛️", "studio",
                "studio effects rack flashes visuals video spiral subliminal brain drain melt mind wipe " +
                "bubbles lock card bouncing text pink filter scheduler ramp exclusion haptics");
            Tab("haptics", "tab_haptics", "📳", "haptics", "haptics toy vibrator buttplug funscript");
            Tab("companion", "tab_companion", "🤖", "companion", "companion ai persona workshop chat");
            Tab("bambitakeover", "tab_takeover", "💫", "bambitakeover", "takeover autonomy");
            Tab("shelistening", "tab_shelistening", "🎙️", "shelistening", "listening voice speech mic");
            Tab("awareness", "tab_awareness", "👁️", "awareness", "awareness screen watching");
            // Phase 6: the Lab page became the Play door's card wall. The row survives (people
            // search for "lab", "gaze", "rabbit hole") but it now names — and navigates to — the
            // thing that actually exists. The 🧪 flask is the Tier 2 lockband badge now, not a
            // room, so the rail's joystick is the honest glyph. Every card on the wall is listed
            // in the aliases so the palette finds a feature by name, not just by room.
            Tab("play", "nav_door_play", "🕹️", "play",
                "play lab games experiments rabbit hole dtrh descent goon gaze focus bureau mantra loom showcase tier 2");
            Tab("deeper", "tab_deeper", "🌊", "deeper", "deeper files audio video");
            Tab("exclusives", "tab_exclusives", "⭐", "exclusives", "premium exclusives velvet vault showcase");
            Tab("gradedintake", "tab_gradedintake", "📝", "gradedintake", "intake quiz graded pass");
            Tab("lockdown", "tab_lockdown_mode", "🔒", "lockdown", "lockdown lock kiosk");
            Tab("blinktrainer", "tab_blink_trainer", "👀", "blinktrainer", "blink trainer eyes");
            Tab("remotecontrol", "tab_remote_control", "📱", "remotecontrol", "remote control phone");
            Tab("availablesubjects", "tab_available_subjects", "🛰️", "availablesubjects", "subjects online users");
            Tab("discord", "tab_profile", "👤", "discord", "profile trainer card wardrobe");
            Tab("quests", "tab_quests", "📜", "quests", "quests daily weekly");
            Tab("achievements", "tab_achievements", "🏆", "achievements", "achievements trophies badges");
            Tab("enhancements", "tab_enhancements", "✨", "enhancements", "skill tree enhancements perks");
            Tab("programs", "tab_programs", "📅", "programs", "programs training multi day");
            Tab("leaderboard", "tab_leaderboard", "📊", "leaderboard", "leaderboard ranks");
            Tab("assets", "tab_assets", "📁", "assets", "assets images videos packs folder");
            Tab("fyp", "tab_fyp", "📲", "fyp", "for you feed scroll fyp");
            Tab("appsettings", "tab_settings", "⚙️", "appsettings", "settings options preferences");

            // ---- Library launchers (Phase 7) --------------------------------------------
            // Four things the Library door opens that are NOT tabs: a dialog, a website, a dialog
            // and a window. The palette has exactly one navigation verb - ShowTab - so a row here
            // cannot press a button for the user, and inventing a second verb would put window
            // launches behind a search box where a mistyped Enter opens a modal. These rows
            // therefore do what the index says it does everywhere else: they take you to where the
            // thing LIVES. TabKey "assets" is the Library door's default destination (opening the
            // door via ExpandDoorForTab for free), and ElementNames pulses the rail row that
            // launches it, so the answer to "where is the phrase manager" is the button itself.
            //
            // If the palette ever grows a launch verb, these four are its first customers.
            void Launcher(string id, string labelKey, string glyph, string element, string aliases) =>
                list.Add(new SettingsPaletteEntry
                {
                    Id = "launch." + id,
                    LabelKey = labelKey,
                    Glyph = glyph,
                    TabKey = "assets",
                    ElementNames = new[] { element },
                    ContextKeys = new[] { GroupNav, "nav_door_library" },
                    Aliases = aliases,
                });

            Launcher("mods", "yl7_nav_mods", "🧩", "BtnNavMods",
                     "mods mod manager install ccpmod creator themes packs");
            Launcher("catalogue", "yl7_nav_catalogue", "🌐", "BtnNavCatalogue",
                     "catalogue community share browse presets sessions download");
            Launcher("phrases", "yl7_nav_phrases", "💬", "BtnNavPhrases",
                     "phrase manager text pools mantras subliminals barks lines");
            Launcher("medialog", "yl7_nav_medialog", "🎞️", "BtnNavMediaLog",
                     "media log history what did i see flashes videos recently shown");

            // ---- the eight Settings sections -------------------------------------------
            void Section(string key, string labelKey, string glyph, string aliases) =>
                list.Add(new SettingsPaletteEntry
                {
                    Id = "section." + key,
                    LabelKey = labelKey,
                    Glyph = glyph,
                    TabKey = "appsettings",
                    SectionKey = key,
                    ContextKeys = new[] { GroupSettings },
                    Aliases = aliases,
                });

            Section("general", "set2_section_general", "🧭", "general language startup tray");
            Section("audio", "set2_section_audio", "🔊", "audio sound volume ducking");
            Section("devices", "set2_section_devices", "🎛️", "devices webcam camera microphone mic panic hotkeys");
            Section("performance", "set2_section_performance", "⚡", "performance motion gpu rendering");
            Section("notifications", "set2_section_notifications", "🔔", "notifications reminders nudge");
            Section("account", "set2_section_account", "👤", "account login patreon discord subscribestar tier");
            Section("data", "set2_section_data", "💾", "data backup export import offline reset");
            Section("updates", "set2_section_updates", "⬆️", "updates version patch notes changelog");

            // ---- individual settings controls -------------------------------------------
            // Every one of these points at the SINGLE surviving editor for its property after the
            // Phase 2 dedup. If a row ever needs two element names it is a sign the dedup slipped.
            void Setting(string id, string labelKey, string glyph, string section,
                         string[] elements, string sectionLabelKey, string aliases) =>
                list.Add(new SettingsPaletteEntry
                {
                    Id = "set." + id,
                    LabelKey = labelKey,
                    Glyph = glyph,
                    TabKey = "appsettings",
                    SectionKey = section,
                    ElementNames = elements,
                    ContextKeys = new[] { GroupSettings, sectionLabelKey },
                    Aliases = aliases,
                });

            // Audio (landed: Views/Controls/AppSettings/AudioSettingsSection.xaml)
            //
            // Eleven rows below point at rf_palette_* keys rather than the form label beside the
            // control they open. A form label is written to be read next to its widget, so it is
            // terse ("Duck", "Master", "Motion"), sometimes carries the widget's punctuation
            // ("Startup Video:") and sometimes an emoji the palette already draws itself
            // ("🔑 Escape" rendered as a second glyph). Standalone captions need standalone
            // strings - and every translation inherited the colon and the doubled glyph.
            Setting("master_volume", "rf_palette_master_volume", "🔊", "audio",
                    new[] { "SliderMaster" }, "set2_section_audio", "master volume loudness");
            Setting("video_volume", "setting_video", "🎬", "audio",
                    new[] { "SliderVideoVolume" }, "set2_section_audio", "video volume mandatory");
            Setting("ducking", "rf_palette_ducking", "🔉", "audio",
                    new[] { "ChkAudioDuck" }, "set2_section_audio", "duck ducking lower other apps");
            Setting("duck_level", "set2_palette_duck_level", "🔉", "audio",
                    new[] { "SliderDuck" }, "set2_section_audio", "ducking level percent");
            Setting("audio_output", "rf_palette_audio_output", "🎚️", "audio",
                    new[] { "CmbAudioOutputDevice" }, "set2_section_audio", "output device speakers headphones playback");
            Setting("dont_duck_browser", "setting_don_t_duck_browser", "🌐", "audio",
                    new[] { "ChkExcludeBambiCloudDucking" }, "set2_section_audio", "browser bambicloud exclude ducking");
            Setting("test_audio", "set2_btn_test_audio", "🔔", "audio",
                    new[] { "BtnTestAudio" }, "set2_section_audio", "test audio sound check");
            Setting("audio_layers", "rf_palette_audio_layers", "🎧", "audio",
                    new[] { "BtnAudioLayers" }, "set2_section_audio", "layered audio ambience loops");

            // General
            Setting("language", "label_language", "🌍", "general",
                    new[] { "CmbLanguageSetting" },
                    "set2_section_general", "language locale translation");
            Setting("run_on_startup", "rf_palette_run_on_startup", "🚀", "general",
                    new[] { "ChkWinStart" }, "set2_section_general", "startup windows boot autostart launch");
            Setting("start_minimized", "setting_start_hidden", "🫥", "general",
                    new[] { "ChkStartHidden" }, "set2_section_general", "start minimized hidden tray");
            Setting("video_on_launch", "rf_palette_video_on_launch", "🎬", "general",
                    new[] { "ChkVidLaunch" }, "set2_section_general", "video on launch mandatory startup");
            Setting("auto_start_engine", "rf_palette_auto_start_engine", "▶️", "general",
                    new[] { "ChkAutoRun" }, "set2_section_general", "auto start engine run");
            Setting("startup_video", "rf_palette_startup_video", "📼", "general",
                    new[] { "TxtStartupVideo" }, "set2_section_general", "startup video pick file");
            Setting("enable_deeper", "setting_deeper_enable", "🌊", "general",
                    new[] { "ChkEnableDeeper" }, "set2_section_general", "deeper enable tab");

            // Devices - webcam
            Setting("webcam_device", "set2_palette_webcam_device", "📷", "devices",
                    new[] { "CmbWebcamDevice" }, "set2_section_devices", "webcam camera device select");
            Setting("webcam_monitor", "set2_palette_webcam_monitor", "🖥️", "devices",
                    new[] { "CmbWebcamMonitor" }, "set2_section_devices", "webcam monitor screen calibration");
            Setting("restrict_gaze", "set2_palette_restrict_gaze", "🎯", "devices",
                    new[] { "ChkRestrictGazeToCalScreen" }, "set2_section_devices",
                    "gaze restrict calibrated screen");
            Setting("blink_recal", "set2_palette_blink_recal", "😳", "devices",
                    new[] { "ChkBlinkRecalWebcamBar" }, "set2_section_devices",
                    "rapid blink recalibrate kill switch stop");
            Setting("webcam_privacy", "set2_palette_webcam_privacy", "🛡️", "devices",
                    new[] { "BtnWebcamReviewPrivacy", "BtnWebcamRevokeConsent" }, "set2_section_devices",
                    "webcam privacy consent revoke");

            // Devices - microphone
            Setting("mic_device", "set2_palette_mic_device", "🎤", "devices",
                    new[] { "CmbMicDevice" }, "set2_section_devices", "microphone mic input device");
            Setting("wake_word", "set2_palette_wake_word", "🗣️", "devices",
                    new[] { "ChkSpeechWakeWord" }, "set2_section_devices", "wake word hey bambi voice trigger");
            Setting("push_to_talk", "set2_palette_push_to_talk", "🎙️", "devices",
                    new[] { "ChkSpeechPushToTalk" }, "set2_section_devices", "push to talk ptt key");
            Setting("headphones_mode", "set2_palette_headphones", "🎧", "devices",
                    new[] { "ChkHeadphones" }, "set2_section_devices",
                    "headphones interrupt barge in speakers");

            // Devices - panic + shortcuts
            Setting("panic_key", "rf_palette_panic_key", "🆘", "devices",
                    new[] { "BtnPanicKey" }, "set2_section_devices", "panic key escape rebind emergency stop");
            Setting("no_panic", "rf_palette_no_panic", "⚠️", "devices",
                    new[] { "ChkNoPanic" }, "set2_section_devices", "no panic disable escape");
            Setting("chat_shortcut", "set2_palette_chat_shortcut", "⌨️", "devices",
                    new[] { "TxtChatShortcutLabel", "BtnChatShortcut" }, "set2_section_devices",
                    "chat shortcut hotkey ctrl t global");
            Setting("camera_shortcut", "set2_palette_camera_shortcut", "⌨️", "devices",
                    new[] { "TxtCameraShortcutLabel", "BtnCameraShortcut" }, "set2_section_devices",
                    "camera shortcut hotkey ctrl alt k global");

            // Performance (landed)
            Setting("performance_mode", "set2_setting_performance_mode", "⚡", "performance",
                    new[] { "ChkPerformanceMode" }, "set2_section_performance", "performance mode lightweight fps lag");
            Setting("auto_performance", "set2_setting_auto_performance", "⚙️", "performance",
                    new[] { "ChkAutoPerformance" }, "set2_section_performance", "auto performance adaptive");
            Setting("unified_overlay", "set2_setting_unified_overlay", "🪟", "performance",
                    new[] { "ChkUnifiedOverlay" }, "set2_section_performance", "unified overlay renderer compositor");
            Setting("motion_level", "rf_palette_motion_level", "🎞️", "performance",
                    new[] { "CmbMotionLevel" }, "set2_section_performance",
                    "motion reduced off animation accessibility kill switch");

            // Notifications (landed)
            Setting("intake_nudge", "label_intake_nudge_enabled", "🔔", "notifications",
                    new[] { "ChkIntakeNudge" }, "set2_section_notifications", "intake pass reminder nudge weekly");

            // Account (landed)
            Setting("patreon_login", "set2_palette_patreon_login", "🅿️", "account",
                    new[] { "BtnPatreonLogin", "PatreonLoginCard" }, "set2_section_account",
                    "patreon login connect subscription");
            Setting("discord_login", "set2_palette_discord_login", "💬", "account",
                    new[] { "BtnDiscordLogin", "DiscordLoginCard" }, "set2_section_account", "discord login link");
            Setting("link_accounts", "label_link_accounts", "🔗", "account",
                    new[] { "AccountLinkingSection", "BtnLinkPatreon" }, "set2_section_account",
                    "link accounts patreon discord");
            Setting("cloud_backup", "label_cloud_settings_backup", "☁️", "account",
                    new[] { "BtnBackupSettingsNow", "CloudSettingsBackupSection" }, "set2_section_account",
                    "cloud backup restore settings sync");
            Setting("data_privacy", "label_data_privacy", "🛡️", "account",
                    new[] { "BtnExportData", "DataPrivacySection" }, "set2_section_account",
                    "privacy export data policy gdpr");

            // Data
            Setting("offline_mode", "setting_offline_mode", "📴", "data",
                    new[] { "ChkOfflineMode" }, "set2_section_data", "offline mode no network airplane");
            Setting("phrase_backup", "set2_palette_phrase_backup", "📤", "data",
                    new[] { "BtnExportPhrases", "BtnImportPhrases" }, "set2_section_data",
                    "phrase backup export import pools mantras subliminals");
            Setting("factory_reset", "set2_palette_factory_reset", "☠️", "data",
                    new[] { "BtnFactoryReset", "DangerZoneSection" }, "set2_section_data",
                    "factory reset wipe erase danger zone start over");

            // Updates (landed)
            Setting("check_updates", "btn_check_updates", "⬆️", "updates",
                    new[] { "BtnCheckUpdates" }, "set2_section_updates", "check updates upgrade new version");
            Setting("patch_notes", "set2_btn_view_patch_notes", "📰", "updates",
                    new[] { "BtnViewPatchNotes", "TxtPatchNotes" }, "set2_section_updates",
                    "patch notes changelog whats new");

            return list.ToArray();
        }
    }
}
