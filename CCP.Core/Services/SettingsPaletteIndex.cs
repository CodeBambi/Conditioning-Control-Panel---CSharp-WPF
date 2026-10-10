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

        /// <summary>
        /// Live availability test, evaluated on every <see cref="SettingsPaletteIndex.Search"/>.
        /// Null - the default, and the case for all but one row - means "always listed".
        ///
        /// <para>This exists for WITHHELD rooms: a door the server has not opened for this account
        /// must not be findable in the search box either, or the palette becomes the one place the
        /// app leaks a feature the rail is deliberately hiding. It is a predicate rather than a
        /// bool because the answer changes mid-session (the flag lands ~9s after launch) and the
        /// index is a static array built once.</para>
        ///
        /// <para>Rows stay in <see cref="SettingsPaletteIndex.All"/> either way. That list is the
        /// registry - what the app CAN reach - and the door/tab parity tests read it; Search is
        /// the view of it this account is allowed to see.</para>
        /// </summary>
        public Func<bool>? IsAvailable { get; init; }

        /// <summary>Retired names this place used to carry (NavSections.Aliases). Searched like
        /// aliases; a hit through one shows a small "(was X)" beside the row.</summary>
        public string[] OldNames { get; init; } = Array.Empty<string>();

        /// <summary>Studio rack key: the row opens the Studio on this module (OpenStudioModule).</summary>
        public string? RackKey { get; init; }

        /// <summary>LauncherCatalogue id: the row starts the game the way the launcher does.</summary>
        public string? GameId { get; init; }

        /// <summary>Library launcher key (mods, catalogue, phrases, medialog): the row opens that
        /// dialog or window through MainWindow.OpenLibraryLauncher, the same verb as the strip pill.</summary>
        public string? LauncherKey { get; init; }

        /// <summary>Play wall zone the row lands on after ShowTab ("games" for the Games row), so the
        /// row means the same thing as the Games pill even when the wall was scrolled down.</summary>
        public string? PlayZone { get; init; }

        /// <summary>The row opens the CC Labs launcher itself (the title-bar button's verb).</summary>
        public bool OpensLauncher { get; init; }

        /// <summary>The caption key carries its own leading emoji (rack form labels); the palette
        /// draws the glyph itself, so the label drops it.</summary>
        public bool StripLeadingGlyph { get; init; }

        /// <summary>Never throws: a predicate that blows up hides its row rather than taking the
        /// palette down, which is the same fail-closed rule ExclusiveFeature.Gate follows.</summary>
        public bool Available
        {
            get
            {
                if (IsAvailable == null) return true;
                try { return IsAvailable(); }
                catch { return false; }
            }
        }

        // ---- display-time resolution (deliberately not cached) ----

        public string Label => StripLeadingGlyph ? SettingsPaletteIndex.StripGlyph(Loc.Get(LabelKey)) : Loc.Get(LabelKey);

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
        /// <summary>Seeded by the head with JustDropService.DoorAvailable. Fail-closed: unset or throwing hides the door.</summary>
        public static volatile Func<bool>? JustDropDoorAvailableProvider;

        internal static bool JustDropDoorAvailable()
        {
            try { return JustDropDoorAvailableProvider?.Invoke() ?? false; }
            catch { return false; }
        }

        // Group captions (also used as the first breadcrumb crumb).
        private const string GroupNav = "set2_palette_group_go_to";
        private const string GroupDoors = "set2_palette_group_doors";
        // Verbs for rows that do NOT navigate: a game row starts the game and a Library launcher
        // opens a dialog, so their breadcrumb must not say "Go to" (desk run 2026-10-06).
        private const string GroupLaunch = "launcher_panel_launch";
        private const string GroupOpen = "btn_open";
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
            // Availability is checked on BOTH paths, empty query included: the withheld door must
            // not appear in the opening list any more than it appears in a search result.
            if (q.Length == 0) return _all.Where(e => e.Available).Take(max).ToList();

            var scored = new List<(int Score, int Order, SettingsPaletteEntry Entry)>();
            for (int i = 0; i < _all.Length; i++)
            {
                if (!_all[i].Available) continue;
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
            // An old name outranks a plain alias: "vault" means the place that WAS the Vault.
            if (OldNameHit(e, q) != null) return 50;
            if (Contains(e.Aliases, q)) return 40;
            if (Contains(e.Context, q)) return 20;
            return 0;
        }

        private static string? OldNameHit(SettingsPaletteEntry e, string q)
        {
            foreach (var old in e.OldNames)
                if (Contains(old, q)) return old;
            return null;
        }

        /// <summary>
        /// The retired name a query reached this row through, for the "(was X)" hint. Null when
        /// the caption itself matched (nobody needs to be told a place was called what they typed
        /// and still is) or when no old name matched.
        /// </summary>
        public static string? WasHint(SettingsPaletteEntry e, string? query)
        {
            var q = (query ?? string.Empty).Trim();
            if (q.Length == 0 || e == null) return null;
            if (Contains(e.Label, q)) return null;
            return OldNameHit(e, q);
        }

        /// <summary>Drops a leading emoji (and the space after it) from a form label.</summary>
        internal static string StripGlyph(string label)
        {
            if (string.IsNullOrEmpty(label)) return label ?? string.Empty;
            int i = 0;
            while (i < label.Length && !char.IsLetterOrDigit(label[i])) i++;
            return i >= label.Length ? label : label.Substring(i);
        }

        /// <summary>
        /// The closest thing to a query that found nothing: the row whose caption, old name or
        /// alias word is a few typos away ("monitr", "vaul", "chesss"). Null when nothing is
        /// close enough to be a fair guess. Returns the row and the word it matched, which is
        /// what the window shows after "Try:".
        /// </summary>
        public static (SettingsPaletteEntry Entry, string Term)? Nearest(string? query)
        {
            var q = (query ?? string.Empty).Trim().ToLowerInvariant();
            if (q.Length < 3) return null;

            // A typo budget that grows with the word: 1 for short words, a third of the length after.
            int budget = Math.Max(1, q.Length / 3);
            (SettingsPaletteEntry Entry, string Term, int Dist)? best = null;

            foreach (var e in _all)
            {
                if (!e.Available) continue;
                foreach (var term in Terms(e))
                {
                    var t = term.ToLowerInvariant();
                    if (Math.Abs(t.Length - q.Length) > budget) continue;
                    int d = Distance(q, t, budget);
                    if (d > budget) continue;
                    if (best == null || d < best.Value.Dist)
                        best = (e, term, d);
                }
            }
            return best == null ? null : (best.Value.Entry, best.Value.Term);
        }

        /// <summary>Caption, each caption word, old names and alias words: what a typo is measured against.</summary>
        private static IEnumerable<string> Terms(SettingsPaletteEntry e)
        {
            var label = e.Label;
            if (!string.IsNullOrWhiteSpace(label))
            {
                yield return label;
                foreach (var w in Words(label)) yield return w;
            }
            foreach (var old in e.OldNames)
            {
                yield return old;
                foreach (var w in Words(old)) yield return w;
            }
            foreach (var w in Words(e.Aliases)) yield return w;
        }

        private static IEnumerable<string> Words(string? text) =>
            (text ?? string.Empty)
                .Split(new[] { ' ', '&', '/', ',', '-', '(', ')' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(w => w.Length >= 3);

        /// <summary>Levenshtein distance with an early exit once every cell passes the budget.</summary>
        internal static int Distance(string a, string b, int budget = int.MaxValue)
        {
            var prev = new int[b.Length + 1];
            var cur = new int[b.Length + 1];
            for (int j = 0; j <= b.Length; j++) prev[j] = j;
            for (int i = 1; i <= a.Length; i++)
            {
                cur[0] = i;
                int rowMin = cur[0];
                for (int j = 1; j <= b.Length; j++)
                {
                    int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                    cur[j] = Math.Min(Math.Min(cur[j - 1] + 1, prev[j] + 1), prev[j - 1] + cost);
                    if (cur[j] < rowMin) rowMin = cur[j];
                }
                if (rowMin > budget) return rowMin;
                (prev, cur) = (cur, prev);
            }
            return prev[b.Length];
        }

        /// <summary>
        /// Every page the palette can open, for the "Show all pages" row: doors, tabs, Settings
        /// sections, Studio modules and games. Individual settings are left out (they are
        /// controls, not pages).
        /// </summary>
        public static IReadOnlyList<SettingsPaletteEntry> AllPages() =>
            _all.Where(e => e.Available && !e.Id.StartsWith("set.", StringComparison.Ordinal)).ToList();

        /// <summary>How many recent destinations the empty box shows.</summary>
        public const int RecentCap = 5;

        /// <summary>Parses the recents setting (a JSON string array); garbage reads as empty.</summary>
        public static List<string> ReadRecents(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return new List<string>();
            try
            {
                return (System.Text.Json.JsonSerializer.Deserialize<List<string>>(json!) ?? new List<string>())
                    .Where(id => !string.IsNullOrWhiteSpace(id)).Take(RecentCap).ToList();
            }
            catch { return new List<string>(); }
        }

        /// <summary>The recents setting with <paramref name="id"/> moved to the front, capped.</summary>
        public static string PushRecent(string? json, string? id)
        {
            var list = ReadRecents(json);
            if (!string.IsNullOrWhiteSpace(id))
            {
                list.RemoveAll(x => string.Equals(x, id, StringComparison.Ordinal));
                list.Insert(0, id!);
            }
            return System.Text.Json.JsonSerializer.Serialize(list.Take(RecentCap).ToList());
        }

        /// <summary>The recent rows that still exist and are available, newest first.</summary>
        public static IReadOnlyList<SettingsPaletteEntry> Recents(string? json) =>
            ReadRecents(json).Select(ById).Where(e => e != null && e.Available).Select(e => e!).ToList();

        /// <summary>The row with this id, or null.</summary>
        public static SettingsPaletteEntry? ById(string? id) =>
            string.IsNullOrEmpty(id) ? null : _all.FirstOrDefault(e => string.Equals(e.Id, id, StringComparison.Ordinal));

        /// <summary>Old names for a destination: NavSections.Aliases keyed by tab key, or by the
        /// Settings section key for the gear's own sections.</summary>
        private static string[] OldNamesFor(string? tabKey, string? sectionKey = null)
        {
            var key = !string.IsNullOrEmpty(sectionKey) ? sectionKey : tabKey;
            if (string.IsNullOrEmpty(key)) return Array.Empty<string>();
            return UI.NavSections.Aliases.TryGetValue(key!, out var names) ? names : Array.Empty<string>();
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
            Door("social", "nav_section_social", "🫂", "availablesubjects", "social lobby friends leash remote leaderboard together");
            Door("library", "nav_door_library", "📚", "assets", "library assets media");
            // The withheld Just Drop row (Studio > Creator Tools since 2026-09-11, a creator tool by
            // owner call). It carries the first IsAvailable predicate in this index: the row is
            // in All - so the door/tab parity tests still see it, and so the day the server opens
            // the door there is nothing left to remember - but Search skips it while
            // JustDropService.DoorAvailable is false. Without that, Ctrl+K would be the one place
            // in the app that admits to a door the rail is deliberately hiding.
            //
            // No matching Tab() row, deliberately: this door has exactly one entry and it is the
            // door, so a tab row would be a second row to the same place.
            list.Add(new SettingsPaletteEntry
            {
                Id = "door.justdrop",
                LabelKey = "jd_door_title",
                Glyph = "🎚",
                TabKey = "justdrop",
                ContextKeys = new[] { GroupNav, "nav_door_studio" },
                Aliases = "just drop shop session order drop express",
                IsAvailable = JustDropDoorAvailable,
            });
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
                "bubbles lock card bouncing text pink filter scheduler ramp exclusion haptics " +
                // The words people actually type for three of those modules. "blur" is Brain Drain
                // (accessibility report 2026-09-20), "starts by itself" is the Scheduler, and
                // "volume gets quiet" is the ramp pulling the master dial down.
                "blur screen blur turn off blur auto start starts by itself starts on its own " +
                "intensity ramp volume gets quiet volume drops");
            Tab("companion", "tab_companion", "🤖", "companion", "companion ai persona workshop chat");
            Tab("bambitakeover", "tab_takeover", "💫", "bambitakeover", "takeover autonomy");
            // "vosk" and "speech model" are the words people bring: the offline model is the one
            // part of voice that can be missing, and the hint that says so lives on this page.
            Tab("shelistening", "tab_shelistening", "🎙️", "shelistening",
                "listening voice speech mic microphone vosk speech model voice model offline model " +
                "wake word hey bambi");
            Tab("awareness", "tab_awareness", "👁️", "awareness", "awareness screen watching");
            // Phase 6: the Lab page became the Play door's card wall. The row survives (people
            // search for "lab", "gaze", "rabbit hole") but it now names — and navigates to — the
            // thing that actually exists. The 🧪 flask is the Tier 2 lockband badge now, not a
            // room, so the rail's joystick is the honest glyph. Every card on the wall is listed
            // in the aliases so the palette finds a feature by name, not just by room.
            // Nav rework polish (2026-10-06): this row is the Games pill ("Go to > Play", caption
            // Games) and lands on the Games zone, so it is the navigation twin of the game.* rows
            // below, which LAUNCH. The Play door row above keeps the "Play" caption.
            list.Add(new SettingsPaletteEntry
            {
                Id = "tab.play",
                LabelKey = "nav_tab_games",
                Glyph = "🕹️",
                TabKey = "play",
                PlayZone = "games",
                ContextKeys = new[] { GroupNav, "nav_door_play" },
                Aliases = "play games lab experiments gaze focus blink trainer intake lockdown remote control loom tier 2 " +
                          "gaze minigame haptics vibration",
            });

            // DECLARED AFTER Play on purpose, and the order is the whole point. Score() gives every
            // alias hit the same 40 and breaks ties by declaration order, so while this row sat
            // above Play a bare "gaze" offered Haptics first - and the Gaze minigame is a card on
            // the PLAY wall. Haptics keeps its own gaze words because the toy is connected here and
            // "how do I get haptics working on the gaze minigame" was asked as one question
            // (ask-support 2026-09-20): that query still lands here, because "haptics gaze" is a
            // substring of nothing else. Moving a row DOWN can only lose ties, never steal them.
            Tab("haptics", "tab_haptics", "📳", "haptics",
                "haptics toy vibrator buttplug funscript vibration connect toy " +
                "haptics gaze gaze minigame vibration mode");
            // card.arcademy and card.backroom (launcher cards on the Play wall) left the index on
            // 2026-09-18: the games live in the CC Labs launcher now, which is not a tab the
            // palette can navigate to.
            Tab("deeper", "tab_deeper", "🌊", "deeper", "deeper files audio video");
            Tab("exclusives", "tab_exclusives", "⭐", "exclusives", "premium exclusives velvet vault showcase");
            // Polish 12 (2026-10-07): the vault is a page again. The retired row above stays
            // registered and out of search; this one answers, old names via NavSections.Aliases.
            Tab("premium", "nav_tab_premium", "⭐", "premium",
                "premium basic prime plan plans unlock unlocked tier showcase what do i get");
            // Pop Quiz's only switch (ChkPopQuizEnabled) lives on this page, so "turn off the pop
            // quiz" has to land here - asked in support 2026-09-16 and answered by hand.
            Tab("gradedintake", "tab_gradedintake", "📝", "gradedintake",
                "intake quiz graded pass pop quiz turn off quiz questions test");
            Tab("lockdown", "tab_lockdown_mode", "🔒", "lockdown", "lockdown lock kiosk");
            Tab("blinktrainer", "tab_blink_trainer", "👀", "blinktrainer", "blink trainer eyes");
            Tab("remotecontrol", "tab_remote_control", "📱", "remotecontrol", "remote control phone");
            Tab("availablesubjects", "nav_tab_lobby", "🛰️", "availablesubjects", "lobby tables open tables open join host chess goon remote subjects online users social");
            Tab("discord", "tab_profile", "👤", "discord", "profile trainer card wardrobe");
            // The Spiral Room (CONTRACT-FUSE-0816 2.4), which replaced the map window. Listed like
            // every other live ShowTab key and deliberately NOT gated on the block: the palette has
            // exactly one verb and the ROOM does the deciding, so an account that is not meant to
            // see a spiral yet lands on the fog or on the waiting panel, exactly as it would from
            // any other door. A row that appeared and disappeared with the block would also turn
            // the search box into a place that leaks who has one.
            Tab("spiral", "tab_spiral", "🌀", "spiral", "spiral descent map devotion stage year one");
            Tab("chaster", "chaster_title", "🔒", "chaster", "chaster lock chastity circe tab time keyholder timer price");
            Tab("quests", "tab_quests", "📜", "quests", "quests daily weekly");
            Tab("achievements", "tab_achievements", "🏆", "achievements", "achievements trophies badges");
            // The streak shield / streak fix is a skill, so "pause my streak" is a purchase on this
            // page and nowhere else (ask-support 2026-09-16: the reporter searched and gave up).
            Tab("enhancements", "tab_enhancements", "✨", "enhancements",
                "skill tree enhancements perks streak pause streak freeze streak shield streak fix " +
                "vacation away days off");
            Tab("programs", "tab_programs", "📅", "programs", "programs training multi day");
            Tab("leaderboard", "tab_leaderboard", "📊", "leaderboard", "leaderboard ranks ranking rankings top players");
            Tab("assets", "tab_assets", "📁", "assets", "assets images videos packs folder");
            Tab("fyp", "tab_fyp", "📲", "fyp", "for you feed scroll fyp");
            Tab("appsettings", "tab_settings", "⚙️", "appsettings", "settings options preferences");

            // ---- Library launchers -------------------------------------------------------
            // Four things the Library opens that are NOT tabs: a dialog, a website, a dialog and a
            // window. Nav rework (2026-10-06): their rail rows are gone, so these rows open the
            // thing itself through MainWindow.OpenLibraryLauncher, the verb the Library strip's
            // pills use. Same rows, same ids, so pinned favourites keep working.
            void Launcher(string id, string labelKey, string glyph, string aliases) =>
                list.Add(new SettingsPaletteEntry
                {
                    Id = "launch." + id,
                    LabelKey = labelKey,
                    Glyph = glyph,
                    TabKey = "assets",
                    LauncherKey = id,
                    ContextKeys = new[] { GroupOpen, "nav_door_library" },
                    Aliases = aliases,
                });

            Launcher("mods", "yl7_nav_mods", "🧩",
                     "mods mod manager install ccpmod creator themes packs");
            Launcher("catalogue", "yl7_nav_catalogue", "🌐",
                     "catalogue community share browse presets sessions download");
            Launcher("phrases", "yl7_nav_phrases", "💬",
                     "phrase manager text pools mantras subliminals barks lines");
            Launcher("medialog", "yl7_nav_medialog", "🎞️",
                     "media log history what did i see flashes videos recently shown");

            // ---- two title-bar buttons ---------------------------------------------------
            // Neither is a tab, and neither BELONGS to a door, so these rows carry no TabKey at
            // all: Navigate skips ShowTab and goes straight to the pulse, which is the whole
            // answer to "where is it" for a control that is always on screen and never scrolls.
            //
            // Both were real support questions this week. The bug button was asked outright
            // ("where in the app is the bug report button?", answered with a screenshot), and the
            // games moved into the CC Labs launcher on 2026-09-18, which took the Arcademy's
            // palette row with them - so the honest answer is "through this button", not nothing.
            // Breadcrumbs (desk run 2026-10-06): a bare "Go to" read as an empty path. The bug
            // button is pulsed where it sits ("Go to > Title bar"); the CC Labs row opens the
            // launcher itself, the same verb as the button, so it says Open.
            void TitleBarButton(string id, string labelKey, string glyph, string element, string aliases,
                                bool opensLauncher = false) =>
                list.Add(new SettingsPaletteEntry
                {
                    Id = "chrome." + id,
                    LabelKey = labelKey,
                    Glyph = glyph,
                    ElementNames = new[] { element },
                    ContextKeys = opensLauncher
                        ? new[] { GroupOpen, "set2_palette_all_games" }
                        : new[] { GroupNav, "set2_palette_title_bar" },
                    OpensLauncher = opensLauncher,
                    Aliases = aliases,
                });

            TitleBarButton("bugreport", "btn_report_bug", "🐛", "BtnTitleBarBugReport",
                           "bug report feedback crash log problem broken send report suggestion");
            TitleBarButton("cclabs", "launcher_window_title", "🕹", "BtnBackToLauncher",
                           "cc labs launcher games arcademy academy campus school back room casino "
                           + "racing thoughts race goon game rabbit hole piece by piece",
                           opensLauncher: true);

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
            Section("emidesk", "set2_section_emidesk", "📺", "emi desk widget companion mascot summon hotkey mute avatar spice glass offers");
            Section("account", "settings_section_plans", "👤", "account plans login patreon discord subscribestar tier upgrade invites subscription");
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
                    new[] { "ChkAutoRun" }, "set2_section_general",
                    "auto start engine run starts by itself starts on its own runs on launch");
            Setting("startup_video", "rf_palette_startup_video", "📼", "general",
                    new[] { "TxtStartupVideo" }, "set2_section_general", "startup video pick file");
            // The app-wide monitor picker. Heavily aliased on purpose: "monitor"/"screen" used to
            // surface only the WEBCAM's calibration monitor, which moves nothing (ask-support
            // 2026-09-08 - the reporter found that row, changed it, and content still covered
            // every screen).
            Setting("content_monitor", "setting_content_monitor", "🖥️", "monitors",
                    new[] { "ContentMonitorPicker", "CmbContentMonitor" },
                    "settings_section_monitors",
                    "monitor screen display multi monitor dual monitor second screen one screen "
                    + "single monitor primary only all monitors show content on");
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
                    new[] { "BtnPanicKey" }, "set2_section_devices",
                    "panic key escape rebind emergency stop exit key quit key esc closes the app");
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
            Setting("suppress_perk_notifications", "label_suppress_perk_notifications", "🔕", "notifications",
                    new[] { "ChkSuppressPerkNotifications" }, "set2_section_notifications",
                    "silence mute perk popup toast lucky 10x 20x xp multiplier pink rush quest complete ping sound immersion");
            Setting("banner_pool", "banner_pool_enabled", "💬", "notifications",
                    new[] { "ChkBannerPool" }, "set2_section_notifications",
                    "banner header line pool taglines trivia rotating message");

            // Account (landed)
            Setting("patreon_login", "set2_palette_patreon_login", "🅿️", "account",
                    new[] { "BtnPatreonLogin", "PatreonLoginCard" }, "settings_section_plans",
                    "patreon login connect subscription");
            Setting("discord_login", "set2_palette_discord_login", "💬", "account",
                    new[] { "BtnDiscordLogin", "DiscordLoginCard" }, "settings_section_plans", "discord login link");
            Setting("link_accounts", "label_link_accounts", "🔗", "account",
                    new[] { "AccountLinkingSection", "BtnLinkPatreon" }, "settings_section_plans",
                    "link accounts patreon discord");
            Setting("cloud_backup", "label_cloud_settings_backup", "☁️", "account",
                    new[] { "BtnBackupSettingsNow", "CloudSettingsBackupSection" }, "settings_section_plans",
                    "cloud backup restore settings sync");
            Setting("data_privacy", "label_data_privacy", "🛡️", "account",
                    new[] { "BtnExportData", "DataPrivacySection" }, "settings_section_plans",
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

            // ---- the 2026-10-06 rework's new pages ---------------------------------------
            AddNavRework(list);

            // Old names (NavSections.Aliases) ride on whichever rows land where the old page
            // used to be. Rows whose key is a retired redirect stay registered (the door/tab
            // parity tests read All) but drop out of search: the new home answers for them.
            for (int i = 0; i < list.Count; i++)
            {
                var e = list[i];
                var old = e.Id.StartsWith("set.", StringComparison.Ordinal) ? Array.Empty<string>() : OldNamesFor(e.TabKey, e.SectionKey);
                bool retired = !string.IsNullOrEmpty(e.TabKey) && UI.NavSections.Redirects.ContainsKey(e.TabKey);
                if (old.Length == 0 && !retired) continue;
                list[i] = new SettingsPaletteEntry
                {
                    Id = e.Id, LabelKey = e.LabelKey, Glyph = e.Glyph, TabKey = e.TabKey,
                    SectionKey = e.SectionKey, ElementNames = e.ElementNames, ContextKeys = e.ContextKeys,
                    Aliases = e.Aliases, RackKey = e.RackKey, GameId = e.GameId,
                    LauncherKey = e.LauncherKey, PlayZone = e.PlayZone, OpensLauncher = e.OpensLauncher,
                    StripLeadingGlyph = e.StripLeadingGlyph,
                    OldNames = old.Length > 0 ? old : e.OldNames,
                    IsAvailable = retired ? () => false : e.IsAvailable,
                };
            }

            return list.ToArray();
        }

        /// <summary>
        /// Rows the nav rework (2026-10-06) added: the new pills (Personality, Permissions, Links,
        /// Friends, Leash, Folders, Sessions, Eyes, Scheduler &amp; Ramp, Settings › Monitors), one
        /// row per Studio rack module, and one per launcher game. Kept apart from the authored
        /// list above so the history of that list stays readable.
        /// </summary>
        private static void AddNavRework(List<SettingsPaletteEntry> list)
        {
            void Pill(string tab, string labelKey, string glyph, string sectionLabelKey, string aliases) =>
                list.Add(new SettingsPaletteEntry
                {
                    Id = "tab." + tab,
                    LabelKey = labelKey,
                    Glyph = glyph,
                    TabKey = tab,
                    ContextKeys = new[] { GroupNav, sectionLabelKey },
                    Aliases = aliases,
                });

            Pill("personality", "nav_tab_personality", "🎭", "nav_door_companion",
                 "personality persona presets prompt editor traits community prompts fork");
            Pill("permissions", "nav_tab_permissions", "🛂", "nav_door_companion",
                 "permissions lock cards lock card permissions ai permissions what she can do allowed consent");
            Pill("companionlinks", "nav_tab_companionlinks", "🔗", "nav_door_companion",
                 "companion links video links hypnotube knowledge links she knows videos she can play");
            Pill("companionai", "label_ai_badge", "🔌", "nav_door_companion",
                 "ai settings connection ai provider model cloud local ollama openai openrouter custom endpoint " +
                 "use my own model engine room sampler temperature test connection " +
                 "memory remember memories preferred name call me recap forget diary what she knows " +
                 "behaviour behavior how often it talks speaks chatter idle chatter bubble duration " +
                 "mute voice lines trigger mode trigger interval whispers pause browser midnight glass tube");
            Pill("friends", "nav_tab_friends", "🤝", "nav_section_social",
                 "friends friend list add friend invite poke requests block");
            Pill("leash", "nav_tab_leash", "🦮", "nav_section_social",
                 "leash dom sub holder leashed cut the leash punishment");
            Pill("folders", "nav_tab_folders", "🗂️", "nav_door_library",
                 "folders folder assets path assets folder pictures folder content folder media folder asset presets");
            Pill("playsessions", "nav_tab_sessions", "🎧", "nav_door_play",
                 "sessions intake for you lockdown deeper sessions play");
            Pill("playeyes", "nav_tab_eyes", "👁️", "nav_door_play",
                 "eyes gaze focus gaze blink trainer webcam eye tracking");
            Pill("ramp", "nav_tab_ramp", "📈", "nav_door_studio",
                 "scheduler ramp intensity ramp schedule timer auto start starts by itself volume gets quiet");

            // Settings › Monitors (the monitor picker lifted out of the Home System pill).
            list.Add(new SettingsPaletteEntry
            {
                Id = "section.monitors",
                LabelKey = "settings_section_monitors",
                Glyph = "🖥️",
                TabKey = "appsettings",
                SectionKey = "monitors",
                ContextKeys = new[] { GroupSettings },
                Aliases = "monitor monitors screen screens second monitor second screen display displays " +
                          "dual monitor multi monitor which screen",
            });

            // ---- Studio rack modules ---------------------------------------------------
            // One row per module so "bubble pop" opens Bubble Pop, not the rack's first module.
            // Haptics and Scheduler/Ramp are not here: they are pills with rows of their own.
            void Rack(string key, string labelKey, string glyph, string aliases) =>
                list.Add(new SettingsPaletteEntry
                {
                    Id = "rack." + key,
                    LabelKey = labelKey,
                    Glyph = glyph,
                    TabKey = "studio",
                    RackKey = key,
                    StripLeadingGlyph = true,
                    ContextKeys = new[] { GroupNav, "nav_door_studio" },
                    Aliases = aliases,
                });

            Rack("flash", "section_flash_images", "⚡", "flash flashes images pictures popups");
            Rack("video", "section_mandatory_video", "🎬", "video mandatory video videos");
            Rack("subliminal", "section_subliminals_2", "💭", "subliminal subliminals words");
            Rack("spiral", "label_spiral_overlay", "🌀", "spiral overlay");
            Rack("pinkfilter", "label_pink_filter", "💗", "pink filter tint");
            Rack("visuals", "section_visuals", "👁", "visuals");
            Rack("bubbles", "label_bubble_pop", "🫧", "bubbles bubble pop");
            Rack("bubblecount", "label_bubble_count", "🔢", "bubble count counting");
            Rack("lockcard", "label_lock_card", "📐", "lock card typing phrase");
            Rack("bouncingtext", "label_bouncing_text", "📺", "bouncing text dvd");
            Rack("mindwipe", "label_mind_wipe", "🧠", "mind wipe");
            Rack("braindrain", "section_brain_drain", "💧", "brain drain blur melt screen blur");

            // ---- launcher games ----------------------------------------------------------
            // Start the game the way the launcher tile does (LauncherCatalogue + its account
            // rule). A game the catalogue reports unavailable is hidden from search.
            void Game(string id, string glyph, string aliases) =>
                list.Add(new SettingsPaletteEntry
                {
                    Id = "game." + id,
                    LabelKey = "launcher_game_" + id + "_title",
                    Glyph = glyph,
                    GameId = id,
                    // "Launch > CC Labs", never "Go to": the row starts the game (desk run 2026-10-06,
                    // shot 09f). The Games row (tab.play) is the navigation twin.
                    ContextKeys = new[] { GroupLaunch, "launcher_window_title" },
                    Aliases = aliases,
                    IsAvailable = () => Launcher.LauncherCatalogue.Find(id)?.Available == true,
                });

            Game("backroom", "🎰", "back room casino slots slot machine wheel blackjack roulette sparkle points");
            Game("race", "🏎️", "racing racing thoughts race kart");
            Game("dtrh", "🕳️", "rabbit hole down the rabbit hole descent dtrh");
            Game("arcademy", "🎓", "arcademy academy campus school");
            Game("goon", "🎮", "goon goon game 1v1 duel");
            Game("piecebypiece", "♟️", "chess piece by piece board game");
            Game("breakout", "🧱", "breakout brick breaker bricks paddle");
            Game("breakoutdemo", "🧱", "breakout demo brick breaker free bricks");
            Game("intake", "📝", "intake graded intake quiz");
        }
    }
}
