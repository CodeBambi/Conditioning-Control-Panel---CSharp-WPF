// PORTED from WPF 7.1.5 Windows/Launcher/LauncherMediaDialog.xaml(.cs): "Media and sound", opened by
// the launcher's Media pill. Built in code (the WPF layout numbers, one for one) so the dialog is one
// file. The rules are Core's LauncherMediaSettings.Apply; the consent ask is the Assets page's, word
// for word, awaited before anything is written.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Fyp.Online;
using ConditioningControlPanel.Services.Launcher;
using Serilog;
using LauncherSfx = ConditioningControlPanel.Avalonia.Views.Windows.LauncherWindow.LauncherSfx;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    /// <summary>
    /// Set the pictures once and every room follows: three source chips and the niche chips edit the
    /// SAME app-wide settings the panel's Assets page edits, with the same one-time consent ask, and
    /// every change points the Back Room back at "follow the app". The niche chips stay live under
    /// "My library" so the list can be set before going online. Below them, the saved asset preset
    /// and the app-wide master volume. Applies on change; Done only closes.
    /// </summary>
    internal sealed class LauncherMediaDialog : Window
    {
        private static readonly IBrush ChipOff = new SolidColorBrush(Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF));
        private static readonly IBrush ChipOn = new SolidColorBrush(Color.FromArgb(0x33, 0x5E, 0xC8, 0xF2));
        private static readonly IBrush ChipOnRim = new SolidColorBrush(Color.FromRgb(0x5E, 0xC8, 0xF2));

        private readonly List<ToggleButton> _sourceChips = new();
        private readonly List<ToggleButton> _nicheChips = new();
        private readonly TextBlock _sourceHint, _ratioLabel, _masterLabel;
        private readonly Grid _ratioRow;
        private readonly Slider _ratioSlider, _masterSlider;
        private readonly ComboBox _presetCombo = new() { FontSize = 13, MinHeight = 32, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Stretch };

        /// <summary>Set while settings are written INTO the controls, so the change handlers can tell
        /// a click from an echo of their own refresh.</summary>
        private bool _syncing = true;
        private bool _asking;

        /// <summary>Tests: answers the consent question in place of the dialog.</summary>
        internal Func<string, string, Task<bool>>? ConsentOverride { get; set; }
        /// <summary>The source change in flight, for tests to await.</summary>
        internal Task LastChange { get; private set; } = Task.CompletedTask;
        /// <summary>"Open asset browser": the launcher brings the panel up on the Assets page.</summary>
        internal Action? OpenAssetBrowser { get; set; }

        internal IReadOnlyList<ToggleButton> SourceChips => _sourceChips;
        internal IReadOnlyList<ToggleButton> NicheChips => _nicheChips;
        internal Slider MasterSlider => _masterSlider;
        internal Slider RatioSlider => _ratioSlider;
        internal bool RatioRowVisible => _ratioRow.IsVisible;

        public LauncherMediaDialog()
        {
            Title = Loc.Get("launcher_media_title");
            (Width, Height) = (560, 790);
            CanResize = false;
            ShowInTaskbar = false;
            WindowDecorations = WindowDecorations.None;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = Res("DarkerBgBrush");

            var copy = Res("TextSecondaryBrush");
            var muted = Res("TextMutedBrush");
            var light = Res("TextLightBrush");

            TextBlock Copy(string key, double size = 14, Thickness margin = default) => new()
            { Text = Loc.Get(key), Foreground = copy, FontSize = size, TextWrapping = TextWrapping.Wrap, Margin = margin };
            TextBlock Eyebrow(string key, Thickness margin) => new()
            { Text = Loc.Get(key), Foreground = muted, FontSize = 11, FontWeight = FontWeight.SemiBold, Margin = margin };

            // ---- title bar: drag handle and the close button --------------------------------
            var close = new Button { Content = "✕", Width = 46, Height = 36, HorizontalAlignment = HorizontalAlignment.Right };
            if (this.TryFindResource("WindowCloseButton", out var closeTheme) && closeTheme is global::Avalonia.Styling.ControlTheme ct) close.Theme = ct;
            close.Click += (_, _) => { LauncherSfx.Click(); Close(); };
            var titleBar = new Border
            {
                Background = Brushes.Transparent,
                Child = new Grid
                {
                    Children =
                    {
                        new TextBlock
                        {
                            Text = Loc.Get("launcher_media_title"), Margin = new Thickness(20, 0, 0, 0),
                            FontFamily = new FontFamily("Fredoka, Segoe UI"), FontSize = 18, FontWeight = FontWeight.SemiBold,
                            Foreground = light, VerticalAlignment = VerticalAlignment.Center,
                        },
                        close,
                    },
                },
            };
            // The close button handles its own press, so a drag never starts on it.
            titleBar.PointerPressed += (_, e) =>
            {
                if (e.Handled || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
                try { BeginMoveDrag(e); } catch (Exception ex) { Log.Debug("[Launcher] media dialog drag: {E}", ex.Message); }
            };

            // ---- the card ------------------------------------------------------------------
            var sourcePanel = new WrapPanel();
            foreach (var key in LauncherMediaSettings.Sources)
            {
                var chip = Chip(Loc.Get("launcher_media_src_" + key), key);
                ToolTip.SetTip(chip, Loc.Get("launcher_media_src_" + key + "_hint"));
                chip.IsCheckedChanged += (s, _) => { PaintChip(chip); SourceChip_Changed(chip); };
                _sourceChips.Add(chip);
                sourcePanel.Children.Add(chip);
            }
            _sourceHint = Copy("launcher_media_src_local_hint", 13, new Thickness(0, 2, 0, 0));

            _ratioSlider = new Slider { Minimum = 5, Maximum = 95, TickFrequency = 5, IsSnapToTickEnabled = true, VerticalAlignment = VerticalAlignment.Center };
            _ratioSlider.ValueChanged += (_, _) => RatioSlider_Changed();
            _ratioLabel = new TextBlock { Margin = new Thickness(14, 0, 0, 0), MinWidth = 90, Foreground = copy, FontSize = 13, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(_ratioLabel, 1);
            _ratioRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 14, 0, 0), IsVisible = false, Children = { _ratioSlider, _ratioLabel } };

            var nichePanel = new WrapPanel();
            foreach (var niche in FypOnlineCoordinator.Catalog)
            {
                var chip = Chip(niche.Label, niche.Id);
                chip.IsCheckedChanged += (s, _) => { PaintChip(chip); NicheChip_Changed(chip); };
                _nicheChips.Add(chip);
                nichePanel.Children.Add(chip);
            }

            _presetCombo.DisplayMemberBinding = new global::Avalonia.Data.Binding(nameof(AssetPreset.DisplayText));
            _presetCombo.SelectionChanged += (_, _) => PresetCombo_Changed();
            var browser = new Button
            {
                Content = Loc.Get("launcher_media_open_browser"), Margin = new Thickness(12, 0, 0, 0), Padding = new Thickness(4, 2),
                VerticalAlignment = VerticalAlignment.Center, Background = Brushes.Transparent, BorderThickness = new Thickness(0),
                Foreground = copy, FontSize = 13, Cursor = new Cursor(StandardCursorType.Hand),
            };
            browser.Click += (_, _) => { LauncherSfx.Click(); Close(); OpenAssetBrowser?.Invoke(); };
            Grid.SetColumn(browser, 1);

            _masterSlider = new Slider { Minimum = 0, Maximum = 100, TickFrequency = 5, IsSnapToTickEnabled = true, VerticalAlignment = VerticalAlignment.Center };
            _masterSlider.ValueChanged += (_, _) => MasterSlider_Changed();
            // The thumb let go: one soft cue at the new level, so the slider can be set by ear.
            _masterSlider.AddHandler(PointerReleasedEvent, (_, _) => LauncherSfx.Hover(), global::Avalonia.Interactivity.RoutingStrategies.Tunnel);
            _masterLabel = new TextBlock { Margin = new Thickness(14, 0, 0, 0), MinWidth = 48, Foreground = copy, FontSize = 13, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(_masterLabel, 1);

            var card = new Border
            {
                Background = Res("SurfaceBgBrush"), CornerRadius = new CornerRadius(14),
                Margin = new Thickness(20, 4, 20, 0), Padding = new Thickness(22, 20),
                Child = new ScrollViewer
                {
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    Content = new StackPanel
                    {
                        Children =
                        {
                            Copy("launcher_media_blurb"),
                            Eyebrow("launcher_media_source", new Thickness(0, 22, 0, 10)),
                            sourcePanel, _sourceHint, _ratioRow,
                            new StackPanel
                            {
                                Margin = new Thickness(0, 22, 0, 0),
                                Children =
                                {
                                    Eyebrow("launcher_media_niches", new Thickness(0, 0, 0, 10)),
                                    nichePanel,
                                    Copy("launcher_media_niches_hint", 13, new Thickness(0, 2, 0, 0)),
                                },
                            },
                            Eyebrow("launcher_media_library", new Thickness(0, 22, 0, 10)),
                            Copy("launcher_media_preset", 13),
                            new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 6, 0, 0), Children = { _presetCombo, browser } },
                            Copy("launcher_media_preset_hint", 13, new Thickness(0, 2, 0, 0)),
                            Eyebrow("launcher_media_sound", new Thickness(0, 22, 0, 10)),
                            Copy("launcher_media_master", 13),
                            new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 6, 0, 0), Children = { _masterSlider, _masterLabel } },
                            Copy("launcher_media_master_hint", 13, new Thickness(0, 2, 0, 0)),
                        },
                    },
                },
            };
            Grid.SetRow(card, 1);

            // ---- footnote and Done ---------------------------------------------------------
            var footnote = Copy("launcher_media_footnote", 12, new Thickness(4, 0, 120, 0));
            footnote.Foreground = muted;
            footnote.VerticalAlignment = VerticalAlignment.Center;
            var done = new Button
            {
                Content = Loc.Get("launcher_media_done"), HorizontalAlignment = HorizontalAlignment.Right, Width = 104, Height = 38,
                FontSize = 14, FontWeight = FontWeight.SemiBold, Foreground = Brushes.White, Background = Res("AccentGradientBrush"),
                BorderThickness = new Thickness(0), CornerRadius = new CornerRadius(10), Cursor = new Cursor(StandardCursorType.Hand),
                HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center,
            };
            done.Click += (_, _) => { LauncherSfx.Click(); Close(); };
            var foot = new Grid { Margin = new Thickness(20, 14, 20, 18), Children = { footnote, done } };
            Grid.SetRow(foot, 2);

            Content = new Border
            {
                BorderBrush = Res("GlassBorderBrush"), BorderThickness = new Thickness(1),
                Child = new Grid { RowDefinitions = new RowDefinitions("44,*,Auto"), Children = { titleBar, card, foot } },
            };

            BuildPresets();
            Refresh();
        }

        private static IBrush Res(string key) =>
            Application.Current != null && Application.Current.TryFindResource(key, Application.Current.ActualThemeVariant, out var v) && v is IBrush b
                ? b : Brushes.Transparent;

        /// <summary>WPF MediaChip: 13 px semibold, 15 px radius, 14,6 padding; on = the cyan wash and rim.</summary>
        private ToggleButton Chip(string label, string tag)
        {
            var chip = new ToggleButton
            {
                Content = label, Tag = tag, Margin = new Thickness(0, 0, 8, 8), Padding = new Thickness(14, 6),
                CornerRadius = new CornerRadius(15), FontSize = 13, FontWeight = FontWeight.SemiBold,
                Foreground = Res("TextLightBrush"), BorderThickness = new Thickness(1),
                Cursor = new Cursor(StandardCursorType.Hand),
            };
            PaintChip(chip);
            return chip;
        }

        private void PaintChip(ToggleButton chip)
        {
            bool on = chip.IsChecked == true;
            chip.Background = on ? ChipOn : ChipOff;
            chip.BorderBrush = on ? ChipOnRim : Res("GlassBorderBrush");
        }

        // ------------------------------------------------------------------ asset presets

        /// <summary>The panel's saved asset presets, the "All Assets" row first. Built once; Save,
        /// Update and Delete live on the Assets page, reached by the link beside the box.</summary>
        private void BuildPresets()
        {
            var s = CoreSettings.Current;
            if (!s.AssetPresets.Any(p => p.IsDefault)) s.AssetPresets.Insert(0, AssetPreset.CreateDefault());
            _presetCombo.ItemsSource = s.AssetPresets.ToList();
        }

        private void PresetCombo_Changed()
        {
            if (_syncing) return;
            try
            {
                var s = CoreSettings.Current;
                if (_presetCombo.SelectedItem is not AssetPreset preset) return;
                if (string.Equals(preset.Id, s.CurrentAssetPresetId, StringComparison.Ordinal)) return;
                LauncherSfx.Click();
                if (AssetPresetService.Apply(s, preset.Id) != null) SaveAndInvalidate();
                Refresh();   // the preset may have switched the source and niches too (ccp-bugs #1142)
                Log.Information("[Launcher] asset preset -> {Name}", preset.Name);
            }
            catch (Exception ex) { Log.Warning(ex, "[Launcher] asset preset change failed"); }
        }

        /// <summary>Pushes live settings into every control.</summary>
        internal void Refresh()
        {
            var s = CoreSettings.Current;
            _syncing = true;
            try
            {
                var source = s.MediaSource;
                foreach (var chip in _sourceChips)
                {
                    chip.IsChecked = string.Equals(chip.Tag as string, source, StringComparison.Ordinal);
                    PaintChip(chip);
                }
                _sourceHint.Text = Loc.Get("launcher_media_src_" + source + "_hint");

                _ratioSlider.Value = s.RemoteMediaRatio;
                _ratioLabel.Text = Loc.GetF("launcher_media_mixed_share", s.RemoteMediaRatio);
                _ratioRow.IsVisible = source == LauncherMediaSettings.SourceMixed;

                var selected = new HashSet<string>(s.FypOnlineNiches ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
                foreach (var chip in _nicheChips)
                {
                    chip.IsChecked = chip.Tag is string id && selected.Contains(id);
                    PaintChip(chip);
                }

                _masterSlider.Value = s.MasterVolume;
                _masterLabel.Text = $"{s.MasterVolume}%";

                var active = AssetPresetService.Active(s);
                _presetCombo.SelectedItem = (_presetCombo.ItemsSource as IEnumerable<AssetPreset>)?.FirstOrDefault(p => p.Id == active?.Id);
            }
            finally { _syncing = false; }
        }

        // ------------------------------------------------------------------ changes

        private void SourceChip_Changed(ToggleButton chip)
        {
            if (_syncing || _asking) return;
            LastChange = ChangeSourceAsync(chip);
        }

        private async Task ChangeSourceAsync(ToggleButton chip)
        {
            try
            {
                if (chip.Tag is not string key) return;
                var s = CoreSettings.Current;

                // Un-clicking the live chip would leave the app with no source at all.
                if (chip.IsChecked != true)
                {
                    if (string.Equals(key, s.MediaSource, StringComparison.Ordinal)) Refresh();
                    return;
                }
                if (string.Equals(key, s.MediaSource, StringComparison.Ordinal)) return;

                LauncherSfx.Click();

                // Leaving "local" starts fetching third-party content: asked exactly once, and never
                // of someone who already said yes to the For You feed (HasRemoteMediaConsent).
                if (key != LauncherMediaSettings.SourceLocal && !s.HasRemoteMediaConsent)
                {
                    Refresh();   // the chips stay on the live source while the question is open
                    if (!await AskRemoteMediaConsentAsync(s)) return;
                }

                Commit(s, key, resetChannels: true);
                Refresh();
                Log.Information("[Launcher] game media source -> {Source}", s.MediaSource);
            }
            catch (Exception ex) { Log.Warning(ex, "[Launcher] game media source change failed"); }
        }

        private void NicheChip_Changed(ToggleButton chip)
        {
            if (_syncing) return;
            try
            {
                var s = CoreSettings.Current;
                if (SelectedNiches().Count == 0)
                {
                    // The last niche stays on: an empty list would only fall back to the first
                    // catalogue niche anyway, and showing that as "nothing" would be a lie.
                    _syncing = true;
                    try { chip.IsChecked = true; PaintChip(chip); } finally { _syncing = false; }
                    LauncherSfx.Denied();
                    return;
                }
                LauncherSfx.Click();
                Commit(s, s.MediaSource, resetChannels: true);
            }
            catch (Exception ex) { Log.Warning(ex, "[Launcher] game media niche toggle failed"); }
        }

        private void RatioSlider_Changed()
        {
            if (_syncing) return;
            try
            {
                var s = CoreSettings.Current;
                // Dragging fires this per tick; no channel reset, the ratio is read at pick time.
                Commit(s, s.MediaSource, resetChannels: false);
                _ratioLabel.Text = Loc.GetF("launcher_media_mixed_share", s.RemoteMediaRatio);
            }
            catch (Exception ex) { Log.Debug("[Launcher] game media ratio change failed: {E}", ex.Message); }
        }

        private void MasterSlider_Changed()
        {
            if (_syncing) return;
            try
            {
                var s = CoreSettings.Current;
                int level = LauncherMediaSettings.ApplyMasterVolume(s, (int)Math.Round(_masterSlider.Value));
                _masterLabel.Text = $"{level}%";
                // Same two lines the Settings page's master slider runs (AudioSettingsBinder).
                LayeredAudio.Instance?.SetMasterVolumeLive();
                CoreSettings.Save();   // debounced: a whole drag is one write
            }
            catch (Exception ex) { Log.Debug("[Launcher] master volume change failed: {E}", ex.Message); }
        }

        private List<string> SelectedNiches() =>
            _nicheChips.Where(c => c.IsChecked == true && c.Tag is string).Select(c => (string)c.Tag!).ToList();

        /// <summary>Writes the dialog's state and tells the media services: rotation state and the
        /// asset pools were built for the old choice, so both are dropped.</summary>
        private void Commit(AppSettings s, string source, bool resetChannels)
        {
            LauncherMediaSettings.Apply(s, source, (int)Math.Round(_ratioSlider.Value), SelectedNiches());
            if (resetChannels) FypOnlineCoordinator.ResetAllChannels();
            SaveAndInvalidate();
        }

        private static void SaveAndInvalidate()
        {
            CoreSettings.Save();
            try { AssetSelection.NotifyChanged(); }
            catch (Exception ex) { Log.Debug("[Launcher] asset pools notify: {E}", ex.Message); }
        }

        /// <summary>The same one-time ask the Assets page makes, word for word, over this dialog.</summary>
        private async Task<bool> AskRemoteMediaConsentAsync(AppSettings s)
        {
            _asking = true;
            try
            {
                var title = LocOr("title_remote_media_consent", "Use Reddit media?");
                var message = LocOr("msg_remote_media_consent",
                    "Pull media from Reddit?\n\n" +
                    "The app will stream images and clips from the subreddits you pick, straight from your own machine. " +
                    "Nothing is saved to your disk, nothing is uploaded, and none of it goes through our servers.\n\n" +
                    "It is adult content and it is not curated by us - you choose the niches and subreddits, and only those are ever fetched.\n\n" +
                    "Turn it on?");
                bool yes = ConsentOverride != null
                    ? await ConsentOverride(title, message)
                    : await Dialogs.MessageDialog.ConfirmAsync(this, title, message, defaultToCancel: true);
                if (!yes) return false;
                s.RemoteMediaConsented = true;
                CoreSettings.Save();
                return true;
            }
            finally { _asking = false; }
        }

        private static string LocOr(string key, string english)
        {
            try
            {
                var value = Loc.Get(key);
                return string.IsNullOrEmpty(value) || string.Equals(value, key, StringComparison.Ordinal) ? english : value;
            }
            catch { return english; }
        }
    }
}
