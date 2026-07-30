using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Rectangle = System.Windows.Shapes.Rectangle;
using NAudio.Wave;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Helpers;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel
{
    // Awareness Engine tab: awareness/attention engine UI and handlers.
    public partial class MainWindow
    {
        #region Awareness Engine Tab

        private bool _awarenessSubscribed;

        /// <summary>
        /// Called each time the Awareness tab is opened. Loads current settings
        /// into the toggles, refreshes the pulse feed, and subscribes to fire events.
        /// </summary>
        private void SyncAwarenessTabUI()
        {
            // Toggle the premium gating overlay based on current subscription state.
            RefreshPremiumGate(AwarenessTab.AwarenessGate);

            var settings = App.Settings?.Current;
            if (settings == null) return;

            _isLoading = true;
            try
            {
                bool masterOn = settings.KeywordTriggersEnabled;
                if (AwarenessTab.ChkAwarenessMaster != null) AwarenessTab.ChkAwarenessMaster.IsChecked = masterOn;
                if (AwarenessTab.ChkAwarenessOcr != null) AwarenessTab.ChkAwarenessOcr.IsChecked = settings.ScreenOcrEnabled;
                if (AwarenessTab.ChkAwarenessKeyboard != null) AwarenessTab.ChkAwarenessKeyboard.IsChecked = settings.KeywordTriggersEnabled;
                if (AwarenessTab.ChkAwarenessIgnoreOwnUi != null) AwarenessTab.ChkAwarenessIgnoreOwnUi.IsChecked = settings.AwarenessIgnoreOwnUi;
                if (AwarenessTab.ChkAwarenessLoopProtection != null) AwarenessTab.ChkAwarenessLoopProtection.IsChecked = settings.AwarenessLoopProtectionEnabled;
                if (AwarenessTab.ChkAwarenessHighlight != null) AwarenessTab.ChkAwarenessHighlight.IsChecked = settings.KeywordHighlightEnabled;
                if (AwarenessTab.ChkAwarenessHighlightVisibleInCapture != null) AwarenessTab.ChkAwarenessHighlightVisibleInCapture.IsChecked = settings.OcrHighlightVisibleInCapture;
                SyncAwarenessHighlightSwatchUi();

                if (AwarenessTab.SliderAwarenessGlobalCooldown != null)
                {
                    var v = Math.Clamp(settings.KeywordGlobalCooldownSeconds, 1, 180);
                    AwarenessTab.SliderAwarenessGlobalCooldown.Value = v;
                    if (AwarenessTab.TxtAwarenessGlobalCooldown != null) AwarenessTab.TxtAwarenessGlobalCooldown.Text = $"{v}s";
                }
                if (AwarenessTab.SliderAwarenessSameWordCooldown != null)
                {
                    var v = Math.Clamp(settings.KeywordPerKeywordCooldownSeconds, 1, 180);
                    AwarenessTab.SliderAwarenessSameWordCooldown.Value = v;
                    if (AwarenessTab.TxtAwarenessSameWordCooldown != null) AwarenessTab.TxtAwarenessSameWordCooldown.Text = $"{v}s";
                }

                UpdateAwarenessStatusIndicator(masterOn);
            }
            finally
            {
                _isLoading = false;
            }

            // Subscribe once to fire events so the feed refreshes live while the tab is open.
            if (!_awarenessSubscribed && App.KeywordTriggers != null)
            {
                App.KeywordTriggers.TriggerFired += OnAwarenessTriggerFired;
                _awarenessSubscribed = true;
            }

            // Subscribe once to preset install/uninstall events so the Exclusives
            // trigger list and the Awareness preset cards both stay in sync with
            // whatever the user does in a preset detail dialog, regardless of
            // which tab they're on when the change happens.
            if (!_presetsChangedSubscribed && App.KeywordPresets != null)
            {
                App.KeywordPresets.PresetsChanged += OnPresetsChanged;
                _presetsChangedSubscribed = true;
            }

            RefreshAwarenessPulseFeed();
            RefreshAwarenessPresetCards();
            // Outside the _isLoading block above because it manages that flag itself, and the
            // seen-app chips have to be rebuilt on every open - the list of apps the user has
            // touched grows while they are on other tabs.
            RefreshAwarenessAppScopeUi();
        }

        private bool _presetsChangedSubscribed;

        private void OnPresetsChanged(object? sender, EventArgs e)
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.HasShutdownStarted) return;
            dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    RefreshKeywordTriggerList();     // Exclusives trigger list — drops removed preset clones
                    RefreshAwarenessPresetCards();   // Awareness tab card highlights
                }
                catch (Exception ex)
                {
                    App.Logger?.Debug("OnPresetsChanged refresh failed: {Error}", ex.Message);
                }
            }));
        }

        private void OnAwarenessTriggerFired(object? sender, KeywordTrigger trigger)
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.HasShutdownStarted) return;
            dispatcher.BeginInvoke(new Action(() =>
            {
                if (AwarenessTab?.Visibility == Visibility.Visible)
                    RefreshAwarenessPulseFeed();
            }));
        }

        private void RefreshAwarenessPulseFeed()
        {
            if (AwarenessTab.AwarenessPulseFeed == null) return;

            var fires = App.KeywordTriggers?.GetRecentFires();
            AwarenessTab.AwarenessPulseFeed.Children.Clear();

            if (fires == null || fires.Count == 0)
            {
                if (AwarenessTab.TxtAwarenessPulseEmpty == null)
                {
                    var empty = new TextBlock
                    {
                        Text = "Nothing yet. Enable the engine and she'll start noticing things.",
                        Foreground = (System.Windows.Media.Brush)FindResource("TextMutedBrush"),
                        FontSize = 12,
                        FontStyle = FontStyles.Italic,
                        TextAlignment = TextAlignment.Center,
                        Margin = new Thickness(0, 18, 0, 18)
                    };
                    AwarenessTab.AwarenessPulseFeed.Children.Add(empty);
                }
                else
                {
                    AwarenessTab.AwarenessPulseFeed.Children.Add(AwarenessTab.TxtAwarenessPulseEmpty);
                }
                if (AwarenessTab.TxtAwarenessFireCount != null) AwarenessTab.TxtAwarenessFireCount.Text = "";
                return;
            }

            // Show up to 5 most recent fires
            int shown = 0;
            foreach (var f in fires)
            {
                if (shown >= 5) break;
                AwarenessTab.AwarenessPulseFeed.Children.Add(BuildPulseRow(f));
                shown++;
            }

            if (AwarenessTab.TxtAwarenessFireCount != null)
                AwarenessTab.TxtAwarenessFireCount.Text = fires.Count == 1 ? "1 fire today" : $"{fires.Count} fires today";
        }

        private Border BuildPulseRow(TriggerFireRecord f)
        {
            var row = new Border
            {
                Background = (System.Windows.Media.Brush)FindResource("DarkerBgBrush"),
                BorderBrush = new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#2A2A40")),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12, 8, 12, 8),
                Margin = new Thickness(0, 0, 0, 6)
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(6) });   // 0 accent bar
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });     // 1 keyword
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });     // 2 action chips
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // 3 filler
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });     // 4 source chip
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });     // 5 time

            // Pink accent bar
            var dot = new System.Windows.Shapes.Rectangle
            {
                Width = 3,
                Height = 16,
                Fill = (System.Windows.Media.Brush)FindResource("PinkBrush"),
                VerticalAlignment = VerticalAlignment.Center,
                RadiusX = 1.5,
                RadiusY = 1.5
            };
            Grid.SetColumn(dot, 0);
            grid.Children.Add(dot);

            // Quote marks around the keyword
            var keywordBlock = new TextBlock
            {
                Text = $"\"{f.Keyword}\"",
                Foreground = System.Windows.Media.Brushes.White,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(10, 0, 0, 0)
            };
            Grid.SetColumn(keywordBlock, 1);
            grid.Children.Add(keywordBlock);

            // Action chip strip — one small emoji per action the trigger fired,
            // with a tooltip explaining what the action does on hover.
            var chipStrip = BuildActionChipStrip(f.ActionKeys);
            Grid.SetColumn(chipStrip, 2);
            grid.Children.Add(chipStrip);

            // Source chip
            var sourceChip = new Border
            {
                Background = new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#2A2A4A")),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(6, 2, 6, 2),
                Margin = new Thickness(10, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            sourceChip.Child = new TextBlock
            {
                Text = f.Source.ToUpperInvariant(),
                Foreground = (System.Windows.Media.Brush)FindResource("TextDimBrush"),
                FontSize = 9,
                FontWeight = FontWeights.Bold
            };
            Grid.SetColumn(sourceChip, 4);
            grid.Children.Add(sourceChip);

            // Time ago
            var timeBlock = new TextBlock
            {
                Text = FormatTimeAgo(f.FiredAt),
                Foreground = (System.Windows.Media.Brush)FindResource("TextMutedBrush"),
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(10, 0, 0, 0)
            };
            Grid.SetColumn(timeBlock, 5);
            grid.Children.Add(timeBlock);

            row.Child = grid;
            return row;
        }

        /// <summary>
        /// Builds a horizontal row of small emoji "chip" icons representing every
        /// action the trigger fired. Each chip has a tooltip that explains what
        /// the action does when hovered. The emoji/tooltip map is kept in
        /// <see cref="GetActionChipDisplay"/> so it can be extended as new
        /// action types are added.
        /// </summary>
        private StackPanel BuildActionChipStrip(List<string> actionKeys)
        {
            var strip = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(12, 0, 0, 0)
            };

            if (actionKeys == null || actionKeys.Count == 0) return strip;

            foreach (var key in actionKeys)
            {
                var (icon, tooltip) = GetActionChipDisplay(key);
                if (string.IsNullOrEmpty(icon)) continue;

                var chip = new TextBlock
                {
                    Text = icon,
                    FontSize = 13,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 4, 0),
                    ToolTip = tooltip,
                };
                // Surface the tooltip after a short hover delay.
                ToolTipService.SetInitialShowDelay(chip, 200);
                ToolTipService.SetBetweenShowDelay(chip, 0);

                strip.Children.Add(chip);
            }

            return strip;
        }

        /// <summary>
        /// Returns the (emoji, tooltip) display pair for a fire-record action key.
        /// Keys come from <c>KeywordTriggerService.BuildActionKeySnapshot</c> and
        /// look like <c>"PlayAudio"</c>, <c>"VisualEffect:ImageFlash"</c>, <c>"AddXp:5"</c>.
        /// Returns empty values for unknown keys so the chip strip silently skips them.
        /// </summary>
        private static (string icon, string tooltip) GetActionChipDisplay(string key)
        {
            if (string.IsNullOrEmpty(key)) return ("", "");

            // Parse leading discriminator and optional ":argument"
            var colon = key.IndexOf(':');
            var type = colon < 0 ? key : key.Substring(0, colon);
            var arg = colon < 0 ? "" : key.Substring(colon + 1);

            return type switch
            {
                "PlayAudio"      => ("🔊", "Plays an audio clip when the word is detected"),
                "Highlight"      => ("👁", "Draws a glowing box around the matched word on screen (OCR matches only)"),
                "Haptic"         => ("💥", "Fires a haptic vibration pattern on connected devices"),
                // "AddXp" intentionally omitted — progression XP is an internal mechanic,
                // not a user-facing trigger effect.
                "AvatarComment"  => ("💬", "Makes the avatar comment on the matched word (AI + canned fallback)"),
                "ExtendSession"  => ("⏱", string.IsNullOrEmpty(arg)
                                        ? "Extends the current session"
                                        : $"Extends the current session by {arg} minutes"),
                "ChasterAddTime" => ("🔒", string.IsNullOrEmpty(arg)
                                        ? "Adds time to the Chaster lock"
                                        : $"Adds {arg} minutes to the Chaster lock"),
                "VisualEffect"   => arg switch
                {
                    "SubliminalFlash" => ("✨", "Flashes a random word from your subliminal pool"),
                    "ExactSubliminal" => ("🔤", "Flashes the matched keyword itself as subliminal text"),
                    "ImageFlash"      => ("⚡", "Fires a flash burst image when the word is detected"),
                    "OverlayPulse"    => ("🌫", "Briefly intensifies the screen overlay"),
                    "MindWipe"        => ("🧠", "Triggers the MindWipe effect"),
                    "Bubbles"         => ("🫧", "Spawns bubbles on screen"),
                    _                 => ("✨", $"Fires visual effect: {arg}"),
                },
                _ => ("", ""),
            };
        }

        private static string FormatTimeAgo(DateTime t)
        {
            var delta = DateTime.Now - t;
            if (delta.TotalSeconds < 10) return "just now";
            if (delta.TotalSeconds < 60) return $"{(int)delta.TotalSeconds}s ago";
            if (delta.TotalMinutes < 60) return $"{(int)delta.TotalMinutes}m ago";
            if (delta.TotalHours < 24) return $"{(int)delta.TotalHours}h ago";
            return t.ToString("HH:mm");
        }

        private void UpdateAwarenessStatusIndicator(bool on)
        {
            if (AwarenessTab.AwarenessStatusDot == null || AwarenessTab.TxtAwarenessStatus == null) return;
            if (on)
            {
                AwarenessTab.AwarenessStatusDot.Fill = (System.Windows.Media.Brush)FindResource("PinkBrush");
                AwarenessTab.TxtAwarenessStatus.Text = "Live";
                AwarenessTab.TxtAwarenessStatus.Foreground = (System.Windows.Media.Brush)FindResource("PinkBrush");
            }
            else
            {
                AwarenessTab.AwarenessStatusDot.Fill = new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#606060"));
                AwarenessTab.TxtAwarenessStatus.Text = "Off";
                AwarenessTab.TxtAwarenessStatus.Foreground = new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#A0A0A0"));
            }
        }

        internal void ChkAwarenessMaster_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            bool on = AwarenessTab.ChkAwarenessMaster?.IsChecked == true;

            if (on && !KeywordTriggerService.HasAccess())
            {
                _isLoading = true;
                try { AwarenessTab.ChkAwarenessMaster!.IsChecked = false; }
                finally { _isLoading = false; }
                MessageBox.Show(
                    Loc.Get("msg_keyword_triggers_patreon_only"),
                    Loc.Get("title_patreon_feature"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var settings = App.Settings?.Current;
            if (settings == null) return;

            if (on)
            {
                settings.KeywordTriggersEnabled = true;
                App.KeywordTriggers?.Start();
                _keyboardHook?.Start();
                if (settings.ScreenOcrEnabled)
                    App.ScreenOcr?.Start();
            }
            else
            {
                settings.KeywordTriggersEnabled = false;
                App.KeywordTriggers?.Stop();
                App.ScreenOcr?.Stop();
                if (settings.PanicKeyEnabled != true)
                    _keyboardHook?.Stop();
            }

            // Keep the sub-toggle in sync with master so the UI reads consistently.
            _isLoading = true;
            try
            {
                if (AwarenessTab.ChkAwarenessKeyboard != null) AwarenessTab.ChkAwarenessKeyboard.IsChecked = on;
            }
            finally { _isLoading = false; }

            UpdateAwarenessStatusIndicator(on);
            UpdateKeywordTriggersButtonState();
            App.Settings?.Save();
        }

        internal void ChkAwarenessOcr_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            bool on = AwarenessTab.ChkAwarenessOcr?.IsChecked == true;

            if (on && !KeywordTriggerService.HasAccess())
            {
                _isLoading = true;
                try { AwarenessTab.ChkAwarenessOcr!.IsChecked = false; }
                finally { _isLoading = false; }
                MessageBox.Show(
                    Loc.Get("msg_screen_ocr_patreon_only"),
                    Loc.Get("title_patreon_feature"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var settings = App.Settings?.Current;
            if (settings == null) return;
            settings.ScreenOcrEnabled = on;

            if (on && settings.KeywordTriggersEnabled)
                App.ScreenOcr?.Start();
            else
                App.ScreenOcr?.Stop();

            // Mirror into the legacy Exclusives OCR checkbox so both screens agree.
            if (PatreonTab.ChkScreenOcrEnabled != null && PatreonTab.ChkScreenOcrEnabled.IsChecked != on)
            {
                _isLoading = true;
                try { PatreonTab.ChkScreenOcrEnabled.IsChecked = on; }
                finally { _isLoading = false; }
            }

            App.Settings?.Save();
        }

        internal void ChkAwarenessKeyboard_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            bool on = AwarenessTab.ChkAwarenessKeyboard?.IsChecked == true;
            var settings = App.Settings?.Current;
            if (settings == null) return;

            // Keyboard is one signal source — toggle it independently of the master switch.
            // If turning keyboard ON and master is OFF, turn master ON too.
            if (on && AwarenessTab.ChkAwarenessMaster?.IsChecked != true)
            {
                AwarenessTab.ChkAwarenessMaster!.IsChecked = true; // triggers ChkAwarenessMaster_Changed
            }
            else if (!on)
            {
                // Turning keyboard off — just stop the keyboard hook if nothing else needs it.
                // Don't turn off master (other sources like OCR may still be active).
                if (settings.PanicKeyEnabled != true && !settings.ScreenOcrEnabled)
                    _keyboardHook?.Stop();
            }
        }

        internal void ChkAwarenessIgnoreOwnUi_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var settings = App.Settings?.Current;
            if (settings == null) return;
            settings.AwarenessIgnoreOwnUi = AwarenessTab.ChkAwarenessIgnoreOwnUi?.IsChecked == true;
            App.Settings?.Save();
        }

        // -------------------------------------------------------------------------------
        // App scope: which applications triggers may fire in
        //
        // The service side is KeywordTriggerService's "Foreground app scope" region; this is
        // just the editor for it. Nothing here starts or stops anything - the gate is read
        // live at match time, so a change takes effect on the very next keystroke with no
        // restart and nothing to re-arm.
        // -------------------------------------------------------------------------------

        /// <summary>
        /// Pushes the three app-scope settings into the controls and shows or hides the list
        /// depending on the mode. Called from SyncAwarenessTabUI, and again after any change,
        /// so the collapsed/expanded state can never disagree with the selected mode.
        /// </summary>
        private void RefreshAwarenessAppScopeUi()
        {
            var settings = App.Settings?.Current;
            var tab = AwarenessTab;
            if (settings == null || tab == null) return;

            _isLoading = true;
            try
            {
                if (tab.CmbAwarenessAppScope != null)
                {
                    // Matched on Tag rather than index so reordering the XAML items cannot
                    // silently remap a saved setting onto the wrong mode.
                    var wanted = settings.KeywordTriggerAppScope.ToString();
                    foreach (var item in tab.CmbAwarenessAppScope.Items.OfType<ComboBoxItem>())
                        item.IsSelected = string.Equals(item.Tag as string, wanted, StringComparison.Ordinal);
                }

                if (tab.TxtAwarenessAppList != null)
                    tab.TxtAwarenessAppList.Text = string.Join(", ", settings.KeywordTriggerApps ?? new List<string>());

                if (tab.ChkAwarenessIgnoreOwnFocus != null)
                    tab.ChkAwarenessIgnoreOwnFocus.IsChecked = settings.KeywordTriggerIgnoreOwnFocus;

                // The list is meaningless in Everywhere mode, and leaving it visible invites
                // someone to fill it in and wonder why nothing changed.
                if (tab.AwarenessAppListPanel != null)
                {
                    tab.AwarenessAppListPanel.Visibility =
                        settings.KeywordTriggerAppScope == AwarenessAppScope.Everywhere
                            ? Visibility.Collapsed
                            : Visibility.Visible;
                }
            }
            finally
            {
                _isLoading = false;
            }

            RefreshAwarenessSeenAppChips();
        }

        /// <summary>
        /// Offers the apps recently seen in the foreground as one-click additions.
        ///
        /// Without this the list is close to unusable: process names are not app names, and
        /// nobody knows offhand that Teams is "ms-teams". The names come from the service's
        /// in-memory ring (never persisted, our own process excluded), so an app the user has
        /// not touched since launch simply is not offered - typing it by hand still works.
        /// </summary>
        private void RefreshAwarenessSeenAppChips()
        {
            var tab = AwarenessTab;
            if (tab?.AwarenessSeenAppsPanel == null) return;

            try
            {
                tab.AwarenessSeenAppsPanel.Children.Clear();

                var settings = App.Settings?.Current;
                var listed = settings?.KeywordTriggerApps ?? new List<string>();

                // Already-listed apps are dropped rather than shown inert: a chip that does
                // nothing when clicked reads as broken.
                var seen = (App.KeywordTriggers?.GetRecentForegroundApps() ?? Array.Empty<string>())
                    .Where(a => !KeywordTriggerService.MatchesAppList(listed, a))
                    .ToList();

                if (tab.TxtAwarenessSeenAppsLabel != null)
                    tab.TxtAwarenessSeenAppsLabel.Visibility = seen.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

                foreach (var app in seen)
                {
                    var chip = new Button
                    {
                        Content = "+ " + app,
                        Tag = app,
                        FontSize = 10,
                        Padding = new Thickness(7, 3, 7, 3),
                        Margin = new Thickness(0, 0, 5, 5),
                        Cursor = Cursors.Hand,
                        Foreground = Brushes.White,
                        Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x44)),
                        BorderBrush = new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x5A)),
                        BorderThickness = new Thickness(1),
                        ToolTip = $"Add '{app}' to the list"
                    };
                    chip.Click += AwarenessSeenAppChip_Click;
                    tab.AwarenessSeenAppsPanel.Children.Add(chip);
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("RefreshAwarenessSeenAppChips failed: {Error}", ex.Message);
            }
        }

        private void AwarenessSeenAppChip_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not string app) return;

            var settings = App.Settings?.Current;
            if (settings == null) return;

            var list = new List<string>(settings.KeywordTriggerApps ?? new List<string>());
            if (!KeywordTriggerService.MatchesAppList(list, app)) list.Add(app);
            settings.KeywordTriggerApps = list;
            App.Settings?.Save();

            App.Logger?.Information("Awareness app scope: added '{App}' ({Mode}, {Count} listed)",
                app, settings.KeywordTriggerAppScope, list.Count);

            RefreshAwarenessAppScopeUi();
        }

        internal void CmbAwarenessAppScope_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoading) return;

            var settings = App.Settings?.Current;
            if (settings == null) return;

            var tag = (AwarenessTab.CmbAwarenessAppScope?.SelectedItem as ComboBoxItem)?.Tag as string;
            if (!Enum.TryParse<AwarenessAppScope>(tag, out var scope)) return;

            settings.KeywordTriggerAppScope = scope;
            App.Settings?.Save();

            App.Logger?.Information("Awareness app scope set to {Mode} ({Count} apps listed)",
                scope, settings.KeywordTriggerApps?.Count ?? 0);

            RefreshAwarenessAppScopeUi();
        }

        internal void TxtAwarenessAppList_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;
            CommitAwarenessAppList();
            // Move focus off the box so the commit is visibly acknowledged (the text is
            // rewritten in canonical form) rather than looking like nothing happened.
            Keyboard.ClearFocus();
        }

        internal void TxtAwarenessAppList_LostFocus(object sender, RoutedEventArgs e) => CommitAwarenessAppList();

        /// <summary>
        /// Parses the box and stores the cleaned list. Deliberately commit-on-Enter/blur rather
        /// than per-keystroke: mid-typing text like "chrome, di" would otherwise be saved as a
        /// real entry, and in Only-in mode that momentarily narrows the scope to nonsense.
        /// </summary>
        private void CommitAwarenessAppList()
        {
            if (_isLoading) return;

            var settings = App.Settings?.Current;
            var box = AwarenessTab?.TxtAwarenessAppList;
            if (settings == null || box == null) return;

            var parsed = KeywordTriggerService.ParseAppList(box.Text);
            var existing = settings.KeywordTriggerApps ?? new List<string>();
            if (parsed.SequenceEqual(existing, StringComparer.OrdinalIgnoreCase)) return;

            settings.KeywordTriggerApps = parsed;
            App.Settings?.Save();

            App.Logger?.Information("Awareness app scope list set to {Count} app(s) ({Mode})",
                parsed.Count, settings.KeywordTriggerAppScope);

            RefreshAwarenessAppScopeUi();
        }

        internal void ChkAwarenessIgnoreOwnFocus_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var settings = App.Settings?.Current;
            if (settings == null) return;
            settings.KeywordTriggerIgnoreOwnFocus = AwarenessTab.ChkAwarenessIgnoreOwnFocus?.IsChecked == true;
            App.Settings?.Save();
        }

        internal void ChkAwarenessLoopProtection_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var settings = App.Settings?.Current;
            if (settings == null) return;
            settings.AwarenessLoopProtectionEnabled = AwarenessTab.ChkAwarenessLoopProtection?.IsChecked == true;
            App.Settings?.Save();
        }

        // ---- Awareness tab: on-screen keyword highlight toggle + color picker ----

        internal void ChkAwarenessHighlight_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var settings = App.Settings?.Current;
            if (settings == null) return;
            settings.KeywordHighlightEnabled = AwarenessTab.ChkAwarenessHighlight?.IsChecked == true;
            App.Settings?.Save();

            // Keep the Exclusives-tab mirror checkbox in sync so both UIs agree.
            if (PatreonTab.ChkKeywordHighlightEnabled != null && PatreonTab.ChkKeywordHighlightEnabled.IsChecked != settings.KeywordHighlightEnabled)
                PatreonTab.ChkKeywordHighlightEnabled.IsChecked = settings.KeywordHighlightEnabled;

            SyncAwarenessHighlightSwatchUi();
        }

        internal void ChkAwarenessHighlightVisibleInCapture_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var settings = App.Settings?.Current;
            if (settings == null) return;
            settings.OcrHighlightVisibleInCapture = AwarenessTab.ChkAwarenessHighlightVisibleInCapture?.IsChecked == true;
            App.Settings?.Save();

            // Flip display affinity on all existing overlay windows immediately.
            App.KeywordHighlight?.RefreshCaptureVisibility();

            // Mirror the Exclusives-tab checkbox so both stay in agreement.
            if (PatreonTab.ChkHighlightVisibleInCapture != null && PatreonTab.ChkHighlightVisibleInCapture.IsChecked != settings.OcrHighlightVisibleInCapture)
                PatreonTab.ChkHighlightVisibleInCapture.IsChecked = settings.OcrHighlightVisibleInCapture;
        }

        internal void AwarenessHighlightSwatch_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement fe) return;
            var hex = fe.Tag?.ToString();
            if (string.IsNullOrEmpty(hex)) return;
            ApplyAwarenessHighlightColor(hex);
        }

        internal void TxtAwarenessHighlightHex_LostFocus(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            ApplyAwarenessHighlightColor(AwarenessTab.TxtAwarenessHighlightHex?.Text);
        }

        internal void TxtAwarenessHighlightHex_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Enter)
            {
                ApplyAwarenessHighlightColor(AwarenessTab.TxtAwarenessHighlightHex?.Text);
                e.Handled = true;
            }
        }

        /// <summary>
        /// Validates a hex color string and writes it to settings. Silently no-ops
        /// on malformed input so a half-typed value in the textbox doesn't wipe
        /// the user's previously-set color.
        /// </summary>
        private void ApplyAwarenessHighlightColor(string? hex)
        {
            if (string.IsNullOrWhiteSpace(hex)) return;
            var trimmed = hex.Trim();
            if (!trimmed.StartsWith('#')) trimmed = "#" + trimmed;

            try
            {
                var obj = System.Windows.Media.ColorConverter.ConvertFromString(trimmed);
                if (obj is not System.Windows.Media.Color) return;
            }
            catch { return; }

            var settings = App.Settings?.Current;
            if (settings == null) return;
            settings.KeywordHighlightColor = trimmed;
            App.Settings?.Save();

            SyncAwarenessHighlightSwatchUi();
        }

        /// <summary>
        /// Refreshes the swatch row and hex textbox from current settings so the
        /// UI reflects any change (from load, swatch click, or the Exclusives tab).
        /// </summary>
        private void SyncAwarenessHighlightSwatchUi()
        {
            var settings = App.Settings?.Current;
            if (settings == null) return;

            if (AwarenessTab.TxtAwarenessHighlightHex != null)
                AwarenessTab.TxtAwarenessHighlightHex.Text = settings.KeywordHighlightColor;

            // Dim all swatches then re-highlight the selected one so the user
            // can see which preset (if any) matches their current color.
            var selected = settings.KeywordHighlightColor?.ToUpperInvariant() ?? "";
            foreach (var swatch in new[] {
                AwarenessTab.SwatchHighlightPink, AwarenessTab.SwatchHighlightCyan, AwarenessTab.SwatchHighlightLime,
                AwarenessTab.SwatchHighlightOrange, AwarenessTab.SwatchHighlightViolet, AwarenessTab.SwatchHighlightWhite })
            {
                if (swatch == null) continue;
                var tag = swatch.Tag?.ToString()?.ToUpperInvariant() ?? "";
                swatch.BorderBrush = tag == selected
                    ? System.Windows.Media.Brushes.White
                    : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x3A, 0x3A, 0x5A));
                swatch.BorderThickness = tag == selected ? new Thickness(2) : new Thickness(1);
            }
        }

        private void AwarenessPresetCard_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement fe) return;
            var presetId = fe.Tag?.ToString();
            if (string.IsNullOrEmpty(presetId)) return;

            var preset = App.KeywordPresets?.GetPreset(presetId);
            if (preset == null) return;

            var dlg = new AwarenessPresetDetailDialog(preset) { Owner = this };
            dlg.ShowDialog();

            if (dlg.Changed)
                RefreshAwarenessPresetCards();
        }

        /// <summary>
        /// Rebuilds the Awareness tab preset card grid from the current preset service
        /// state. Cards are generated imperatively so that a trailing "+ New Preset"
        /// tile can share the wrap flow with the real preset cards.
        ///
        /// Called on tab open and after any install/uninstall/create/delete so the
        /// "installed" pip and card list stay in sync.
        /// </summary>
        public void RefreshAwarenessPresetCards()
        {
            if (AwarenessTab.AwarenessPresetItems == null) return;
            AwarenessTab.AwarenessPresetItems.Children.Clear();

            var presets = App.KeywordPresets?.VisiblePresets;
            if (presets != null)
            {
                foreach (var preset in presets)
                {
                    if (preset == null) continue;
                    AwarenessTab.AwarenessPresetItems.Children.Add(BuildAwarenessPresetCard(preset));
                }
            }

            // Trailing "New Preset" card — starts a fresh user-authored preset.
            AwarenessTab.AwarenessPresetItems.Children.Add(BuildNewPresetCard());

            UpdateAwarenessAdvancedLinkText();
        }

        /// <summary>
        /// Imperative version of the old DataTemplate card markup. Produces the same
        /// pink-accented tile with icon, name, description, AI badge, and ✓ Installed
        /// indicator. Click routes to <see cref="AwarenessPresetCard_Click"/>.
        /// </summary>
        private System.Windows.Controls.Border BuildAwarenessPresetCard(KeywordTriggerPreset preset)
        {
            var card = new System.Windows.Controls.Border
            {
                Background = (System.Windows.Media.Brush)FindResource("SurfaceBgBrush"),
                BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x2E, 0x2E, 0x48)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(16, 14, 16, 14),
                Margin = new Thickness(0, 0, 12, 12),
                Width = 218,
                Height = 150,
                Cursor = System.Windows.Input.Cursors.Hand,
                Tag = preset.Id,
                ToolTip = preset.LongDescription,
            };
            System.Windows.Controls.ToolTipService.SetShowDuration(card, 20000);
            System.Windows.Controls.ToolTipService.SetInitialShowDelay(card, 400);
            card.MouseLeftButtonUp += AwarenessPresetCard_Click;

            // Active presets get a pink accent border so the grid reads at a glance.
            if (preset.MasterEnabled)
            {
                card.BorderBrush = (System.Windows.Media.Brush)FindResource("PinkBrush");
                card.BorderThickness = new Thickness(1.5);
            }

            // Two-row layout: content fills, an action row docks to the bottom so the
            // activate toggle / trash button sit at a consistent spot on every card.
            var layout = new System.Windows.Controls.Grid();
            layout.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            layout.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = GridLength.Auto });
            card.Child = layout;

            var stack = new System.Windows.Controls.StackPanel { ClipToBounds = true };
            System.Windows.Controls.Grid.SetRow(stack, 0);
            layout.Children.Add(stack);

            // Top row: emoji + optional AI badge.
            var topRow = new System.Windows.Controls.StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
            };
            stack.Children.Add(topRow);

            topRow.Children.Add(new System.Windows.Controls.Image
            {
                Source = Helpers.EmojiImage.Get(preset.Icon),
                Width = 28,
                Height = 28,
                Stretch = System.Windows.Media.Stretch.Uniform,
            });

            if (preset.RequiresAi)
            {
                topRow.Children.Add(new System.Windows.Controls.Border
                {
                    Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x2A, 0x1A, 0x3E)),
                    BorderBrush = (System.Windows.Media.Brush)FindResource("PinkBrush"),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(5, 1, 5, 1),
                    Margin = new Thickness(8, 4, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Child = new System.Windows.Controls.TextBlock
                    {
                        Text = "AI",
                        Foreground = (System.Windows.Media.Brush)FindResource("PinkBrush"),
                        FontSize = 9,
                        FontWeight = FontWeights.Bold,
                    },
                });
            }

            // Name.
            stack.Children.Add(new System.Windows.Controls.TextBlock
            {
                Text = preset.Name,
                Foreground = System.Windows.Media.Brushes.White,
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 6, 0, 2),
            });

            // Description (trimmed by card size).
            stack.Children.Add(new System.Windows.Controls.TextBlock
            {
                Text = preset.Description,
                Foreground = (System.Windows.Media.Brush)FindResource("TextMutedBrush"),
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
            });

            // Action row (docked bottom): activate/deactivate toggle + optional trash.
            var actionRow = new System.Windows.Controls.Grid { Margin = new Thickness(0, 10, 0, 0) };
            actionRow.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            actionRow.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = GridLength.Auto });
            System.Windows.Controls.Grid.SetRow(actionRow, 1);
            layout.Children.Add(actionRow);

            var toggle = BuildPresetToggleButton(preset);
            System.Windows.Controls.Grid.SetColumn(toggle, 0);
            actionRow.Children.Add(toggle);

            // Trash button only for user-created presets — built-ins reappear on launch.
            if (!preset.IsBuiltIn)
            {
                var trash = BuildPresetTrashButton(preset);
                System.Windows.Controls.Grid.SetColumn(trash, 1);
                actionRow.Children.Add(trash);
            }

            return card;
        }

        /// <summary>
        /// Inline pill toggle on a preset card. Activates/deactivates the preset
        /// without opening the detail dialog. Being a Button, it swallows the click
        /// so the card's own MouseLeftButtonUp (open detail) does not also fire.
        /// </summary>
        private System.Windows.Controls.Button BuildPresetToggleButton(KeywordTriggerPreset preset)
        {
            var active = preset.MasterEnabled;
            var btn = new System.Windows.Controls.Button
            {
                Content = active ? "✓ Active" : "Activate",
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Padding = new Thickness(10, 4, 10, 4),
                BorderThickness = new Thickness(1),
                Cursor = System.Windows.Input.Cursors.Hand,
                HorizontalAlignment = HorizontalAlignment.Left,
                Foreground = active ? System.Windows.Media.Brushes.White : (System.Windows.Media.Brush)FindResource("PinkBrush"),
                Background = active
                    ? (System.Windows.Media.Brush)FindResource("PinkBrush")
                    : System.Windows.Media.Brushes.Transparent,
                BorderBrush = (System.Windows.Media.Brush)FindResource("PinkBrush"),
                ToolTip = active ? "Turn this preset off" : "Turn this preset on",
            };
            btn.Click += (_, _) =>
            {
                if (App.KeywordPresets == null) return;
                if (App.KeywordPresets.IsInstalled(preset.Id))
                    App.KeywordPresets.UninstallPreset(preset.Id);
                else
                    App.KeywordPresets.InstallPreset(preset.Id);
                // PresetsChanged → OnPresetsChanged rebuilds the grid.
            };
            return btn;
        }

        /// <summary>
        /// Small trash button on a custom preset card. Confirms, then deletes the
        /// preset (deactivating it first so cloned triggers / canned phrases are
        /// cleaned up). Built-in presets never get this button.
        /// </summary>
        private System.Windows.Controls.Button BuildPresetTrashButton(KeywordTriggerPreset preset)
        {
            var btn = new System.Windows.Controls.Button
            {
                Content = "🗑",
                FontSize = 12,
                Padding = new Thickness(6, 3, 6, 3),
                BorderThickness = new Thickness(0),
                Cursor = System.Windows.Input.Cursors.Hand,
                Background = System.Windows.Media.Brushes.Transparent,
                Foreground = (System.Windows.Media.Brush)FindResource("TextMutedBrush"),
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = "Delete this preset",
            };
            btn.Click += (_, _) =>
            {
                var label = string.IsNullOrWhiteSpace(preset.Name) ? "this preset" : $"\"{preset.Name}\"";
                var confirm = System.Windows.MessageBox.Show(this,
                    $"Delete {label}?\n\nThis removes the preset and all its triggers permanently.",
                    "Delete preset", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (confirm != MessageBoxResult.Yes) return;

                if (App.KeywordPresets?.IsInstalled(preset.Id) == true)
                    App.KeywordPresets.UninstallPreset(preset.Id);

                var list = App.Settings?.Current?.KeywordTriggerPresets;
                list?.RemoveAll(p => p.Id == preset.Id);
                App.Settings?.Save();

                RefreshAwarenessPresetCards();
            };
            return btn;
        }

        /// <summary>
        /// Dashed-border "+ New Preset" tile that sits at the end of the preset grid
        /// and kicks off the user-authored preset flow.
        /// </summary>
        private System.Windows.Controls.Border BuildNewPresetCard()
        {
            var card = new System.Windows.Controls.Border
            {
                Background = System.Windows.Media.Brushes.Transparent,
                BorderBrush = (System.Windows.Media.Brush)FindResource("PinkBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(16, 14, 16, 14),
                Margin = new Thickness(0, 0, 12, 12),
                Width = 218,
                Height = 150,
                Cursor = System.Windows.Input.Cursors.Hand,
                ToolTip = "Create your own keyword preset. You pick the words and what happens when they fire.",
            };
            card.MouseLeftButtonUp += NewPresetCard_Click;

            var stack = new System.Windows.Controls.StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            card.Child = stack;

            stack.Children.Add(new System.Windows.Controls.TextBlock
            {
                Text = "＋",
                FontSize = 40,
                FontWeight = FontWeights.Light,
                Foreground = (System.Windows.Media.Brush)FindResource("PinkBrush"),
                HorizontalAlignment = HorizontalAlignment.Center,
            });
            stack.Children.Add(new System.Windows.Controls.TextBlock
            {
                Text = "New Preset",
                Foreground = System.Windows.Media.Brushes.White,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 6, 0, 0),
            });
            stack.Children.Add(new System.Windows.Controls.TextBlock
            {
                Text = "Pick your own words",
                Foreground = (System.Windows.Media.Brush)FindResource("TextMutedBrush"),
                FontSize = 10,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 2, 0, 0),
            });

            return card;
        }

        /// <summary>
        /// Entry point for user-authored presets. Stages an unsaved preset shell and
        /// opens the detail dialog in "new" mode — the first metadata edit or trigger
        /// add is what commits it to settings.
        /// </summary>
        private void NewPresetCard_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            var preset = new KeywordTriggerPreset
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

            var dlg = new AwarenessPresetDetailDialog(preset, isNewCustomPreset: true) { Owner = this };
            dlg.ShowDialog();

            if (dlg.Changed)
                RefreshAwarenessPresetCards();
        }

        /// <summary>
        /// The "Customize individual triggers" hyperlink relabels itself based on
        /// whether any built-in preset is installed: "Customize installed presets →"
        /// when at least one is installed, or the plain "advanced editor" fallback
        /// when none are, which also flips the click target to the Exclusives tab.
        /// </summary>
        private void UpdateAwarenessAdvancedLinkText()
        {
            if (AwarenessTab.LnkAwarenessAdvancedText == null) return;
            AwarenessTab.LnkAwarenessAdvancedText.Text = GetMostRecentlyInstalledPreset() != null
                ? "Customize installed presets →"
                : "Advanced editor →";
        }

        /// <summary>
        /// Returns the preset to open when the user clicks the Awareness "customize"
        /// hyperlink. Tie-break: first visible preset whose MasterEnabled is true.
        /// Returns null when no preset is installed.
        /// </summary>
        private KeywordTriggerPreset? GetMostRecentlyInstalledPreset()
        {
            var presets = App.KeywordPresets?.VisiblePresets;
            if (presets == null) return null;
            return presets.FirstOrDefault(p => p?.MasterEnabled == true);
        }

        internal void LnkAwarenessAdvanced_Click(object sender, RoutedEventArgs e)
        {
            // Prefer opening the installed preset's inline editor dialog.
            var installed = GetMostRecentlyInstalledPreset();
            if (installed != null)
            {
                var dlg = new AwarenessPresetDetailDialog(installed) { Owner = this };
                dlg.ShowDialog();
                if (dlg.Changed)
                    RefreshAwarenessPresetCards();
                return;
            }

            // No preset installed — the Exclusives tab is gone, so just open the
            // dashboard's App Info popup as a safe landing point. In practice the
            // Awareness tab handles the full customization flow now.
            ShowAppInfoPopup();
        }

        #endregion
    }
}
