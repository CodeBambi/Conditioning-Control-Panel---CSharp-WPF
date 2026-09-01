using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel.Avalonia.Views.Controls.Companion.Runtime
{
    /// <summary>
    /// Z8 · ROSTER. See the XAML header.
    ///
    /// <para>Like every re-parented cell, this control does no work of its own: it forwards to the
    /// MainWindow handler the old tab forwarded to, so the roster's behaviour is byte-for-byte what
    /// it was before the move.</para>
    /// </summary>
    public partial class WorkshopRosterCell : UserControl
    {
        // The WPF cell forwards both clicks to MainWindow (Window.GetWindow(this) is MainWindow).
        // This head has no MainWindow API surface yet and the cell must not grow App. coupling, so
        // the two actions leave the cell as events and the host wires them - the same contract, one
        // indirection later. Unlike the sibling cells' bare EventHandler these carry the card index,
        // which is what MainWindow.CompanionCard_Click / BtnCompanionPersonality_Click parse out of
        // the sender's Tag; the Tag stays on the markup so the two files still diff.
        public event EventHandler<int>? CompanionCardClicked;
        public event EventHandler<int>? PersonalityAssignRequested;

        public WorkshopRosterCell()
        {
            AvaloniaXamlLoader.Load(this);
            DataContext = new WorkshopRosterCellViewModel();

            for (int i = 0; i < 5; i++)
            {
                int index = i;

                // WPF's MouseLeftButtonDown is left-button-only; Avalonia's PointerPressed fires for
                // every button, so the guard below is what keeps a right-click from switching
                // companions. That guard is the only new mechanic in this port.
                this.FindControl<Border>($"CompanionCard{index}")!.PointerPressed += (s, e) =>
                {
                    if (e.GetCurrentPoint((Border)s!).Properties.IsLeftButtonPressed)
                        CompanionCardClicked?.Invoke(this, index);
                };

                // WPF needed PreviewMouseLeftButtonDown here (plus a visual-tree walk in
                // MainWindow.CompanionCard_Click that ignores clicks originating inside a
                // "Personality" button) because the card's bubbling MouseLeftButtonDown would
                // otherwise fire too. Neither is ported: Avalonia's Button marks PointerPressed
                // handled, and a routed handler does not see handled events by default, so the
                // card's handler never runs for a click on this button.
                this.FindControl<Button>($"BtnCompanion{index}Personality")!.Click +=
                    (_, _) => PersonalityAssignRequested?.Invoke(this, index);
            }
        }
    }

    /// <summary>Strings from CCP.Core's Loc. See the porting notes in the repo-root CLAUDE.md
    /// for why {loc:Str} becomes a binding.</summary>
    public sealed class WorkshopRosterCellViewModel
    {
        public string LocBeta => Loc.Get("label_beta");
        public string LocSyntheticBlowdoll => Loc.Get("label_synthetic_blowdoll");
        public string LocPerfectFuckpuppet => Loc.Get("label_perfect_fuckpuppet");
        public string LocBrainwashedSlavedoll => Loc.Get("label_brainwashed_slavedoll");
        public string LocPlatinumPuppet => Loc.Get("label_platinum_puppet");
        public string LocBambiCow => Loc.Get("label_bambi_cow");
        public string LocTooltipAssignAiPersonality => Loc.Get("tooltip_assign_ai_personality");
        // Placeholder only, exactly as in WPF: the host overwrites TxtCompanionNLevel.Text with
        // "MAX" or $"Lv.{progress.Level}" (MainWindow.CompanionTab.cs:117). The five card names and
        // the prompt labels are likewise re-written from there once mods and progress are known.
        public string LocLv1 => Loc.Get("label_lv_1");
    }
}
