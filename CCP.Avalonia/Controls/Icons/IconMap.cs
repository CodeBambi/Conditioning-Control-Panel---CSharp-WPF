using System.Collections.Generic;
using static ConditioningControlPanel.Avalonia.Controls.IconBrushes;

namespace ConditioningControlPanel.Avalonia.Controls
{
    /// <summary>One mapped icon: a Fluent kind (or an in-house brand glyph), its variant and,
    /// where the emoji's colour carried meaning, a semantic brush resource key.</summary>
    public readonly record struct IconEntry(IconKind Kind, IconVariant Variant, string? Brush = null, IconBrand Brand = IconBrand.None);

    /// <summary>
    /// The emoji/symbol -> Fluent table, oracle design ~/ccp-port/evidence/oracle/fluent-icons.md
    /// tables D1-D5, as typed code (a missing icon name does not compile). Lookups ignore the
    /// emoji variation selectors (U+FE0E/U+FE0F), so "⚙" and "⚙️" share an entry. Names the oracle
    /// flagged and the package lacks use its listed fallback (evidence/icons/package-api.md).
    /// Content (chat text, user session/preset icons, kaomoji) never goes through this map.
    /// Ids beat emoji: ForFeature/ForSkill/ForTab map Core's stable ids, so a Core emoji edit
    /// cannot change an icon.
    /// </summary>
    public static class IconMap
    {
        private const IconVariant R = IconVariant.Regular, F = IconVariant.Filled;
        private static readonly Dictionary<string, IconEntry> ByGlyph = new();
        private static readonly Dictionary<char, IconKind> ByMdl2 = new();

        public static IReadOnlyDictionary<string, IconEntry> Entries => ByGlyph;

        public static bool TryGet(string? glyph, out IconEntry entry)
        {
            entry = default;
            return !string.IsNullOrEmpty(glyph) && ByGlyph.TryGetValue(Normalize(glyph), out entry);
        }

        /// <summary>Segoe MDL2 codepoint (WPF PUA glyph) -> Fluent kind (table D5).</summary>
        public static bool TryGetMdl2(char codepoint, out IconKind kind) => ByMdl2.TryGetValue(codepoint, out kind);

        internal static string Normalize(string glyph) => glyph.Replace("\uFE0F", "").Replace("\uFE0E", "").Trim();

        private static void M(IconKind k, IconVariant v, string? brush, params string[] glyphs)
        {
            foreach (var g in glyphs) ByGlyph[Normalize(g)] = new IconEntry(k, v, brush);
        }

