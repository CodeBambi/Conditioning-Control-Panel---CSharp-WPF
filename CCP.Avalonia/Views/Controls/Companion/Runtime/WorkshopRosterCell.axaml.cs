using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia;
using Avalonia.Media;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;

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
                var personality = this.FindControl<Button>($"BtnCompanion{index}Personality")!;
                personality.Click += (_, _) => PersonalityAssignRequested?.Invoke(this, index);
                // Hidden: assigning a prompt file to a companion waits for CommunityPromptService
                // (WPF Services/Companion) to cross to CCP.Core.
                personality.IsVisible = false;
            }
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            Refresh();
        }

        /// <summary>
        /// WPF MainWindow.CompanionTab.cs:85 UpdateCompanionCardsUI, the five cards' half: hidden
        /// when the mod does not support the companion, mod-aware name, "MAX"/"Lv.N", flavour +
        /// XP-mechanic tooltip, accent ring on the active one, lock visuals off. Every input is in
        /// Core (CoreSettings progress/active id, CompanionDefinition, CoreMods.Service), read the
        /// way CompanionService.GetProgress/ActiveCompanion read them. Called on attach and when
        /// the room's tab is shown again (CompanionRoomView.ResumeClocks).
        /// </summary>
        public void Refresh()
        {
            var s = CoreSettings.Current;
            if (s == null) return;
            var mods = CoreMods.Service;
            var activeId = (CompanionId)s.ActiveCompanionId;
            var colors = new[] { mods?.GetAccentColorHex() ?? "#FF69B4", "#9370DB", "#50C878", "#FF6B6B", "#F5DEB3" };

            for (int i = 0; i < 5; i++)
            {
                var id = (CompanionId)i;
                var card = this.FindControl<Border>($"CompanionCard{i}")!;
                if (mods?.IsCompanionSupported(id) == false)
                {
                    card.IsVisible = false;
                    continue;
                }
                card.IsVisible = true;

                var def = CompanionDefinition.GetById(id);
                var name = def.GetDisplayName(s.SlutModeEnabled);
                this.FindControl<TextBlock>($"TxtCompanion{i}Name")!.Text = mods?.MakeModAware(name) ?? name;

                // Read-only, unlike CompanionService.GetProgress, which also seeds a missing entry.
                var progress = s.CompanionProgressData.TryGetValue(i, out var saved) ? saved : CompanionProgress.CreateNew(id);
                this.FindControl<TextBlock>($"TxtCompanion{i}Level")!.Text = progress.IsMaxLevel ? "MAX" : $"Lv.{progress.Level}";

                var mechanic = mods?.MakeModAware(def.XPMechanicDescription) ?? def.XPMechanicDescription;
                var flavor = mods?.MakeModAware(def.Description) ?? def.Description;
                ToolTip.SetTip(card, string.IsNullOrWhiteSpace(mechanic) ? flavor : $"{flavor}\n{mechanic}");

                card.BorderBrush = id == activeId && Color.TryParse(colors[i], out var c)
                    ? new SolidColorBrush(c) : Brushes.Transparent;

                this.FindControl<TextBlock>($"TxtCompanion{i}Lock")!.IsVisible = false;
                card.Opacity = 1.0;
            }
        }
    }

    /// <summary>Strings from CCP.Core's Loc. See the porting notes in the repo-root CLAUDE.md
    /// for why {loc:Str} becomes a binding.</summary>
    public sealed class WorkshopRosterCellViewModel
    {
        public string LocBeta => Loc.Get("label_beta");
        public string LocTooltipAssignAiPersonality => Loc.Get("tooltip_assign_ai_personality");
    }
}
