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
    // Keyword triggers tab: handlers for keyword/trigger word configuration.
    public partial class MainWindow
    {
        #region Keyword Triggers Handlers

        internal void BtnKeywordTriggersStartStop_Click(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;

            var isRunning = App.Settings.Current.KeywordTriggersEnabled;

            if (!isRunning)
            {
                // Starting — check Patreon access
                if (!KeywordTriggerService.HasAccess())
                {
                    MessageBox.Show(
                        Loc.Get("msg_keyword_triggers_patreon_only"),
                        Loc.Get("title_patreon_feature"),
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    return;
                }

                App.Settings.Current.KeywordTriggersEnabled = true;
                App.KeywordTriggers?.Start();
                _keyboardHook?.Start();

                if (App.Settings.Current.ScreenOcrEnabled)
                    App.ScreenOcr?.Start();
            }
            else
            {
                // Stopping
                App.Settings.Current.KeywordTriggersEnabled = false;
                App.KeywordTriggers?.Stop();
                App.ScreenOcr?.Stop();

                // Only stop hook if panic key also disabled
                if (App.Settings.Current.PanicKeyEnabled != true)
                    _keyboardHook?.Stop();
            }

            UpdateKeywordTriggersButtonState();
            App.Settings.Save();
        }

        private void UpdateKeywordTriggersButtonState()
        {
            if (PatreonTab.BtnKeywordTriggersStartStop == null) return;
            var running = App.Settings?.Current?.KeywordTriggersEnabled == true;

            PatreonTab.BtnKeywordTriggersStartStop.Content = running ? Loc.Get("btn_stop") : Loc.Get("btn_start");
            PatreonTab.BtnKeywordTriggersStartStop.Background = running
                ? new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#555555"))
                : new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(App.Mods?.GetAccentColorHex() ?? "#FF69B4"));
        }

        internal void SliderKeywordBufferTimeout_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading || AwarenessTab.TxtKeywordBufferTimeout == null) return;
            var value = (int)AwarenessTab.SliderKeywordBufferTimeout.Value;
            AwarenessTab.TxtKeywordBufferTimeout.Text = $"{value / 1000.0:F1}s";
            App.Settings.Current.KeywordBufferTimeoutMs = value;
        }

        internal void SliderKeywordGlobalCooldown_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading || PatreonTab.TxtKeywordGlobalCooldown == null) return;
            var value = (int)PatreonTab.SliderKeywordGlobalCooldown.Value;
            PatreonTab.TxtKeywordGlobalCooldown.Text = $"{value}s";
            App.Settings.Current.KeywordGlobalCooldownSeconds = value;
        }

        internal void SliderAwarenessGlobalCooldown_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading || AwarenessTab.TxtAwarenessGlobalCooldown == null) return;
            var value = (int)AwarenessTab.SliderAwarenessGlobalCooldown.Value;
            AwarenessTab.TxtAwarenessGlobalCooldown.Text = $"{value}s";
            if (App.Settings?.Current != null)
            {
                App.Settings.Current.KeywordGlobalCooldownSeconds = value;
                App.Settings.Save();
                // PHASE 5 (G3): the mirror into PatreonTab.SliderKeywordGlobalCooldown is gone.
                // That twin is on the permanently-Collapsed corpse tab, so nobody could see it -
                // and it clamped at 120 while this slider goes to 180, so mirroring 150 wrote 120
                // straight back into KeywordGlobalCooldownSeconds via its own ValueChanged.
                // This slider is now the only editor for the property.
            }
        }

        internal void SliderAwarenessSameWordCooldown_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading || AwarenessTab.TxtAwarenessSameWordCooldown == null) return;
            var value = (int)AwarenessTab.SliderAwarenessSameWordCooldown.Value;
            AwarenessTab.TxtAwarenessSameWordCooldown.Text = $"{value}s";
            if (App.Settings?.Current != null)
            {
                App.Settings.Current.KeywordPerKeywordCooldownSeconds = value;
                App.Settings.Save();
            }
        }

        internal void SliderKeywordSessionMultiplier_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading || AwarenessTab.TxtKeywordSessionMultiplier == null) return;
            var value = AwarenessTab.SliderKeywordSessionMultiplier.Value;
            AwarenessTab.TxtKeywordSessionMultiplier.Text = $"{value:F1}x";
            App.Settings.Current.KeywordSessionMultiplier = value;
        }

        internal void ChkScreenOcrEnabled_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var isEnabled = PatreonTab.ChkScreenOcrEnabled.IsChecked == true;

            if (isEnabled && !KeywordTriggerService.HasAccess())
            {
                PatreonTab.ChkScreenOcrEnabled.IsChecked = false;
                MessageBox.Show(
                    Loc.Get("msg_screen_ocr_patreon_only"),
                    Loc.Get("title_patreon_feature"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            App.Settings.Current.ScreenOcrEnabled = isEnabled;

            if (isEnabled)
                App.ScreenOcr?.Start();
            else
                App.ScreenOcr?.Stop();

            if (PatreonTab.ScreenOcrIntervalPanel != null)
                PatreonTab.ScreenOcrIntervalPanel.Visibility = isEnabled ? Visibility.Visible : Visibility.Collapsed;

            App.Settings.Save();
        }

        internal void SliderScreenOcrInterval_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading || AwarenessTab.TxtScreenOcrInterval == null) return;
            var value = (int)AwarenessTab.SliderScreenOcrInterval.Value;
            AwarenessTab.TxtScreenOcrInterval.Text = $"{value}s";
            App.Settings.Current.ScreenOcrIntervalMs = value * 1000;
            App.ScreenOcr?.UpdateInterval(value * 1000);
        }

        internal void ChkKeywordHighlightEnabled_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            App.Settings.Current.KeywordHighlightEnabled = PatreonTab.ChkKeywordHighlightEnabled.IsChecked == true;
            if (PatreonTab.HighlightDurationPanel != null)
                PatreonTab.HighlightDurationPanel.Visibility = PatreonTab.ChkKeywordHighlightEnabled.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        }

        internal void SliderKeywordHighlightDuration_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading || AwarenessTab.SliderKeywordHighlightDuration == null) return;
            var ms = (int)(AwarenessTab.SliderKeywordHighlightDuration.Value * 1000);
            AwarenessTab.TxtKeywordHighlightDuration.Text = $"{AwarenessTab.SliderKeywordHighlightDuration.Value:0.0}s";
            App.Settings.Current.KeywordHighlightDurationMs = ms;
        }

        internal void CmbOcrHighlightMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoading || AwarenessTab.CmbOcrHighlightMode == null) return;
            App.Settings.Current.OcrHighlightAll = AwarenessTab.CmbOcrHighlightMode.SelectedIndex == 0;
        }

        internal void CmbOcrConfirmation_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoading || AwarenessTab.CmbOcrConfirmation == null) return;
            // Index 0/1/2 → 1/2/3 consecutive scans required before a keyword fires.
            App.Settings.Current.OcrConfirmationScans = AwarenessTab.CmbOcrConfirmation.SelectedIndex + 1;
            App.Settings.Save();
        }

        internal void ChkHighlightVisibleInCapture_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            App.Settings.Current.OcrHighlightVisibleInCapture = PatreonTab.ChkHighlightVisibleInCapture.IsChecked == true;
            App.KeywordHighlight?.RefreshCaptureVisibility();
        }

        internal void BtnAddKeywordTrigger_Click(object sender, RoutedEventArgs e)
        {
            var triggers = App.Settings.Current.KeywordTriggers;

            var newTrigger = new KeywordTrigger
            {
                Keyword = "",
                MatchType = KeywordMatchType.PlainText,
                Enabled = true,
                CooldownSeconds = 30,
                AudioVolume = 80,
                VisualEffect = KeywordVisualEffect.SubliminalFlash,
                HapticEnabled = true,
                HapticIntensity = 0.5,
                DuckAudio = true,
                XPAward = 10
            };

            KeywordTriggerService.RebuildActionsFromFlatFields(newTrigger);
            triggers.Add(newTrigger);
            App.Settings.Save();
            RefreshKeywordTriggerList();
        }

        internal void BtnImportFromCustomTriggers_Click(object sender, RoutedEventArgs e)
        {
            var imported = App.KeywordTriggers?.ImportFromCustomTriggers();
            if (imported == null || imported.Count == 0)
            {
                MessageBox.Show(Loc.Get("msg_no_new_triggers_to_import_all_existing_trigge"),
                    Loc.Get("title_import_complete"), MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var triggers = App.Settings.Current.KeywordTriggers;
            triggers.AddRange(imported);
            App.Settings.Save();
            RefreshKeywordTriggerList();

            MessageBox.Show(Loc.GetF("msg_imported_0_trigger_s_from_your_trigger_mode_l", imported.Count),
                Loc.Get("title_import_complete"), MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void BtnDeleteKeywordTrigger_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn) return;
            var triggerId = btn.Tag?.ToString();
            if (string.IsNullOrEmpty(triggerId)) return;

            var triggers = App.Settings.Current.KeywordTriggers;
            var trigger = triggers.FirstOrDefault(t => t.Id == triggerId);
            if (trigger != null)
            {
                triggers.Remove(trigger);
                App.Settings.Save();
                RefreshKeywordTriggerList();
            }
        }

        private void ChkKeywordTriggerEnabled_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            if (sender is not CheckBox chk) return;
            var triggerId = chk.Tag?.ToString();
            if (string.IsNullOrEmpty(triggerId)) return;

            var trigger = App.Settings.Current.KeywordTriggers.FirstOrDefault(t => t.Id == triggerId);
            if (trigger != null)
            {
                trigger.Enabled = chk.IsChecked == true;
                App.Settings.Save();
            }
        }

        private void TxtKeywordTriggerKeyword_LostFocus(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            if (sender is not TextBox txt) return;
            var triggerId = txt.Tag?.ToString();
            if (string.IsNullOrEmpty(triggerId)) return;

            var trigger = App.Settings.Current.KeywordTriggers.FirstOrDefault(t => t.Id == triggerId);
            if (trigger != null)
            {
                trigger.Keyword = txt.Text;

                // Auto-match audio file if none set
                if (string.IsNullOrEmpty(trigger.AudioFilePath))
                {
                    trigger.AudioFilePath = App.KeywordTriggers?.FindLinkedAudio(txt.Text);
                }

                KeywordTriggerService.RebuildActionsFromFlatFields(trigger);
                App.Settings.Save();
            }
        }

        private void BtnKeywordTriggerBrowseAudio_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn) return;
            var triggerId = btn.Tag?.ToString();
            if (string.IsNullOrEmpty(triggerId)) return;

            var trigger = App.Settings.Current.KeywordTriggers.FirstOrDefault(t => t.Id == triggerId);
            if (trigger == null) return;

            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Audio Files|*.mp3;*.wav;*.ogg|All Files|*.*",
                Title = Loc.Get("title_select_trigger_audio")
            };

            if (dlg.ShowDialog() == true)
            {
                trigger.AudioFilePath = dlg.FileName;
                KeywordTriggerService.RebuildActionsFromFlatFields(trigger);
                App.Settings.Save();
                RefreshKeywordTriggerList();
            }
        }

        private void CmbKeywordVisualEffect_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoading) return;
            if (sender is not ComboBox cmb) return;
            var triggerId = cmb.Tag?.ToString();
            if (string.IsNullOrEmpty(triggerId)) return;

            var trigger = App.Settings.Current.KeywordTriggers.FirstOrDefault(t => t.Id == triggerId);
            if (trigger != null)
            {
                trigger.VisualEffect = (KeywordVisualEffect)cmb.SelectedIndex;
                KeywordTriggerService.RebuildActionsFromFlatFields(trigger);
                App.Settings.Save();
            }
        }

        private void SliderKeywordTriggerCooldown_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading) return;
            if (sender is not Slider slider) return;
            var triggerId = slider.Tag?.ToString();
            if (string.IsNullOrEmpty(triggerId)) return;

            var trigger = App.Settings.Current.KeywordTriggers.FirstOrDefault(t => t.Id == triggerId);
            if (trigger != null)
            {
                trigger.CooldownSeconds = (int)slider.Value;
                App.Settings.Save();
            }
        }

        private void SliderKeywordTriggerVolume_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading) return;
            if (sender is not Slider slider) return;
            var triggerId = slider.Tag?.ToString();
            if (string.IsNullOrEmpty(triggerId)) return;

            var trigger = App.Settings.Current.KeywordTriggers.FirstOrDefault(t => t.Id == triggerId);
            if (trigger != null)
            {
                trigger.AudioVolume = (int)slider.Value;
                KeywordTriggerService.RebuildActionsFromFlatFields(trigger);
                App.Settings.Save();
            }
        }

        private void ChkKeywordTriggerHaptic_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            if (sender is not CheckBox chk) return;
            var triggerId = chk.Tag?.ToString();
            if (string.IsNullOrEmpty(triggerId)) return;

            var trigger = App.Settings.Current.KeywordTriggers.FirstOrDefault(t => t.Id == triggerId);
            if (trigger != null)
            {
                trigger.HapticEnabled = chk.IsChecked == true;
                KeywordTriggerService.RebuildActionsFromFlatFields(trigger);
                App.Settings.Save();
            }
        }

        private void ChkKeywordTriggerDuckAudio_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            if (sender is not CheckBox chk) return;
            var triggerId = chk.Tag?.ToString();
            if (string.IsNullOrEmpty(triggerId)) return;

            var trigger = App.Settings.Current.KeywordTriggers.FirstOrDefault(t => t.Id == triggerId);
            if (trigger != null)
            {
                trigger.DuckAudio = chk.IsChecked == true;
                KeywordTriggerService.RebuildActionsFromFlatFields(trigger);
                App.Settings.Save();
            }
        }

        private void RefreshKeywordTriggerList()
        {
            if (AwarenessTab.KeywordTriggerListPanel == null) return;
            AwarenessTab.KeywordTriggerListPanel.Children.Clear();

            var triggers = App.Settings?.Current?.KeywordTriggers;
            if (triggers == null) return;

            // Filter out preset clones (Id prefix "preset:") — those are managed
            // via their own preset detail dialogs on the Awareness tab's PRESETS row.
            // This list is for user-authored custom triggers only, so installing or
            // uninstalling a preset pack never pollutes it.
            foreach (var trigger in triggers)
            {
                if (trigger?.Id?.StartsWith("preset:", StringComparison.Ordinal) == true) continue;
                var row = CreateKeywordTriggerRow(trigger);
                AwarenessTab.KeywordTriggerListPanel.Children.Add(row);
            }
        }

        /// <summary>
        /// PHASE 5 (G3): seeds the rescued keyword/OCR panel on the Awareness tab from settings.
        /// <para>
        /// The detail blocks it owns (scan interval + confirmation, highlight mode + duration)
        /// follow masters that live *above* them on the same page - <c>ChkAwarenessOcr</c> and
        /// <c>ChkAwarenessHighlight</c> - so this is the one place that reconciles the two.
        /// Called on tab open (<c>SyncAwarenessTabUI</c>), from those two masters' handlers, and
        /// after <c>LoadSettings</c>. Manages <c>_isLoading</c> itself so seeding a Slider.Value
        /// never re-enters the ValueChanged handlers.
        /// </para>
        /// </summary>
        internal void SyncKeywordRescuePanelUi()
        {
            var s = App.Settings?.Current;
            if (s == null || AwarenessTab?.KeywordPanel == null) return;

            var hasAccess = KeywordTriggerService.HasAccess();
            var wasLoading = _isLoading;
            _isLoading = true;
            try
            {
                AwarenessTab.SliderKeywordBufferTimeout.Value = Math.Clamp(s.KeywordBufferTimeoutMs, 1000, 10000);
                AwarenessTab.TxtKeywordBufferTimeout.Text = $"{s.KeywordBufferTimeoutMs / 1000.0:F1}s";

                AwarenessTab.SliderKeywordSessionMultiplier.Value = Math.Clamp(s.KeywordSessionMultiplier, 1.0, 3.0);
                AwarenessTab.TxtKeywordSessionMultiplier.Text = $"{s.KeywordSessionMultiplier:F1}x";

                // ---- Screen OCR detail: only meaningful while the OCR source is on ----
                var ocrOn = s.ScreenOcrEnabled && hasAccess;
                AwarenessTab.SliderScreenOcrInterval.Value = Math.Clamp(s.ScreenOcrIntervalMs / 1000.0, 2, 10);
                AwarenessTab.TxtScreenOcrInterval.Text = $"{s.ScreenOcrIntervalMs / 1000}s";
                AwarenessTab.CmbOcrConfirmation.SelectedIndex = Math.Clamp(s.OcrConfirmationScans - 1, 0, 2);
                AwarenessTab.ScreenOcrIntervalPanel.Visibility = ocrOn ? Visibility.Visible : Visibility.Collapsed;
                AwarenessTab.TxtScreenOcrOffHint.Visibility = ocrOn ? Visibility.Collapsed : Visibility.Visible;

                // ---- Highlight detail: follows the SAFETY highlight toggle ----
                var highlightOn = s.KeywordHighlightEnabled;
                AwarenessTab.CmbOcrHighlightMode.SelectedIndex = s.OcrHighlightAll ? 0 : 1;
                AwarenessTab.SliderKeywordHighlightDuration.Value = Math.Clamp(s.KeywordHighlightDurationMs / 1000.0, 0.3, 5.0);
                AwarenessTab.TxtKeywordHighlightDuration.Text = $"{s.KeywordHighlightDurationMs / 1000.0:0.0}s";
                AwarenessTab.HighlightDurationPanel.Visibility = highlightOn ? Visibility.Visible : Visibility.Collapsed;
                AwarenessTab.TxtHighlightOffHint.Visibility = highlightOn ? Visibility.Collapsed : Visibility.Visible;
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "SyncKeywordRescuePanelUi failed");
            }
            finally { _isLoading = wasLoading; }

            RefreshKeywordTriggerList();
        }

        private Border CreateKeywordTriggerRow(KeywordTrigger trigger)
        {
            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x3A)),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10),
                Margin = new Thickness(0, 0, 0, 6),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x35, 0x35, 0x50)),
                BorderThickness = new Thickness(1)
            };

            var mainStack = new StackPanel();

            // Row 1: Enable toggle + Keyword + Delete button
            var topRow = new Grid();
            topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var enableChk = new CheckBox
            {
                IsChecked = trigger.Enabled,
                Tag = trigger.Id,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            };
            enableChk.Checked += ChkKeywordTriggerEnabled_Changed;
            enableChk.Unchecked += ChkKeywordTriggerEnabled_Changed;
            Grid.SetColumn(enableChk, 0);
            topRow.Children.Add(enableChk);

            var keywordTxt = new TextBox
            {
                Text = trigger.Keyword,
                Tag = trigger.Id,
                Background = new SolidColorBrush(Color.FromRgb(0x25, 0x25, 0x42)),
                Foreground = new SolidColorBrush(Colors.White),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x50, 0x50, 0x70)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(6, 4, 6, 4),
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center
            };
            keywordTxt.LostFocus += TxtKeywordTriggerKeyword_LostFocus;
            Grid.SetColumn(keywordTxt, 1);
            topRow.Children.Add(keywordTxt);

            var deleteBtn = new Button
            {
                Content = "\u2716",
                Tag = trigger.Id,
                Background = new SolidColorBrush(Colors.Transparent),
                Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x69, 0xB4)),
                BorderThickness = new Thickness(0),
                FontSize = 14,
                Padding = new Thickness(6, 2, 6, 2),
                Margin = new Thickness(6, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Cursor = Cursors.Hand
            };
            deleteBtn.Click += BtnDeleteKeywordTrigger_Click;
            Grid.SetColumn(deleteBtn, 2);
            topRow.Children.Add(deleteBtn);

            mainStack.Children.Add(topRow);

            // Row 2: Audio file + Browse + Visual effect
            var settingsRow = new Grid { Margin = new Thickness(0, 6, 0, 0) };
            settingsRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            settingsRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            settingsRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var audioLabel = new TextBlock
            {
                Text = string.IsNullOrEmpty(trigger.AudioFilePath) ? "No audio" : Path.GetFileName(trigger.AudioFilePath),
                Foreground = new SolidColorBrush(string.IsNullOrEmpty(trigger.AudioFilePath) ? Color.FromRgb(0x80, 0x80, 0x80) : Color.FromRgb(0xFF, 0x69, 0xB4)),
                FontSize = 10,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            Grid.SetColumn(audioLabel, 0);
            settingsRow.Children.Add(audioLabel);

            var browseBtn = new Button
            {
                Content = "Browse",
                Tag = trigger.Id,
                Background = new SolidColorBrush(Color.FromRgb(0x35, 0x35, 0x50)),
                Foreground = new SolidColorBrush(Colors.White),
                BorderThickness = new Thickness(0),
                FontSize = 10,
                Padding = new Thickness(8, 3, 8, 3),
                Margin = new Thickness(6, 0, 0, 0),
                Cursor = Cursors.Hand
            };
            browseBtn.Click += BtnKeywordTriggerBrowseAudio_Click;
            Grid.SetColumn(browseBtn, 1);
            settingsRow.Children.Add(browseBtn);

            var visualCombo = new ComboBox
            {
                Tag = trigger.Id,
                SelectedIndex = (int)trigger.VisualEffect,
                Style = (Style)FindResource("DarkComboBoxStyle"),
                Margin = new Thickness(6, 0, 0, 0),
                MinWidth = 100
            };
            visualCombo.Items.Add("None");
            visualCombo.Items.Add("Highlight Only");
            visualCombo.Items.Add("Subliminal");
            visualCombo.Items.Add("Exact Subliminal");
            visualCombo.Items.Add("Image Flash");
            visualCombo.Items.Add("Overlay Pulse");
            visualCombo.Items.Add("Mind Wipe");
            visualCombo.Items.Add("Bubbles");
            visualCombo.SelectionChanged += CmbKeywordVisualEffect_SelectionChanged;
            Grid.SetColumn(visualCombo, 2);
            settingsRow.Children.Add(visualCombo);

            mainStack.Children.Add(settingsRow);

            // Row 3: Cooldown + Volume + Haptic + Duck
            var optionsRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };

            optionsRow.Children.Add(new TextBlock { Text = "CD:", Foreground = new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0x80)), FontSize = 10, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) });
            var cooldownSlider = new Slider
            {
                Tag = trigger.Id,
                Minimum = 1, Maximum = 300,
                Value = trigger.CooldownSeconds,
                Width = 60,
                VerticalAlignment = VerticalAlignment.Center
            };
            cooldownSlider.ValueChanged += SliderKeywordTriggerCooldown_ValueChanged;
            optionsRow.Children.Add(cooldownSlider);
            optionsRow.Children.Add(new TextBlock { Text = $"{trigger.CooldownSeconds}s", Foreground = new SolidColorBrush(Color.FromRgb(0xA0, 0xA0, 0xA0)), FontSize = 10, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 10, 0) });

            optionsRow.Children.Add(new TextBlock { Text = "Vol:", Foreground = new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0x80)), FontSize = 10, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) });
            var volumeSlider = new Slider
            {
                Tag = trigger.Id,
                Minimum = 0, Maximum = 100,
                Value = trigger.AudioVolume,
                Width = 50,
                VerticalAlignment = VerticalAlignment.Center
            };
            volumeSlider.ValueChanged += SliderKeywordTriggerVolume_ValueChanged;
            optionsRow.Children.Add(volumeSlider);

            var hapticChk = new CheckBox
            {
                Content = "Haptic",
                IsChecked = trigger.HapticEnabled,
                Tag = trigger.Id,
                Foreground = new SolidColorBrush(Color.FromRgb(0xA0, 0xA0, 0xA0)),
                FontSize = 10,
                Margin = new Thickness(10, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            hapticChk.Checked += ChkKeywordTriggerHaptic_Changed;
            hapticChk.Unchecked += ChkKeywordTriggerHaptic_Changed;
            optionsRow.Children.Add(hapticChk);

            var duckChk = new CheckBox
            {
                Content = "Duck",
                IsChecked = trigger.DuckAudio,
                Tag = trigger.Id,
                Foreground = new SolidColorBrush(Color.FromRgb(0xA0, 0xA0, 0xA0)),
                FontSize = 10,
                Margin = new Thickness(10, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            duckChk.Checked += ChkKeywordTriggerDuckAudio_Changed;
            duckChk.Unchecked += ChkKeywordTriggerDuckAudio_Changed;
            optionsRow.Children.Add(duckChk);

            mainStack.Children.Add(optionsRow);

            border.Child = mainStack;
            return border;
        }

        #endregion
    }
}
