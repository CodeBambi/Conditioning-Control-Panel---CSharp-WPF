using System;
using System.Collections.Generic;
using Avalonia.Media;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models.Program;
using ConditioningControlPanel.Services.Program;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        /// <summary>
        /// Maps the Core catalogue to browse-only rows. No ProgramService is constructed here: this
        /// layer must not start the program clock or write a ledger merely to show the library.
        /// </summary>
        internal static IReadOnlyList<ProgramBrowseItem> BuildProgramBrowseItems(
            IReadOnlyList<ProgramDefinition> library)
        {
            var items = new List<ProgramBrowseItem>(library.Count);
            // WPF 608ff3181 (ccp-bugs #966): the active mod's programs lead.
            foreach (var definition in ProgramBrowseOrder.Sort(library, CoreMods.ActiveModId))
            {
                var premium = definition.Tier == ProgramTier.Premium;
                // WPF MainWindow.ProgramsTab.cs:605 (ProgramHasPremium = Patreon.HasPremiumAccess, the
                // same answer CoreEntitlement is seeded with): owned premium cards drop the padlock.
                var locked = premium && !CoreEntitlement.HasPremium;
                var accent = AccentBrush(definition.AccentColor);
                // WPF ProgramsTab.cs:633-660. Banner: no default, five cards in the same fallback
                // strip read as a copy-paste bug. Crest: sigil, else day 1's mood plate; the art is a
                // luminance mask, so it masks an accent fill, never an Image (ProgramArt.cs header).
                var banner = ModArt.FirstOf(ProgramArtPaths.Banner(definition, includeDefault: false));
                var sigil = ProgramArtPaths.Sigil(definition);
                var crest = ModArt.FirstOf(sigil != null ? new[] { sigil } : Array.Empty<string>(), 256)
                            ?? ModArt.FirstOf(ProgramArtPaths.DayPlate(definition, definition.GetDay(1)), 256);
                items.Add(new ProgramBrowseItem
                {
                    BannerArt = banner,
                    BannerVisible = banner != null,
                    ArtMask = crest == null ? null : new ImageBrush(crest) { Stretch = Stretch.Uniform },
                    ArtGlowBrush = crest == null ? Brushes.Transparent : ProgramRadialGlowBrush(accent, 130),
                    ArtVisible = crest != null,
                    IconOnlyVisible = crest == null,
                    Definition = definition,
                    ProgramId = definition.Id,
                    Icon = definition.Icon,
                    Title = definition.Title,
                    Subtitle = definition.Subtitle,
                    Pitch = definition.Pitch,
                    LengthLabel = Loc.GetF("programs_length_days", definition.LengthDays),
                    TierLabel = Loc.Get(premium ? "programs_tier_premium" : "programs_tier_free"),
                    TierBrush = premium ? accent : new SolidColorBrush(Color.Parse("#FFA9A3C2")),
                    TierBackground = premium
                        ? new SolidColorBrush(Color.Parse("#33FF69B4"))
                        : new SolidColorBrush(Color.Parse("#332DFF9E")),
                    AccentBrush = accent,
                    IsLocked = locked,
                    ActionText = Loc.Get(locked ? "btn_program_locked" : "btn_program_enroll"),
                    IsActionEnabled = false,
                    ActionOpacity = 0.5,
                    // Tier metadata stays, but nothing here can be started - by a pledge or
                    // otherwise - so every card gets the truthful unavailable explanation rather
                    // than programs_locked_hint, which promises a pledge unlocks execution. A program
                    // this head can never finish says what it is missing (programs-3a decision).
                    ReasonText = ProgramService.UnavailableReason(definition, Platform.ProgramCapabilities.IsAvailable)
                                 ?? Loc.Get("programs_unavailable"),
                    ReasonVisible = true,
                    CardOpacity = locked ? 0.72 : 1.0
                });
            }

            return items;
        }

        /// <summary>WPF ProgramsTab.cs:309 ProgramRadialGlowBrush: accent at alpha, fading out at the radius.</summary>
        internal static IBrush ProgramRadialGlowBrush(IBrush accent, byte alpha,
            double centerX = 0.5, double centerY = 0.5, double radius = 0.75)
        {
            if (accent is not ISolidColorBrush solid) return Brushes.Transparent;
            var c = solid.Color;
            var centre = new global::Avalonia.RelativePoint(centerX, centerY, global::Avalonia.RelativeUnit.Relative);
            return new RadialGradientBrush
            {
                Center = centre,
                GradientOrigin = centre,
                GradientStops =
                {
                    new GradientStop(Color.FromArgb(alpha, c.R, c.G, c.B), 0),
                    new GradientStop(Color.FromArgb(0, c.R, c.G, c.B), 1),
                },
                RadiusX = new global::Avalonia.RelativeScalar(radius, global::Avalonia.RelativeUnit.Relative),
                RadiusY = new global::Avalonia.RelativeScalar(radius, global::Avalonia.RelativeUnit.Relative),
            };
        }

        internal static IBrush AccentBrush(string? hex)
        {
            try
            {
                return string.IsNullOrWhiteSpace(hex)
                    ? new SolidColorBrush(Color.Parse("#FFFF69B4"))
                    : new SolidColorBrush(Color.Parse(hex));
            }
            catch (FormatException)
            {
                return new SolidColorBrush(Color.Parse("#FFFF69B4"));
            }
        }
    }
}
