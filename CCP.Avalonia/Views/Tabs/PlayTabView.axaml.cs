using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ConditioningControlPanel.Avalonia.Controls.NavRail;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Models;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Tabs
{
    /// <summary>
    /// PORTED from WPF 7.1.5 Views/Tabs/PlayTabView.xaml.cs (the host, ScrollToZone) and
    /// PlayTabView.Cards.cs (the shims), plus the card half of MainWindow.PlayTab.cs
    /// (RefreshPlayCards, LaunchPlay*).
    ///
    /// <para>The Play door (tab key <c>play</c>): GAMES first (Breakout Demo, Breakout, Goon,
    /// Piece by Piece, Back Room, Down the Rabbit Hole, Arcademy, Racing Thoughts, Web App), then
    /// Together, Eyes, Sessions, More. 7.1.5 removed the DtRH hero (Fall In / Quick Drop and the two
    /// Chaos boxes), the Goon perk lines and the Just Drop card (Studio > Creator Tools); none of
    /// them are here.</para>
    ///
    /// <para><b>Launch parity is the contract.</b> Every game button runs the launcher's own call
    /// (<see cref="LauncherWindow.LaunchGame(MainShellWindow, string)"/>): the same sign-in ask, the
    /// same leash gate, the same game host. The lockbands are decoration; the gate refuses.</para>
    ///
    /// <para><b>No ambient loop</b> since the Rabbit Hole hero left the wall (WPF 2026-09-18).</para>
    /// </summary>
    public partial class PlayTabView : UserControl
    {
        private MainShellWindow? Owner => TopLevel.GetTopLevel(this) as MainShellWindow;

        public PlayTabView()
        {
            // InitializeComponent, not AvaloniaXamlLoader.Load: only the generated one assigns the
            // x:Name fields, and Load leaves every one of them permanently null.
            InitializeComponent();
            // The two Breakout doors draw vector covers (WPF BreakoutCardArt, no media load).
            PlayBreakoutDemoArt.Source = BreakoutCardArt.Demo;
            PlayBreakoutArt.Source = BreakoutCardArt.Full;
            RefreshHeroArt();
        }

        // The mod-switch repaint: once per attach, off on every detach.
        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            CoreMods.ModChanged -= OnModChangedRepaintArt;
            CoreMods.ModChanged += OnModChangedRepaintArt;
            App.IntakePass.PassStateChanged += OnIntakePassStateChanged;
            LocalizationManager.Instance.LanguageChanged += OnIntakePassStateChanged;
            Platform.WebcamTracker.Instance.StateChanged -= OnTrackerStateChanged;
            Platform.WebcamTracker.Instance.StateChanged += OnTrackerStateChanged;
            LocalizationManager.Instance.LanguageChanged += OnTrackerLanguageChanged;
            RefreshPlayCards();
            RefreshTrackerUi();
        }

        private void OnTrackerStateChanged() => Dispatcher.UIThread.Post(() => RefreshTrackerUi());
        private void OnTrackerLanguageChanged(object? sender, EventArgs e) => Dispatcher.UIThread.Post(() => RefreshTrackerUi());

        /// <summary>WPF MainWindow.LabTab.cs UpdateLabTrackerUi + UpdateWebcamStatusChips, the Play door's
        /// share: the chip's dot and line, the two Eyes cards' dimming and their "start tracking" pills
        /// follow the tracker. Display only. <paramref name="live"/> is for tests.</summary>
        internal void RefreshTrackerUi(bool? live = null)
        {
            try
            {
                var on = live ?? Platform.WebcamTracker.Instance.IsRunning;
                var brush = this.FindResource(on ? "SuccessGreenBrush" : "TextMutedBrush") as IBrush;
                if (brush != null) WebcamStatusChipPlayDot.Fill = brush;
                TxtWebcamStatusChipPlay.Text = Loc.Get(on ? "rf_webcam_tracking" : "rf_webcam_stopped");
                PlayGazeCard.Opacity = on ? 1.0 : 0.62;
                PlayFocusCard.Opacity = on ? 1.0 : 0.62;
                PlayGazeNeedsTracker.IsVisible = !on;
                PlayFocusNeedsTracker.IsVisible = !on;
            }
            catch (Exception ex) { Log.Debug("RefreshTrackerUi: {E}", ex.Message); }
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            CoreMods.ModChanged -= OnModChangedRepaintArt;
            App.IntakePass.PassStateChanged -= OnIntakePassStateChanged;
            LocalizationManager.Instance.LanguageChanged -= OnIntakePassStateChanged;
            Platform.WebcamTracker.Instance.StateChanged -= OnTrackerStateChanged;
            LocalizationManager.Instance.LanguageChanged -= OnTrackerLanguageChanged;
            base.OnDetachedFromVisualTree(e);
        }

        // The shell shows a tab by flipping IsVisible (P01), which is WPF's ShowTab "play" arrival.
        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);
            if (change.Property == IsVisibleProperty && IsVisible && VisualRoot != null) RefreshPlayCards();
        }

        // The pass is spent (or entitlement lands) off the UI thread.
        private void OnIntakePassStateChanged(object? sender, EventArgs e) =>
            Dispatcher.UIThread.Post(RefreshPlayCards);

        /// <summary>Every art plate on the wall: WPF's ImageSource for ImageSource.</summary>
        internal static readonly (string Plate, string Art, Stretch Fit)[] HeroPlates =
        {
            ("PlayGoonHeroPlate",     "features/goon_game_tile.png",    Stretch.UniformToFill),
            ("PlayPbpHeroPlate",      "features/piecebypiece.png",      Stretch.UniformToFill),
            ("PlayBackRoomHeroPlate", "features/backroom.png",          Stretch.UniformToFill),
            ("PlayDtrhHeroPlate",     "features/dtrh.png",              Stretch.UniformToFill),
            ("PlayArcademyHeroPlate", "features/arcademy.png",          Stretch.UniformToFill),
            ("PlayRaceHeroPlate",     "features/race.png",              Stretch.UniformToFill),
            ("PlayWebAppHeroPlate",   "billboard/webapp.png",           Stretch.UniformToFill),
            ("PlayRemoteHeroPlate",   "features/remote_control.png",    Stretch.UniformToFill),
            ("PlayGazeHeroPlate",     "features/lab_gaze_hero.png",     Stretch.UniformToFill),
            ("PlayFocusHeroPlate",    "features/lab_focusgaze_hero.png",Stretch.UniformToFill),
            ("PlayBlinkHeroPlate",    "features/blink_trainer.png",     Stretch.UniformToFill),
            ("PlayIntakeHeroPlate",   "features/lab_quiz_hero.png",     Stretch.UniformToFill),
            ("PlayFypHeroPlate",      "features/fyp.png",               Stretch.UniformToFill),
            ("PlayLockdownHeroPlate", "lockdown_icon.png",              Stretch.UniformToFill),
            ("PlayLoomHeroPlate",     "features/loom.png",              Stretch.UniformToFill),
        };

        private void OnModChangedRepaintArt(object? sender, ModPackage? mod) =>
            Dispatcher.UIThread.Post(RefreshHeroArt);

        private void RefreshHeroArt()
        {
            foreach (var (plateName, art, fit) in HeroPlates)
            {
                try
                {
                    if (this.FindControl<Border>(plateName) is not { } plate) continue;
                    if (ModArt.TryLoad(art) is not { } bmp) continue;
                    plate.Background = new ImageBrush(bmp) { Stretch = fit };
                }
                catch (Exception ex)
                {
                    Log.Debug(ex, "[Play] hero art {Art} would not paint", art);
                }
            }
        }

        /// <summary>WPF MainWindow.PlayTab.cs RefreshPlayCards: the tier bands (the same TierGate
        /// verdicts the launch handlers consult), the FREE TODAY stamps and the Graded Intake's pass
        /// states. Decoration only. Never throws.</summary>
        internal void RefreshPlayCards()
        {
            try
            {
                PlayLockBreakout.IsVisible = !TierGate.RequiresLab(Loc.Get("launcher_game_breakout_title")).Allowed;
                PlayLockDtrh.IsVisible = !TierGate.RequiresLab(Loc.Get("launcher_game_dtrh_title"), "dtrh").Allowed;
                PlayLockArcademy.IsVisible = !TierGate.RequiresLab(Loc.Get("launcher_game_arcademy_title")).Allowed;
                PlayLockGaze.IsVisible = !TierGate.RequiresLab(Loc.Get("label_gaze_minigame")).Allowed;
                PlayLockFocusGaze.IsVisible = !TierGate.RequiresLab(Loc.Get("label_focus_gaze")).Allowed;
                PlayLockRemote.IsVisible = !TierGate.RequiresPremium(Loc.Get("tab_remote_control"), "remote").Allowed;
                PlayLockLockdown.IsVisible = !TierGate.RequiresPremium(Loc.Get("tab_lockdown_mode")).Allowed;
                PlayLockBlink.IsVisible = !TierGate.RequiresPremium(Loc.Get("tab_blink_trainer")).Allowed;
                PlayLockFyp.IsVisible = !TierGate.RequiresPremium(Loc.Get("tab_fyp"), "fyp").Allowed;

                // FREE TODAY (WPF :145): only on a door that is otherwise shut - an owner gets no gift.
                bool premium = CoreEntitlement.HasPremium;
                PlayBadgeRemote.FreeToday = !premium && CoreEntitlement.IsFreeToday("remote");
                PlayBadgeFyp.FreeToday = !premium && CoreEntitlement.IsFreeToday("fyp");
                // The descent is a Prime feature, so it asks its own question (WPF :147).
                PlayBadgeDtrh.FreeToday = !CoreEntitlement.HasLab && CoreEntitlement.IsFreeToday("dtrh");

                RefreshPlayIntakeCard();
            }
            catch (Exception ex) { Log.Debug("RefreshPlayCards: {E}", ex.Message); }
        }

        /// <summary>WPF MainWindow.PlayTab.cs:183 RefreshPlayIntakeCard, read from the same
        /// IntakePassService the page's gate reads. Premium: nothing; Available: no band, the
        /// announcement and the "where" button; Spent / NeedsLogin: band + the page's own copy.</summary>
        private void RefreshPlayIntakeCard()
        {
            var state = App.IntakePass.State;
            PlayLockIntake.IsVisible = state is not (IntakePassState.Premium or IntakePassState.Available);
            BtnPlayIntakePassHome.IsVisible = state == IntakePassState.Available;
            var days = IntakePassService.DaysUntilNextPass;
            TxtPlayIntakeState.Text = state switch
            {
                IntakePassState.Available => Loc.Get("pl6_intake_state_available"),
                // Two keys rather than one with a {0}: "unlocks in 1 days" gets screenshotted.
                IntakePassState.Spent => days == 1 ? Loc.Get("intake_gate_spent_body_one_day") : Loc.GetF("intake_gate_spent_body", days),
                IntakePassState.NeedsLogin => Loc.Get("intake_gate_login_body"),
                _ => string.Empty,
            };
            TxtPlayIntakeState.IsVisible = state != IntakePassState.Premium;
        }

        // ---- zones (WPF PlayTabView.xaml.cs:43-90) ------------------------------------------

        /// <summary>Zone keys the Play section strip reaches (nav rework contract 2).</summary>
        public static readonly string[] ZoneKeys = { "games", "sessions", "eyes" };

        /// <summary>Gap kept above a zone header after a zone scroll (header fully visible).</summary>
        internal const double ZoneTopGap = 16;

        /// <summary>The header element a zone key names, or null.</summary>
        internal Control? ZoneHeader(string? zone) => (zone ?? "").Trim().ToLowerInvariant() switch
        {
            "games" => ZoneGames,
            "sessions" => ZoneSessions,
            "eyes" => ZoneEyes,
            _ => null,
        };

        /// <summary>The scroll offset a zone lands on: 0 for Games (the intro line shows too), else
        /// the header's top less <see cref="ZoneTopGap"/>. Null when the header is not laid out.</summary>
        internal double? ZoneOffset(string zone)
        {
            var header = ZoneHeader(zone);
            if (header == null) return null;
            if (string.Equals(zone, "games", StringComparison.OrdinalIgnoreCase)) return 0;
            if (WallScroll.Content is not Visual content) return null;
            var p = header.TranslatePoint(new Point(0, 0), content);
            return p is { } at ? Math.Max(0, at.Y - ZoneTopGap) : null;
        }

        /// <summary>
        /// Brings a zone header to the top of the wall: "games" | "sessions" | "eyes". An unknown key
        /// does nothing. The header glows once (NavGlow skips it under reduced or no motion).
        /// </summary>
        public void ScrollToZone(string zone)
        {
            var header = ZoneHeader(zone);
            if (header == null) { Log.Debug("Play ScrollToZone({Zone}): unknown zone", zone); return; }
            Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    if (ZoneOffset(zone) is { } y) WallScroll.Offset = new Vector(WallScroll.Offset.X, y);
                    else header.BringIntoView();
                    Dispatcher.UIThread.Post(() =>
                        NavGlow.Once(header, global::ConditioningControlPanel.Nav.NavStripRules.Accent(global::ConditioningControlPanel.Nav.NavSections.Play), why: "play." + zone),
                        DispatcherPriority.Background);
                }
                catch (Exception ex) { Log.Debug("Play ScrollToZone({Zone}): {E}", zone, ex.Message); }
            }, DispatcherPriority.Normal);
        }

        // ==================================================================================
        // Shims (WPF PlayTabView.Cards.cs). Nothing here re-implements a launch or decides a tier.
        // ==================================================================================

        // ---- GAMES -------------------------------------------------------------------------

        private void BtnPlayBreakoutDemo_Click(object? sender, RoutedEventArgs e) => LaunchGame("breakoutdemo");
        private void BtnPlayBreakout_Click(object? sender, RoutedEventArgs e) => LaunchGame("breakout");
        private void BtnPlayGoon_Click(object? sender, RoutedEventArgs e) => LaunchGame("goon");
        private void BtnPlayChess_Click(object? sender, RoutedEventArgs e) => LaunchGame("piecebypiece");
        private void BtnPlayBackRoom_Click(object? sender, RoutedEventArgs e) => LaunchGame("backroom");
        private void BtnPlayDtrh_Click(object? sender, RoutedEventArgs e) => LaunchGame("dtrh");
        private void BtnPlayArcademy_Click(object? sender, RoutedEventArgs e) => LaunchGame("arcademy");
        private void BtnPlayRacingThoughts_Click(object? sender, RoutedEventArgs e) => LaunchRace();
        private void BtnPlayWebApp_Click(object? sender, RoutedEventArgs e) => Owner?.OpenPlayWebApp();

        /// <summary>WPF LaunchPlay* / LaunchExclusiveGame: the launcher's own entry answers, so its
        /// sign-in ask, leash gate, tier refusal and host are the ones the card gets.</summary>
        private void LaunchGame(string id)
        {
            if (Owner is not { } shell) { Log.Information("[Play] {Id}: no shell", id); return; }
            try
            {
                if (!shell.LaunchCardGame(id))
                    Log.Information("[Play] {Id}: no game host on this head", id);
            }
            catch (Exception ex) { Log.Warning(ex, "[Play] launch {Id} failed", id); }
        }

        /// <summary>WPF LaunchPlayLauncherGame("race") -> LaunchExclusiveGame: signed out, the sign-in
        /// dialog; else the race host itself (CaucusHostService.Launch), which refuses without a track.
        /// The launcher's mystery card sends its Play to the Back Room counter; this card never does.</summary>
        internal void LaunchRace()
        {
            if (Owner is not { } shell) { Log.Information("[Play] race: no shell"); return; }
            try
            {
                if (RaceDoorOpen()) { shell.LaunchCardGame("race"); return; }
                if (!CoreAccount.IsLoggedIn) { _ = shell.OpenUnifiedLoginDialog(); return; }
                Games.RaceWindow.Launch();   // refuses and logs: no racing purchase
            }
            catch (Exception ex) { Log.Warning(ex, "[Play] launch race failed"); }
        }

        /// <summary>True when the launcher's own entry would open the race, not the counter.</summary>
        internal static bool RaceDoorOpen(bool? signedIn = null, Func<string, bool>? owns = null) =>
            (signedIn ?? CoreAccount.IsLoggedIn) && Games.RaceWindow.CanLaunch(owns);

        // ---- TOGETHER ----------------------------------------------------------------------

        private void BtnPlayRemoteControl_Click(object? sender, RoutedEventArgs e) => Owner?.ShowTab("remotecontrol");

        // ---- EYES --------------------------------------------------------------------------

        /// <summary>WPF <c>mw.OpenDeviceSettings()</c>: Settings door, Devices section.</summary>
        private void BtnOpenDeviceSettings_Click(object? sender, RoutedEventArgs e) => Owner?.OpenDeviceSettings();

        /// <summary>
        /// RESTORED, on the condition the previous note set: "the day an entitlement seam exists".
        /// It does — <c>TierGate</c> is CCP.Core/Services/TierGate.cs over <c>CoreEntitlement</c> —
        /// so this is MainWindow.LabTab.cs:770 verbatim: Tier 2 checked BEFORE the window is
        /// constructed, because the Lab smokescreen is a tab-wide overlay and not a gate on this
        /// door. The window runs on the webcam tracker (consent, start, calibration gates as WPF).
        ///
        /// <para>The seam is seeded by Platform.AccountSeed, so a Tier 2 account opens the window.</para>
        /// </summary>
        private void BtnGazeMinigame_Click(object? sender, RoutedEventArgs e) => OpenGazeMinigame();

        internal void OpenGazeMinigame()
        {
            if (!TierGate.DemandLab(Loc.Get("label_gaze_minigame"))) return;
            // A non-modal Show still needs a visible owner; the shell can be minimised to tray.
            if (Owner is not { IsVisible: true } owner) return;
            new Lab.GazeMinigame.GazeMinigameWindow().Show(owner);
        }

        private bool _focusGazeSyncing;

        /// <summary>WPF MainWindow.LabTab.cs:929 ChkFocusGaze_Changed, as far as this head goes. The
        /// Prime gate answers first on the ON branch (revert, then tell), exactly as WPF. The engine
        /// behind the switch (GazeFocusService: dwell on flashes, bubbles and floating video) is not on
        /// this head, so an allowed ON reverts too and the status line says so. OFF is never gated.</summary>
        private void ChkFocusGaze_Changed(object? sender, RoutedEventArgs e) => FocusGazeChanged();

        /// <returns>What the ON press met: "off", "tier" or "build".</returns>
        internal string FocusGazeChanged()
        {
            if (_focusGazeSyncing) return "sync";
            if (ChkPlayFocusGaze.IsChecked != true)
            {
                SyncFocusGazeToggle(false);
                TxtPlayFocusGazeStatus.Text = "";
                return "off";
            }
            var verdict = TierGate.RequiresLab(Loc.Get("label_focus_gaze"));
            SyncFocusGazeToggle(false);
            if (!verdict.Allowed)
            {
                TierGate.ShowDenied(verdict);
                return "tier";
            }
            TxtPlayFocusGazeStatus.Text = Loc.Get("exclusives_not_on_this_build");
            return "build";
        }

        /// <summary>WPF SyncFocusGazeToggle: the setting is the intent, the box follows it silently.</summary>
        private void SyncFocusGazeToggle(bool enabled)
        {
            CoreSettings.Current.FocusGazeEnabled = enabled;
            if (ChkPlayFocusGaze.IsChecked == enabled) return;
            _focusGazeSyncing = true;
            try { ChkPlayFocusGaze.IsChecked = enabled; }
            finally { _focusGazeSyncing = false; }
        }

        private void BtnLabBlinkTrainerOpenNew_Click(object? sender, RoutedEventArgs e) => Owner?.ShowTab("blinktrainer");

        // ---- SESSIONS ----------------------------------------------------------------------

        // All four pass states navigate: the page's own gate explains a spent week or a missing login.
        private void BtnPlayGradedIntake_Click(object? sender, RoutedEventArgs e) => Owner?.ShowTab("gradedintake");

        private void BtnPlayIntakePassHome_Click(object? sender, RoutedEventArgs e) => Owner?.ShowTab("settings");

        private void BtnPlayFyp_Click(object? sender, RoutedEventArgs e) => Owner?.ShowTab("fyp");

        private void BtnPlayLockdown_Click(object? sender, RoutedEventArgs e) => Owner?.ShowTab("lockdown");

        // ---- MORE --------------------------------------------------------------------------

        /// <summary>Loom NAVIGATES to the one editor: WPF OpenStudioModule("spiral").</summary>
        private void BtnPlayLoom_Click(object? sender, RoutedEventArgs e) => Owner?.OpenStudioModule("spiral");
    }
}
