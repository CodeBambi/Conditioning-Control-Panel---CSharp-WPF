using System;
using System.Collections.Generic;

namespace ConditioningControlPanel.Models
{
    /// <summary>What the Exclusives tab paints on a card's entitlement chip.</summary>
    public enum ExclusiveGateState
    {
        /// <summary>Fully available - warm chip, no veil.</summary>
        Unlocked,
        /// <summary>Not premium, but the feature is legitimately open right now
        /// (Graded Intake with an unspent weekly pass) - gold chip, no veil.</summary>
        PassReady,
        /// <summary>Premium required - fogged veil + breathing padlock.</summary>
        Locked,
    }

    /// <summary>
    /// One entry on the Exclusives tab. The whole presentation - card geometry, art
    /// treatment, ambient motion, gating veil, badges - lives in the card builder
    /// (MainWindow.Exclusives.cs); a future exclusive ships by adding one entry here,
    /// its art, and its loc keys. Navigation always goes through ShowTab(Key): the
    /// card never blocks, the destination tab's own gate does (house pattern).
    /// </summary>
    public sealed class ExclusiveFeature
    {
        /// <summary>The ShowTab key. Also the suffix of the tagline loc key.</summary>
        public required string Key { get; init; }

        /// <summary>Emoji prefix for the title plate (matches the old submenu's icons).</summary>
        public required string Emoji { get; init; }

        /// <summary>Loc key for the display name (e.g. "tab_remote_control").</summary>
        public required string TitleLocKey { get; init; }

        /// <summary>Loc key for the one-line tagline ("exclusives_tag_&lt;key&gt;").</summary>
        public required string TaglineLocKey { get; init; }

        /// <summary>Pack-relative art path (e.g. "Resources/features/vibe.png").</summary>
        public required string ArtResource { get; init; }

        /// <summary>
        /// Optional pack-relative art cut for the ultra-wide spotlight band (~5:1).
        /// Only the spotlight uses it; shelf cards always use <see cref="ArtResource"/>.
        /// If it is missing or fails to load, the spotlight silently falls back to
        /// <see cref="ArtResource"/> - so a feature may be promoted to hero before its
        /// banner art exists.
        /// </summary>
        public string? BannerArtResource { get; init; }

        /// <summary>Optional badge loc key ("exclusives_badge_new" / "exclusives_badge_beta").</summary>
        public string? BadgeLocKey { get; init; }

        /// <summary>
        /// The LIVERY tier this card wears: 1 = gold "BASIC SUBJECT", 2 = diamond "PRIME SUBJECT",
        /// 0 = no livery at all. It drives the animated rim and the stamped tier badge
        /// (MainWindow.Exclusives.cs), and nothing else.
        ///
        /// <para><b>This is a price tag, not an entitlement check.</b> It says what the feature
        /// costs, which is why every account sees it - a patron who owns the shelf still gets to
        /// see which doors are the expensive ones. What an account may actually OPEN is
        /// <see cref="GateState"/> and, at the destination, TierGate: those two are the only
        /// things that ever refuse, and this number must never be read as either.</para>
        ///
        /// <para>Graded Intake is deliberately 0: it is the weekly-pass feature, legitimately open
        /// to free accounts on their unspent pass, so hanging a tier badge on it would be the
        /// shelf telling a small lie about the one door that is not sold by tier.</para>
        /// </summary>
        public int Tier { get; init; }

        /// <summary>
        /// This exclusive's <see cref="Services.DailyFreeService"/> pool key, or null for the
        /// entries the daily rotation never names (Blink Trainer, Graded Intake, Lockdown).
        /// On the day the ? box rotates a key in, the matching vault card renders open and
        /// wears the gold FREE TODAY pill - see MainWindow.Exclusives.cs.
        ///
        /// <para>These keys are API shared with the server override and with
        /// <c>CardMystery_Click</c> / <c>RailDailyKey</c>; never rename one side alone. They are
        /// deliberately NOT the ShowTab <see cref="Key"/> ("remote" vs "remotecontrol",
        /// "takeover" vs "bambitakeover", "voice" vs "shelistening").</para>
        /// </summary>
        public string? DailyFreeKey { get; init; }

