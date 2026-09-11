using System;
using System.Collections.Generic;
using System.Linq;

namespace ConditioningControlPanel.Models.Dashboard
{
    /// <summary>
    /// The 27 features a Home slot may hold, in ring order. The first registry in the app that
    /// covers the whole wall: the Studio rack knows the 15 FX modules, ExclusiveFeature the 10
    /// Vault doors, EmiTargets 26 desk cards, and none of them is the set a user can put on a
    /// tile. Hand-maintained; FeatureCatalogTests pins every column that can drift silently.
    /// Vault, the ? BOX and the logo are deliberately absent: fixed furniture, not slot contents.
    /// </summary>
    public static class FeatureCatalog
    {
        /// <summary>The catalog, ring 1 first. 27 rows.</summary>
        public static IReadOnlyList<DashboardFeature> All { get; } = Build();

        private static readonly Dictionary<string, DashboardFeature> _byKey =
            All.ToDictionary(f => f.Key, StringComparer.OrdinalIgnoreCase);

        /// <summary>The row for a key, or null when the key is not one of ours.</summary>
        public static DashboardFeature? Find(string? key)
            => string.IsNullOrEmpty(key) ? null : _byKey.TryGetValue(key!, out var f) ? f : null;

        /// <summary>
        /// True when this feature may be half of a split tile: an FX, and an UNGATED one.
        ///
        /// <para>The tier half of that rule is not taste, it is livery. A tile the account cannot
        /// open wears a lockband, a tier rim and a price badge, and all three of those live on
        /// <c>FeatureCard</c> - <c>SplitFeatureCard</c> has no half-sized version of any of them,
        /// so a gated feature dropped into half a cell would be the one tile on the wall that
        /// never names its price. A gated feature therefore always gets a whole tile.</para>
        ///
        /// <para><c>focusgaze</c> is the only row this reaches: Fx, Tier 2. The server's
        /// <c>FX_KEYS</c> list still names it, and that stays harmless - every wire that arrives
        /// goes through <c>DashboardLayoutRule.Sanitize</c>, which asks this, so a
        /// <c>flash|focusgaze</c> from anywhere reduces to <c>flash</c> before anything is
        /// rendered or written back.</para>
        /// </summary>
        public static bool CanSplit(string? key)
        {
            var row = Find(key);
            return row != null && row.Kind == DashboardKind.Fx && row.Tier == 0;
        }