        static IconMap()
        {
            // D1 status, feedback, system
            M(IconKind.CheckmarkCircle, F, Success, "✅");
            M(IconKind.Checkmark, R, null, "✓", "✔"); M(IconKind.CheckboxChecked, R, null, "☑"); M(IconKind.CheckboxUnchecked, R, null, "☐");
            M(IconKind.DismissCircle, F, Danger, "❌");
            M(IconKind.Dismiss, R, null, "✕", "×", "✖");
            M(IconKind.Warning, F, Warn, "⚠");
            M(IconKind.QuestionCircle, R, null, "❓"); M(IconKind.Info, R, null, "ℹ");
            M(IconKind.Prohibited, R, Danger, "🚫");
            M(IconKind.Prohibited, F, Danger, "⛔");
            M(IconKind.HourglassHalf, R, null, "⏳", "⌛"); M(IconKind.Timer, R, null, "⏱"); M(IconKind.Clock, R, null, "🕰");
            M(IconKind.ArrowSync, R, null, "🔄"); M(IconKind.ArrowClockwise, R, null, "↻"); M(IconKind.ArrowRepeatAll, R, null, "🔁");
            M(IconKind.Lightbulb, R, null, "💡"); M(IconKind.Search, R, null, "🔍", "🔎"); M(IconKind.Alert, R, null, "🔔");
            M(IconKind.Megaphone, R, null, "📣"); M(IconKind.Settings, R, null, "⚙"); M(IconKind.WrenchScrewdriver, R, null, "🛠");
            M(IconKind.Wrench, R, null, "🔧"); M(IconKind.Bug, R, null, "🐛", "🐞"); M(IconKind.SignOut, R, null, "⏻");
            M(IconKind.CloudOff, R, null, "📴"); M(IconKind.PlugDisconnected, R, null, "🔌"); M(IconKind.DoorArrowLeft, R, null, "🚪");
            M(IconKind.Rocket, R, null, "🚀"); M(IconKind.LocalLanguage, R, null, "🌍"); M(IconKind.Desktop, R, null, "🖥");
            M(IconKind.Keyboard, R, null, "⌨", "📐"); M(IconKind.Window, R, null, "🪟"); M(IconKind.ArrowCircleUp, R, null, "⬆");
            M(IconKind.ArrowDown, R, null, "⬇", "↓");

            // D2 media, files, data
            M(IconKind.Play, F, null, "▶");
            M(IconKind.Pause, F, null, "⏸");
            M(IconKind.Stop, F, null, "⏹", "■");
            M(IconKind.ChevronLeft, R, null, "◀", "‹"); M(IconKind.ChevronRight, R, null, "▸", "›"); M(IconKind.ChevronDown, R, null, "▼", "▾");
            M(IconKind.ChevronUp, R, null, "▲", "▴"); M(IconKind.Speaker2, R, null, "🔊"); M(IconKind.Speaker1, R, null, "🔉");
            M(IconKind.SpeakerMute, R, null, "🔇"); M(IconKind.MusicNote2, R, null, "🎵"); M(IconKind.Video, R, null, "🎬");
            M(IconKind.Tv, R, null, "📺"); M(IconKind.Headphones, R, null, "🎧"); M(IconKind.Mic, R, null, "🎤", "🎙");
            M(IconKind.Options, R, null, "🎚", "🎛"); M(IconKind.Camera, R, null, "📷", "📸"); M(IconKind.Image, R, null, "🖼");
            M(IconKind.PhoneVibrate, R, null, "📳", "💥"); M(IconKind.Phone, R, null, "📱"); M(IconKind.Folder, R, null, "📁");
            M(IconKind.FolderOpen, R, null, "📂"); M(IconKind.Document, R, null, "📄");
            M(IconKind.Clipboard, R, null, "📋");          // ClipboardTextLtr missing -> listed fallback
            M(IconKind.NoteEdit, R, null, "📝"); M(IconKind.Edit, R, null, "✏", "✎"); M(IconKind.Delete, R, null, "🗑");
            M(IconKind.Save, R, null, "💾"); M(IconKind.ArrowUpload, R, null, "📤"); M(IconKind.ArrowDownload, R, null, "📥");
            M(IconKind.Box, R, null, "📦"); M(IconKind.PuzzlePiece, R, null, "🧩"); M(IconKind.Pin, R, null, "📌");
            M(IconKind.Link, R, null, "🔗", "⛓");
            M(IconKind.Globe, R, null, "🌐", "🛰");        // Satellite missing -> listed fallback
            M(IconKind.Library, R, null, "📚"); M(IconKind.BookOpen, R, null, "📖"); M(IconKind.History, R, null, "📜");
            M(IconKind.Calendar, R, null, "📅");          // CalendarLtr is not in 2.1.343
            M(IconKind.DataBarVertical, R, null, "📊"); M(IconKind.ArrowTrending, R, null, "📈"); M(IconKind.NumberSymbol, R, null, "🔢");
            M(IconKind.TextFont, R, null, "🔤"); M(IconKind.Open, R, null, "↗"); M(IconKind.ArrowUpLeft, R, null, "↖");
            M(IconKind.ArrowDownRight, R, null, "↘"); M(IconKind.ArrowDownLeft, R, null, "↙"); M(IconKind.ArrowUp, R, null, "↑");
            M(IconKind.ArrowLeft, R, null, "←"); M(IconKind.ArrowRight, R, null, "→"); M(IconKind.Add, R, null, "➕", "＋");
            M(IconKind.Subtract, R, null, "−", "➖", "─"); M(IconKind.FullScreenMaximize, R, null, "⛶");

            // D3 security, tier, currency, rewards
            M(IconKind.LockClosed, F, Tier1, "🔒");
            M(IconKind.LockOpen, R, null, "🔓");
            M(IconKind.LockClosedKey, F, null, "🔐");
            M(IconKind.Key, R, null, "🔑");
            M(IconKind.Beaker, F, Tier2, "🧪", "⚗");
            M(IconKind.Shield, F, null, "🛡", "🛟");       // Lifebuoy missing -> listed fallback
            M(IconKind.Diamond, F, Gem, "💎");
            M(IconKind.CoinMultiple, F, Gold, "🪙");
            M(IconKind.Money, R, null, "💰");
            M(IconKind.Star, F, Gold, "⭐", "★");
            M(IconKind.Star, R, Gold, "☆");
            M(IconKind.Trophy, F, Gold, "🏆");
            M(IconKind.Crown, F, Gold, "👑");
            M(IconKind.Gift, R, null, "🎁"); M(IconKind.TicketDiagonal, R, null, "🎟");
            M(IconKind.Sparkle, F, null, "✨", "✦", "🎉", "🔮"); // Confetti missing -> listed fallback
            M(IconKind.Sparkle, R, null, "✧", "💫");
            M(IconKind.Heart, F, Heart, "💗", "💖", "💕", "💞", "🩷", "💜", "♥", "❤");
            M(IconKind.HatGraduation, R, null, "🎓");

            // D4 feature brands, objects, nature, people (🎀 ☠ 💀 are per-site calls: unmapped)
            M(IconKind.Flash, R, null, "⚡"); M(IconKind.BubbleMultiple, R, null, "🫧"); M(IconKind.Chat, R, null, "💬");
            M(IconKind.Comment, R, null, "💭"); M(IconKind.BrainCircuit, R, null, "🧠"); M(IconKind.Drop, R, null, "💧");
            M(IconKind.EmojiMeh, R, null, "🫠", "😵"); M(IconKind.Water, R, null, "🌊"); M(IconKind.Eye, R, null, "👁", "👀", "😉");
            M(IconKind.EyeOff, R, null, "🙈", "😎"); M(IconKind.EmojiSurprise, R, null, "😮"); M(IconKind.Target, R, null, "🎯");
            M(IconKind.Games, R, null, "🎮", "🕹", "🎪", "🎰"); M(IconKind.ArrowShuffle, R, null, "🎲"); M(IconKind.Bot, R, null, "🤖");
            M(IconKind.Emoji, R, null, "🎭"); M(IconKind.PersonEdit, R, null, "🪞"); M(IconKind.Wand, R, null, "👗");
            M(IconKind.Beaker, R, null, "🫙"); M(IconKind.Fire, R, null, "🕯"); M(IconKind.Link, R, null, "🧵");
            M(IconKind.HeadphonesSoundWave, R, null, "👂");   // Ear missing -> listed fallback
            M(IconKind.Fire, F, Fire, "🔥");
            M(IconKind.WeatherSnowflake, R, null, "❄"); M(IconKind.WeatherSunny, R, null, "☀"); M(IconKind.WeatherMoon, R, null, "🌙");
            M(IconKind.WeatherSunnyLow, R, null, "🌅"); M(IconKind.WeatherFog, R, null, "🌫"); M(IconKind.WeatherSqualls, R, null, "💨");
            M(IconKind.AnimalRabbit, R, null, "🐇", "🐰");
            M(IconKind.LeafThree, F, Gold, "🍀");
            M(IconKind.HandRight, R, null, "✋"); M(IconKind.Person, R, null, "👤"); M(IconKind.People, R, null, "👯‍♀️", "🫂");
            M(IconKind.Home, R, null, "🏠"); M(IconKind.ShoppingBag, R, null, "🛍"); M(IconKind.CompassNorthwest, R, null, "🧭");
            M(IconKind.Square, F, null, "🟪");
            M(IconKind.Circle, F, null, "🟢", "●");
            M(IconKind.Circle, R, null, "○"); M(IconKind.RadioButton, R, null, "◉");
            M(IconKind.Diamond, F, null, "◆", "◈");
            M(IconKind.Diamond, R, null, "◇"); M(IconKind.Color, R, null, "🎨", "🌈"); M(IconKind.New, R, null, "🆕");
            M(IconKind.Flag, R, null, "🏁"); M(IconKind.Building, R, null, "🏫"); M(IconKind.Grid, R, null, "🧱");
            ByGlyph["🌀"] = new IconEntry(IconKind.Circle, R, null, IconBrand.Spiral);   // no Fluent spiral

            // D5 Segoe MDL2 codepoints the WPF head draws
            void D(IconKind k, params char[] cps) { foreach (var c in cps) ByMdl2[c] = k; }
            D(IconKind.Settings, '\uE713'); D(IconKind.ChevronDown, '\uE70D'); D(IconKind.ChevronUp, '\uE70E');
            D(IconKind.ChevronLeft, '\uE76B'); D(IconKind.ChevronRight, '\uE76C'); D(IconKind.Home, '\uE80F');
            D(IconKind.Star, '\uE735', '\uE734'); D(IconKind.Color, '\uE790'); D(IconKind.Options, '\uE9E9');
            D(IconKind.PhoneVibrate, '\uE877'); D(IconKind.Drop, '\uEB42'); D(IconKind.ArrowTrending, '\uE9D2');
            D(IconKind.Chat, '\uE8BD'); D(IconKind.Emoji, '\uE76E'); D(IconKind.Shield, '\uEA18');
            D(IconKind.Link, '\uE71B'); D(IconKind.Bot, '\uE701'); D(IconKind.ArrowRotateClockwise, '\uE7AD');
            D(IconKind.Mic, '\uE720'); D(IconKind.Lightbulb, '\uEA80'); D(IconKind.Games, '\uE7FC');
            D(IconKind.Eye, '\uE7B3'); D(IconKind.Play, '\uE768'); D(IconKind.Layer, '\uE81E');
            D(IconKind.Door, '\uE7EE'); D(IconKind.People, '\uE716'); D(IconKind.Trophy, '\uE9F9');
            D(IconKind.PhoneDesktop, '\uE703'); D(IconKind.Person, '\uE77B'); D(IconKind.TaskList, '\uE9D5');
            D(IconKind.Flash, '\uE945'); D(IconKind.Calendar, '\uE787'); D(IconKind.Key, '\uE72E');
            D(IconKind.Folder, '\uE8B7'); D(IconKind.FolderOpen, '\uE838'); D(IconKind.PuzzlePiece, '\uEA86');
            D(IconKind.Book, '\uE736'); D(IconKind.TextQuote, '\uE8D2'); D(IconKind.TextBulletList, '\uE8FD');
            D(IconKind.Phone, '\uE8EA'); D(IconKind.SignOut, '\uE7E8'); D(IconKind.Camera, '\uE722');
            D(IconKind.Library, '\uE8F1'); D(IconKind.Info, '\uE946'); D(IconKind.MoreHorizontal, '\uE712');
            D(IconKind.Open, '\uE8A7'); D(IconKind.Delete, '\uE74D');
        }

