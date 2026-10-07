using System;
using System.Collections.Generic;
using System.Linq;

namespace ConditioningControlPanel.Services.UI
{
    /// <summary>What a pill on a section page does when clicked.</summary>
    public enum NavTabKind
    {
        /// <summary>A ShowTab key with its own view.</summary>
        Tab,
        /// <summary>A place inside another view (a scroll target, a rack entry, a Settings section).</summary>
        Zone,
        /// <summary>Opens its own window (Just Drop).</summary>
        Window,
        /// <summary>Opens a dialog, a site or a window from the Library (mods, catalogue, phrases, media log).</summary>
        Launcher,
    }

    /// <summary>One pill on a section page. <paramref name="Tier"/> 0 = free, 1 = Basic, 2 = Prime.
    /// <paramref name="Hidden"/> = owned by the section (SectionForTab answers it) but drawn as no pill.</summary>
    public sealed record NavTab(string Key, string LabelKey, NavTabKind Kind, int Tier = 0, bool Hidden = false);

    /// <summary>One labelled row on the rail and the pills its page carries.</summary>
    public sealed record NavSection(string Key, string LabelKey, string DefaultTab, NavTab[] Tabs);

    /// <summary>
    /// The navigation table, 2026-10-06 rework: seven sections and the Settings gear. Pure on
    /// purpose (no WPF), so the rail, the tab strip, the palette and the tests read one truth.
    ///
    /// <para>Tab keys are ShowTab keys except where <see cref="NavTabKind"/> says otherwise. The
    /// Settings gear's pills are AppSettingsTabView section keys (Zone), and its page key is
    /// "appsettings": the tab key "settings" is the DASHBOARD, owned by Home.</para>
    ///
    /// <para>Order and labels freeze for six months once the rework ships (NavSectionsTests).</para>
    /// </summary>
    public static class NavSections
    {
        public const string Home = "home";
        public const string Studio = "studio";
        public const string Companion = "companion";
        public const string Play = "play";
        public const string Social = "social";
        public const string You = "you";
        public const string Library = "library";
        public const string Settings = "settings";

        private static NavTab T(string key, string label, int tier = 0, bool hidden = false) =>
            new(key, label, NavTabKind.Tab, tier, hidden);
        private static NavTab Z(string key, string label, int tier = 0) => new(key, label, NavTabKind.Zone, tier);
        private static NavTab W(string key, string label, int tier = 0) => new(key, label, NavTabKind.Window, tier);
        private static NavTab L(string key, string label) => new(key, label, NavTabKind.Launcher);

        /// <summary>Rail order, top to bottom, gear last. Frozen by NavSectionsTests.</summary>
        public static readonly IReadOnlyList<NavSection> Order = new[]
        {
            new NavSection(Home, "nav_door_home", "settings", new[]
            {
                T("settings", "tab_dashboard"),
                // Polish 12 (owner, 2026-10-07): the full vault is back as its own page.
                T("premium", "nav_tab_premium"),
            }),
            new NavSection(Studio, "nav_door_studio", "studio", new[]
            {
                T("studio", "st4_nav_studio"),
                T("presets", "tab_presets"),
                Z("haptics", "tab_haptics", tier: 1),        // StudioTab.FocusRackEntry("haptics")
                W("justdrop", "jd_door_title"),
                Z("ramp", "nav_tab_ramp"),                   // StudioTab.FocusRackEntry("scheduler")
            }),
            new NavSection(Companion, "nav_door_companion", "companion", new[]
            {
                T("companion", "nav_tab_chat"),
                T("personality", "nav_tab_personality"),
                T("permissions", "nav_tab_permissions"),
                T("companionlinks", "nav_tab_companionlinks"),
                T("bambitakeover", "tab_takeover", tier: 1),
                T("shelistening", "tab_shelistening", tier: 1),
                T("awareness", "tab_awareness", tier: 1),
            }),
            new NavSection(Play, "nav_door_play", "play", new[]
            {
                T("play", "nav_tab_games"),
                Z("playeyes", "nav_tab_eyes"),               // PlayTab scrolled to EYES (page order wins)
                Z("playsessions", "nav_tab_sessions"),       // PlayTab scrolled to SESSIONS
                T("deeper", "tab_deeper"),
                // Pages of their own that live inside Play's zones: owned here, no pill.
                T("gradedintake", "tab_gradedintake", hidden: true),
                T("lockdown", "tab_lockdown_mode", tier: 1, hidden: true),
                T("blinktrainer", "tab_blink_trainer", tier: 1, hidden: true),
            }),
            new NavSection(Social, "nav_section_social", "availablesubjects", new[]
            {
                T("availablesubjects", "nav_tab_lobby"),
                T("friends", "nav_tab_friends"),
                T("leaderboard", "tab_leaderboard"),
                T("remotecontrol", "tab_remote_control", tier: 1),
                T("leash", "nav_tab_leash"),
            }),
            new NavSection(You, "nav_door_you", "discord", new[]
            {
                T("discord", "tab_profile"),
                T("quests", "tab_quests"),
                T("achievements", "tab_achievements"),
                T("enhancements", "tab_enhancements"),
                T("programs", "tab_programs"),
                T("chaster", "chaster_title"),
                // Collapsed unless this account is in the fog era (MainWindow.SpiralRoom.cs).
                T("spiral", "tab_spiral", hidden: true),
            }),
            new NavSection(Library, "nav_door_library", "assets", new[]
            {
                T("assets", "tab_assets"),
                T("folders", "nav_tab_folders"),
                L("mods", "yl7_nav_mods"),
                L("catalogue", "yl7_nav_catalogue"),
                L("phrases", "yl7_nav_phrases"),
                L("medialog", "yl7_nav_medialog"),
            }),
            new NavSection(Settings, "nav_door_settings", "appsettings", new[]
            {
                Z("general", "set2_section_general"),
                Z("account", "settings_section_plans"),
                Z("audio", "set2_section_audio"),
                Z("devices", "set2_section_devices"),
                Z("monitors", "settings_section_monitors"),
                Z("emidesk", "set2_section_emidesk"),
                Z("notifications", "set2_section_notifications"),
                Z("performance", "set2_section_performance"),
                Z("data", "set2_section_data"),
                Z("updates", "set2_section_updates"),
            }),
        };

