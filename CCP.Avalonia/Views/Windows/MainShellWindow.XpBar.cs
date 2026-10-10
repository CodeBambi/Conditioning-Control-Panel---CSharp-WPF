// PORTED from WPF 7.1.5 MainWindow/MainWindow.UiUpdates.cs: UpdateXPBarLoginState (:412, the XP bar half;
// the identity half is MainShellWindow.AccountChip.cs RefreshAccountIdentity), RefreshXPBarBonuses (:641) and
// GetBonusChipTooltip (:674). Page wave k2, ledger row H11.

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ConditioningControlPanel.Models;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        /// <summary>Signed out: the "log in to track your XP" overlay over a greyed bar (0.3). Signed in: the
        /// bar as it is. Rides the identity repaint, the choke point every auth change runs through.</summary>
        internal void UpdateXPBarLoginState()
        {
            try
            {
                if (Named<Border>("XPBarLoginOverlay") is not { } overlay || Named<Grid>("XPBarContent") is not { } content) return;
                bool signedIn = CoreAccount.IsLoggedIn;
                overlay.IsVisible = !signedIn;
                content.Opacity = signedIn ? 1.0 : 0.3;
            }
            catch (Exception ex) { Log.Debug("UpdateXPBarLoginState: {E}", ex.Message); }
        }

        /// <summary>One chip per active XP bonus, left of the stat pills: "+10% Sparkle Boost". The breakdown is
        /// Core SkillTreeRules, the same rules the Skill Tree's own bonus list reads.</summary>
        internal void RefreshXPBarBonuses()
        {
            try
            {
                if (Named<StackPanel>("XPBarBonusList") is not { } list) return;
                var breakdown = SkillTreeRules.GetMultiplierBreakdown(CoreSettings.Current, DateTime.Now.Hour, 0.0);
                // Painted often (the stat pill tick): rebuild only when the chips would read differently.
                var signature = string.Join("|", breakdown.ConvertAll(b => $"{b.Source}:{b.Value:P0}")) + "|" + CoreMods.ActiveModId;
                if (ReferenceEquals(list.Tag, null) == false && (string)list.Tag! == signature) return;
                list.Tag = signature;
                list.Children.Clear();

                IBrush text;
                try { text = new SolidColorBrush(Color.Parse(App.Mods?.GetAccentLightColorHex() ?? "#FFB6C1")); }
                catch { text = new SolidColorBrush(Color.Parse("#FFB6C1")); }

                foreach (var (source, value) in breakdown)
                {
                    if (source == "Base") continue;
                    var chip = new Border
                    {
                        Background = new SolidColorBrush(Color.FromRgb(42, 42, 74)),   // #2A2A4A, the stat pills' face
                        CornerRadius = new CornerRadius(10),
                        BorderThickness = new Thickness(1),
                        Padding = new Thickness(6, 3, 6, 3),
                        Margin = new Thickness(0, 0, 8, 0),
                        Child = new TextBlock
                        {
                            Text = $"+{value:P0} {CoreMods.MakeModAware(source)}",
                            Foreground = text,
                            FontSize = 10,
                            VerticalAlignment = VerticalAlignment.Center,
                        },
                    };
                    // The raised edge its neighbours wear (Depth law: never a hand-picked shade).
                    chip.Bind(Border.BorderBrushProperty, chip.GetResourceObservable("DepthRaisedBevel"));
                    if (GetBonusChipTooltip(source) is { } tip) ToolTip.SetTip(chip, tip);
                    list.Children.Add(chip);
                }
            }
            catch (Exception ex) { Log.Debug("RefreshXPBarBonuses: {E}", ex.Message); }
        }

        /// <summary>The mod's own tooltip for the boost when it has one, else the default made mod-aware.</summary>
        internal static string? GetBonusChipTooltip(string source)
        {
            static string M(string text) => CoreMods.MakeModAware(text);

            string? modTip = null;
            if (source.StartsWith("Streak Power", StringComparison.Ordinal)) modTip = App.Mods?.GetBoostTooltip("streak_power");
            else
            {
                var skillId = source switch
                {
                    "Sparkle Boost" => "sparkle_boost_1",
                    "Extra Sparkly" => "sparkle_boost_2",
                    "Maximum Sparkle" => "sparkle_boost_3",
                    "Night Shift" => "night_shift",
                    "Early Bird Bimbo" => "early_bird_bimbo",
                    "PINK RUSH ACTIVE!" => "pink_rush",
                    _ => null,
                };
                if (skillId != null) modTip = App.Mods?.GetBoostTooltip(skillId);
            }
            if (modTip != null) return modTip;

            if (source.StartsWith("Streak Power", StringComparison.Ordinal)) return M("Skill tree bonus: +0.5% XP per day of consecutive use (max 15%)");
            return source switch
            {
                "Sparkle Boost" => M("Skill tree bonus: +10% XP from Sparkle Boost"),
                "Extra Sparkly" => M("Skill tree bonus: +15% XP from Extra Sparkly (stacks with Sparkle Boost)"),
                "Maximum Sparkle" => M("Skill tree bonus: +20% XP from Maximum Sparkle (stacks with other Sparkle skills)"),
                "Night Shift" => M("Skill tree bonus: +50% XP for conditioning between 11 PM and 5 AM"),
                "Early Bird Bimbo" => M("Skill tree bonus: +50% XP for conditioning between 5 AM and 8 AM"),
                "PINK RUSH ACTIVE!" => M("Skill tree bonus: 3x XP multiplier! Random 60-second windows of boosted XP"),
                _ => null,
            };
        }
    }
}