        private static List<DashboardFeature> Build()
        {
            var list = new List<DashboardFeature>();

            // FX: right-click toggles, left-click opens the Studio module. Art is the same
            // features/*.png the mod contract has always named (MainWindow.xaml.cs:2539-2606).
            void Fx(string key, int ring, string titleKey, string art, int tier = 0, bool rackless = false)
                => list.Add(new DashboardFeature(key, ring, tier, DashboardKind.Fx, titleKey,
                    "dash_blurb_" + key, art, rackless ? null : key, null, null));

            // Doors. blurb is the Vault tagline when one exists, otherwise a new dash_blurb_ row.
            void Dest(string key, int ring, int tier, string? titleKey, string art,
                      string? tab = null, string? daily = null, string? blurb = null, string? literal = null)
                => list.Add(new DashboardFeature(key, ring, tier, DashboardKind.Destination, titleKey,
                    blurb ?? "dash_blurb_" + key, art, null, tab, daily, literal));

            // ---- ring 1: the six every account starts with -------------------------
            Fx("flash", 1, "section_flash_images", "features/flash.png");
            Fx("video", 1, "section_mandatory_video", "features/mandatory_videos.png");
            Fx("subliminal", 1, "section_subliminals_2", "features/subliminal.png");
            Fx("bouncingtext", 1, "label_bouncing_text", "features/bouncing_text.png");
            Fx("bubblecount", 1, "label_bubble_count", "features/Bubble_count.png");
            Fx("bubbles", 1, "label_bubble_pop", "features/Bubble_pop.png");

            // ---- ring 2: the rest of the free wall, plus the two free doors --------
            Fx("spiral", 2, "label_spiral_overlay", "features/spiral_overlay.png");
            Fx("pinkfilter", 2, "label_pink_filter", "features/Pink_filter.png");
            Fx("mindwipe", 2, "label_mind_wipe", "features/Mind_Wipers.png");
            Fx("braindrain", 2, "section_brain_drain", "features/brain_drain.png");
            Fx("lockcard", 2, "label_lock_card", "features/Phrase_Lock.png");
            Dest("goon", 2, 0, null, "features/goon_game.png", literal: "Goon Game");
            // Tier 0 on purpose, the same call ExclusiveFeature makes: the weekly pass opens this
            // door without a subscription, so a tier badge here would be a small lie.
            Dest("gradedintake", 2, 0, "tab_gradedintake", "features/lab_quiz_hero.png",
                tab: "gradedintake", blurb: "exclusives_tag_gradedintake");

            // ---- ring 3: the Tier 1 shelf -----------------------------------------
            Dest("fyp", 3, 1, "tab_fyp", "features/fyp.png", tab: "fyp", daily: "fyp",
                blurb: "exclusives_tag_fyp");
            Dest("blinktrainer", 3, 1, "tab_blink_trainer", "features/blink_trainer.png",
                tab: "blinktrainer", blurb: "exclusives_tag_blinktrainer");
            Dest("remotecontrol", 3, 1, "tab_remote_control", "features/remote_control.png",
                tab: "remotecontrol", daily: "remote", blurb: "exclusives_tag_remotecontrol");
            Dest("bambitakeover", 3, 1, "tab_takeover", "features/takeover.png",
                tab: "bambitakeover", daily: "takeover", blurb: "exclusives_tag_bambitakeover");
            Dest("shelistening", 3, 1, "tab_shelistening", "features/audio_whispers.png",
                tab: "shelistening", daily: "voice", blurb: "exclusives_tag_shelistening");
            Dest("haptics", 3, 1, "tab_haptics", "features/vibe.png", tab: "haptics",
                daily: "haptics", blurb: "exclusives_tag_haptics");
            Dest("awareness", 3, 1, "tab_awareness", "features/awareness.png", tab: "awareness",
                daily: "awareness", blurb: "exclusives_tag_awareness");
            // The one door whose art has never lived under features/ (EmiTargets does the same).
            Dest("lockdown", 3, 1, "tab_lockdown_mode", "lockdown_icon.png", tab: "lockdown",
                blurb: "exclusives_tag_lockdown");

            // ---- ring 4: the Tier 2 doors -----------------------------------------
            Dest("dtrh", 4, 2, null, "features/dtrh.png", daily: "dtrh",
                literal: "Down the Rabbit Hole");
            Dest("justdrop", 4, 2, "jd_door_title", "features/justdrop.png", tab: "justdrop",
                blurb: "exclusives_tag_justdrop");
            // STAND-IN ART: the Arcademy ships no features/*.png and its own plate lives under
            // Resources/web (Content, not a WPF Resource). Swap the day real tile art exists.
            Dest("arcademy", 4, 2, null, "features/lab_quiz_hero.png", literal: "The Arcademy");
            // STAND-IN ART for the same reason: the four-panel plate at least reads as pieces.
            Dest("piecebypiece", 4, 2, null, "features/4new.png", literal: "Piece by Piece");
            Dest("gaze", 4, 2, "label_gaze_minigame", "features/lab_gaze_hero.png");
            // FX-shaped but not a rack module: its toggle is ChkFocusGaze_Changed on the Lab tab,
            // so RackKey stays null and the renderer routes right-click at that checkbox instead.
            Fx("focusgaze", 4, "label_focus_gaze", "features/lab_focusgaze_hero.png", tier: 2, rackless: true);

            return list;
        }
    }
}