        /// <summary>
        /// Null means the card always shows. A probe that returns false HIDES the card (not a
        /// veil): for a door that does not exist in this build or until the server opens it
        /// (Just Drop, the Arcademy). A veil means "buy this", so it is only for things for sale.
        /// </summary>
        public Func<bool>? IsShown { get; init; }

        public bool Shown()
        {
            try { return IsShown?.Invoke() ?? true; }
            catch { return false; }
        }

        /// <summary>Normalized (0-1) focal point of the art, where the hover bloom sits.</summary>
        public double FocalX { get; init; } = 0.5;
        public double FocalY { get; init; } = 0.45;

        /// <summary>
        /// Entitlement probe. Null means the default premium gate
        /// (CoreEntitlement.HasPremium, seeded from PatreonService.HasPremiumAccess). Evaluated on every refresh.
        /// </summary>
        public Func<ExclusiveGateState>? Gate { get; init; }

        public ExclusiveGateState GateState()
        {
            try
            {
                if (Gate != null) return Gate();
                return CoreEntitlement.HasPremium
                    ? ExclusiveGateState.Unlocked
                    : ExclusiveGateState.Locked;
            }
            catch
            {
                // Fail-closed but never throw into the UI builder.
                return ExclusiveGateState.Locked;
            }
        }

        /// <summary>Tier 2 ("Lab"), whitelist folded in, same bar as TierGate.RequiresLab.</summary>
        private static ExclusiveGateState LabGate() =>
            CoreEntitlement.HasLab ? ExclusiveGateState.Unlocked : ExclusiveGateState.Locked;

        /// <summary>Head probes for doors whose state lives in a head service (main 2e9080399): WPF
        /// JustDropService.DoorAvailable, ArcademyHostService.DoorAvailable, BreakoutAccess.FullAllowed.
        /// Unseeded: the card hides (doors) or stays locked (Breakout), the direction a gate must fail.</summary>
        public static volatile Func<bool>? JustDropDoorProvider, ArcademyDoorProvider, BreakoutFullProvider;

        /// <summary>
        /// Collection order: Prime (tier 2) first, then Basic (tier 1), then the untiered doors.
        /// Stable, so roster order holds inside each shelf. The spotlight still reads All[0].
        /// </summary>
        public static IEnumerable<ExclusiveFeature> ShelfOrder(IEnumerable<ExclusiveFeature> roster) =>
            System.Linq.Enumerable.OrderBy(roster, f => f.Tier switch { 2 => 0, 1 => 1, _ => 2 });

        /// <summary>
        /// True when this exclusive is today's daily free unlock AND the account does not already
        /// own it (premium owns the whole pool, so it never wears the gift tag).
        /// </summary>
        public bool IsFreeToday(ExclusiveGateState state) =>
            state == ExclusiveGateState.Locked
            && DailyFreeKey != null
            && CoreEntitlement.IsFreeToday(DailyFreeKey);

