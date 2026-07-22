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
    // Session import/export: serialize and load session configurations.
    public partial class MainWindow
    {
        #region Session Import/Export

        private Services.SessionManager? _sessionManager;
        private Services.SessionFileService? _sessionFileService;
        private Services.AssetImportService? _assetImportService;

        private void InitializeSessionManager()
        {
            _sessionFileService = new Services.SessionFileService();
            _sessionManager = new Services.SessionManager();
            _sessionManager.SessionAdded += OnSessionAdded;
            _sessionManager.SessionRemoved += OnSessionRemoved;
            _sessionManager.LoadAllSessions();

            // Populate UI with any custom sessions loaded from disk
            foreach (var session in _sessionManager.CustomSessions)
            {
                AddCustomSessionCard(session);
            }
        }

        /// <summary>
        /// Register a session that was written to disk by something OTHER than the session
        /// editor with the LIVE <see cref="Services.SessionManager"/>, so the Sessions tab
        /// lists it immediately.
        ///
        /// BUG #614: the Graded Intake drafts a session and auto-saves it straight into the
        /// CustomSessions folder (see IntakeHostService.OnQuizResult - deliberately no
        /// SaveFileDialog). Nothing told the running SessionManager about the new file, and
        /// <see cref="Services.SessionManager.LoadAllSessions"/> only ever runs once, from
        /// <see cref="InitializeSessionManager"/> at startup. So the session existed on disk
        /// but was invisible in the UI until the next app launch - which read, to the user,
        /// as "the intake didn't create a session at all".
        ///
        /// This reuses the existing AddNewSession path (the one the editor's "save as new"
        /// uses) rather than inventing a parallel one: it sets Source/SourceFilePath, resolves
        /// an Id collision, re-writes the file at <paramref name="filePath"/> (idempotent - the
        /// caller already wrote identical bytes there) and raises SessionAdded, which lands in
        /// <see cref="OnSessionAdded"/> and builds the card + selects it.
        ///
        /// Safe to call before the manager exists (it is created on demand) and safe to call
        /// off the UI thread. It NEVER throws at the caller: the file is already safely on disk
        /// by the time we get here, and a failure to refresh a list must not be reported as a
        /// failure to draft the session.
        /// </summary>
        public bool RegisterExternallySavedSession(Models.Session session, string filePath)
        {
            if (session == null || string.IsNullOrWhiteSpace(filePath)) return false;

            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => RegisterExternallySavedSession(session, filePath)));
                return true;
            }

            try
            {
                if (_sessionManager == null) InitializeSessionManager();
                if (_sessionManager == null) return false;

                // Already known (e.g. a LoadAllSessions raced us, or a double-register)?
                // Adding twice would leave two identical cards in the Sessions tab.
                if (_sessionManager.GetSession(session.Id) != null) return true;

                _sessionManager.AddNewSession(session, filePath);
                return true;
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "RegisterExternallySavedSession failed for '{Name}'", session.Name);
                return false;
            }
        }

        private void OnSessionAdded(Models.Session session)
        {
            Dispatcher.Invoke(() =>
            {
                App.Logger?.Information("Session imported: {Name}", session.Name);
                AddCustomSessionCard(session);

                // Show "Session loaded!" notification
                ShowDropZoneStatus($"Session loaded: {session.Name}", isError: false);

                // Auto-select the new session
                SelectSession(session);
            });
        }

        private void OnSessionRemoved(Models.Session session)
        {
            Dispatcher.Invoke(() =>
            {
                App.Logger?.Information("Session removed: {Name}", session.Name);
                RemoveCustomSessionCard(session);
            });
        }

        private void AddCustomSessionCard(Models.Session session)
        {
            // Show the "Your Sessions" header
            PresetsTab.TxtCustomSessionsHeader.Visibility = Visibility.Visible;

            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(42, 42, 74)), // #2A2A4A
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(16, 14, 16, 14),
                Margin = new Thickness(0, 0, 0, 8),
                Cursor = System.Windows.Input.Cursors.Hand,
                Tag = session.Id
            };

            // Style with border
            border.SetValue(Border.BorderBrushProperty, Application.Current.Resources["PanelAccentBrush"] as SolidColorBrush ?? new SolidColorBrush(Color.FromRgb(64, 64, 96)));
            border.SetValue(Border.BorderThicknessProperty, new Thickness(2));

            border.MouseEnter += (s, e) => border.BorderBrush = FindResource("PinkBrush") as SolidColorBrush;
            border.MouseLeave += (s, e) => border.BorderBrush = Application.Current.Resources["PanelAccentBrush"] as SolidColorBrush ?? new SolidColorBrush(Color.FromRgb(64, 64, 96));
            border.MouseLeftButtonUp += SessionCard_Click;

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // Left side: Session info
            var infoPanel = new StackPanel();
            Grid.SetColumn(infoPanel, 0);

            var headerPanel = new StackPanel { Orientation = Orientation.Horizontal };

            var nameText = new TextBlock
            {
                Text = $"{session.Icon} {session.GetModeAwareName()}",
                Foreground = new SolidColorBrush(Colors.White),
                FontWeight = FontWeights.SemiBold,
                FontSize = 15
            };
            headerPanel.Children.Add(nameText);

            // Duration badge
            var durationBadge = new Border
            {
                Background = FindResource("PinkBrush") as SolidColorBrush,
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(8, 3, 8, 3),
                Margin = new Thickness(10, 0, 0, 0)
            };
            durationBadge.Child = new TextBlock
            {
                Text = $"{session.DurationMinutes} MIN",
                Foreground = new SolidColorBrush(Colors.White),
                FontSize = 10,
                FontWeight = FontWeights.Bold
            };
            headerPanel.Children.Add(durationBadge);

            // Difficulty badge
            var (diffBg, diffFg) = session.Difficulty switch
            {
                Models.SessionDifficulty.Easy => ("#2A3A2A", "#90EE90"),
                Models.SessionDifficulty.Medium => ("#3A3A2A", "#FFD700"),
                Models.SessionDifficulty.Hard => ("#4A3A2A", "#FFA500"),
                Models.SessionDifficulty.Extreme => ("#4A2A2A", "#FF6347"),
                _ => ("#2A3A2A", "#90EE90")
            };
            var diffBadge = new Border
            {
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(diffBg)),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(8, 3, 8, 3),
                Margin = new Thickness(6, 0, 0, 0)
            };
            diffBadge.Child = new TextBlock
            {
                Text = session.GetDifficultyText(),
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(diffFg)),
                FontSize = 10,
                FontWeight = FontWeights.Bold
            };
            headerPanel.Children.Add(diffBadge);

            // Custom badge
            var customBadge = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(106, 90, 205)), // Purple
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(8, 3, 8, 3),
                Margin = new Thickness(6, 0, 0, 0)
            };
            customBadge.Child = new TextBlock
            {
                Text = "CUSTOM",
                Foreground = new SolidColorBrush(Colors.White),
                FontSize = 10,
                FontWeight = FontWeights.Bold
            };
            headerPanel.Children.Add(customBadge);

            // Catalogue share status badge (pending/approved/rejected), if shared.
            var sessKey = string.IsNullOrEmpty(session.SourceFilePath) ? null : CanonicalCataloguePathKey(session.SourceFilePath);
            var sessRec = sessKey != null ? GetCatalogueRecord(CatalogueKindSessions, sessKey) : null;
            var sessStatusBadge = CreateCatalogueStatusBadge(sessRec);
            if (sessStatusBadge != null) headerPanel.Children.Add(sessStatusBadge);

            infoPanel.Children.Add(headerPanel);

            // Description
            var descText = new TextBlock
            {
                Text = string.IsNullOrEmpty(session.Description)
                    ? "Custom session"
                    : session.GetModeAwareDescription().Split('\n')[0].Substring(0, Math.Min(60, session.GetModeAwareDescription().Split('\n')[0].Length)) + "...",
                Foreground = new SolidColorBrush(Color.FromRgb(160, 160, 160)),
                FontSize = 13,
                Margin = new Thickness(0, 6, 0, 0)
            };
            infoPanel.Children.Add(descText);

            grid.Children.Add(infoPanel);

            // Right side: Action buttons
            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(buttonPanel, 1);

            var editBtn = CreateSessionActionButton("✏", "Edit Session", session.Id, SessionBtn_Edit);
            var exportBtn = CreateSessionActionButton("📤", Loc.Get("tooltip_export_session"), session.Id, SessionBtn_Export);
            var shareBtn = CreateSessionActionButton("☁", Loc.Get("tooltip_share_to_catalogue"), session.Id, SessionBtn_Share);
            var deleteBtn = CreateSessionDeleteButton("🗑", "Delete Session", session.Id, SessionBtn_Delete);

            buttonPanel.Children.Add(editBtn);
            buttonPanel.Children.Add(exportBtn);
            buttonPanel.Children.Add(shareBtn);
            buttonPanel.Children.Add(deleteBtn);

            grid.Children.Add(buttonPanel);
            border.Child = grid;

            PresetsTab.CustomSessionsPanel.Children.Add(border);
        }

        private Button CreateSessionActionButton(string content, string tooltip, string tag, RoutedEventHandler handler)
        {
            var btn = new Button
            {
                Content = content,
                ToolTip = tooltip,
                Tag = tag,
                Width = 26,
                Height = 26,
                Background = new SolidColorBrush(Color.FromRgb(53, 53, 85)),
                Foreground = new SolidColorBrush(Color.FromRgb(144, 144, 144)),
                BorderThickness = new Thickness(0),
                FontSize = 12,
                Cursor = System.Windows.Input.Cursors.Hand,
                Margin = new Thickness(2, 0, 0, 0)
            };
            btn.Click += handler;

            // Create template for rounded corners and hover effect
            var template = new ControlTemplate(typeof(Button));
            var borderFactory = new FrameworkElementFactory(typeof(Border));
            borderFactory.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
            borderFactory.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
            var contentFactory = new FrameworkElementFactory(typeof(ContentPresenter));
            contentFactory.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            contentFactory.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            borderFactory.AppendChild(contentFactory);
            template.VisualTree = borderFactory;

            var hoverTrigger = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hoverTrigger.Setters.Add(new Setter(Control.BackgroundProperty, FindResource("PinkBrush")));
            hoverTrigger.Setters.Add(new Setter(Control.ForegroundProperty, new SolidColorBrush(Colors.White)));
            template.Triggers.Add(hoverTrigger);

            btn.Template = template;
            return btn;
        }

        private Button CreateSessionDeleteButton(string content, string tooltip, string tag, RoutedEventHandler handler)
        {
            var btn = CreateSessionActionButton(content, tooltip, tag, handler);

            // Update hover to red
            var template = new ControlTemplate(typeof(Button));
            var borderFactory = new FrameworkElementFactory(typeof(Border));
            borderFactory.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
            borderFactory.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
            var contentFactory = new FrameworkElementFactory(typeof(ContentPresenter));
            contentFactory.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            contentFactory.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            borderFactory.AppendChild(contentFactory);
            template.VisualTree = borderFactory;

            var hoverTrigger = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hoverTrigger.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush(Color.FromRgb(232, 17, 35))));
            hoverTrigger.Setters.Add(new Setter(Control.ForegroundProperty, new SolidColorBrush(Colors.White)));
            template.Triggers.Add(hoverTrigger);

            btn.Template = template;
            return btn;
        }

        private void RemoveCustomSessionCard(Models.Session session)
        {
            var cardToRemove = PresetsTab.CustomSessionsPanel.Children
                .OfType<Border>()
                .FirstOrDefault(b => b.Tag as string == session.Id);

            if (cardToRemove != null)
            {
                PresetsTab.CustomSessionsPanel.Children.Remove(cardToRemove);
            }

            // Hide header if no more custom sessions
            if (PresetsTab.CustomSessionsPanel.Children.Count == 0)
            {
                PresetsTab.TxtCustomSessionsHeader.Visibility = Visibility.Collapsed;
            }
        }

        private void SelectSession(Models.Session session)
        {
            _selectedSession = session;

            // Clear preset selection
            _selectedPreset = null;
            RefreshPresetsList();

            // Hide preset panel, show session panel
            PresetsTab.PresetDetailScroller.Visibility = Visibility.Collapsed;
            PresetsTab.PresetButtonsPanel.Visibility = Visibility.Collapsed;
            PresetsTab.SessionDetailScroller.Visibility = Visibility.Visible;
            PresetsTab.SessionButtonsPanel.Visibility = Visibility.Visible;
            PresetsTab.SessionSpoilerPanel.Visibility = Visibility.Collapsed;
            PresetsTab.BtnRevealSpoilers.Content = Loc.Get("btn_reveal_details");

            PresetsTab.TxtDetailTitle.Text = $"{session.Icon} {session.GetModeAwareName()}";
            PresetsTab.TxtDetailSubtitle.Text = GenerateSessionTimelineDescription(session);
            PresetsTab.TxtSessionDuration.Text = $"{session.DurationMinutes} minutes";

            // Apply level-based XP multiplier for display
            var level = App.Settings?.Current?.PlayerLevel ?? 1;
            var multiplier = App.Progression?.GetSessionXPMultiplier(level) ?? 1.0;
            var scaledXP = (int)Math.Round(session.BonusXP * multiplier);
            if (multiplier > 1.0)
                PresetsTab.TxtSessionXP.Text = $"+{scaledXP} XP ({multiplier:F1}x)";
            else
                PresetsTab.TxtSessionXP.Text = $"+{scaledXP} XP";

            PresetsTab.TxtSessionDifficulty.Text = session.GetDifficultyText();

            // Show manual description + auto-generated feature summary (mode-aware)
            var description = session.GetModeAwareDescription() ?? "";
            var featureSummary = session.GenerateFeatureDescription();
            if (!string.IsNullOrWhiteSpace(description))
                PresetsTab.TxtSessionDescription.Text = description + "\n\n─────────────────\n\n" + featureSummary;
            else
                PresetsTab.TxtSessionDescription.Text = featureSummary;

            // Update XP color based on difficulty
            PresetsTab.TxtSessionXP.Foreground = session.Difficulty switch
            {
                Models.SessionDifficulty.Easy => new SolidColorBrush(Color.FromRgb(144, 238, 144)),
                Models.SessionDifficulty.Medium => new SolidColorBrush(Color.FromRgb(255, 215, 0)),
                Models.SessionDifficulty.Hard => new SolidColorBrush(Color.FromRgb(255, 165, 0)),
                Models.SessionDifficulty.Extreme => new SolidColorBrush(Color.FromRgb(255, 99, 71)),
                _ => new SolidColorBrush(Color.FromRgb(144, 238, 144))
            };

            // Hide corner GIF option for custom sessions
            PresetsTab.CornerGifOptionPanel.Visibility = session.HasCornerGifOption ? Visibility.Visible : Visibility.Collapsed;

            // Populate spoiler details
            PresetsTab.TxtSessionFlash.Text = session.GetSpoilerFlash();
            PresetsTab.TxtSessionSubliminal.Text = session.GetSpoilerSubliminal();
            PresetsTab.TxtSessionAudio.Text = session.GetSpoilerAudio();
            PresetsTab.TxtSessionOverlays.Text = session.GetSpoilerOverlays();
            PresetsTab.TxtSessionExtras.Text = session.GetSpoilerInteractive();
            PresetsTab.TxtSessionTimeline.Text = session.GetSpoilerTimeline();

            PresetsTab.BtnStartSession.IsEnabled = session.IsAvailable;
            PresetsTab.BtnStartSession.Content = session.IsAvailable ? "▶ Start Session" : "🔒 Coming Soon";
            PresetsTab.BtnExportSession.IsEnabled = true;
        }

        private string GenerateSessionTimelineDescription(Models.Session session)
        {
            var parts = new List<string>();

            if (session.Settings.FlashEnabled)
                parts.Add($"⚡ Flashes ({session.Settings.FlashPerHour}/hr)");
            if (session.Settings.SubliminalEnabled)
                parts.Add($"💭 Subliminals ({session.Settings.SubliminalPerMin}/min)");
            if (session.Settings.AudioWhispersEnabled)
                parts.Add("🔊 Audio Whispers");
            if (session.Settings.PinkFilterEnabled)
                parts.Add("💗 Pink Filter");
            if (session.Settings.SpiralEnabled)
                parts.Add("🌀 Spiral");
            if (session.Settings.BouncingTextEnabled)
                parts.Add("📝 Bouncing Text");
            if (session.Settings.BubblesEnabled)
                parts.Add("🫧 Bubbles");
            if (session.Settings.LockCardEnabled)
                parts.Add("🔒 Lock Cards");
            if (session.Settings.MandatoryVideosEnabled)
                parts.Add("🎬 Videos");
            if (session.Settings.MindWipeEnabled)
                parts.Add("🧠 Mind Wipe");

            if (parts.Count == 0)
                return "";

            return string.Join(" • ", parts);
        }

        internal void SessionDropZone_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files.Length == 1 && files[0].EndsWith(".session.json", StringComparison.OrdinalIgnoreCase))
                {
                    e.Effects = DragDropEffects.Copy;
                    PresetsTab.SessionDropZone.BorderBrush = FindResource("PinkBrush") as SolidColorBrush;
                    PresetsTab.DropZoneIcon.Text = "📥";
                    PresetsTab.DropZoneIcon.Foreground = FindResource("PinkBrush") as SolidColorBrush;
                }
                else
                {
                    e.Effects = DragDropEffects.None;
                    PresetsTab.SessionDropZone.BorderBrush = new SolidColorBrush(Color.FromRgb(255, 100, 100));
                    PresetsTab.DropZoneIcon.Text = "❌";
                    PresetsTab.DropZoneIcon.Foreground = new SolidColorBrush(Color.FromRgb(255, 100, 100));
                }
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
            e.Handled = true;
        }

        internal void SessionDropZone_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files.Length == 1 && files[0].EndsWith(".session.json", StringComparison.OrdinalIgnoreCase))
                {
                    e.Effects = DragDropEffects.Copy;
                }
                else
                {
                    e.Effects = DragDropEffects.None;
                }
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
            e.Handled = true;
        }

        internal void SessionDropZone_DragLeave(object sender, DragEventArgs e)
        {
            PresetsTab.SessionDropZone.BorderBrush = Application.Current.Resources["PanelAccentBrush"] as SolidColorBrush ?? new SolidColorBrush(Color.FromRgb(64, 64, 96));
            PresetsTab.DropZoneIcon.Text = "📂";
            PresetsTab.DropZoneIcon.Foreground = new SolidColorBrush(Color.FromRgb(112, 112, 144));
            PresetsTab.DropZoneStatus.Visibility = Visibility.Collapsed;
        }

        // Global window drag-drop handlers
        private void Window_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                var dropType = DetectDropType(files);

                if (dropType != DropType.None)
                {
                    e.Effects = DragDropEffects.Copy;
                    UpdateDropOverlay(dropType, files);
                    // Hide browser to avoid WebView2 airspace issue (renders on top of WPF)
                    SettingsTab.BrowserContainer.Visibility = Visibility.Hidden;
                    GlobalDropOverlay.Visibility = Visibility.Visible;
                }
                else
                {
                    e.Effects = DragDropEffects.None;
                }
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
            e.Handled = true;
        }

        /// <summary>
        /// Ctrl+T from the main window — open the avatar chat input panel.
        /// Bound via Window.InputBindings + Window.CommandBindings using
        /// AvatarTubeWindow.OpenChatCommand.
        /// </summary>
        private void OpenAvatarChat_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            App.AvatarWindow?.OpenChatInput();
            e.Handled = true;
        }

        /// <summary>
        /// Click on the Companion-tab "Chat: Ctrl+T" pill — opens the capture dialog,
        /// saves the new shortcut to settings, then re-applies the binding to both
        /// MainWindow and AvatarTubeWindow without restart.
        /// </summary>
        internal void BtnChatShortcut_Click(object sender, RoutedEventArgs e)
        {
            var settings = App.Settings?.Current?.CompanionPrompt;
            if (settings == null) return;

            var dlg = new ChatShortcutCaptureDialog
            {
                Owner = this,
                GlobalHotkey = settings.ChatShortcutGlobal,
            };
            var ok = dlg.ShowDialog();
            if (ok != true) return;

            if (dlg.ResetToDefault)
            {
                settings.ChatShortcutKey = "T";
                settings.ChatShortcutModifiers = "Control";
            }
            else
            {
                settings.ChatShortcutKey = dlg.CapturedKey.ToString();
                settings.ChatShortcutModifiers = AvatarTubeWindow.SerializeModifiers(dlg.CapturedModifiers);
            }
            settings.ChatShortcutGlobal = dlg.GlobalHotkey;
            App.Settings?.Save();

            // Re-apply on both windows so the new shortcut takes effect immediately.
            AvatarTubeWindow.ApplyChatShortcutTo(this);
            if (App.AvatarWindow != null) AvatarTubeWindow.ApplyChatShortcutTo(App.AvatarWindow);

            // Re-arm (or unregister) the system-wide hotkey based on the toggle.
            ApplyGlobalChatHotkey();

            RefreshChatShortcutLabel();
        }

        /// <summary>
        /// Reads the user's saved chat-shortcut combo and registers it as a system-wide
        /// hotkey. Falls back silently if the OS rejects the combo (already taken).
        /// </summary>
        private void ApplyGlobalChatHotkey()
        {
            var s = App.Settings?.Current?.CompanionPrompt;

            // Honor the user's toggle. When off, we still rely on the in-window KeyBinding
            // (registered by AvatarTubeWindow.ApplyChatShortcutTo) so the shortcut works
            // when one of our windows has focus — but it won't steal focus from a browser
            // or other foreground app.
            if (s?.ChatShortcutGlobal == false)
            {
                Services.GlobalHotkeyService.Unregister();
                return;
            }

            var keyName = string.IsNullOrWhiteSpace(s?.ChatShortcutKey) ? "T" : s!.ChatShortcutKey;
            var modsName = s?.ChatShortcutModifiers ?? "Control";

            if (!Enum.TryParse<Key>(keyName, ignoreCase: true, out var key)) key = Key.T;
            var mods = ModifierKeys.None;
            if (!string.IsNullOrWhiteSpace(modsName))
            {
                foreach (var part in modsName.Split(new[] { ',', '+', ' ' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (Enum.TryParse<ModifierKeys>(part, ignoreCase: true, out var mk)) mods |= mk;
                }
            }
            if (mods == ModifierKeys.None) mods = ModifierKeys.Control;

            Services.GlobalHotkeyService.Register(this, mods, key, () =>
            {
                // Marshal to UI thread — Win32 hotkeys arrive on the message-pump thread
                // (which is the dispatcher in WPF, but the helper API doesn't enforce it).
                Dispatcher.BeginInvoke(new Action(BringToForegroundAndOpenChat));
            });
        }

        /// <summary>
        /// Full "wake up the app" sequence for the global chat hotkey: un-minimize
        /// MainWindow, bring it to the foreground, re-show the avatar tube (which
        /// auto-hides while the main window is minimized in attached mode), then
        /// open the chat input. Used when the shortcut is pressed from another app.
        /// </summary>
        private void BringToForegroundAndOpenChat()
        {
            try
            {
                // 1. Restore MainWindow if minimized.
                if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
                Show();

                // 2. Force MainWindow to the foreground. Topmost flicker is the WPF idiom
                //    that bypasses focus-stealing prevention without Win32 antics.
                Activate();
                Topmost = true;
                Topmost = false;

                // 3. Re-show the avatar tube. When MainWindow minimizes, attached avatars
                //    are hidden by HideAvatarTube — so without this the chat input would
                //    open on a hidden window.
                ShowAvatarTube();

                // 4. Open chat input. AvatarTubeWindow.OpenChatInput does its own
                //    AttachThreadInput dance to claim keyboard focus reliably.
                App.AvatarWindow?.OpenChatInput();
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "BringToForegroundAndOpenChat failed");
            }
        }

        /// <summary>Updates the hero pill text to match the saved shortcut.</summary>
        public void RefreshChatShortcutLabel()
        {
            try
            {
                if (CompanionTab.TxtChatShortcutLabel != null)
                    CompanionTab.TxtChatShortcutLabel.Text = AvatarTubeWindow.FormatChatShortcut();
            }
            catch { /* Tab not yet realized, fine */ }
        }

        private void Window_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                var dropType = DetectDropType(files);
                e.Effects = dropType != DropType.None ? DragDropEffects.Copy : DragDropEffects.None;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
            e.Handled = true;
        }

        private void Window_DragLeave(object sender, DragEventArgs e)
        {
            GlobalDropOverlay.Visibility = Visibility.Collapsed;
            SettingsTab.BrowserContainer.Visibility = Visibility.Visible;
        }

        private async void Window_Drop(object sender, DragEventArgs e)
        {
            GlobalDropOverlay.Visibility = Visibility.Collapsed;
            SettingsTab.BrowserContainer.Visibility = Visibility.Visible;

            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;

            var files = (string[])e.Data.GetData(DataFormats.FileDrop);

            // Single-file media drop: prompt Play / Edit / Add-to-Library.
            // Multi-file, folder, and .zip drops fall through to the existing flow.
            if (files.Length == 1 && File.Exists(files[0]) && IsDeeperPlayableMedia(files[0]))
            {
                var choice = ShowMediaDropChoiceDialog(files[0]);
                switch (choice)
                {
                    case MediaDropChoice.Play: OpenInDeeperPlayer(files[0]); return;
                    case MediaDropChoice.Edit: OpenInDeeperEditorForMedia(files[0]); return;
                    case MediaDropChoice.Library: await HandleAssetDropAsync(files); return;
                    case MediaDropChoice.Cancel: return;
                }
            }

            var dropType = DetectDropType(files);

            switch (dropType)
            {
                case DropType.Session:
                    HandleSessionDrop(files[0]);
                    break;

                case DropType.Preset:
                    HandlePresetDrop(files[0]);
                    break;

                case DropType.Enhancement:
                    ImportEnhancementFiles(files);
                    break;

                case DropType.Mod:
                    await HandleModDropAsync(files[0]);
                    break;

                case DropType.Assets:
                case DropType.Zip:
                case DropType.Folder:
                    await HandleAssetDropAsync(files);
                    break;
            }
        }

        private enum DropType { None, Session, Preset, Assets, Zip, Folder, Enhancement, Mod }

        private static readonly HashSet<string> AssetVideoExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".mp4", ".avi", ".mkv", ".mov", ".wmv", ".webm", ".m4v", ".flv", ".mpeg", ".mpg", ".3gp"
        };

        private static readonly HashSet<string> AssetImageExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".tiff", ".tif"
        };

        // Deeper-playable subsets — narrower than AssetVideoExtensions because the
        // player's WebView2 + NAudio backends only handle these. Used by the
        // "Open with CCP" file association and the single-file drop prompt.
        private static readonly HashSet<string> DeeperVideoExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".mp4", ".webm", ".mkv", ".mov", ".avi", ".m4v"
        };

        private static readonly HashSet<string> DeeperAudioExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".mp3", ".wav", ".m4a", ".aac", ".flac", ".ogg"
        };

        private enum MediaDropChoice { Cancel, Play, Edit, Library }

        private static bool IsDeeperPlayableMedia(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            var ext = Path.GetExtension(path);
            return DeeperVideoExtensions.Contains(ext) || DeeperAudioExtensions.Contains(ext);
        }

        private static DropType DetectDropType(string[] files)
        {
            if (files.Length == 0) return DropType.None;

            // Single session file
            if (files.Length == 1 && files[0].EndsWith(".session.json", StringComparison.OrdinalIgnoreCase))
                return DropType.Session;

            // Single preset file
            if (files.Length == 1 && files[0].EndsWith(".preset.json", StringComparison.OrdinalIgnoreCase))
                return DropType.Preset;

            // Single mod package. Must be detected before the generic zip/asset
            // branches — a .ccpmod IS a zip, but it installs, not extracts.
            if (files.Length == 1 && files[0].EndsWith(".ccpmod", StringComparison.OrdinalIgnoreCase))
                return DropType.Mod;

            // Deeper enhancement project file(s). Accept the canonical *.ccpenh.json
            // double-suffix or a plain *.json (the serializer rejects non-enhancement
            // JSON on import), but never a *.session.json / *.preset.json — those are
            // handled above and would otherwise be swallowed by the plain-*.json rule.
            if (files.All(IsImportableEnhancementPath)
                && !files.Any(f => f.EndsWith(".session.json", StringComparison.OrdinalIgnoreCase))
                && !files.Any(f => f.EndsWith(".preset.json", StringComparison.OrdinalIgnoreCase)))
                return DropType.Enhancement;

            // Single folder
            if (files.Length == 1 && Directory.Exists(files[0]))
                return DropType.Folder;

            // Check for ZIP files or asset files
            var hasZip = false;
            var hasAssets = false;

            foreach (var file in files)
            {
                if (Directory.Exists(file))
                {
                    hasAssets = true;
                    continue;
                }

                var ext = Path.GetExtension(file);
                if (ext.Equals(".zip", StringComparison.OrdinalIgnoreCase))
                    hasZip = true;
                else if (AssetVideoExtensions.Contains(ext) || AssetImageExtensions.Contains(ext))
                    hasAssets = true;
            }

            if (hasZip) return DropType.Zip;
            if (hasAssets) return DropType.Assets;

            return DropType.None;
        }

        private void UpdateDropOverlay(DropType dropType, string[] files)
        {
            switch (dropType)
            {
                case DropType.Session:
                    DropOverlayIcon.Text = "📋";
                    DropOverlayTitle.Text = Loc.Get("label_drop_to_import_session");
                    DropOverlaySubtitle.Text = Path.GetFileName(files[0]);
                    break;

                case DropType.Preset:
                    DropOverlayIcon.Text = "🎛️";
                    DropOverlayTitle.Text = Loc.Get("label_drop_to_import_preset");
                    DropOverlaySubtitle.Text = Path.GetFileName(files[0]);
                    break;

                case DropType.Enhancement:
                    DropOverlayIcon.Text = "🌊";
                    DropOverlayTitle.Text = Loc.Get("label_drop_to_import_enhancement");
                    DropOverlaySubtitle.Text = files.Length == 1
                        ? Path.GetFileName(files[0])
                        : $"{files.Length} enhancements";
                    break;

                case DropType.Mod:
                    DropOverlayIcon.Text = "🧩";
                    DropOverlayTitle.Text = Loc.Get("label_drop_to_install_mod");
                    DropOverlaySubtitle.Text = Path.GetFileName(files[0]);
                    break;

                case DropType.Zip:
                    DropOverlayIcon.Text = "📦";
                    DropOverlayTitle.Text = Loc.Get("label_drop_to_extract_assets");
                    var zipCount = files.Count(f => Path.GetExtension(f).Equals(".zip", StringComparison.OrdinalIgnoreCase));
                    DropOverlaySubtitle.Text = zipCount == 1
                        ? Path.GetFileName(files.First(f => f.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)))
                        : $"{zipCount} ZIP files";
                    break;

                case DropType.Folder:
                    DropOverlayIcon.Text = "📁";
                    DropOverlayTitle.Text = Loc.Get("label_drop_to_import_folder");
                    DropOverlaySubtitle.Text = $"Scan for images & videos";
                    break;

                case DropType.Assets:
                    DropOverlayIcon.Text = "🖼️";
                    DropOverlayTitle.Text = Loc.Get("label_drop_to_import_assets");
                    DropOverlaySubtitle.Text = files.Length == 1
                        ? Path.GetFileName(files[0])
                        : $"{files.Length} files";
                    break;
            }
        }

        private void HandleSessionDrop(string filePath)
        {
            // Validate and import session
            if (_sessionFileService == null)
            {
                _sessionFileService = new Services.SessionFileService();
            }

            if (!_sessionFileService.ValidateSessionFile(filePath, out var errorMessage))
            {
                ShowDropZoneStatus($"Invalid: {errorMessage}", isError: true);
                return;
            }

            if (_sessionManager == null)
            {
                InitializeSessionManager();
            }

            var result = _sessionManager!.ImportSession(filePath);
            if (result.success)
            {
                ShowDropZoneStatus($"Session loaded: {result.session?.Name}", isError: false);
                App.Logger?.Information("Session imported via drag-drop: {Name}", result.session?.Name);
            }
            else
            {
                ShowDropZoneStatus($"Failed: {result.message}", isError: true);
            }
        }

        private async Task HandleAssetDropAsync(string[] paths)
        {
            try
            {
                _assetImportService ??= new Services.AssetImportService();

                var progress = new Progress<Services.ImportProgress>(p =>
                {
                    // Could update a progress indicator here if needed
                    App.Logger?.Debug("Import progress: {Current}/{Total} - {File}", p.Current, p.Total, p.CurrentFile);
                });

                var result = await Task.Run(() => _assetImportService.ImportAsync(paths, progress));

                // Refresh the asset lists if any were imported
                if (result.ImagesImported > 0)
                {
                    App.Flash?.RefreshImagesPath();
                    RefreshImagesList();
                }

                if (result.VideosImported > 0)
                {
                    App.Video?.RefreshVideosPath();
                    RefreshVideosList();
                }

                RefreshAssetTree();
                ShowTab("assets");

                App.Logger?.Information("Asset import complete: {Summary}", result.GetSummary());
                MessageBox.Show(result.GetSummary(), Loc.Get("title_import_complete"), MessageBoxButton.OK,
                    result.TotalImported > 0 ? MessageBoxImage.Information : MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "Asset import failed");
                MessageBox.Show(Loc.GetF("msg_import_failed_0", ex.Message), Loc.Get("title_import_error"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void RefreshImagesList()
        {
            // The FlashService manages its own file list internally
            // RefreshImagesPath() already clears and reloads the cache
            App.Logger?.Debug("Images list refreshed after import");
        }

        private void RefreshVideosList()
        {
            // The VideoService manages its own file list internally
            // RefreshVideosPath() already clears and reloads the cache
            App.Logger?.Debug("Videos list refreshed after import");
        }

        // Session action button handlers
        internal void SessionBtn_Edit(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string sessionId)
            {
                var session = GetSessionById(sessionId);
                if (session == null) return;

                var editor = new SessionEditorWindow(session);
                editor.Owner = this;
                if (editor.ShowDialog() == true && editor.ResultSession != null)
                {
                    if (_sessionFileService == null) _sessionFileService = new Services.SessionFileService();
                    if (_sessionManager == null) InitializeSessionManager();

                    var editedSession = editor.ResultSession;

                    if (session.Source == Models.SessionSource.BuiltIn)
                    {
                        // Editing a built-in session creates a new custom session
                        editedSession.Id = Guid.NewGuid().ToString(); // New ID

                        var dialog = new Microsoft.Win32.SaveFileDialog
                        {
                            Filter = "Session Files (*.session.json)|*.session.json",
                            Title = Loc.Get("title_save_as_new_custom_session"),
                            InitialDirectory = SessionFileService.CustomSessionsFolder,
                            FileName = SessionFileService.GetExportFileName(editedSession)
                        };

                        if (dialog.ShowDialog() == true)
                        {
                            _sessionManager.AddNewSession(editedSession, dialog.FileName);
                            MessageBox.Show(Loc.Get("msg_built_in_session_saved_as_a_new_custom_sessio"), Loc.Get("title_success"), MessageBoxButton.OK, MessageBoxImage.Information);
                        }
                    }
                    else // Custom session
                    {
                        // Preserve original ID, source, and file path to save over existing file
                        editedSession.Id = session.Id;
                        editedSession.Source = session.Source;
                        editedSession.SourceFilePath = session.SourceFilePath;
                        _sessionManager.UpdateCustomSession(editedSession);
                        
                        SelectSession(editedSession);
                        ShowDropZoneStatus($"Session updated: {editedSession.Name}", isError: false);
                    }
                }
            }
            e.Handled = true;
        }

        internal void SessionBtn_Export(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string sessionId)
            {
                var session = GetSessionById(sessionId);
                if (session != null)
                {
                    ExportSessionToFile(session);
                }
            }
            e.Handled = true;
        }

        private async void SessionBtn_Share(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            if (sender is not Button btn || btn.Tag is not string sessionId) return;
            var session = GetSessionById(sessionId);
            if (session == null) return;
            await ShareSessionToCatalogueAsync(session);
        }

        // Clears and repopulates the custom session cards (used to refresh share
        // status badges after a submission resolves or a status poll updates).
        private void RefreshCustomSessionCards()
        {
            if (_sessionManager == null) return;
            PresetsTab.CustomSessionsPanel.Children.Clear();
            foreach (var session in _sessionManager.CustomSessions)
            {
                AddCustomSessionCard(session);
            }
            if (PresetsTab.CustomSessionsPanel.Children.Count == 0)
            {
                PresetsTab.TxtCustomSessionsHeader.Visibility = Visibility.Collapsed;
            }
        }

        private void SessionBtn_Delete(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string sessionId)
            {
                var session = GetSessionById(sessionId);
                if (session == null) return;

                // Confirm deletion
                var result = ShowStyledDialog(
                    Loc.Get("title_delete_session"),
                    Loc.GetF("msg_delete_session_confirm_0", session.Name),
                    Loc.Get("btn_delete"), Loc.Get("btn_cancel"));

                if (result && _sessionManager != null)
                {
                    _sessionManager.DeleteSession(session);
                    ShowDropZoneStatus($"Deleted: {session.Name}", isError: false);

                    // Clear selection if this was selected
                    if (_selectedSession?.Id == sessionId)
                    {
                        _selectedSession = null;
                        PresetsTab.TxtDetailTitle.Text = Loc.Get("label_select_a_session");
                        PresetsTab.TxtDetailSubtitle.Text = Loc.Get("label_click_on_a_session_to_see_details");
                    }
                }
            }
            e.Handled = true;
        }

        private Models.Session? GetSessionById(string sessionId)
        {
            // Check session manager first
            if (_sessionManager != null)
            {
                var session = _sessionManager.GetSession(sessionId);
                if (session != null) return session;
            }

            // Fall back to hardcoded sessions
            return Models.Session.GetAllSessions().FirstOrDefault(s => s.Id == sessionId);
        }

        internal void SessionDropZone_Drop(object sender, DragEventArgs e)
        {
            // Mark handled to prevent Window_Drop from also importing the session
            e.Handled = true;

            // Reset visual state
            PresetsTab.SessionDropZone.BorderBrush = Application.Current.Resources["PanelAccentBrush"] as SolidColorBrush ?? new SolidColorBrush(Color.FromRgb(64, 64, 96));
            PresetsTab.DropZoneIcon.Text = "📂";
            PresetsTab.DropZoneIcon.Foreground = new SolidColorBrush(Color.FromRgb(112, 112, 144));

            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;

            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files.Length != 1) return;

            var filePath = files[0];
            if (!filePath.EndsWith(".session.json", StringComparison.OrdinalIgnoreCase))
            {
                ShowDropZoneStatus(Loc.Get("msg_only_session_json_files_allowed"), isError: true);
                return;
            }

            // Validate and import
            if (_sessionFileService == null)
            {
                _sessionFileService = new Services.SessionFileService();
            }

            if (!_sessionFileService.ValidateSessionFile(filePath, out var errorMessage))
            {
                ShowDropZoneStatus($"Invalid: {errorMessage}", isError: true);
                return;
            }

            if (_sessionManager == null)
            {
                InitializeSessionManager();
            }

            var result = _sessionManager!.ImportSession(filePath);
            if (result.success)
            {
                ShowDropZoneStatus($"Imported: {result.session?.Name}", isError: false);
                App.Logger?.Information("Session imported via drag-drop: {Name}", result.session?.Name);
            }
            else
            {
                ShowDropZoneStatus($"Failed: {result.message}", isError: true);
            }
        }

        private void ShowDropZoneStatus(string message, bool isError)
        {
            PresetsTab.DropZoneStatus.Text = message;
            PresetsTab.DropZoneStatus.Foreground = isError
                ? new SolidColorBrush(Color.FromRgb(255, 100, 100))
                : FindResource("PinkBrush") as SolidColorBrush;
            PresetsTab.DropZoneStatus.Visibility = Visibility.Visible;

            // Auto-hide after 3 seconds
            var timer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(3)
            };
            timer.Tick += (s, e) =>
            {
                PresetsTab.DropZoneStatus.Visibility = Visibility.Collapsed;
                timer.Stop();
            };
            timer.Start();
        }

        internal void BtnExportSession_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedSession == null) return;
            ExportSessionToFile(_selectedSession);
        }

        internal void BtnCreateSession_Click(object sender, RoutedEventArgs e)
        {
            var editor = new SessionEditorWindow();
            editor.Owner = this;
            if (editor.ShowDialog() == true && editor.ResultSession != null)
            {
                if (_sessionFileService == null)
                {
                    _sessionFileService = new Services.SessionFileService();
                }

                var session = editor.ResultSession;

                var dialog = new Microsoft.Win32.SaveFileDialog
                {
                    Filter = "Session Files (*.session.json)|*.session.json",
                    Title = Loc.Get("title_save_new_session"),
                    InitialDirectory = SessionFileService.CustomSessionsFolder,
                    FileName = SessionFileService.GetExportFileName(session)
                };

                if (dialog.ShowDialog() == true)
                {
                    if (_sessionManager == null) InitializeSessionManager();
                    _sessionManager.AddNewSession(session, dialog.FileName);

                    // The OnSessionAdded event will handle UI updates
                    MessageBox.Show(Loc.Get("msg_new_session_saved"), Loc.Get("title_success"), MessageBoxButton.OK, MessageBoxImage.Information);
                    App.Logger?.Information("Session created: {Name} at {Path}", session.Name, dialog.FileName);
                }
            }
        }

        private void SessionContextMenu_Export(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem && menuItem.Tag is string sessionId)
            {
                var sessions = Models.Session.GetAllSessions();
                var session = sessions.FirstOrDefault(s => s.Id == sessionId);
                if (session != null)
                {
                    ExportSessionToFile(session);
                }
            }
        }

        private void ExportSessionToFile(Models.Session session)
        {
            if (_sessionFileService == null)
            {
                _sessionFileService = new Services.SessionFileService();
            }

            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = Loc.Get("title_export_session"),
                Filter = "Session files (*.session.json)|*.session.json",
                FileName = Services.SessionFileService.GetExportFileName(session),
                DefaultExt = ".session.json"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    _sessionFileService.ExportSession(session, dialog.FileName);
                    ShowStyledDialog(Loc.Get("title_export_complete"), Loc.GetF("msg_session_exported_to_0", dialog.FileName), "OK", "");
                    App.Logger?.Information("Session exported: {Name} to {Path}", session.Name, dialog.FileName);
                }
                catch (Exception ex)
                {
                    ShowStyledDialog(Loc.Get("title_export_failed"), Loc.GetF("msg_failed_to_export_session_0", ex.Message), "OK", "");
                    App.Logger?.Error(ex, "Failed to export session");
                }
            }
        }

        #endregion
    }
}