        /// <summary>Core FeatureDefinition.Id -> icon (Lock Card is typing, not a premium padlock).</summary>
        public static IconEntry? ForFeature(string? id) => id switch
        {
            "audio_whispers" => new(IconKind.Speaker2, R), "mind_wipe" => new(IconKind.BrainCircuit, R),
            "flash" => new(IconKind.Flash, R), "mandatory_videos" => new(IconKind.Video, R),
            "subliminal" => new(IconKind.Comment, R), "bouncing_text" => new(IconKind.TextFont, R),
            "pink_filter" => new(IconKind.Heart, F, Heart), "spiral" => new(IconKind.Circle, R, null, IconBrand.Spiral),
            "brain_drain" => new(IconKind.Drop, R), "bubbles" => new(IconKind.BubbleMultiple, R),
            "lock_cards" => new(IconKind.Keyboard, R), "bubble_count" => new(IconKind.NumberSymbol, R),
            "corner_gif" => new(IconKind.Image, R),
            _ => null,
        };

        /// <summary>Core SkillTree skill id -> icon (the first, meaningful half of its compound emoji).</summary>
        public static IconEntry? ForSkill(string? id) => id switch
        {
            "pink_hours" => new(IconKind.Timer, R), "ditzy_data" or "ditzy_data_pro" => new(IconKind.DataBarVertical, R),
            "sparkle_boost_1" or "sparkle_boost_3" or "certified_data_bimbo" => new(IconKind.Sparkle, F), "sparkle_boost_2" or "eternal_doll" => new(IconKind.Diamond, F, Gem),
            "good_girl_streak" or "streak_power" => new(IconKind.Fire, F, Fire), "hive_mind" => new(IconKind.People, R),
            "trophy_case" or "bestie_records" => new(IconKind.Trophy, F, Gold), "lucky_bimbo" => new(IconKind.LeafThree, F, Gold),
            "milestone_rewards" => new(IconKind.Gift, R), "oopsie_insurance" => new(IconKind.Shield, F),
            "popular_girl" => new(IconKind.Crown, F, Gold), "quest_refresh" => new(IconKind.ArrowSync, R),
            "better_quests" => new(IconKind.History, R), "lucky_bubbles" => new(IconKind.BubbleMultiple, R),
            "pink_rush" => new(IconKind.Flash, F), "reroll_addict" => new(IconKind.ArrowShuffle, R),
            "perfect_bimbo_week" => new(IconKind.Star, F, Gold), "night_shift" => new(IconKind.WeatherMoon, R),
            "early_bird_bimbo" => new(IconKind.WeatherSunnyLow, R), "season_rewind" => new(IconKind.ArrowCounterclockwise, R),
            "brain_drain_report" => new(IconKind.BrainCircuit, R),
            _ => null,
        };