        /// <summary>Old ShowTab keys that no longer have a home of their own: key -> (section, tab).
        /// Never dead: ShowTab lands them on the new home (and the first few hits toast "Moved").</summary>
        public static readonly IReadOnlyDictionary<string, (string Section, string Tab)> Redirects =
            new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase)
            {
                ["exclusives"] = (Home, "premium"),
                ["lab"] = (Play, "play"),
                ["progression"] = (Home, "settings"),
                ["patreon"] = (Settings, "account"),
                ["together"] = (Social, "availablesubjects"),
            };

        /// <summary>Old and alternate names per destination, for search ("(was X)" hints).
        /// Keyed by tab key; the Settings account section is "account", the full vault "premium".</summary>
        public static readonly IReadOnlyDictionary<string, string[]> Aliases =
            new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                ["premium"] = new[] { "Exclusives", "Premium", "Velvet Vault", "The Vault" },
                ["account"] = new[] { "Plans" },
                ["play"] = new[] { "Lab" },
                ["studio"] = new[] { "Effects Rack" },
                ["availablesubjects"] = new[] { "Available Subjects", "Together" },
            };

        /// <summary>The section by key (null when unknown).</summary>
        public static NavSection? Find(string? sectionKey)
        {
            if (string.IsNullOrEmpty(sectionKey)) return null;
            foreach (var s in Order)
                if (string.Equals(s.Key, sectionKey, StringComparison.OrdinalIgnoreCase)) return s;
            return null;
        }

        /// <summary>The tab a section opens on (its first pill unless the table says otherwise).</summary>
        public static string? DefaultTab(string? sectionKey) => Find(sectionKey)?.DefaultTab;

        /// <summary>
        /// Which section owns a tab key. Redirected old keys answer with their new section;
        /// "appsettings" answers Settings. Null for an unknown key.
        /// </summary>
        public static string? SectionForTab(string? tabKey)
        {
            if (string.IsNullOrEmpty(tabKey)) return null;
            if (Redirects.TryGetValue(tabKey!, out var r)) return r.Section;
            foreach (var s in Order)
            {
                if (string.Equals(s.DefaultTab, tabKey, StringComparison.OrdinalIgnoreCase)) return s.Key;
                foreach (var t in s.Tabs)
                    if (string.Equals(t.Key, tabKey, StringComparison.OrdinalIgnoreCase)) return s.Key;
            }
            return null;
        }

        /// <summary>Every tab key in the table, section order.</summary>
        public static IEnumerable<NavTab> AllTabs => Order.SelectMany(s => s.Tabs);
    }
}
