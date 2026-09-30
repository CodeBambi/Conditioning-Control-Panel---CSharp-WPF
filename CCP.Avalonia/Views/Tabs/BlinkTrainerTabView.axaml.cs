using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Lab.GazeMinigame;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Webcam;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Tabs
{
    /// <summary>
    /// BLINK TRAINER tab, ported from the WPF head. On WPF these handlers hop to
    /// <c>MainWindow.BlinkTrainer.cs</c>; here the view owns the parts that need no camera:
    /// the tab-show refresh (<c>MainWindow.TabNavigation.cs RefreshBlinkTrainerTab</c>), the demo
    /// stage loop, the premium gate, the status row (decided by Core <see cref="BlinkTrainerState"/>,
    /// which WPF also calls), the folder library and the session settings editors.
    /// </summary>
    public partial class BlinkTrainerTabView : UserControl
    {
        private static readonly IBrush Amber = new SolidColorBrush(Color.FromRgb(0xFF, 0xD0, 0x80));
        private static readonly IBrush Green = new SolidColorBrush(Color.FromRgb(0x4A, 0xDE, 0x80));
        private static readonly IBrush Red = new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44));

        private DispatcherTimer? _demoTimer;
        private DispatcherTimer? _tick;
        private bool _liveSubscribed;
        private string? _liveLast;
        private readonly List<Bitmap> _liveBitmaps = new();
        private List<Bitmap>? _demoAssets;
        private int _demoIndex;
        private bool _demoUsingA = true;
        private Action? _statusAction;

        /// <summary>The status row's current state; read by tests and by nothing else yet.</summary>
        internal BlinkTrainerStatusState StatusState { get; private set; }

        public BlinkTrainerTabView()
        {
            InitializeComponent(); // generated: fills the x:Name fields (AvaloniaXamlLoader.Load would not)
            Helpers.ModArt.BindFeaturePlates(this, "features/blink_trainer.png", HeroArt, SideArt);

            // WPF fades with a 200ms QuadraticEase InOut storyboard per swap.
            foreach (var img in new[] { BlinkTrainerStageImageA, BlinkTrainerStageImageB })
                img.Transitions = new Transitions { new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(200), Easing = new QuadraticEaseInOut() } };

            // WPF listens on PreviewMouseLeftButtonDown; Slider handles the bubbling press itself.
            foreach (var s in new[] { SliderBlinkTrainerDurationNew, SliderBlinkTrainerOpacityNew })
                s.AddHandler(PointerPressedEvent, BlinkTrainerSlider_DragStart, RoutingStrategies.Tunnel);

            BlinkTrainerStatusAction.Click += (_, _) => _statusAction?.Invoke();

            // Calibration needs the gaze feed WebcamTracker does not emit yet: visibly off, with the
            // reason on hover. Session and tracker toggle are live (Overlays/BlinkTrainerSession, Platform/WebcamTracker).
            foreach (var b in new[] { BtnBlinkTrainerCalibrate, BtnBlinkTrainerQuickRecal })
            {
                b.IsEnabled = false;
                ToolTip.SetShowOnDisabled(b, true);
                b.Bind(ToolTip.TipProperty, new Binding("[deeper_player_eye_tracking_unavailable]") { Source = LocalizationManager.Instance });
            }

            // WPF ShowTab: RefreshBlinkTrainerTab on entry, StopBlinkTrainerDemoLoop on exit.
            PropertyChanged += (_, e) =>
            {
                if (e.Property != IsVisibleProperty) return;
                if (IsVisible) Refresh(); else StopDemoLoop();
            };

            // WPF HookBlinkTrainerService: one fan-out for session and tracker state.
            AttachedToVisualTree += (_, _) =>
            {
                BlinkTrainerSession.StateChanged += OnSessionStateChanged;
                Platform.WebcamTracker.Instance.StateChanged += OnSessionStateChanged;
            };
            DetachedFromVisualTree += (_, _) =>
            {
                BlinkTrainerSession.StateChanged -= OnSessionStateChanged;
                Platform.WebcamTracker.Instance.StateChanged -= OnSessionStateChanged;
                Platform.WebcamTracker.Instance.OnBlink -= OnStagePreviewBlink;
                _tick?.Stop();
                _tick = null;
            };
        }

        /// <summary>WPF OnBlinkTrainerServiceStateChanged: countdown timer, status row, stage mode.</summary>
        private void OnSessionStateChanged()
        {
            try
            {
                if (BlinkTrainerSession.IsRunning) _tick ??= new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Normal, (_, _) => Tick());
                else { _tick?.Stop(); _tick = null; }
                RefreshTrackerButton();
                RefreshStatusRow();
                ApplyStageMode();
            }
            catch (Exception ex) { Log.Warning(ex, "BlinkTrainer state refresh failed"); }
        }

        /// <summary>WPF BlinkTrainerTick: the Running text counts down.</summary>
        private void Tick()
        {
            if (!BlinkTrainerSession.IsRunning || StatusState != BlinkTrainerStatusState.Running) return;
            var rem = BlinkTrainerSession.Remaining;
            BlinkTrainerStatusText.Text = Loc.GetF("blink_trainer_status_running", rem.ToString(rem.TotalHours >= 1 ? @"h\:mm\:ss" : @"mm\:ss"));
        }

        internal void Refresh()
        {
            try
            {
                var s = CoreSettings.Current;
                // A programmatic set raises Changed, which writes back the value just read - as on
                // WPF. The markup defaults never land: XAML assigns Value before ValueChanged is hooked
                // (BlinkTrainerTabLiveTests proves a saved 42 survives construction).
                {
                    ToggleBlinkTrainerIncludeVideos.IsChecked = s.BlinkTrainerIncludeVideos;
                    SliderBlinkTrainerDurationNew.Value = s.BlinkTrainerDurationMinutes;
                    TxtBlinkTrainerDurationValue.Text = $"{s.BlinkTrainerDurationMinutes} min";
                    SliderBlinkTrainerOpacityNew.Value = s.BlinkTrainerOpacity;
                    TxtBlinkTrainerOpacityValue.Text = $"{s.BlinkTrainerOpacity}%";
                    ApplyOpacityFill(s.BlinkTrainerOpacity);
                    SetMixModeSelection(s.BlinkTrainerMixImages);
                }

                RebuildFolderCards();
                RefreshTrackerButton();
                RefreshWebcamColumn();
                RefreshGate();
                RefreshStatusRow();
                ApplyStageMode();
            }
            catch (Exception ex) { Log.Warning(ex, "RefreshBlinkTrainerTab failed"); }
        }

        // ---- gate + stage ----------------------------------------------------------------

        private void RefreshGate()
        {
            bool premium = CoreEntitlement.HasPremium;
            BlinkTrainerGate.IsVisible = !premium;
            BlinkTrainerGatedContent.IsEnabled = premium;
            BlinkTrainerStageActions.IsEnabled = premium;
        }

        /// <summary>WPF DetermineBlinkTrainerStageMode + ApplyBlinkTrainerStageMode: non-premium always
        /// sees the demo; a running session or consent + folders is live, which parks the stage blank
        /// (ResetBlinkTrainerStageForLive) and swaps it on every real blink.</summary>
        private void ApplyStageMode()
        {
            var s = CoreSettings.Current;
            bool live = CoreEntitlement.HasPremium
                && (BlinkTrainerSession.IsRunning || (WebcamConsent.IsCurrent(s) && s.BlinkTrainerFolders.Count > 0));
            if (!live)
            {
                if (_liveSubscribed) { Platform.WebcamTracker.Instance.OnBlink -= OnStagePreviewBlink; _liveSubscribed = false; }
                StartDemoLoop();
                return;
            }
            if (_liveSubscribed) return;   // LivePreview <-> LiveSession is a no-op, as on WPF
            StopDemoLoop();
            SetOpacityNow(BlinkTrainerStageImageA, 0);
            SetOpacityNow(BlinkTrainerStageImageB, 0);
            _demoUsingA = true;
            _liveLast = null;
            Platform.WebcamTracker.Instance.OnBlink += OnStagePreviewBlink;
            _liveSubscribed = true;
        }

        internal bool LivePreview => _liveSubscribed;

        private BlinkTrainerAssetPool? _livePool;
        private string _livePoolToken = "";

        /// <summary>WPF GetOrBuildBlinkTrainerLivePool: rebuilt only when the folders or the videos toggle change.</summary>
        internal BlinkTrainerAssetPool LivePool(Models.AppSettings s)
        {
            var token = string.Join("|", s.BlinkTrainerFolders) + "::" + s.BlinkTrainerIncludeVideos;
            if (_livePool == null || _livePoolToken != token) { _livePool = BlinkTrainerAssetPool.Build(s.BlinkTrainerFolders, s.BlinkTrainerIncludeVideos); _livePoolToken = token; }
            return _livePool;
        }

        /// <summary>WPF OnBlinkTrainerStagePreviewBlink + ApplyBlinkTrainerLiveImage: a hard-cut swap.
        /// ponytail: a video pick is skipped on the stage (WPF plays it in a MediaElement); the
        /// session overlay plays it.</summary>
        internal void OnStagePreviewBlink()
        {
            try
            {
                var s = CoreSettings.Current;
                var path = LivePool(s).PickRandom(_liveLast);
                if (path == null || BlinkTrainerAssetPool.IsVideo(path)) return;
                _liveLast = path;
                var bmp = new Bitmap(path);
                var incoming = _demoUsingA ? BlinkTrainerStageImageB : BlinkTrainerStageImageA;
                var outgoing = _demoUsingA ? BlinkTrainerStageImageA : BlinkTrainerStageImageB;
                // Two live frames at most: the one fading out and the new one.
                if (incoming.Source is Bitmap old && _liveBitmaps.Remove(old)) { incoming.Source = null; old.Dispose(); }
                _liveBitmaps.Add(bmp);
                incoming.Source = bmp;
                SetOpacityNow(incoming, 1);
                SetOpacityNow(outgoing, 0);
                _demoUsingA = !_demoUsingA;
            }
            catch (Exception ex) { Log.Warning(ex, "OnBlinkTrainerStagePreviewBlink failed"); }
        }

        internal bool DemoRunning => _demoTimer != null;

        private void StartDemoLoop()
        {
            if (_demoTimer != null) return;
            if (_demoAssets == null)
            {
                _demoAssets = Enumerable.Range(1, 4)
                    .Select(i => Helpers.ModArt.TryLoad($"BlinkTrainer/Demo/demo_{i:00}.png"))
                    .OfType<Bitmap>().OrderBy(_ => Random.Shared.Next()).ToList();
            }
            if (_demoAssets.Count == 0) { Log.Warning("BlinkTrainer: demo loop skipped — no demo assets loaded"); return; }

            _demoIndex = 0;
            _demoUsingA = true;
            BlinkTrainerStageImageA.Source = _demoAssets[0];
            SetOpacityNow(BlinkTrainerStageImageA, 1);
            BlinkTrainerStageImageB.Source = null;
            SetOpacityNow(BlinkTrainerStageImageB, 0);

            _demoTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.0) };
            _demoTimer.Tick += (_, _) => AdvanceDemo();
            _demoTimer.Start();
        }

        private void StopDemoLoop()
        {
            _demoTimer?.Stop();
            _demoTimer = null;
        }

        internal void AdvanceDemo()
        {
            if (_demoAssets is not { Count: > 0 }) return;
            _demoIndex = (_demoIndex + 1) % _demoAssets.Count;
            var incoming = _demoUsingA ? BlinkTrainerStageImageB : BlinkTrainerStageImageA;
            var outgoing = _demoUsingA ? BlinkTrainerStageImageA : BlinkTrainerStageImageB;
            incoming.Source = _demoAssets[_demoIndex];
            incoming.Opacity = 1;
            outgoing.Opacity = 0;
            _demoUsingA = !_demoUsingA;
        }

        /// <summary>A hard set, like WPF's BeginAnimation(null) + assign: no 200ms fade.</summary>
        private static void SetOpacityNow(Image img, double value)
        {
            var t = img.Transitions;
            img.Transitions = null;
            img.Opacity = value;
            img.Transitions = t;
        }

        // ---- status row --------------------------------------------------------------------

        private void RefreshStatusRow()
        {
            var s = CoreSettings.Current;
            bool multiMonitor = TopLevel.GetTopLevel(this) is Window w && w.Screens.ScreenCount > 1;
            // ponytail: no calibration on this head yet (it needs the gaze feed), so HasUsableCalibration
            // is false: multi-monitor reads NeedsCalibration, which still lets the session start (WPF).
            StatusState = BlinkTrainerState.Status(BlinkTrainerSession.IsRunning, BlinkTrainerSession.LastError,
                WebcamConsent.IsCurrent(s), s.BlinkTrainerFolders.Count, multiMonitor, calibrationUsable: false);

            BlinkTrainerStatusDot.Fill = StatusState switch
            {
                BlinkTrainerStatusState.IdleReady => this.FindResource("PinkBrush") as IBrush ?? Brushes.HotPink,
                BlinkTrainerStatusState.Running => Green,
                BlinkTrainerStatusState.Error => Red,
                _ => Amber,
            };
            BlinkTrainerStatusText.Foreground = StatusState == BlinkTrainerStatusState.Error ? Red
                : this.FindResource("TextMutedBrush") as IBrush ?? Brushes.Gray;
            if (StatusState is BlinkTrainerStatusState.Running or BlinkTrainerStatusState.Error)
            {
                BlinkTrainerStatusText.ClearValue(TextBlock.TextProperty);   // drop the {loc} binding first
                BlinkTrainerStatusText.Text = BlinkTrainerSession.LastError;   // WPF passes the error through as-is
                Tick();
            }
            else BindLoc(BlinkTrainerStatusText, StatusState switch
            {
                BlinkTrainerStatusState.NeedsConsent => "blink_trainer_status_needs_consent",
                BlinkTrainerStatusState.NeedsFolders => "blink_trainer_status_needs_folders",
                BlinkTrainerStatusState.NeedsCalibration => "blink_trainer_status_needs_calibration",
                _ => "blink_trainer_status_ready",
            });
            switch (StatusState)
            {
                case BlinkTrainerStatusState.NeedsConsent: WireStatusAction("blink_trainer_consent_grant", GrantConsent); break;
                case BlinkTrainerStatusState.NeedsFolders: WireStatusAction("blink_trainer_add_folder", () => BtnBlinkTrainerAddFolderCard_Click(null, new RoutedEventArgs())); break;
                // ponytail: calibration is WebcamCalibrationWindow over the tracker; with no camera
                // there is nothing to calibrate, so the fix-it button is not offered.
                default: WireStatusAction(null, null); break;
            }
            // WPF SetStartButtonState: off only while consent or folders are missing; Stop while running.
            BtnBlinkTrainerStartSession.IsEnabled = StatusState is not (BlinkTrainerStatusState.NeedsConsent or BlinkTrainerStatusState.NeedsFolders);
            if (BtnBlinkTrainerStartSession.Content is TextBlock label)
                BindLoc(label, BlinkTrainerSession.IsRunning ? "blink_trainer_stop_session" : "blink_trainer_start_session");
        }

        private void WireStatusAction(string? key, Action? action)
        {
            _statusAction = action;
            BlinkTrainerStatusAction.IsVisible = key != null;
            if (key != null) BlinkTrainerStatusAction.Content = BindLoc(new TextBlock(), key);
        }

        private static TextBlock BindLoc(TextBlock tb, string key)
        {
            tb.Bind(TextBlock.TextProperty, new Binding($"[{key}]") { Source = LocalizationManager.Instance, Mode = BindingMode.OneWay });
            return tb;
        }

        private async void GrantConsent()
        {
            if (TopLevel.GetTopLevel(this) is not Window owner) return;
            await new Dialogs.WebcamConsentDialog().ShowDialog(owner);
            Refresh();
        }

        /// <summary>WPF RefreshBlinkTrainerWebcamColumn's consent card and calibration line.</summary>
        private void RefreshWebcamColumn()
        {
            bool consented = WebcamConsent.IsCurrent(CoreSettings.Current);
            BlinkTrainerConsentCard.Background = new SolidColorBrush(consented ? Color.FromArgb(0x1A, 0x4A, 0xDE, 0x80) : Color.FromArgb(0x1A, 0xFF, 0xD0, 0x80));
            BlinkTrainerConsentCard.BorderBrush = consented ? Green : Amber;
            BindLoc(BlinkTrainerConsentStatus, consented ? "blink_trainer_consent_granted" : "blink_trainer_consent_required");
            BtnBlinkTrainerManageConsent.Content = BindLoc(new TextBlock { FontSize = 11 }, consented ? "blink_trainer_consent_manage" : "blink_trainer_consent_grant");
            BtnBlinkTrainerRevokeConsent.IsVisible = consented;
            BindLoc(BlinkTrainerCalibrationStatus, "blink_trainer_calibration_none");
        }

        // ---- folder library ----------------------------------------------------------------

        private void RebuildFolderCards()
        {
            BlinkTrainerFolderCardsHost.Children.Clear();
            var s = CoreSettings.Current;
            foreach (var folder in s.BlinkTrainerFolders.ToList())
                BlinkTrainerFolderCardsHost.Children.Add(BuildFolderCard(folder, s.BlinkTrainerIncludeVideos));
        }

        private Border BuildFolderCard(string folder, bool includeVideos)
        {
            var pink = this.FindResource("PinkBrush") as IBrush ?? Brushes.HotPink;
            var rest = new SolidColorBrush(Color.FromRgb(0xFF, 0x69, 0xB4), 0.3);
            var muted = this.FindResource("TextMutedBrush") as IBrush ?? Brushes.Gray;

            var countLine = BlinkTrainerState.FolderCountLine(AssetPack.FromFolder(folder), includeVideos);
            var info = new StackPanel();
            info.Children.Add(new TextBlock
            {
                Text = BlinkTrainerState.FolderDisplayName(folder), Foreground = Brushes.White,
                FontWeight = FontWeight.Medium, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis,
                [ToolTip.TipProperty] = folder,
            });
            info.Children.Add(new TextBlock
            {
                Text = countLine ?? Loc.Get("blink_trainer_folder_empty_or_invalid"),
                Foreground = countLine == null ? (this.FindResource("TextDimBrush") as IBrush ?? muted) : muted,
                FontSize = 11, Margin = new Thickness(0, 2, 0, 0),
            });

            var remove = new Button
            {
                Content = new TextBlock { Text = "×", FontSize = 16 }, Background = Brushes.Transparent,
                BorderThickness = new Thickness(0), Foreground = muted, Padding = new Thickness(6, 0),
                Cursor = new Cursor(StandardCursorType.Hand), VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Top,
                Tag = folder,
            };
            remove.Click += BtnBlinkTrainerRemoveFolderCard_Click;
            Grid.SetColumn(remove, 1);

            var card = new Border
            {
                CornerRadius = new CornerRadius(8), Background = new SolidColorBrush(Color.FromArgb(0x11, 0, 0, 0)),
                BorderBrush = rest, BorderThickness = new Thickness(1), Padding = new Thickness(10, 8),
                Margin = new Thickness(0, 0, 0, 8),
                Child = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Children = { info, remove } },
            };
            card.PointerEntered += (_, _) => card.BorderBrush = pink;
            card.PointerExited += (_, _) => card.BorderBrush = rest;
            return card;
        }

        private async void BtnBlinkTrainerAddFolderCard_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } sp) return;
                var picked = await sp.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Pick a folder of images / GIFs for Blink Trainer" });
                AddFolder(picked.FirstOrDefault()?.TryGetLocalPath());
            }
            catch (Exception ex) { Log.Warning(ex, "BtnBlinkTrainerAddFolderCard_Click failed"); }
        }

        internal void AddFolder(string? folder)
        {
            if (string.IsNullOrWhiteSpace(folder)) return;
            var s = CoreSettings.Current;
            if (s.BlinkTrainerFolders.Any(f => string.Equals(f, folder, StringComparison.OrdinalIgnoreCase))) return;
            s.BlinkTrainerFolders.Add(folder);
            CoreSettings.Save();
            AfterFoldersChanged();
        }

        private void BtnBlinkTrainerRemoveFolderCard_Click(object? sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.Tag is not string folder) return;
            CoreSettings.Current.BlinkTrainerFolders.RemoveAll(f => string.Equals(f, folder, StringComparison.OrdinalIgnoreCase));
            CoreSettings.Save();
            AfterFoldersChanged();
        }

        private void AfterFoldersChanged()
        {
            RebuildFolderCards();
            RefreshStatusRow();
            ApplyStageMode();
        }

        // ---- session settings --------------------------------------------------------------

        private void ToggleBlinkTrainerIncludeVideos_Changed(object? sender, RoutedEventArgs e)
        {
            var s = CoreSettings.Current;
            bool v = ToggleBlinkTrainerIncludeVideos.IsChecked == true;
            if (s.BlinkTrainerIncludeVideos == v) return;
            s.BlinkTrainerIncludeVideos = v;
            CoreSettings.Save();
            RebuildFolderCards();
        }

        // WPF writes the value and does not Save here; the next save (or exit) persists it.
        private void SliderBlinkTrainerDurationNew_Changed(object? sender, RangeBaseValueChangedEventArgs e)
        {
            int v = (int)Math.Round(e.NewValue);
            TxtBlinkTrainerDurationValue.Text = $"{v} min";
            CoreSettings.Current.BlinkTrainerDurationMinutes = v;
        }

        private void SliderBlinkTrainerOpacityNew_Changed(object? sender, RangeBaseValueChangedEventArgs e)
        {
            int v = (int)Math.Round(e.NewValue);
            TxtBlinkTrainerOpacityValue.Text = $"{v}%";
            ApplyOpacityFill(v);
            CoreSettings.Current.BlinkTrainerOpacity = v;
        }

        private void SliderBlinkTrainerOpacityNew_Loaded(object? sender, RoutedEventArgs e)
            => ApplyOpacityFill((int)Math.Round(SliderBlinkTrainerOpacityNew.Value));

        /// <summary>WPF H.7: the opacity slider's fill fades with its value, 1-100 -> 0.109-1.0.</summary>
        private void ApplyOpacityFill(int value)
        {
            var track = global::Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(SliderBlinkTrainerOpacityNew).OfType<Track>().FirstOrDefault();
            if (track?.DecreaseButton is { } fill) fill.Opacity = Math.Clamp(value, 1, 100) / 100.0 * 0.9 + 0.1;
        }

        private void BlinkTrainerSlider_DragStart(object? sender, PointerPressedEventArgs e) => ScaleLabel(sender, 1.15, 100, new BackEaseOut());
        private void BlinkTrainerSlider_DragEnd(object? sender, PointerReleasedEventArgs e) => ScaleLabel(sender, 1.0, 150, new QuadraticEaseOut());
        private void BlinkTrainerSlider_LostCapture(object? sender, PointerCaptureLostEventArgs e) => ScaleLabel(sender, 1.0, 150, new QuadraticEaseOut());

        private void ScaleLabel(object? slider, double to, int ms, Easing easing)
        {
            var label = slider == SliderBlinkTrainerDurationNew ? TxtBlinkTrainerDurationValue
                : slider == SliderBlinkTrainerOpacityNew ? TxtBlinkTrainerOpacityValue : null;
            if (label?.RenderTransform is not ScaleTransform st) return;
            var d = TimeSpan.FromMilliseconds(ms);
            st.Transitions = new Transitions
            {
                new DoubleTransition { Property = ScaleTransform.ScaleXProperty, Duration = d, Easing = easing },
                new DoubleTransition { Property = ScaleTransform.ScaleYProperty, Duration = d, Easing = easing },
            };
            st.ScaleX = st.ScaleY = to;
        }

        private void BlinkTrainerMixOptionSame_Click(object? sender, PointerReleasedEventArgs e) => SetMixMode(false);
        private void BlinkTrainerMixOptionMix_Click(object? sender, PointerReleasedEventArgs e) => SetMixMode(true);

        private void SetMixMode(bool isMix)
        {
            if (!BlinkTrainerGatedContent.IsEnabled) return; // WPF: a disabled panel gets no mouse input
            var s = CoreSettings.Current;
            if (s.BlinkTrainerMixImages != isMix) { s.BlinkTrainerMixImages = isMix; CoreSettings.Save(); }
            SetMixModeSelection(isMix);
        }

        /// <summary>Selected option: full pink border + a pink 16px glow at 0.6; the other clear.</summary>
        private void SetMixModeSelection(bool isMix)
        {
            var pink = this.FindResource("PinkBrush") as IBrush ?? Brushes.HotPink;
            foreach (var (b, on) in new[] { (BlinkTrainerMixOptionSame, !isMix), (BlinkTrainerMixOptionMix, isMix) })
            {
                b.BorderBrush = on ? pink : Brushes.Transparent;
                b.BoxShadow = on ? BoxShadows.Parse("0 0 16 0 #99FF69B4") : default;
            }
        }

        private void BtnBlinkTrainerGateUnlock_Click(object? sender, RoutedEventArgs e)
            => (TopLevel.GetTopLevel(this) as Windows.MainShellWindow)?.OpenAppSettingsSection("account");

        private void BtnOpenDeviceSettings_Click(object? sender, RoutedEventArgs e)
            => (TopLevel.GetTopLevel(this) as Windows.MainShellWindow)?.OpenAppSettingsSection("devices");

        /// <summary>WPF: the same consent dialog both ways (grant, or review when granted).</summary>
        private void BtnBlinkTrainerManageConsent_Click(object? sender, RoutedEventArgs e) => GrantConsent();

        /// <summary>WPF BtnBlinkTrainerRevokeConsent_Click: confirm (Cancel default), then
        /// WebcamTrackingService.RevokeConsent in full (Platform.WebcamTracker.RevokeConsent: stop,
        /// delete the calibration file, clear consent, turn the webcam features off).</summary>
        private async void BtnBlinkTrainerRevokeConsent_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                if (TopLevel.GetTopLevel(this) is not Window owner) return;
                if (!await Dialogs.MessageDialog.ConfirmAsync(owner, Loc.Get("blink_trainer_consent_revoke_confirm_title"),
                        Loc.Get("blink_trainer_consent_revoke_confirm_body"), defaultToCancel: true)) return;
                RevokeConsent();
            }
            catch (Exception ex) { Log.Warning(ex, "BtnBlinkTrainerRevokeConsent_Click failed"); }
        }

        internal void RevokeConsent()
        {
            Platform.WebcamTracker.RevokeConsent();   // what CoreWebcam.RevokeConsent is seeded with
            Refresh();
        }

        // Disabled in the constructor: WebcamCalibrationWindow needs the gaze feed (see
        // MainShellWindow.BlinkTrainer.cs).
        private void BtnBlinkTrainerCalibrate_Click(object? sender, RoutedEventArgs e) { }
        private void BtnBlinkTrainerQuickRecal_Click(object? sender, RoutedEventArgs e) { }

        /// <summary>WPF BtnBlinkTrainerStartSession_Click: stop if running; else bring the tracker up
        /// off the UI thread (only with current consent), then start. A refusal lands in the status
        /// row as Error, as on WPF. The start goes through StartEffect so the Wayland panic shortcut is
        /// bound before the overlay appears.</summary>
        private async void BtnBlinkTrainerStartSession_Click(object? sender, RoutedEventArgs e)
        {
            BtnBlinkTrainerStartSession.IsEnabled = false;   // #743: work is in flight
            try
            {
                if (BlinkTrainerSession.IsRunning) { BlinkTrainerSession.Stop(); return; }
                // Read BEFORE any await: a panic or Stop during the tracker start or the portal bind
                // (up to 30 s) cancels this start.
                var gen = BlinkTrainerSession.Generation;
                var tracker = Platform.WebcamTracker.Instance;
                if (!tracker.IsRunning && WebcamConsent.IsCurrent(CoreSettings.Current)) await tracker.StartAsync();
                if (gen != BlinkTrainerSession.Generation) return;
                Windows.MainShellWindow.StartEffect(() => { if (gen == BlinkTrainerSession.Generation) BlinkTrainerSession.Start(this); });
            }
            catch (Exception ex) { Log.Warning(ex, "Blink Trainer Start handler failed"); }
            finally { OnSessionStateChanged(); }
        }

        /// <summary>WPF ToggleWebcamTrackingAsync (MainWindow.BlinkTrainer.cs:310): stop if running;
        /// else consent dialog when stale, then start off the UI thread. A failed start says why
        /// (OpenCV missing, no camera) instead of silently staying off.</summary>
        private async void BtnBlinkTrainerStartStopTracker_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                var tracker = Platform.WebcamTracker.Instance;
                if (tracker.IsRunning) { await tracker.StopAsync(); RefreshTrackerButton(); return; }
                if (TopLevel.GetTopLevel(this) is not Window owner) return;
                if (!WebcamConsent.IsCurrent(CoreSettings.Current))
                {
                    var dlg = new Dialogs.WebcamConsentDialog();
                    await dlg.ShowDialog(owner);
                    Refresh();
                    if (!WebcamConsent.IsCurrent(CoreSettings.Current)) return;
                }
                BtnBlinkTrainerStartStopTracker.IsEnabled = false;
                bool started = await tracker.StartAsync();
                BtnBlinkTrainerStartStopTracker.IsEnabled = true;
                RefreshTrackerButton();
                if (!started && tracker.LastError != null)
                    await Dialogs.MessageDialog.ShowAsync(owner, "Webcam tracking", tracker.LastError);
            }
            catch (Exception ex) { Log.Warning(ex, "Blink Trainer tracker toggle failed"); }
        }

        /// <summary>WPF RefreshBlinkTrainerTrackerButton: the literal label WPF authors untranslated.</summary>
        private void RefreshTrackerButton()
        {
            if (BtnBlinkTrainerStartStopTracker.Content is TextBlock tb)
                tb.Text = Platform.WebcamTracker.Instance.IsRunning ? "Stop tracker" : "Start tracker";
        }
    }
}
