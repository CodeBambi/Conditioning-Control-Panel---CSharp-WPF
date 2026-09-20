using System;
using System.Collections.Generic;
using Avalonia.Media;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models.Program;

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
            foreach (var definition in library)
            {
                var premium = definition.Tier == ProgramTier.Premium;
                var accent = AccentBrush(definition.AccentColor);
                items.Add(new ProgramBrowseItem
                {
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
                    IsLocked = premium,
                    ActionText = Loc.Get(premium ? "btn_program_locked" : "btn_program_enroll"),
                    IsActionEnabled = false,
                    ActionOpacity = 0.5,
                    // Tier metadata stays, but nothing here can be started - by a pledge or
                    // otherwise - so every card gets the truthful unavailable explanation rather
                    // than programs_locked_hint, which promises a pledge unlocks execution.
                    ReasonText = Loc.Get("programs_unavailable"),
                    ReasonVisible = true,
                    CardOpacity = premium ? 0.72 : 1.0
                });
            }

            return items;
        }

        private static IBrush AccentBrush(string? hex)
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
