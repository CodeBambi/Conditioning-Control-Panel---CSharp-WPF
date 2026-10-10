// PORTED from ConditioningControlPanel/MainWindow/MainWindow.Awareness.cs (7.1.5): the live half of the
// Awareness page that needs the running engine - the "Last detected" pulse feed (RefreshAwarenessPulseFeed,
// BuildPulseRow, BuildActionChipStrip, GetActionChipDisplay, FormatTimeAgo), the fire-count label, the
// recently-focused app chips (RefreshAwarenessSeenAppChips, AwarenessSeenAppChip_Click) and the breathing
// status dot. Same layout numbers, colours and strings as WPF. Lives with the tab (the controls moved
// here from MainWindow), reading Platform/KeywordTriggerHead's engine.

using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.KeywordTriggers;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Tabs
{
    public partial class AwarenessTabView
    {
        /// <summary>WPF shows at most the five newest fires.</summary>
        internal const int PulseRowsShown = 5;

        /// <summary>Test seam: the engine whose fires the page shows.</summary>
        internal KeywordTriggerEngine Engine { get; set; } = KeywordTriggerHead.Engine;

        private bool _fireSubscribed;

        /// <summary>WPF subscribed for the window's life and refreshed only while the tab was visible;
        /// here the page listens only while it is on screen.</summary>
        private void HookLiveFeed()
        {
            AttachedToVisualTree += (_, _) =>
            {
                if (_fireSubscribed) return;
                Engine.TriggerFired += OnTriggerFired;
                _fireSubscribed = true;
            };
            DetachedFromVisualTree += (_, _) =>
            {
                if (!_fireSubscribed) return;
                Engine.TriggerFired -= OnTriggerFired;
                _fireSubscribed = false;
            };
        }

        private void OnTriggerFired(KeywordTrigger trigger, string source) => Dispatcher.UIThread.Post(() =>
        {
            if (IsVisible) RefreshAwarenessPulseFeed();
        });

        // ------------------------------------------------------------------ pulse feed

        internal void RefreshAwarenessPulseFeed()
        {
            try
            {
                var fires = Engine.GetRecentFires();
                AwarenessPulseFeed.Children.Clear();
                if (fires.Count == 0)
                {
                    AwarenessPulseFeed.Children.Add(TxtAwarenessPulseEmpty);
                    TxtAwarenessFireCount.Text = "";
                    return;
                }
                foreach (var f in fires.Take(PulseRowsShown)) AwarenessPulseFeed.Children.Add(BuildPulseRow(f));
                TxtAwarenessFireCount.Text = fires.Count == 1 ? "1 fire today" : $"{fires.Count} fires today";
            }
            catch (Exception ex) { Log.Debug("Awareness pulse feed refresh failed: {Error}", ex.Message); }
        }

        private Border BuildPulseRow(TriggerFireRecord f)
        {
            var grid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("6,Auto,Auto,*,Auto,Auto"),   // bar, keyword, chips, filler, source, time
            };

            var bar = new Rectangle
            {
                Width = 3, Height = 16, RadiusX = 1.5, RadiusY = 1.5, VerticalAlignment = VerticalAlignment.Center,
                Fill = Res("PinkBrush", Brushes.HotPink),
            };
            grid.Children.Add(bar);

            var keyword = new TextBlock
            {
                Text = $"\"{f.Keyword}\"", Foreground = Brushes.White, FontSize = 13, FontWeight = FontWeight.SemiBold,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0),
            };
            Grid.SetColumn(keyword, 1);
            grid.Children.Add(keyword);

            var chips = BuildActionChipStrip(f.ActionKeys);
            Grid.SetColumn(chips, 2);
            grid.Children.Add(chips);

            var source = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x4A)), CornerRadius = new CornerRadius(3),
                Padding = new Thickness(6, 2, 6, 2), Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = (f.Source ?? "").ToUpperInvariant(), FontSize = 9, FontWeight = FontWeight.Bold,
                    Foreground = Res("TextDimBrush", Brushes.Gray),
                },
            };
            Grid.SetColumn(source, 4);
            grid.Children.Add(source);

            var time = new TextBlock
            {
                Text = FormatTimeAgo(f.FiredAt, DateTime.Now), FontSize = 11, VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(10, 0, 0, 0), Foreground = Res("TextMutedBrush", Brushes.Gray),
            };
            Grid.SetColumn(time, 5);
            grid.Children.Add(time);

            return new Border
            {
                Background = Res("DarkerBgBrush", new SolidColorBrush(Color.FromRgb(0x15, 0x15, 0x24))),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x40)), BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6), Padding = new Thickness(12, 8, 12, 8), Margin = new Thickness(0, 0, 0, 6),
                Child = grid, Tag = "PulseRow",
            };
        }

        private static StackPanel BuildActionChipStrip(List<string>? actionKeys)
        {
            var strip = new StackPanel
            {
                Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0),
            };
            if (actionKeys == null) return strip;
            foreach (var key in actionKeys)
            {
                var (icon, tooltip) = ActionChipDisplay(key);
                if (string.IsNullOrEmpty(icon)) continue;
                var chip = new TextBlock
                {
                    Text = icon, FontSize = 13, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0),
                    FontFamily = new FontFamily("Segoe UI Emoji, Noto Color Emoji, Segoe UI Symbol, Segoe UI"),
                };
                ToolTip.SetTip(chip, tooltip);
                ToolTip.SetShowDelay(chip, 200);
                strip.Children.Add(chip);
            }
            return strip;
        }

        /// <summary>WPF GetActionChipDisplay: the (icon, tooltip) for one fire-record action key
        /// ("PlayAudio", "VisualEffect:ImageFlash", "ExtendSession:5"). Unknown keys draw nothing.</summary>
        internal static (string Icon, string Tooltip) ActionChipDisplay(string? key)
        {
            if (string.IsNullOrEmpty(key)) return ("", "");
            var colon = key.IndexOf(':');
            var type = colon < 0 ? key : key[..colon];
            var arg = colon < 0 ? "" : key[(colon + 1)..];
            return type switch
            {
                "PlayAudio" => ("\U0001F50A", "Plays an audio clip when the word is detected"),
                "Highlight" => ("\U0001F441", "Draws a glowing box around the matched word on screen (OCR matches only)"),
                "Haptic" => ("\U0001F4A5", "Fires a haptic vibration pattern on connected devices"),
                "AvatarComment" => ("\U0001F4AC", "Makes the avatar comment on the matched word (AI + canned fallback)"),
                "ExtendSession" => ("⏱", string.IsNullOrEmpty(arg) ? "Extends the current session" : $"Extends the current session by {arg} minutes"),
                "ChasterAddTime" => ("\U0001F512", string.IsNullOrEmpty(arg) ? "Adds time to the Chaster lock" : $"Adds {arg} minutes to the Chaster lock"),
                "VisualEffect" => arg switch
                {
                    "SubliminalFlash" => ("✨", "Flashes a random word from your subliminal pool"),
                    "ExactSubliminal" => ("\U0001F524", "Flashes the matched keyword itself as subliminal text"),
                    "ImageFlash" => ("⚡", "Fires a flash burst image when the word is detected"),
                    "OverlayPulse" => ("\U0001F32B", "Briefly intensifies the screen overlay"),
                    "MindWipe" => ("\U0001F9E0", "Triggers the MindWipe effect"),
                    "Bubbles" => ("\U0001FAE7", "Spawns bubbles on screen"),
                    _ => ("✨", $"Fires visual effect: {arg}"),
                },
                _ => ("", ""),
            };
        }

        /// <summary>WPF FormatTimeAgo.</summary>
        internal static string FormatTimeAgo(DateTime t, DateTime now)
        {
            var delta = now - t;
            if (delta.TotalSeconds < 10) return "just now";
            if (delta.TotalSeconds < 60) return $"{(int)delta.TotalSeconds}s ago";
            if (delta.TotalMinutes < 60) return $"{(int)delta.TotalMinutes}m ago";
            if (delta.TotalHours < 24) return $"{(int)delta.TotalHours}h ago";
            return t.ToString("HH:mm");
        }

        // ------------------------------------------------------------------ recently focused apps

        /// <summary>WPF RefreshAwarenessSeenAppChips: the apps recently seen in the foreground (the
        /// engine's in-memory ring, never persisted, this app excluded) as one-click additions.
        /// Already-listed apps are dropped rather than shown inert.</summary>
        internal void RefreshAwarenessSeenAppChips()
        {
            try
            {
                AwarenessSeenAppsPanel.Children.Clear();
                var listed = CoreSettings.Current.KeywordTriggerApps ?? new List<string>();
                var seen = Engine.GetRecentForegroundApps().Where(a => !KeywordTriggerEngine.MatchesAppList(listed, a)).ToList();
                TxtAwarenessSeenAppsLabel.IsVisible = seen.Count > 0;
                foreach (var app in seen)
                {
                    var chip = new Button
                    {
                        Content = "+ " + app, Tag = app, FontSize = 10, Padding = new Thickness(7, 3, 7, 3),
                        Margin = new Thickness(0, 0, 5, 5), Cursor = new Cursor(StandardCursorType.Hand), Foreground = Brushes.White,
                        Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x44)),
                        BorderBrush = new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x5A)), BorderThickness = new Thickness(1),
                    };
                    ToolTip.SetTip(chip, $"Add '{app}' to the list");
                    chip.Click += (_, _) => AddSeenApp(app);
                    AwarenessSeenAppsPanel.Children.Add(chip);
                }
            }
            catch (Exception ex) { Log.Debug("Awareness seen-app chips refresh failed: {Error}", ex.Message); }
        }

        /// <summary>WPF AwarenessSeenAppChip_Click.</summary>
        internal void AddSeenApp(string app)
        {
            var settings = CoreSettings.Current;
            var list = new List<string>(settings.KeywordTriggerApps ?? new List<string>());
            if (!KeywordTriggerEngine.MatchesAppList(list, app)) list.Add(app);
            settings.KeywordTriggerApps = list;
            CoreSettings.Save();
            Log.Information("Awareness app scope: added '{App}' ({Mode}, {Count} listed)", app, settings.KeywordTriggerAppScope, list.Count);
            SyncAwarenessTabUi();
        }

        // ------------------------------------------------------------------ status dot

        /// <summary>WPF SetAwarenessStatusPulse: the dot breathes only while the engine is live. The
        /// shell owns the pulse (MainShellWindow.TabFxTakeoverLabStatus); a page hosted elsewhere
        /// (a test, a preview) simply has no breath.</summary>
        private void PulseStatusDot(bool live)
        {
            try { (TopLevel.GetTopLevel(this) as MainShellWindow)?.SetAwarenessStatusPulse(live); }
            catch (Exception ex) { Log.Debug("Awareness status pulse failed: {Error}", ex.Message); }
        }
    }
}