        /// <summary>
        /// The roster, in shelf order. The first entry is additionally the spotlight
        /// (newest exclusive, big hero card) - but every entry, spotlight included,
        /// also gets a card in the collection grid so the shelf is never missing one.
        /// </summary>
        public static readonly IReadOnlyList<ExclusiveFeature> All = new List<ExclusiveFeature>
        {
            new()
            {
                // "fyp" is not a tab - ShowTab launches the feed window for this key.
                Key = "fyp", Emoji = "📱", Tier = 1,
                TitleLocKey = "tab_fyp", TaglineLocKey = "exclusives_tag_fyp",
                ArtResource = "Resources/features/fyp.png",
                // Wide cut for the hero band; the card keeps the 16:9 art above.
                BannerArtResource = "Resources/features/fyp_banner.png",
                BadgeLocKey = "exclusives_badge_new",
                DailyFreeKey = "fyp",
                // The art's glowing phone sits left of center, low.
                FocalX = 0.33, FocalY = 0.60,
            },
            new()
            {
                // "justdrop" is not a tab either - ShowTab intercepts the key and launches the
                // shop window (JustDropHostService). Deliberately NOT first: the spotlight is
                // All[0] and a hero band for a door most accounts cannot open yet would be an ad
                // for nothing. MainWindow.Exclusives hides this card outright while
                // JustDropService.DoorAvailable is false - a hide, not a veil, because a veil
                // means "buy this" and this one is not for sale yet.
                Key = "justdrop", Emoji = "🎚", Tier = 0,
                TitleLocKey = "jd_door_title", TaglineLocKey = "exclusives_tag_justdrop",
                ArtResource = "Resources/features/justdrop.png",
                FocalX = 0.5, FocalY = 0.5,
                // Just Drop is for everyone (owner, 2026-09-25): no livery, no Prime badge,
                // no veil and no rail star. The door itself is still the server's DoorAvailable.
                Gate = () => ExclusiveGateState.Unlocked,
                IsShown = () => JustDropDoorProvider?.Invoke() == true,
            },
            new()
            {
                Key = "blinktrainer", Emoji = "💫", Tier = 1,
                TitleLocKey = "tab_blink_trainer", TaglineLocKey = "exclusives_tag_blinktrainer",
                ArtResource = "Resources/features/blink_trainer.png",
                FocalX = 0.30, FocalY = 0.45,
            },
            new()
            {
                Key = "remotecontrol", Emoji = "🎮", Tier = 1,
                TitleLocKey = "tab_remote_control", TaglineLocKey = "exclusives_tag_remotecontrol",
                ArtResource = "Resources/features/remote_control.png",
                DailyFreeKey = "remote",
            },
            new()
            {
                Key = "bambitakeover", Emoji = "🤖", Tier = 1,
                TitleLocKey = "tab_takeover", TaglineLocKey = "exclusives_tag_bambitakeover",
                ArtResource = "Resources/features/takeover.png",
                FocalY = 0.35,
                DailyFreeKey = "takeover",
            },
            new()
            {
                Key = "shelistening", Emoji = "🎙️", Tier = 1,
                TitleLocKey = "tab_shelistening", TaglineLocKey = "exclusives_tag_shelistening",
                ArtResource = "Resources/features/audio_whispers.png",
                BadgeLocKey = "exclusives_badge_beta",
                // Benched from the wheel, but a server override can still hand it out.
                DailyFreeKey = "voice",
            },
            new()
            {
                // Tier deliberately left at 0 - the weekly pass opens this door without one.
                // Unlimited runs are tier 2 (HasLabAccess) since Sep 18 2026, same as
                // IntakePassService.IsPremium.
                Key = "gradedintake", Emoji = "❓",
                TitleLocKey = "tab_gradedintake", TaglineLocKey = "exclusives_tag_gradedintake",
                ArtResource = "Resources/features/lab_quiz_hero.png",
                // A free account with this week's pass unspent gets the gold
                // "pass ready" chip instead of a padlock - the one Exclusive
                // whose door is legitimately open without premium.
                Gate = () =>
                {
                    if (CoreEntitlement.HasLab) return ExclusiveGateState.Unlocked;
                    if (CoreEntitlement.IsIntakePassAvailable) return ExclusiveGateState.PassReady;
                    return ExclusiveGateState.Locked;
                },
            },
            new()
            {
                Key = "haptics", Emoji = "💜", Tier = 1,
                TitleLocKey = "tab_haptics", TaglineLocKey = "exclusives_tag_haptics",
                ArtResource = "Resources/features/vibe.png",
                // Benched from the wheel, but a server override can still hand it out.
                DailyFreeKey = "haptics",
            },
            new()
            {
                Key = "awareness", Emoji = "👁", Tier = 1,
                TitleLocKey = "tab_awareness", TaglineLocKey = "exclusives_tag_awareness",
                ArtResource = "Resources/features/awareness.png",
                DailyFreeKey = "awareness",
            },
            new()
            {
                Key = "lockdown", Emoji = "🔒", Tier = 1,
                TitleLocKey = "tab_lockdown_mode", TaglineLocKey = "exclusives_tag_lockdown",
                ArtResource = "Resources/lockdown_icon.png",
            },
            // ---- Prime (tier 2) games and Lab features. Owner, 2026-10-02: "add the other
            // premium features, or this is not an exclusive page". The collection grid puts
            // Prime first (MainWindow.Exclusives.cs ShelfRank); the order here is the order
            // inside each shelf. Each card opens the same door the launcher / Play wall uses,
            // and that door keeps its own refusal (MainWindow.OpenExclusiveFeature).
            new()
            {
                Key = "dtrh", Emoji = "🕳", Tier = 2,
                TitleLocKey = "launcher_game_dtrh_title", TaglineLocKey = "exclusives_tag_dtrh",
                ArtResource = "Resources/features/dtrh.png",
                // Never on the daily wheel, but a server drop day names it (DailyFreeService
                // OverridableKeys), and the card then wears FREE TODAY like the pool doors.
                DailyFreeKey = "dtrh",
                Gate = LabGate,
            },
            new()
            {
                Key = "arcademy", Emoji = "🏫", Tier = 2,
                TitleLocKey = "launcher_game_arcademy_title", TaglineLocKey = "exclusives_tag_arcademy",
                ArtResource = "Resources/features/arcademy.png",
                Gate = LabGate,
                // Same build flag that hides the launcher tile.
                IsShown = () => ArcademyDoorProvider?.Invoke() == true,
            },
            new()
            {
                // The full game: eight Story walls and Endless. The three-wall demo is free and
                // lives on the launcher and the Play wall, not here.
                Key = "breakout", Emoji = "🧱", Tier = 2,
                TitleLocKey = "launcher_game_breakout_title", TaglineLocKey = "exclusives_tag_breakout",
                // A render of the launcher's own vector cover (Services/Launcher/BreakoutCardArt).
                ArtResource = "Resources/features/breakout.png",
                FocalY = 0.5,
                Gate = () => BreakoutFullProvider?.Invoke() == true
                    ? ExclusiveGateState.Unlocked
                    : ExclusiveGateState.Locked,
            },
            new()
            {
                Key = "gazeminigame", Emoji = "🎯", Tier = 2,
                TitleLocKey = "label_gaze_minigame", TaglineLocKey = "exclusives_tag_gazeminigame",
                ArtResource = "Resources/features/lab_gaze_hero.png",
                Gate = LabGate,
            },
            new()
            {
                // A switch, not a window: the card opens the Play wall, where the switch lives.
                Key = "focusgaze", Emoji = "👀", Tier = 2,
                TitleLocKey = "label_focus_gaze", TaglineLocKey = "exclusives_tag_focusgaze",
                ArtResource = "Resources/features/lab_focusgaze_hero.png",
                Gate = LabGate,
            },
            new()
            {
                // Tier 1 is HOSTING and sending your own pictures. Joining is free for any
                // signed-in account, so the card always opens the game; the veil only says
                // that hosting is sold.
                Key = "goon", Emoji = "🟢", Tier = 1,
                TitleLocKey = "launcher_game_goon_title", TaglineLocKey = "exclusives_tag_goon",
                ArtResource = "Resources/features/goon_game_tile.png",
            },
            new()
            {
                Key = "backroom", Emoji = "🎰", Tier = 0,
                TitleLocKey = "play_backroom_title", TaglineLocKey = "play_backroom_blurb",
                ArtResource = "Resources/features/backroom.png",
                Gate = () => ExclusiveGateState.Unlocked,
            },
        };
    }
}
