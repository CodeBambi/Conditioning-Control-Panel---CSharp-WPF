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

        /// <summary>Normalized (0-1) focal point of the art, where the hover bloom sits.</summary>
        public double FocalX { get; init; } = 0.5;
        public double FocalY { get; init; } = 0.45;

        /// <summary>
        /// Entitlement probe. Null means the default premium gate
        /// (App.Patreon.HasPremiumAccess). Evaluated on every refresh.
        /// </summary>
        public Func<ExclusiveGateState>? Gate { get; init; }

        public ExclusiveGateState GateState()
        {
            try
            {
                if (Gate != null) return Gate();
                return App.Patreon?.HasPremiumAccess == true
                    ? ExclusiveGateState.Unlocked
                    : ExclusiveGateState.Locked;
            }
            catch
            {
                // Fail-closed but never throw into the UI builder.
                return ExclusiveGateState.Locked;
            }
        }

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
                Key = "fyp", Emoji = "📱",
                TitleLocKey = "tab_fyp", TaglineLocKey = "exclusives_tag_fyp",
                ArtResource = "Resources/features/fyp.png",
                // Wide cut for the hero band; the card keeps the 16:9 art above.
                BannerArtResource = "Resources/features/fyp_banner.png",
                BadgeLocKey = "exclusives_badge_new",
                // The art's glowing phone sits left of center, low.
                FocalX = 0.33, FocalY = 0.60,
            },
            new()
            {
                Key = "blinktrainer", Emoji = "💫",
                TitleLocKey = "tab_blink_trainer", TaglineLocKey = "exclusives_tag_blinktrainer",
                ArtResource = "Resources/features/blink_trainer.png",
                FocalX = 0.30, FocalY = 0.45,
            },
            new()
            {
                Key = "remotecontrol", Emoji = "🎮",
                TitleLocKey = "tab_remote_control", TaglineLocKey = "exclusives_tag_remotecontrol",
                ArtResource = "Resources/features/remote_control.png",
            },
            new()
            {
                Key = "bambitakeover", Emoji = "🤖",
                TitleLocKey = "tab_takeover", TaglineLocKey = "exclusives_tag_bambitakeover",
                ArtResource = "Resources/features/takeover.png",
                FocalY = 0.35,
            },
            new()
            {
                Key = "shelistening", Emoji = "🎙️",
                TitleLocKey = "tab_shelistening", TaglineLocKey = "exclusives_tag_shelistening",
                ArtResource = "Resources/features/audio_whispers.png",
                BadgeLocKey = "exclusives_badge_beta",
            },
            new()
            {
                Key = "gradedintake", Emoji = "❓",
                TitleLocKey = "tab_gradedintake", TaglineLocKey = "exclusives_tag_gradedintake",
                ArtResource = "Resources/features/lab_quiz_hero.png",
                // A free account with this week's pass unspent gets the gold
                // "pass ready" chip instead of a padlock - the one Exclusive
                // whose door is legitimately open without premium.
                Gate = () =>
                {
                    if (App.Patreon?.HasPremiumAccess == true) return ExclusiveGateState.Unlocked;
                    if (App.IntakePass?.IsPassAvailable == true) return ExclusiveGateState.PassReady;
                    return ExclusiveGateState.Locked;
                },
            },
            new()
            {
                Key = "haptics", Emoji = "💜",
                TitleLocKey = "tab_haptics", TaglineLocKey = "exclusives_tag_haptics",
                ArtResource = "Resources/features/vibe.png",
            },
            new()
            {
                Key = "awareness", Emoji = "👁",
                TitleLocKey = "tab_awareness", TaglineLocKey = "exclusives_tag_awareness",
                ArtResource = "Resources/features/awareness.png",
            },
            new()
            {
                Key = "lockdown", Emoji = "🔒",
                TitleLocKey = "tab_lockdown_mode", TaglineLocKey = "exclusives_tag_lockdown",
                ArtResource = "Resources/lockdown_icon.png",
            },
        };
    }
}
