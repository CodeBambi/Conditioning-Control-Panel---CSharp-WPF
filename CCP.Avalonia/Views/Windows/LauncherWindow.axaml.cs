// PORTED (slice 1) from WPF LauncherWindow.xaml.cs/.Tiles.cs and LauncherHost.cs; rules are Core's
// LauncherCards/LauncherRules; slice 2 adds the boot surface and the second-instance handoff (WPF App.xaml.cs
// RouteBootSurface/RouteSurfaceHandoff, LauncherHost.OnBareRelaunch). Slice 3 adds the account chip, mod pill and panel-card status/stats (WPF LauncherWindow.xaml.cs:220-606).
// Slice 5 (FX) lives in LauncherWindow.Fx.cs. Parity wave 1: the shelf shows every card WPF 7.1.5 shows (order, pills,
// NEW, the Racing mystery card, the Breakout covers, whole-card press, grow-to-fit). Every card has a
// destination (Racing Thoughts = Views/Games/RaceWindow, play#42); a card with none would flinch and say so.
using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Launcher;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class LauncherWindow : Window
    {
        private const double TileRadius = 16;

        /// <summary>Cards whose destination exists on this head: padlock probe + how to open it.</summary>
        internal static readonly IReadOnlyDictionary<string, (Func<bool> Locked, Action<MainShellWindow> Open)> Destinations =
            new Dictionary<string, (Func<bool>, Action<MainShellWindow>)>(StringComparer.OrdinalIgnoreCase)
            {
                // Graded Intake (WPF IntakePassService.CanStartIntake); locked still opens the tab's gate.
                ["intake"] = (() => !(CoreEntitlement.HasLab || CoreEntitlement.IsIntakePassAvailable),
                              panel => panel.ShowTab("gradedintake")),
                // The web games (Views/Games/GameWindow, WPF's per-game WebView2 hosts). The lock face is
                // the same probe FaceLocks used; the window's own gate raises the refusal.
                ["backroom"] = (() => false, _ => Games.GameWindow.Launch("backroom")),
                ["breakoutdemo"] = (() => false, _ => Games.GameWindow.Launch("breakoutdemo")),
                ["breakout"] = (() => FaceLocked("breakout"), _ => Games.GameWindow.Launch("breakout")),
                ["piecebypiece"] = (() => false, _ => Games.GameWindow.Launch("piecebypiece")),
                ["goon"] = (() => false, _ => Games.GameWindow.Launch("goon")),
                ["dtrh"] = (() => FaceLocked("dtrh"), _ => Games.GameWindow.Launch("dtrh")),
                ["arcademy"] = (() => FaceLocked("arcademy"), _ => Games.GameWindow.Launch("arcademy")),
                // Racing Thoughts (WPF LauncherCatalogue.cs:243 -> CaucusHostService.Launch), its own
                // window (Views/Games/RaceWindow.cs); revealed only once a track is owned.
                ["race"] = (() => false, _ => Games.RaceWindow.Launch()),
            };

        private static bool FaceLocked(string id) => FaceLocks.TryGetValue(id, out var probe) && probe();

        /// <summary>Cards this head can open (a destination exists): boot/handoff routing and the companion's offers.</summary>
        internal static IEnumerable<LauncherCard> VisibleCards => LauncherCards.All.Where(c => Destinations.ContainsKey(c.Id));

        /// <summary>Every card WPF 7.1.5 shows, in launcher order (WPF LauncherCatalogue.Games.Where(Available):
        /// PieceByPieceAvailable and ArcademyHostService.DoorAvailable are both on), host or not.</summary>
        internal static IEnumerable<LauncherCard> ShelfCards => LauncherCards.All;

        /// <summary>WPF LauncherCatalogue's padlock probes for the cards with no destination here, so the face
        /// matches WPF's (Prime pill, gold rim). A probe that throws reads as locked, as WPF LabOk.</summary>
        private static readonly IReadOnlyDictionary<string, Func<bool>> FaceLocks =
            new Dictionary<string, Func<bool>>(StringComparer.OrdinalIgnoreCase)
            {
                // WPF BreakoutAccess.FullAllowed.
                ["breakout"] = () => !TierGate.RequiresLab(Loc.Get("launcher_game_breakout_title")).Allowed,
                ["dtrh"] = () => !TierGate.RequiresLab(Loc.Get("launcher_game_dtrh_title"), "dtrh").Allowed,
                ["arcademy"] = () => !TierGate.RequiresLab(Loc.Get("launcher_game_arcademy_title")).Allowed,
            };

        /// <summary>WPF LauncherEntry.Revealed: Racing Thoughts is the mystery card until a track is owned
        /// (RacingAccess.CanLaunch with the purchase door armed: any rt.original.00..10 grant).</summary>
        internal static bool Revealed(LauncherCard card)
        {
            if (!string.Equals(card.Id, "race", StringComparison.OrdinalIgnoreCase)) return true;
            return Games.RaceWindow.CanLaunch();
        }

        /// <summary>The padlock as WPF draws it: the destination's probe, else the face probe, else free.</summary>
        internal static bool LockedFor(LauncherCard card)
        {
            if (Destinations.TryGetValue(card.Id, out var dest)) return Locked(dest);
            if (!FaceLocks.TryGetValue(card.Id, out var probe)) return false;
            try { return probe(); }
            catch (Exception ex) { Log.Debug(ex, "[Launcher] lock probe threw for {Id}", card.Id); return true; }
        }

        private static LauncherWindow? _window;

        /// <summary>The panel that sent us here, else the app's main window.</summary>
        private MainShellWindow? _panel;
        private MainShellWindow? Panel => _panel ?? MainShellWindow.Current;

        /// <summary>The launcher, if it has been built this run.</summary>
        internal static LauncherWindow? Instance => _window;

        /// <summary>WPF _statusTimer: the status line and the stats, every second while shown.</summary>
        private readonly DispatcherTimer _statusTimer = new() { Interval = TimeSpan.FromSeconds(1) };

        /// <summary>Each tile's reveal as drawn (WPF _revealedAtBuild).</summary>
        private readonly Dictionary<string, bool> _revealedAtBuild = new(StringComparer.OrdinalIgnoreCase);

        public LauncherWindow()
        {
            InitializeComponent();
            GamesColumn.SizeChanged += (_, _) => SeatGrid();
            XpTrack.SizeChanged += (_, _) => RefreshStats();
            _statusTimer.Tick += (_, _) => { RefreshStatus(); RefreshStats(); RefreshSpReadout(); };
            CoreMods.ModChanged += OnModChanged;
            Closed += (_, _) =>
            {
                if (ReferenceEquals(_window, this)) _window = null;
                _statusTimer.Stop();
                CoreMods.ModChanged -= OnModChanged;
            };
            // WPF OnIsVisibleChanged -> OnShown / OnHidden.
            PropertyChanged += (_, e) =>
            {
                if (e.Property != IsVisibleProperty) return;
                if (IsVisible) OnShown();
                else _statusTimer.Stop();
            };
            BuildTiles();
            HookVeil();
            HookOpenTables();
            HookFx();
            PaintSoundButton();
            HookLinks();
            HookFeel();
        }

        /// <summary>WPF OnShown; its FX half is FxOnShown (LauncherWindow.Fx.cs).</summary>
        private void OnShown()
        {
            try
            {
                BuildTiles();
                RefreshAccount();
                RefreshMod();
                RefreshStatus();
                RefreshStats();
                _statusTimer.Start();
            }
            catch (Exception ex) { Log.Warning(ex, "[Launcher] OnShown failed"); }
        }

        /// <summary>WPF LauncherHost.Show: bring the launcher up, creating it on first use.</summary>
        internal static LauncherWindow Open()
        {
            var w = _window ??= new LauncherWindow();
            w.BuildTiles();
            w.SkipToPanel.IsChecked = CoreSettings.Current.LauncherSkipToPanel;
            if (!w.IsVisible) w.Show();
            if (w.WindowState == WindowState.Minimized) w.WindowState = WindowState.Normal;
            w.Activate();
            return w;
        }

        /// <summary>WPF LauncherHost.BackToLauncher: the panel's door; refused under Lockdown.</summary>
        internal static bool BackToLauncher(MainShellWindow panel)
        {
            if (MainShellWindow.LockdownActive)
            {
                try { Services.LockdownService.Current?.NotifyEscapeAttempt(Services.Possession.EscapeKinds.Close); } catch { }
                return false;
            }
            panel.Hide();
            panel.HideAvatarTube();
            OpenFor(panel);
            return true;
        }

        /// <summary>WPF LauncherHost.Show, tied to <paramref name="panel"/>.</summary>
        internal static LauncherWindow OpenFor(MainShellWindow panel) => Open().Link(panel);

        /// <summary>The launcher goes with its panel: hidden, it would keep a trayless process alive UI-less.</summary>
        private LauncherWindow Link(MainShellWindow panel)
        {
            if (!ReferenceEquals(_panel, panel)) { _panel = panel; panel.Closed += (_, _) => Close(); }
            return this;
        }

        /// <summary>WPF App.Boot: the surface this run opened with (decided in App, Core LauncherBoot.Decide).</summary>
        internal static BootDecision Boot { get; set; } = BootDecision.PanelFirst;

        /// <summary>WPF LauncherHost.SurfaceInPlay: the launcher is part of this run.</summary>
        internal static bool SurfaceInPlay => _window != null || Boot.Surface != BootSurface.Panel;

        /// <summary>WPF App.RouteBootSurface: tuck the panel away (it was shown only so its Opened work
        /// runs, as WPF's ShowHiddenForBoot) and bring the decided surface up. Any failure shows the panel.</summary>
        internal static void RouteBoot(MainShellWindow panel)
        {
            try
            {
                panel.Hide();
                panel.HideAvatarTube();
                if (!(Boot.Surface == BootSurface.Game && Boot.GameId is { } id && LaunchGame(panel, id)))
                    OpenFor(panel);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[Launcher] boot routing failed; leaving the panel up");
                try { panel.ShowFromTray(); } catch (Exception ex2) { Log.Debug(ex2, "[Launcher] ShowFromTray after failed boot routing"); }
            }
        }

        /// <summary>WPF LauncherHost.LaunchGame for the cards this head can open; false = no such card here.</summary>
        internal static bool LaunchGame(MainShellWindow panel, string id)
        {
            var card = VisibleCards.FirstOrDefault(c => string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase));
            if (card == null) return false;
            // The sign-in ask is owned by the launcher, so only then does it come on screen.
            var w = card.RequiresAccount && !CoreAccount.IsLoggedIn ? OpenFor(panel) : (_window ??= new LauncherWindow()).Link(panel);
            w.Play(card);
            return true;
        }

        /// <summary>WPF LauncherHost.OpenPanel without a launcher instance in hand.</summary>
        internal static void OpenPanel(MainShellWindow panel)
        {
            if (_window != null) _window.OpenPanel();
            else panel.ShowFromTray();
        }

        /// <summary>A second launch (Platform/SingleInstance): WPF RouteSurfaceHandoff for a
        /// <see cref="LauncherHandoff"/> payload, LauncherHost.OnBareRelaunch for none.</summary>
        internal static void RouteHandoff(MainShellWindow panel, string? payload)
        {
            try
            {
                if (payload == null)
                {
                    if (panel.IsVisible) panel.ShowFromTray();
                    else if (CoreSettings.Current.LauncherSkipToPanel) OpenPanel(panel);
                    else OpenFor(panel);
                    return;
                }
                var (kind, id) = LauncherHandoff.Decode(payload);
                Log.Information("[Launcher] second instance asked for {Kind} {Id}", kind, id);
                switch (kind)
                {
                    case LauncherHandoff.PanelKind:
                        OpenPanel(panel);
                        break;
                    case LauncherHandoff.GameKind:
                        if (id == null || !LaunchGame(panel, id)) OpenFor(panel);
                        break;
                    default:
                        if (panel.IsVisible) BackToLauncher(panel);   // Lockdown vetoes on its own
                        else OpenFor(panel);
                        break;
                }
            }
            catch (Exception ex) { Log.Warning(ex, "[Launcher] surface handoff failed"); }
        }

        /// <summary>WPF SkipToPanel_Click: the box binds LauncherSkipToPanel; the click saves it.</summary>
        private void SkipToPanel_Click(object? sender, RoutedEventArgs e)
        {
            CoreSettings.Current.LauncherSkipToPanel = SkipToPanel.IsChecked == true;
            try { CoreSettings.Save(); }
            catch (Exception ex) { Log.Warning(ex, "[Launcher] saving LauncherSkipToPanel failed"); }
        }

        /// <summary>WPF LauncherHost.OpenPanel: the launcher hides, the panel comes up, then <paramref name="then"/>.</summary>
        internal void OpenPanel(Action<MainShellWindow>? then = null)
        {
            var panel = Panel;
            if (panel == null) { Log.Warning("[Launcher] OpenPanel with no main window"); return; }
            AfterExitBeat(() =>
            {
                Hide();
                panel.ShowFromTray();
                try { then?.Invoke(panel); }
                catch (Exception ex) { Log.Debug(ex, "[Launcher] step after OpenPanel failed"); }
            });
        }

        /// <summary>WPF LauncherHost.RequestClose, decided by Core's LauncherRules.Close.</summary>
        internal void RequestClose()
        {
            var panel = Panel;
            var outcome = LauncherRules.Close(MainShellWindow.LockdownActive,
                CoreEngine.IsRunning || CoreSession.IsSessionRunning,
                panel is { IsVisible: true }, panel != null);
            switch (outcome)
            {
                case LauncherCloseOutcome.Veto:
                    try { Services.LockdownService.Current?.NotifyEscapeAttempt(Services.Possession.EscapeKinds.Close); } catch { }
                    break;
                case LauncherCloseOutcome.Hide:
                    // WPF hides behind the panel's tray icon. With no tray host (a bare Linux
                    // desktop) a hidden launcher over a hidden panel has no way back: minimize.
                    if (panel is { IsVisible: false } && !panel.TrayHostPresent()) WindowState = WindowState.Minimized;
                    else Hide();
                    break;
                case LauncherCloseOutcome.Exit:
                    panel!.RequestExit();
                    break;
            }
        }

        /// <summary>Closing is never the user's own decision (WPF OnClosing): the host decides.
        /// An application shutdown and a Close() from code go through.</summary>
        protected override void OnClosing(WindowClosingEventArgs e)
        {
            if (!e.IsProgrammatic && e.CloseReason == WindowCloseReason.WindowClosing)
            {
                e.Cancel = true;
                Dispatcher.UIThread.Post(RequestClose);
            }
            base.OnClosing(e);
        }

        private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) BeginMoveDrag(e);
        }

        private void BtnMinimize_Click(object? sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

        private void BtnClose_Click(object? sender, RoutedEventArgs e) => RequestClose();

        // Lockdown: the launcher veil (LauncherWindow.Veil.cs) covers these doors; these refusals are the belt-and-braces layer (P05).
        private void PanelCta_Click(object? sender, RoutedEventArgs e) { if (MainShellWindow.LockdownActive) return; FxPanelLaunchBeat(); OpenPanel(); }

        /// <summary>WPF AccountChip_Click: the panel's account settings.</summary>
        private void AccountChip_Click(object? sender, RoutedEventArgs e)
        {
            if (!MainShellWindow.LockdownActive) OpenPanel(p => p.ShowTab("appsettings"));
        }

        private void SignIn_Click(object? sender, RoutedEventArgs e) { if (!MainShellWindow.LockdownActive) OpenSignIn(); }

        /// <summary>WPF StopLink_Click.</summary>
        private void StopLink_Click(object? sender, RoutedEventArgs e)
        {
            // ponytail: no LockdownVeil on this head yet; this refusal stands in for it.
            if (MainShellWindow.RefuseStopUnderLockdown()) return;
            try { MainShellWindow.StopEngine(); }
            catch (Exception ex) { Log.Warning(ex, "[Launcher] StopEngine failed"); }
            RefreshStatus();
        }

        /// <summary>WPF RefreshStatus: running since HH:mm + Stop, or idle; the CTA reads Open or Launch.
        /// Runs on the 1 s tick and at once when the engine stops (LauncherWindow.Feel.cs).</summary>
        private bool? _ctaRunning;

        internal void RefreshStatus()
        {
            try
            {
                bool running = CoreEngine.IsRunning;
                StatusText.Text = running
                    ? Loc.GetF("launcher_panel_running", (CoreEngine.StartedUtc ?? DateTime.UtcNow).ToLocalTime().ToString("HH:mm"))
                    : Loc.Get("launcher_panel_idle");
                StopLink.IsVisible = running;
                if (_ctaRunning == running) return;
                _ctaRunning = running;
                PanelCtaText.Bind(TextBlock.TextProperty,
                    (global::Avalonia.Data.Binding)new Localization.StrExtension(running ? "launcher_panel_open" : "launcher_panel_launch")
                        .ProvideValue(null!));
            }
            catch (Exception ex) { Log.Debug(ex, "[Launcher] RefreshStatus failed"); }
        }

        /// <summary>WPF RefreshAccount: the chip (name, initial, tier badge) when signed in, else the pill.</summary>
        internal void RefreshAccount()
        {
            try
            {
                var name = CoreSettings.Current.UserDisplayName;
                bool loggedIn = CoreAccount.IsLoggedIn;
                bool signedIn = loggedIn && !string.IsNullOrWhiteSpace(name);
                AccountName.Text = signedIn ? name!.Trim() : "-";
                AvatarInitial.Text = signedIn ? name!.Trim()[..1].ToUpperInvariant() : "-";
                AccountChipButton.IsVisible = loggedIn;
                SignInPill.IsVisible = !loggedIn;

                var tier = Platform.AccountSeed.Patreon?.CurrentTier ?? PatreonTier.None;
                string? badge = tier switch
                {
                    PatreonTier.Level1 => "features/tier_badge_t1.png",
                    PatreonTier.Level2 => "features/tier_badge_t2.png",
                    _ => null,
                };
                TierBadge.Source = badge == null ? null : ModArt.TryLoad(badge, 64);
                TierBadge.IsVisible = TierBadge.Source != null;
                if (TierBadge.Source == null) TierBadgePopup.IsOpen = false;
                else EnsureTierBadgeFx();   // LauncherWindow.Feel.cs: the 8 s shake
                RefreshSpReadout();
            }
            catch (Exception ex) { Log.Debug(ex, "[Launcher] RefreshAccount failed"); }
        }

        /// <summary>WPF RefreshSpReadout: the chip's Sparkle Points while signed in, counted up
        /// (LauncherWindow.Feel.cs ShowSp).</summary>
        private void RefreshSpReadout()
        {
            int sp = CoreSettings.Current.SkillPoints;
            SpChip.IsVisible = sp >= 0 && CoreAccount.IsLoggedIn;
            if (SpChip.IsVisible) ShowSp(sp);
            else _spShown = double.NaN;
        }

        /// <summary>WPF TierBadge_MouseEnter/Leave: the big copy in a popup under the badge; the 8 s
        /// shake and the pop-in are LauncherWindow.Feel.cs.</summary>
        private void TierBadge_PointerEntered(object? sender, PointerEventArgs e)
        {
            if (TierBadge.Source is not { } src) return;
            TierBadgeBig.Source = src;
            double bigWidth = src.Size.Height > 0 ? TierBadgeBig.Height * src.Size.Width / src.Size.Height : TierBadgeBig.Height;
            TierBadgePopup.HorizontalOffset = (TierBadge.Bounds.Width - bigWidth) / 2 - TierBadgeBig.Margin.Left;
            TierBadgePopup.IsOpen = true;
            TierBadgeBigPopIn();
        }

        private void TierBadge_PointerExited(object? sender, PointerEventArgs e) => TierBadgePopup.IsOpen = false;

        /// <summary>WPF RefreshStats: level, Sparkle Points, time under and the XP bar. The numbers
        /// odometer and the bar tweens (LauncherWindow.Feel.cs ShowStats).</summary>
        internal void RefreshStats()
        {
            try
            {
                var s = CoreSettings.Current;
                int level = Math.Max(1, s.PlayerLevel);
                double xp = Math.Max(0, s.PlayerXP);
                double need = 0;
                try { need = ConditioningControlPanel.Services.XpCurve.GetXPForLevel(level, ConditioningControlPanel.Services.XpCurve.EpochOf(s)); } catch { }
                double minutes = Math.Max(0, s.TotalConditioningMinutes);

                int hours = (int)(minutes / 60), mins = (int)(minutes % 60);
                StatTime.Text = hours > 0 ? $"{hours}h {mins:00}m" : $"{mins}m";

                double ratio = need > 0 ? Math.Clamp(xp / need, 0, 1) : 0;
                ShowStats(level, Math.Max(0, s.SkillPoints), XpTrack.Bounds.Width * ratio);
                XpCaption.Text = Loc.GetF("launcher_stat_xp", ((int)xp).ToString("N0"), ((int)need).ToString("N0"));
            }
            catch (Exception ex) { Log.Debug(ex, "[Launcher] RefreshStats failed"); }
        }

        /// <summary>WPF RefreshMod: the pill reads the active mod's name.</summary>
        internal void RefreshMod()
        {
            try { ModPillText.Text = LauncherModMenu.Label(Loc.Get("launcher_mod_label"), App.Mods?.ActiveMod?.Name, "-"); }
            catch (Exception ex) { Log.Debug(ex, "[Launcher] RefreshMod failed"); }
        }

        /// <summary>WPF OnModChanged: the pill at once, the tiles a dispatcher turn later (ccp-bugs #1292).
        /// ponytail: WPF also redraws on PrizeGrants.GrantsChanged; no grant service or grant-revealed
        /// card exists on this head yet (launcher-games slice).</summary>
        private void OnModChanged(object? sender, ModPackage mod) => Dispatcher.UIThread.Post(() =>
        {
            RefreshMod();
            RefreshTiles(LauncherTileTrigger.ModChanged);
        });

        /// <summary>WPF RefreshTiles: redraw when Core's LauncherTileRefresh says the event changed a tile.</summary>
        internal void RefreshTiles(LauncherTileTrigger trigger)
        {
            try
            {
                var now = ShelfCards.Select(c => new KeyValuePair<string, bool>(c.Id, Revealed(c)));
                if (!LauncherTileRefresh.ShouldRebuild(trigger, IsVisible, _revealedAtBuild, now)) return;
                Log.Information("[Launcher] {Trigger} redrawing tiles", trigger);
                BuildTiles();
            }
            catch (Exception ex) { Log.Warning(ex, "[Launcher] tile refresh on {Trigger} failed", trigger); }
        }

        /// <summary>WPF ModPill_Click: installed mods in stock order, the active one ticked, then Manage mods.</summary>
        private void ModPill_Click(object? sender, RoutedEventArgs e)
        {
            // ponytail: no LockdownVeil on this head yet; this refusal stands in for it.
            if (MainShellWindow.LockdownActive) return;
            try
            {
                var mods = App.Mods;
                var menu = new ContextMenu { Placement = PlacementMode.Bottom, MaxHeight = 420 };
                if (mods != null)
                {
                    var rows = mods.InstalledMods.Values.Select(m => new LauncherModRow(m.Id, m.Name, m.IsBuiltIn));
                    foreach (var row in LauncherModMenu.Order(rows))
                    {
                        var item = new MenuItem
                        {
                            Header = row.Name, ToggleType = MenuItemToggleType.CheckBox,
                            IsChecked = string.Equals(row.Id, mods.ActiveModId, StringComparison.OrdinalIgnoreCase),
                        };
                        var id = row.Id;
                        item.Click += (_, _) => SwitchMod(id);
                        menu.Items.Add(item);
                    }
                    menu.Items.Add(new Separator());
                }
                var manage = new MenuItem { Header = Loc.Get("launcher_mod_manage") };
                manage.Click += (_, _) => OpenPanel(p => p.OpenModManagerFromLauncher());
                menu.Items.Add(manage);
                ModPill.ContextMenu = menu;
                menu.Open(ModPill);
            }
            catch (Exception ex) { Log.Warning(ex, "[Launcher] mod menu failed"); }
        }

        internal void SwitchMod(string modId)
        {
            if (MainShellWindow.LockdownActive) { Log.Information("[Launcher] mod switch refused under Lockdown"); return; }
            try
            {
                var panel = Panel;
                if (panel == null) { Log.Warning("[Launcher] mod switch with no main window"); return; }
                panel.SwitchActiveModFromLauncher(modId);
                RefreshMod();
            }
            catch (Exception ex) { Log.Warning(ex, "[Launcher] mod switch to {Id} failed", modId); }
        }

        /// <summary>WPF LauncherHost.LaunchGame for the destinations this head has.</summary>
        internal void Play(LauncherCard card)
        {
            bool revealed = Revealed(card);
            bool needsAccount = card.RequiresAccount && !CoreAccount.IsLoggedIn;
            bool locked = !needsAccount && revealed && LockedFor(card);
            // The mystery card's Play goes to the counter, not to the game it hides (WPF LaunchGame("backroom")).
            var targetId = revealed ? card.Id : "backroom";
            bool hosted = Destinations.TryGetValue(targetId, out var dest);
            // A card with no host on this head flinches like a refusal; its tooltip says why.
            FxPlayBeat(card.Id, needsAccount || locked || !hosted);
            // Every destination here is a panel tab: a locked one still opens it (its own gate paints the
            // refusal), as WPF's does. A pending leash punishment sends the tile to the gate on the panel
            // first (WPF LauncherHost.cs:360, play#48).
            var step = LauncherRules.Game(needsAccount, locked, leashBlocks: MainShellWindow.LeashBlocksGames);
            if (step == LauncherGameStep.SignIn)
            {
                OpenSignIn();
                return;
            }
            if (step == LauncherGameStep.Leash && hosted)
            {
                Log.Information("[Launcher] {Id} waits: a leash punishment is pending", targetId);
                OpenPanel(p => p.PresentLeashGateFromLauncher());
                return;
            }
            if (!hosted)
            {
                Log.Information("[Launcher] {Id}: no game host on this head yet", targetId);
                return;
            }
            if (Games.GameWindow.Games.ContainsKey(targetId) || IsRace(targetId))
            {
                LaunchGame(targetId);
                return;
            }
            OpenPanel(dest.Open);
        }

        /// <summary>WPF LauncherHost.LaunchGame: the game opens, then the launcher hides behind it (never
        /// trays); the return poll brings the launcher back when the game closes and the panel is not up.
        /// A gate that refused leaves the launcher where it is.</summary>
        private void LaunchGame(string id)
        {
            var game = IsRace(id) ? Games.RaceWindow.Launch() : Games.GameWindow.Launch(id);
            if (game == null) return;
            AfterExitBeat(() =>
            {
                if (!game.IsVisible) return;
                Hide();
                game.Activate();
            });
            game.Closed += (_, _) =>
            {
                if (Panel is { IsVisible: true }) return;
                try { Show(); Activate(); }
                catch (Exception ex) { Log.Debug(ex, "[Launcher] return after {Id} failed", id); }
            };
        }

        private static bool IsRace(string id) => string.Equals(id, "race", StringComparison.OrdinalIgnoreCase);

        private static bool Locked((Func<bool> Locked, Action<MainShellWindow> Open) dest)
        {
            try { return dest.Locked(); }
            catch (Exception ex) { Log.Debug(ex, "[Launcher] lock probe threw"); return true; }
        }

        /// <summary>WPF LauncherWindow.OpenSignIn: the panel's login dialog, owned by the launcher.</summary>
        private async void OpenSignIn()
        {
            var panel = Panel;
            if (panel == null) { Log.Warning("[Launcher] sign-in with no main window"); return; }
            try { await panel.OpenUnifiedLoginDialog(this); }
            catch (Exception ex) { Log.Warning(ex, "[Launcher] sign-in dialog failed"); }
            RefreshAccount();
            RefreshStats();
            BuildTiles();
        }

        internal void BuildTiles()
        {
            GamesGrid.Children.Clear();
            _revealedAtBuild.Clear();
            foreach (var card in ShelfCards)
            {
                bool revealed = Revealed(card);
                try { GamesGrid.Children.Add(CreateTile(card, revealed)); _revealedAtBuild[card.Id] = revealed; }
                catch (Exception ex) { Log.Warning(ex, "[Launcher] tile {Id} failed to build", card.Id); }
            }
            SeatGrid();
        }

        /// <summary>WPF FitTiles: columns and card height from LauncherGridLayout; a block that fits sits in the
        /// middle of the column, one that overflows hangs from the top, scrolls, and grows the window once.</summary>
        private void SeatGrid()
        {
            try
            {
                int count = GamesGrid.Children.Count;
                double width = GamesColumn.Bounds.Width;
                int columns = LauncherGridLayout.Columns(width, count);
                GamesGrid.Columns = columns;
                double height = LauncherGridLayout.TileHeight(LauncherGridLayout.TileWidth(width, columns));
                foreach (var tile in GamesGrid.Children) tile.Height = height;

                double available = GamesScroller.Bounds.Height;
                if (count == 0 || available <= 0 || width <= 0) return;
                double gridHeight = LauncherGridLayout.GridHeight(width, columns, LauncherGridLayout.Rows(count, columns));
                bool scroll = LauncherGridLayout.NeedsScroll(available, gridHeight);
                var seat = scroll ? VerticalAlignment.Top : VerticalAlignment.Center;
                if (GamesGrid.VerticalAlignment != seat) GamesGrid.VerticalAlignment = seat;
                if (scroll) GrowToFitTiles(available, gridHeight);
            }
            catch (Exception ex) { Log.Debug(ex, "[Launcher] tile fit failed"); }
        }

        private bool _grewToFitTiles;

        /// <summary>WPF 7.1.5 GrowToFitTiles: the default 1280x800 cut the third row off. The first time the
        /// tiles overflow the window grows (once per window, so it never fights a hand resize) by the overflow,
        /// capped at the screen's work area, and stays centred on it. A short screen keeps the scroller.</summary>
        private void GrowToFitTiles(double available, double gridHeight)
        {
            if (_grewToFitTiles || WindowState != WindowState.Normal || !IsVisible) return;
            _grewToFitTiles = true;
            if (Screens?.ScreenFromWindow(this) is not { } screen) return;
            double scale = screen.Scaling > 0 ? screen.Scaling : 1;
            var wa = screen.WorkingArea;
            double waTop = wa.Y / scale, waHeight = wa.Height / scale;
            double old = Bounds.Height > 0 ? Bounds.Height : Height;
            double grown = LauncherGridLayout.FitWindowHeight(old, available, gridHeight, waHeight);
            if (grown <= old + 0.5) return;
            double top = Position.Y / scale - (grown - old) / 2;
            Height = grown;
            top = Math.Max(waTop, Math.Min(top, waTop + waHeight - grown));
            Position = new PixelPoint(Position.X, (int)Math.Round(top * scale));
            Log.Debug("[Launcher] grew {Old:0} -> {New:0} px to seat every tile row", old, grown);
        }

        private Border CreateTile(LauncherCard card, bool revealed)
        {
            bool needsAccount = card.RequiresAccount && !CoreAccount.IsLoggedIn;
            bool locked = !needsAccount && revealed && LockedFor(card);
            bool hosted = Destinations.ContainsKey(revealed ? card.Id : "backroom");
            var hue = Color.FromRgb(card.R, card.G, card.B);

            var tile = new Border
            {
                CornerRadius = new CornerRadius(TileRadius),
                Background = Res("SurfaceBgBrush"),
                BorderBrush = locked ? Res("Tier2DiamondBorderBrush") : RimBrush(hue),
                BorderThickness = new Thickness(LauncherGridLayout.TileBorder),
                Margin = new Thickness(LauncherGridLayout.TileMargin),
                ClipToBounds = true,
                Cursor = new Cursor(StandardCursorType.Hand),
                Tag = card.Id,
            };

            var body = new Grid { RowDefinitions = new RowDefinitions("*,Auto") };

            // --- the art plate: the mod's art (or the Breakout covers), else the glyph plate; the mystery face ---
            var plate = new Panel { MinHeight = LauncherGridLayout.MinArtHeight, Background = new SolidColorBrush(hue, 0.35) };
            IImage? art = null;
            if (revealed)
                art = (IImage?)ModArt.TryLoad(card.ArtPath, 640) ?? card.Id switch
                {
                    "breakoutdemo" => BreakoutCardArt.Demo,
                    "breakout" => BreakoutCardArt.Full,
                    _ => null,
                };
            if (!revealed) BuildMysteryPlate(plate, hue);
            else if (art != null) plate.Children.Add(new Image { Source = art, Stretch = Stretch.UniformToFill });
            else BuildGlyphPlate(plate, card.Glyph, hue);
            var surface = ((ISolidColorBrush)Res("SurfaceBgBrush")).Color;
            plate.Children.Add(new Border
            {
                IsHitTestVisible = false,
                Background = new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                    EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                    GradientStops =
                    {
                        new GradientStop(Colors.Transparent, 0),
                        new GradientStop(Color.FromArgb(0x30, 0, 0, 0), 0.55),
                        new GradientStop(Color.FromArgb(0xB0, surface.R, surface.G, surface.B), 0.88),
                        new GradientStop(surface, 1),
                    },
                },
            });
            if (needsAccount) plate.Children.Add(Pill("launcher_pill_sign_in", null, Res("AccentGradientBrush"), Brushes.White, 10));
            else if (locked) plate.Children.Add(Pill("launcher_prime_pill", "🔒", Res("Tier2DiamondBorderBrush"),
                new SolidColorBrush(Color.FromRgb(0x2A, 0x1C, 0x08)), 10));
            if (OpenTablesBadgeFor(card.Id, revealed && !needsAccount) is { } openBadge) plate.Children.Add(openBadge);   // LauncherWindow.OpenTables.cs
            if (revealed) plate.Children.Add(ShortcutButton(tile, card.Id));   // LauncherWindow.Links.cs
            if (card.IsNew && revealed) plate.Children.Add(Pill("exclusives_badge_new", null, Res("AccentGradientBrush"), Brushes.White,
                needsAccount || locked ? 40 : 10));
            body.Children.Add(plate);

            // --- title, blurb, play: fixed heights so every title sits on the same line ---
            var text = new StackPanel
            {
                Margin = new Thickness(16, LauncherGridLayout.TextPadTop, 16, LauncherGridLayout.TextPadBottom),
                Height = LauncherGridLayout.TextHeight - LauncherGridLayout.TextPadTop - LauncherGridLayout.TextPadBottom,
            };
            Grid.SetRow(text, 1);
            text.Children.Add(new TextBlock
            {
                Text = Loc.Get(revealed ? card.TitleKey : "launcher_mystery_title"), FontFamily = new FontFamily("Fredoka, Segoe UI"), FontSize = 19,
                FontWeight = FontWeight.SemiBold, Foreground = Res("TextLightBrush"),
                Height = LauncherGridLayout.TitleHeight, VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
            text.Children.Add(new TextBlock
            {
                Text = Loc.Get(revealed ? card.BlurbKey : "launcher_mystery_blurb"), FontSize = 14, Margin = new Thickness(0, LauncherGridLayout.BlurbGap, 0, 0),
                Foreground = Res("TextSecondaryBrush"), TextWrapping = TextWrapping.Wrap,
                TextTrimming = TextTrimming.CharacterEllipsis, Height = LauncherGridLayout.BlurbHeight,
            });
            var play = PlayButton(hue, locked, needsAccount, revealed ? null : "launcher_mystery_play");
            // ponytail: no LockdownVeil on this head yet (WPF's swallows every tile click); this refusal stands in for it.
            play.Click += (_, _) => { if (!MainShellWindow.LockdownActive) Play(card); };
            text.Children.Add(play);
            body.Children.Add(text);

            // WPF 7.1.5 (tester: "only the Play button works"): the WHOLE card is the Play button. A press must
            // start AND end on the tile; the Play button handles its own press, so nothing fires twice.
            bool tileArmed = false;
            tile.PointerPressed += (_, e) => { if (!e.Handled && e.GetCurrentPoint(tile).Properties.IsLeftButtonPressed) tileArmed = true; };
            tile.PointerExited += (_, _) => tileArmed = false;
            tile.PointerReleased += (_, e) =>
            {
                bool armed = tileArmed;
                tileArmed = false;
                if (!armed || e.Handled || MainShellWindow.LockdownActive) return;
                e.Handled = true;
                Play(card);
            };
            if (!hosted && !needsAccount) ToolTip.SetTip(tile, Loc.Get("exclusives_not_on_this_build"));

            tile.Child = body;
            DecorateTileFx(tile, hue);
            return tile;
        }

        /// <summary>WPF BuildPlayButton: outline at rest, hue fill on hover; Sign in / Locked / Play.
        /// <paramref name="labelKey"/> overrides the Play/Locked label; a signed-out tile always reads Sign in.</summary>
        private Button PlayButton(Color hue, bool locked, bool needsAccount, string? labelKey = null)
        {
            var label = new StackPanel { Orientation = Orientation.Horizontal };
            if (!needsAccount)
                label.Children.Add(new TextBlock
                {
                    Text = locked ? "🔒" : "▶", FontSize = locked ? 13 : 11, Margin = new Thickness(0, 0, 8, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                });
            label.Children.Add(new TextBlock
            {
                Text = Loc.Get(needsAccount ? "launcher_sign_in" : labelKey ?? (locked ? "launcher_locked" : "launcher_play")),
                VerticalAlignment = VerticalAlignment.Center,
            });
            IBrush fill = needsAccount ? Res("AccentGradientBrush")
                : locked ? Res("Tier2DiamondBorderBrush")
                : new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                    EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
                    GradientStops = { new GradientStop(Lighten(hue, 0.15), 0), new GradientStop(Darken(hue, 0.75), 1) },
                };
            return new Button
            {
                Content = label,
                Theme = (ControlTheme)this.FindResource("LauncherPlay")!,
                Height = LauncherGridLayout.PlayHeight, Margin = new Thickness(0, LauncherGridLayout.PlayGap, 0, 0),
                Background = fill,
                BorderBrush = new SolidColorBrush(Lighten(hue, 0.25)),
                Foreground = new SolidColorBrush(Lighten(hue, 0.45)),
            };
        }

        private Border Pill(string key, string? icon, IBrush background, IBrush ink, double top)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            if (icon != null) row.Children.Add(new TextBlock { Text = icon, FontSize = 10, Margin = new Thickness(0, 0, 5, 0), Foreground = ink });
            row.Children.Add(new TextBlock { Text = Loc.Get(key), FontSize = 11, FontWeight = FontWeight.Bold, Foreground = ink });
            return new Border
            {
                Background = background, CornerRadius = new CornerRadius(10), Padding = new Thickness(9, 3, 10, 3),
                Margin = new Thickness(12, top, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
                Child = row,
            };
        }

        /// <summary>WPF BuildGlyphPlate: a warm radial in the hue, a road drawn in perspective (lanes converging on
        /// a low horizon, a dashed centre line) and the glyph sitting on it. Reads as a picture, not a placeholder.</summary>
        private static void BuildGlyphPlate(Panel host, string glyph, Color hue)
        {
            host.Background = new RadialGradientBrush
            {
                GradientOrigin = new RelativePoint(0.5, 0.3, RelativeUnit.Relative),
                Center = new RelativePoint(0.5, 0.3, RelativeUnit.Relative),
                RadiusX = new RelativeScalar(0.85, RelativeUnit.Relative),
                RadiusY = new RelativeScalar(0.95, RelativeUnit.Relative),
                GradientStops = { new GradientStop(Lighten(hue, 0.1), 0), new GradientStop(Darken(hue, 0.4), 1) },
            };
            var road = new Canvas { IsHitTestVisible = false, Opacity = 0.22, Width = 330, Height = 200 };
            void Line(double x1, double y1, double x2, double y2, double thickness, double opacity = 1, AvaloniaList<double>? dash = null) =>
                road.Children.Add(new global::Avalonia.Controls.Shapes.Line
                {
                    StartPoint = new Point(x1, y1), EndPoint = new Point(x2, y2), Stroke = Brushes.White,
                    StrokeThickness = thickness, Opacity = opacity, StrokeDashArray = dash,
                });
            // The geometry assumes ~330 x 200; the Viewbox scales it with the tile.
            Line(-30, 200, 150, 96, 1.5); Line(90, 200, 158, 96, 1.5); Line(240, 200, 172, 96, 1.5); Line(360, 200, 180, 96, 1.5);
            Line(165, 200, 165, 96, 2, 1, new AvaloniaList<double> { 4, 5 });
            Line(0, 96, 330, 96, 1, 0.6);
            host.Children.Add(new Viewbox { Stretch = Stretch.Fill, IsHitTestVisible = false, Child = road });
            host.Children.Add(new TextBlock
            {
                Text = glyph, FontSize = 64, FontFamily = new FontFamily("Fredoka, Segoe UI"),
                Foreground = Brushes.White, Opacity = 0.95, Margin = new Thickness(0, 0, 0, 12),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            });
        }

        /// <summary>WPF BuildMysteryPlate: the hue pulled down to dusk and a large "?". Nothing on it names the game.</summary>
        private static void BuildMysteryPlate(Panel host, Color hue)
        {
            host.Background = new RadialGradientBrush
            {
                GradientOrigin = new RelativePoint(0.5, 0.35, RelativeUnit.Relative),
                Center = new RelativePoint(0.5, 0.35, RelativeUnit.Relative),
                RadiusX = new RelativeScalar(0.85, RelativeUnit.Relative),
                RadiusY = new RelativeScalar(0.95, RelativeUnit.Relative),
                GradientStops = { new GradientStop(Darken(hue, 0.55), 0), new GradientStop(Darken(hue, 0.22), 1) },
            };
            host.Children.Add(new TextBlock
            {
                Text = "?", FontSize = 96, FontWeight = FontWeight.Bold, FontFamily = new FontFamily("Fredoka, Segoe UI"),
                Foreground = new SolidColorBrush(Lighten(hue, 0.55)), Opacity = 0.95, Margin = new Thickness(0, 0, 0, 12),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            });
        }

        private IBrush Res(string key) => (IBrush)this.FindResource(key)!;

        /// <summary>WPF RimBrush: the hue at the corners, the glass border between.</summary>
        private IBrush RimBrush(Color hue)
        {
            var glass = ((ISolidColorBrush)Res("GlassBorderBrush")).Color;
            return new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(Color.FromArgb(0x99, hue.R, hue.G, hue.B), 0),
                    new GradientStop(glass, 0.6),
                    new GradientStop(Color.FromArgb(0x55, hue.R, hue.G, hue.B), 1),
                },
            };
        }

        private static Color Lighten(Color c, double t) => Color.FromRgb(
            (byte)(c.R + (255 - c.R) * t), (byte)(c.G + (255 - c.G) * t), (byte)(c.B + (255 - c.B) * t));

        private static Color Darken(Color c, double f) => Color.FromRgb((byte)(c.R * f), (byte)(c.G * f), (byte)(c.B * f));
    }
}
