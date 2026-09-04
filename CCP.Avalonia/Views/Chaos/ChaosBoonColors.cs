using System;
using System.Collections.Generic;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace ConditioningControlPanel.Avalonia.Views.Chaos
{
    /// <summary>
    /// The Rabbit Hole's payload-based COLOR LANGUAGE, copied id for id from
    /// ConditioningControlPanel/Chaos/ChaosBoonColors.cs. Every boon belongs to a family by what it
    /// DOES (electric / pleasure / economy / mind / risk), and that one colour drives the draft
    /// card and the sidebar tile. Unmapped ids fall back to the caller's own colour, so neutral
    /// mechanics keep their green and nothing regresses.
    ///
    /// <para>Why it is copied rather than referenced or hoisted: the head's class is a data table
    /// wrapped in <c>System.Windows.Media.Color</c> and <c>SkiaSharp.SKColor</c>, neither of which
    /// resolves in Core - so the TABLE is portable and the TYPE is not. The two chaos windows both
    /// need it (<c>ChaosOverlayWindow</c>'s draft cards through <c>ForOrDefault</c>,
    /// <c>ChaosHudWindow</c>'s tiles through <c>ChaosSidebarBoon.AccentBrush</c>, which calls
    /// <c>BrushForOrDefault</c>), which is why this is one file next to them and not a private copy
    /// in each.</para>
    ///
    /// <para>ponytail: the right home is a Core table of plain RGB triples with each head wrapping
    /// its own colour type - CCP.Core/Services/Chaos/, beside ChaosTuning. Until that exists this
    /// file and ConditioningControlPanel/Chaos/ChaosBoonColors.cs must be edited together; the head's
    /// own comment already calls itself "the single source of truth", and it is, for the ids.
    /// The Skia half (<c>SkForOrDefault</c>) is deliberately absent - this head has no SkiaSharp.</para>
    /// </summary>
    internal static class ChaosBoonColors
    {
        public static readonly Color Electric = Color.FromRgb(0x42, 0xDC, 0xE6);  // cyan — E-Stim / lightning / freeze
        public static readonly Color Pleasure = Color.FromRgb(0xFF, 0x4D, 0xC4);  // hot pink — buzz / rabbits / touch
        public static readonly Color Economy  = Color.FromRgb(0xFF, 0xC8, 0x3D);  // gold — drops / luck / payout
        public static readonly Color Mind     = Color.FromRgb(0xB9, 0x8C, 0xFF);  // purple — perception / trance / intrusion
        public static readonly Color Risk     = Color.FromRgb(0xFF, 0x5A, 0x5A);  // red — sins / gambles / last-second
        public static readonly Color Neutral  = Color.FromRgb(0x9C, 0xE8, 0xA0);  // green — pure mechanics

        private static readonly Dictionary<string, Color> Map = new(StringComparer.OrdinalIgnoreCase)
        {
            // ⚡ Electric
            ["e_stim"] = Electric, ["overload"] = Electric, ["tail_plug"] = Electric, ["unleashed"] = Electric,
            ["electrified_rabbits"] = Electric, ["body_buzz"] = Electric, ["aftermath"] = Electric,
            ["freeze_trigger"] = Electric, ["freeze"] = Electric, ["snap_field"] = Electric, ["size_queen"] = Electric,
            // 💗 Pleasure
            ["vibe_popping"] = Pleasure, ["afterglow"] = Pleasure, ["rabbit_caller"] = Pleasure, ["gg_rabbits"] = Pleasure,
            ["the_spanker"] = Pleasure, ["intrusive_thoughts"] = Pleasure, ["casting_couch"] = Pleasure,
            ["porn_dvd"] = Pleasure, ["the_pull"] = Pleasure, ["chain_reaction"] = Pleasure,
            // 💰 Economy
            ["rabbits_foot"] = Economy, ["gold_digger"] = Economy, ["golden_touch"] = Economy, ["drip_feed"] = Economy,
            ["welcome_shower"] = Economy, ["heavy_drop"] = Economy, ["taking_chances"] = Economy,
            // 🧠 Mind
            ["blindfold"] = Mind, ["blank_eyes"] = Mind, ["the_urge"] = Mind, ["slowburner"] = Mind,
            ["bright_colors"] = Mind, ["skipping_stone"] = Mind, ["slow_fuses"] = Mind, ["slow_recovery"] = Mind,
            ["pendulum_swing"] = Mind, ["focus_here"] = Mind, ["breast_enlargement"] = Mind,
            // 🔥 Risk
            ["hair_trigger"] = Risk, ["playing_fire"] = Risk, ["cam_girl"] = Risk, ["double_or_nothing"] = Risk,
            ["last_breath"] = Risk, ["surrender"] = Risk,
        };

        /// <summary>The family colour for <paramref name="id"/>, or <paramref name="fallback"/> if unmapped.</summary>
        public static Color ForOrDefault(string? id, Color fallback)
            => (id != null && Map.TryGetValue(id, out var c)) ? c : fallback;

        // WPF froze each brush and cached it; an ImmutableSolidColorBrush is already frozen, so the
        // cache is only about allocation - one brush per family, built once.
        private static readonly Dictionary<Color, IBrush> BrushCache = new();

        /// <summary>An immutable accent brush for <paramref name="id"/>, or <paramref name="fallback"/>
        /// if unmapped.</summary>
        public static IBrush BrushForOrDefault(string? id, IBrush fallback)
        {
            if (id == null || !Map.TryGetValue(id, out var c)) return fallback;
            if (!BrushCache.TryGetValue(c, out var b)) { b = new ImmutableSolidColorBrush(c); BrushCache[c] = b; }
            return b;
        }
    }
}
