// PORTED from ConditioningControlPanel/MainWindow/MainWindow.Awareness.cs:832-1200 - the preset
// card grid, the "+ New Preset" tile and the advanced link's preset-first branch - all through
// Core KeywordTriggerPresetService and AwarenessPresetDetailDialog.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Tabs
{
    public partial class AwarenessTabView
    {
        /// <summary>WPF's App.KeywordPresets. Stateless over CoreSettings, like the dialog's own.</summary>
        private static readonly KeywordTriggerPresetService Presets = new();

        private IBrush Res(string key, IBrush fallback)
            => this.TryFindResource(key, out var v) && v is IBrush b ? b : fallback;

        /// <summary>WPF RefreshAwarenessPresetCards (MainWindow.Awareness.cs:847).</summary>
        internal void RefreshAwarenessPresetCards()
        {
            AwarenessPresetItems.Children.Clear();
            foreach (var preset in Presets.VisiblePresets)
                if (preset != null) AwarenessPresetItems.Children.Add(BuildPresetCard(preset));
            AwarenessPresetItems.Children.Add(BuildNewPresetCard());

            LnkAwarenessAdvancedText.Text = GetMostRecentlyInstalledPreset() != null
                ? Loc.Get("cp5_awareness_advanced_link_presets")
                : Loc.Get("cp5_awareness_advanced_link_editor");
        }

        private static KeywordTriggerPreset? GetMostRecentlyInstalledPreset()
            => Presets.VisiblePresets.FirstOrDefault(p => p?.MasterEnabled == true);

        /// <summary>Opens the preset editor, and repaints the grid if it changed anything.</summary>
        internal async Task OpenPresetDetail(KeywordTriggerPreset preset, bool isNew = false)
        {
            if (TopLevel.GetTopLevel(this) is not Window owner) return;
            var dlg = new AwarenessPresetDetailDialog(preset, isNew);
            await dlg.ShowDialogSafe(owner);
            if (dlg.Changed) RefreshAwarenessPresetCards();
        }

        private Border BuildPresetCard(KeywordTriggerPreset preset)
        {
            var pink = Res("PinkBrush", Brushes.HotPink);
            var muted = Res("TextMutedBrush", Brushes.Gray);
            var card = new Border
            {
                Background = Res("SurfaceBgBrush", Brushes.Transparent),
                BorderBrush = preset.MasterEnabled ? pink : new SolidColorBrush(Color.FromRgb(0x2E, 0x2E, 0x48)),
                BorderThickness = new Thickness(preset.MasterEnabled ? 1.5 : 1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(16, 14, 16, 14),
                Margin = new Thickness(0, 0, 12, 12),
                Width = 218,
                Height = 150,
                Cursor = new Cursor(StandardCursorType.Hand),
                Tag = preset.Id,
            };
            ToolTip.SetTip(card, preset.LongDescription);
            // Buttons inside mark their own release handled, so Activate/trash never open the editor.
            card.PointerReleased += async (_, e) =>
            {
                if (e.Handled || e.InitialPressMouseButton != MouseButton.Left) return;
                if (Presets.GetPreset(preset.Id) is { } live) await OpenPresetDetail(live);
            };

            var topRow = new StackPanel { Orientation = Orientation.Horizontal };
            topRow.Children.Add(new TextBlock { Text = preset.Icon, FontSize = 24 });
            if (preset.RequiresAi)
                topRow.Children.Add(new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x1A, 0x3E)),
                    BorderBrush = pink, BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(4), Padding = new Thickness(5, 1, 5, 1),
                    Margin = new Thickness(8, 4, 0, 0), VerticalAlignment = VerticalAlignment.Center,
                    Child = new TextBlock { Text = "AI", Foreground = pink, FontSize = 9, FontWeight = FontWeight.Bold },
                });

            var stack = new StackPanel { ClipToBounds = true };
            stack.Children.Add(topRow);
            stack.Children.Add(new TextBlock
            {
                Text = preset.Name, Foreground = Brushes.White, FontSize = 14,
                FontWeight = FontWeight.Bold, Margin = new Thickness(0, 6, 0, 2),
            });
            stack.Children.Add(new TextBlock
            {
                Text = preset.Description, Foreground = muted, FontSize = 11, TextWrapping = TextWrapping.Wrap,
            });

            var actionRow = new Grid { Margin = new Thickness(0, 10, 0, 0), ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            actionRow.Children.Add(BuildPresetToggleButton(preset, pink));
            if (!preset.IsBuiltIn)
            {
                var trash = BuildPresetTrashButton(preset, muted);
                Grid.SetColumn(trash, 1);
                actionRow.Children.Add(trash);
            }

            var layout = new Grid { RowDefinitions = new RowDefinitions("*,Auto") };
            layout.Children.Add(stack);
            Grid.SetRow(actionRow, 1);
            layout.Children.Add(actionRow);
            card.Child = layout;
            return card;
        }

        private Button BuildPresetToggleButton(KeywordTriggerPreset preset, IBrush pink)
        {
            var active = preset.MasterEnabled;
            var btn = new Button
            {
                // TextBlock content: Avalonia's Button would read '_' as an access key.
                Content = new TextBlock { Text = active ? "✓ Active" : "Activate" },
                FontSize = 10, FontWeight = FontWeight.Bold,
                Padding = new Thickness(10, 4, 10, 4), BorderThickness = new Thickness(1),
                HorizontalAlignment = HorizontalAlignment.Left,
                Foreground = active ? Brushes.White : pink,
                Background = active ? pink : Brushes.Transparent,
                BorderBrush = pink,
                Cursor = new Cursor(StandardCursorType.Hand),
            };
            ToolTip.SetTip(btn, active ? "Turn this preset off" : "Turn this preset on");
            btn.Click += async (_, _) =>
            {
                if (Presets.IsInstalled(preset.Id)) Presets.UninstallPreset(preset.Id);
                else if (TopLevel.GetTopLevel(this) is Window owner)   // lock time needs a yes first (WPF Awareness.cs:1019)
                    Presets.InstallPreset(preset.Id, await ChasterImportConfirmDialog.AskAsync(owner, Presets.ChasterSummary(preset.Id)));
                RefreshAwarenessPresetCards();
            };
            return btn;
        }

        private Button BuildPresetTrashButton(KeywordTriggerPreset preset, IBrush muted)
        {
            var btn = new Button
            {
                Content = new TextBlock { Text = "🗑" },
                FontSize = 12, Padding = new Thickness(6, 3, 6, 3), BorderThickness = new Thickness(0),
                Background = Brushes.Transparent, Foreground = muted,
                VerticalAlignment = VerticalAlignment.Center,
                Cursor = new Cursor(StandardCursorType.Hand),
            };
            ToolTip.SetTip(btn, "Delete this preset");
            btn.Click += async (_, _) =>
            {
                if (TopLevel.GetTopLevel(this) is not Window owner) return;
                var label = string.IsNullOrWhiteSpace(preset.Name) ? "this preset" : $"\"{preset.Name}\"";
                if (!await MessageDialog.ConfirmAsync(owner, "Delete preset",
                        $"Delete {label}?\n\nThis removes the preset and all its triggers permanently.",
                        defaultToCancel: true)) return;

                if (Presets.IsInstalled(preset.Id)) Presets.UninstallPreset(preset.Id);
                CoreSettings.Current.KeywordTriggerPresets?.RemoveAll(p => p.Id == preset.Id);
                CoreSettings.Save();
                RefreshAwarenessPresetCards();
            };
            return btn;
        }

        private Border BuildNewPresetCard()
        {
            var pink = Res("PinkBrush", Brushes.HotPink);
            var muted = Res("TextMutedBrush", Brushes.Gray);
            var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            stack.Children.Add(new TextBlock { Text = "＋", FontSize = 40, FontWeight = FontWeight.Light, Foreground = pink, HorizontalAlignment = HorizontalAlignment.Center });
            stack.Children.Add(new TextBlock { Text = "New Preset", Foreground = Brushes.White, FontSize = 13, FontWeight = FontWeight.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 0) });
            stack.Children.Add(new TextBlock { Text = "Pick your own words", Foreground = muted, FontSize = 10, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 2, 0, 0) });

            var card = new Border
            {
                Name = "NewPresetCard",
                Background = Brushes.Transparent, BorderBrush = pink, BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10), Padding = new Thickness(16, 14, 16, 14),
                Margin = new Thickness(0, 0, 12, 12), Width = 218, Height = 150,
                Cursor = new Cursor(StandardCursorType.Hand), Child = stack,
            };
            ToolTip.SetTip(card, "Create your own keyword preset. You pick the words and what happens when they fire.");
            card.PointerReleased += async (_, e) =>
            {
                if (e.InitialPressMouseButton == MouseButton.Left) await OpenPresetDetail(NewCustomPreset(), isNew: true);
            };
            return card;
        }

        /// <summary>WPF NewPresetCard_Click (MainWindow.Awareness.cs:1129): an unsaved shell.</summary>
        internal static KeywordTriggerPreset NewCustomPreset() => new()
        {
            Id = "custom." + Guid.NewGuid().ToString("N")[..8],
            Name = "My Preset",
            Icon = "✨",
            Description = "",
            LongDescription = "",
            Author = "You",
            Version = 1,
            IsBuiltIn = false,
            RequiresAi = false,
            MasterEnabled = false,
            Triggers = new List<KeywordTrigger>(),
        };
    }
}