        /// <summary>Nav tab id (WPF SectionTabStrip.xaml.cs:213-254, table D5) -> icon. Also serves
        /// SettingsPaletteIndex entries ("door.x" / "tab.x") through <see cref="ForPalette"/>.</summary>
        public static IconEntry? ForTab(string? id) => id switch
        {
            "home" or "dashboard" => new(IconKind.Home, R), "premium" or "exclusives" => new(IconKind.Star, F, Gold),
            "achievements" => new(IconKind.Trophy, R), "studio" => new(IconKind.Color, R),
            "presets" => new(IconKind.Options, R), "haptics" => new(IconKind.PhoneVibrate, R),
            "justdrop" => new(IconKind.Drop, R), "ramp" => new(IconKind.ArrowTrending, R),
            "companion" => new(IconKind.Chat, R), "personality" => new(IconKind.Emoji, R),
            "permissions" => new(IconKind.Shield, R), "companionlinks" or "leash" => new(IconKind.Link, R),
            "companionai" => new(IconKind.Bot, R), "bambitakeover" or "spiral" => new(IconKind.Circle, R, null, IconBrand.Spiral),
            "shelistening" => new(IconKind.Mic, R), "awareness" => new(IconKind.Lightbulb, R),
            "play" => new(IconKind.Games, R), "playeyes" or "blinktrainer" => new(IconKind.Eye, R),
            "playsessions" => new(IconKind.Play, F), "deeper" => new(IconKind.Layer, R),
            "availablesubjects" => new(IconKind.Door, R), "friends" or "social" => new(IconKind.People, R),
            "leaderboard" => new(IconKind.Trophy, R), "remotecontrol" => new(IconKind.PhoneDesktop, R),
            "discord" or "profile" or "you" => new(IconKind.Person, R), "quests" => new(IconKind.TaskList, R),
            "enhancements" => new(IconKind.Flash, R), "programs" => new(IconKind.Calendar, R),
            "chaster" => new(IconKind.Key, R),              // decision: Key, the padlock means premium
            "assets" => new(IconKind.Folder, R), "mods" => new(IconKind.PuzzlePiece, R),
            "catalogue" or "library" => new(IconKind.Book, R), "phrases" => new(IconKind.TextQuote, R),
            "medialog" => new(IconKind.TextBulletList, R), "settings" or "appsettings" => new(IconKind.Settings, R),
            "lockdown" => new(IconKind.LockClosed, F, Tier1), "gradedintake" => new(IconKind.TicketDiagonal, R),
            _ => null,
        };

        /// <summary>SettingsPaletteIndex door/tab entry id ("door.studio", "tab.quests") -> icon by its
        /// tab id; other entries (null) draw their Glyph through <see cref="TryGet"/>.</summary>
        public static IconEntry? ForPalette(string? entryId) =>
            entryId?.StartsWith("door.") == true ? ForTab(entryId[5..])
            : entryId?.StartsWith("tab.") == true ? ForTab(entryId[4..]) : null;
    }
}
