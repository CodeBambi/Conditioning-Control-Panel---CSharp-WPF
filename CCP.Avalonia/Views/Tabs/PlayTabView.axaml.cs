using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Tabs
{
    /// <summary>
    /// PORTED from ConditioningControlPanel/Views/Tabs/PlayTabView.xaml.cs (the host) and
    /// PlayTabView.Cards.cs (the shims).
    ///
    /// <para>The Play door (tab key <c>play</c>): a card wall over the game-shaped features.</para>
    ///
    /// <para><b>This file is the host plus the shims.</b> It owns the page frame and the one
    /// ambient loop; it owns no card content, no launch, and no tier decision. On WPF every card's
    /// click is a one-line passthrough to the MainWindow handler of the same name. That pattern
    /// ports: <see cref="Owner"/> is <c>Window.GetWindow(this) as MainWindow</c> written for
    /// Avalonia, and every click that WPF answers with <c>ShowTab</c> is answered with the shell's
    /// <c>ShowTab</c> here, so the Play wall navigates for real. The clicks that WPF answers with a
    /// host service (Chaos, Goon, Arcademy, the gaze minigame, the blink trainer) have no service on
    /// this head and each one names what it is waiting for.</para>
    ///
    /// <para><b>The one loop.</b> <c>RabbitHoleFx</c> is this surface's single focal ambient canvas
    /// (FX_OVERHAUL_PLAN: one per surface). It is composed once, on first attach, and on WPF is then
    /// handed to <c>MainWindow.RegisterTabFx("play", …)</c> - simultaneously the park/resume hook
    /// and the motion kill-switch's reach. Starting the layers is portable and carried over for
    /// real; the registration is what is still missing.</para>
    /// </summary>
    public partial class PlayTabView : UserControl
    {
        /// <summary>
        /// Ember density behind the portal card. Twin of
        /// <c>MainWindow.TabFxTakeoverLabStatus.cs</c>'s private <c>RabbitHoleFxIntensity</c>, kept
        /// to the digit so the hero looks identical to the Lab card it replaces.
        /// </summary>
        private const double RabbitHoleFxIntensity = 0.62;

        /// <summary>The ShowTab key this view answers to, and therefore the ambient registry key.
        /// <c>"lab"</c> is a permanent alias that routes here; it is NEVER the registry key.</summary>
        private const string TabKey = "play";

        private bool _fxComposed;

        /// <summary>The one cast every shim makes - the port of WPF's
        /// <c>Window.GetWindow(this) as MainWindow</c>. Null while the view is being built and under
        /// <c>--render-view</c>, where a card that fires simply does nothing, exactly as WPF's
        /// designer case does.</summary>
        private MainShellWindow? Owner => TopLevel.GetTopLevel(this) as MainShellWindow;

        public PlayTabView()
        {
            // InitializeComponent, not AvaloniaXamlLoader.Load: only the generated one assigns the
            // x:Name fields, and Load leaves every one of them permanently null - a silent no-op
            // that compiles, renders and reviews clean.
            InitializeComponent();

            // Composed on first ATTACH rather than in the constructor: the canvas needs a live
            // visual tree to size its layers against. WPF hooked IsVisibleChanged; Avalonia's twin
            // for "the tree is up" is AttachedToVisualTree. Views stay instantiated for the app's
            // life, so the _fxComposed guard keeps this to exactly once.
            AttachedToVisualTree += OnPlayTabAttached;

            // The hero plates, and the repaint that keeps them honest across a mod switch. Not
            // deferred to attach like the FX canvas: painting a Background needs no measured tree,
            // and a card that draws its scrim first and its art a frame later flickers.
            RefreshHeroArt();
            LoadChaosBoxes();
        }

        // The mod-switch repaint, subscribed the way DeeperTabView and SheListeningTabView do it:
        // ONCE PER ATTACH, off on every detach. Subscribing in the constructor instead would be
        // one += against a -= that runs every detach, so the first detach would end the repaints
        // for the life of the process - and nothing would say so.
        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            CoreMods.ModChanged -= OnModChangedRepaintArt;   // idempotent: attach fires more than once
            CoreMods.ModChanged += OnModChangedRepaintArt;
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            CoreMods.ModChanged -= OnModChangedRepaintArt;
            base.OnDetachedFromVisualTree(e);
        }

        /// <summary>
        /// Every card hero, WPF's <c>PlayTabView.xaml</c> table restated: the Border that owns the
        /// plate, the Resources-relative art, and the Stretch each one was authored with. The Loom
        /// strip and the two 168px plates are UniformToFill like the rest; the Goon wordmark is the
        /// one exception - Uniform inside WPF's <c>Viewbox="0.03,0.22,0.94,0.68"</c>, which is
        /// <see cref="ImageBrush.SourceRect"/> here.
        ///
        /// <para><c>lockdown_icon.png</c> sits at the RESOURCE ROOT, not under <c>features/</c>.
        /// That is the path a mod keys its override against, so it is kept verbatim.</para>
        /// </summary>
        private static readonly (string Plate, string Art, Stretch Fit)[] HeroPlates =
        {
            ("PlayDtrhHeroPlate",     "features/dtrh.png",              Stretch.UniformToFill),
            ("PlayGoonHeroPlate",     "features/goon_game.png",         Stretch.Uniform),
            ("PlayRemoteHeroPlate",   "features/remote_control.png",    Stretch.UniformToFill),
            ("PlayGazeHeroPlate",     "features/lab_gaze_hero.png",     Stretch.UniformToFill),
            ("PlayFocusHeroPlate",    "features/lab_focusgaze_hero.png",Stretch.UniformToFill),
            ("PlayBlinkHeroPlate",    "features/blink_trainer.png",     Stretch.UniformToFill),
            ("PlayIntakeHeroPlate",   "features/lab_quiz_hero.png",     Stretch.UniformToFill),
            ("PlayFypHeroPlate",      "features/fyp.png",               Stretch.UniformToFill),
            ("PlayJustDropHeroPlate", "features/justdrop.png",          Stretch.UniformToFill),
            ("PlayLockdownHeroPlate", "lockdown_icon.png",              Stretch.UniformToFill),
            ("PlayLoomHeroPlate",     "features/loom.png",              Stretch.UniformToFill),
        };

        private void OnModChangedRepaintArt(object? sender, ModPackage? mod) =>
            Dispatcher.UIThread.Post(RefreshHeroArt);

        /// <summary>
        /// Paints each hero plate, mod override first (<see cref="ModArt.TryLoad"/> over
        /// <see cref="CoreModArt"/>), this head's shipped avares:// copy second.
        ///
        /// <para>A null resolve LEAVES the plate as it is rather than clearing it: the scrim and
        /// the card colour underneath are the WPF null path, and a mod that ships no override for
        /// one card must not blank the ten it does not mention either.</para>
        /// </summary>
        private void RefreshHeroArt()
        {
            foreach (var (plateName, art, fit) in HeroPlates)
            {
                try
                {
                    if (this.FindControl<Border>(plateName) is not { } plate) continue;
                    if (ModArt.TryLoad(art) is not { } bmp) continue;

                    var brush = new ImageBrush(bmp) { Stretch = fit };
                    // The Goon wordmark's crop. WPF's Viewbox is relative, and so is SourceRect
                    // when it is told to be; the numbers are the original's.
                    if (plateName == "PlayGoonHeroPlate")
                        brush.SourceRect = new RelativeRect(0.03, 0.22, 0.94, 0.68, RelativeUnit.Relative);
                    plate.Background = brush;
                }
                catch (Exception ex)
                {
                    Log.Debug(ex, "[Play] hero art {Art} would not paint", art);
                }
            }
        }

        // ---- the two Chaos boxes ------------------------------------------------------------
        //
        // WPF binds both TwoWay to App.Settings.Current; ChaosAnnouncerEnabled and
        // ChaosWebGameEnabled are in Core, so this is that same round-trip written out.
        //
        // TRUE, not false. Avalonia raises IsCheckedChanged on a PROGRAMMATIC set, including the
        // one below, and the handler is wired in the markup - so a flag that started false would
        // let the first paint save the value it just read, and on a fresh profile write the
        // markup default over the user's.

        private bool _isLoadingChaosBoxes = true;

        /// <summary>
        /// Pulls both boxes from settings. Called from the ctor and again on every attach, because
        /// a cloud restore replaces the whole AppSettings instance and a box left showing the old
        /// one would be reporting a value nothing holds. (The WRITES are always current:
        /// CoreSettings.Current is read per access, never captured.)
        /// </summary>
        private void LoadChaosBoxes()
        {
            _isLoadingChaosBoxes = true;
            try
            {
                var s = CoreSettings.Current;
                ChkPlayChaosAnnouncer.IsChecked = s.ChaosAnnouncerEnabled;
                ChkPlayChaosWebGame.IsChecked = s.ChaosWebGameEnabled;
            }
            catch (Exception ex) { Log.Debug(ex, "[Play] the Chaos boxes would not load"); }
            finally { _isLoadingChaosBoxes = false; }
        }

        private void ChkPlayChaosAnnouncer_Changed(object? sender, RoutedEventArgs e)
        {
            if (_isLoadingChaosBoxes) return;
            bool on = ChkPlayChaosAnnouncer.IsChecked == true;
            if (CoreSettings.Current.ChaosAnnouncerEnabled == on) return;   // compare before write
            CoreSettings.Current.ChaosAnnouncerEnabled = on;
            CoreSettings.Save();
        }

        private void ChkPlayChaosWebGame_Changed(object? sender, RoutedEventArgs e)
        {
            if (_isLoadingChaosBoxes) return;
            bool on = ChkPlayChaosWebGame.IsChecked == true;
            if (CoreSettings.Current.ChaosWebGameEnabled == on) return;
            CoreSettings.Current.ChaosWebGameEnabled = on;
            CoreSettings.Save();
        }

        private void OnPlayTabAttached(object? sender, EventArgs e)
        {
            LoadChaosBoxes();
            try
            {
                if (_fxComposed || RabbitHoleFx == null) return;
                _fxComposed = true;

                // Embers, not weather: a DustField alone. The card already carries a three-stop
                // gradient of its own and a fog layer on top would just wash it out. Colour is
                // FxTheme's particle slot, so this is an ember on a Bambi build and a green mote
                // on Dronification - no orange is hard-coded into the Play door.
                RabbitHoleFx.StartLayers(new AmbientFxConfig
                {
                    Layers = AmbientFxLayers.DustField,
                    Intensity = RabbitHoleFxIntensity,
                });

                // ponytail: needs RegisterTabFx(TabKey, RabbitHoleFx) - the park/resume hook and
                // the motion kill-switch's reach. It is one of the four members stubbed out of
                // CCP.Avalonia/Views/Windows/MainShellWindow.AmbientFx.cs. Until it exists the
                // canvas parks itself on detach (AmbientFxCanvas.Evaluate), which is why running
                // it here is safe.
            }
            catch (Exception ex)
            {
                Log.Debug("PlayTabView FX compose: {E}", ex.Message);
            }
        }

        // ==================================================================================
        // Launch shims. Every name below is the MainWindow handler the WPF card forwards to,
        // and launch parity is the contract, so the names are kept. Nothing on this surface
        // re-implements a launch, and nothing here decides a tier: the lockbands are
        // decoration and TierGate does the refusing inside the handler.
        // ==================================================================================

        // ---- DESCENT ---------------------------------------------------------------------

        /// <summary>ponytail: there is no "ChaosHostService" anywhere in the repo - the name an
        /// earlier note invented. MainWindow.Lab.cs:259 is TierGate.DemandLab("Down the Rabbit
        /// Hole", "dtrh"), then Services/Chaos/DtrhHostService.cs (the web path) or App.Chaos +
        /// ChaosHappyPath (the WPF path), with Services/Chaos/ChaosMeta.cs switching the save slot
        /// between them. CCP.Avalonia/Views/Chaos/ChaosSlotPickerWindow.axaml.cs IS ported, so the
        /// picker is the one part that would work; the gate and both hosts are head-side, and a
        /// picker that opens a save and then descends into nothing is the wrong half to ship.</summary>
        private void BtnStartChaos_Click(object? sender, RoutedEventArgs e) { }

        /// <summary>ponytail: MainWindow.Lab.cs:390 - the same TierGate.DemandLab door, then
        /// DtrhHostService.Launch() or App.Chaos.StartRun against ChaosMeta's already-live slot.
        /// Quick Start skips the picker, never the gate, which is why this cannot be the one
        /// launch path that ships first.</summary>
        private void BtnQuickStartChaos_Click(object? sender, RoutedEventArgs e) { }

        // ---- TOGETHER --------------------------------------------------------------------

        /// <summary>ponytail: needs ConditioningControlPanel/Services/Goon/GoonHostService.cs.</summary>
        private void BtnStartGoon_Click(object? sender, RoutedEventArgs e) { }

        private void BtnPlayRemoteControl_Click(object? sender, RoutedEventArgs e) => Owner?.ShowTab("remotecontrol");

        // ---- EYES ------------------------------------------------------------------------

        /// <summary>WPF: <c>mw.OpenDeviceSettings()</c> = ShowTab("appsettings") +
        /// AppSettingsTab.FocusSection("devices"). ponytail: the focus half is the shell's helper and
        /// MainShellWindow has no OpenDeviceSettings yet, so this lands on the door's first
        /// section.</summary>
        private void BtnOpenDeviceSettings_Click(object? sender, RoutedEventArgs e) => Owner?.ShowTab("appsettings");

        /// <summary>ponytail: needs MainWindow.BtnGazeMinigame_Click - the gaze minigame window plus
        /// Services/Webcam/WebcamTrackingService.cs and its consent flow.</summary>
        private void BtnGazeMinigame_Click(object? sender, RoutedEventArgs e) { }

        /// <summary>The only Focus Gaze switch in the app. ponytail: needs
        /// ConditioningControlPanel/Services/Tracking/GazeFocusService.cs, WebcamTrackingService and
        /// TierGate - MainWindow.LabTab.cs:817 gates the ON edge on Tier 2 and on webcam consent
        /// before it arms anything, and there is nothing safe to write without them. Turning the box
        /// OFF is never gated on WPF either, but there is no consumer here to release.</summary>
        private void ChkFocusGaze_Changed(object? sender, RoutedEventArgs e) { }

        /// <summary>ponytail: needs MainWindow.BtnLabBlinkTrainerOpenNew_Click; the Blink Trainer
        /// page exists here but its host service does not.</summary>
        private void BtnLabBlinkTrainerOpenNew_Click(object? sender, RoutedEventArgs e) { }

        // ---- SESSIONS --------------------------------------------------------------------

        // All four pass states navigate: the page's own gate is what explains a spent week or a
        // missing login, so a locked click has to ARRIVE somewhere rather than be swallowed.
        private void BtnPlayGradedIntake_Click(object? sender, RoutedEventArgs e) => Owner?.ShowTab("gradedintake");

        /// <summary>"Where does a pass come from?" - the Home logo tile's flip ceremony hands them
        /// out, and "settings" is Home's tab key (the Settings DOOR is "appsettings").</summary>
        private void BtnPlayIntakePassHome_Click(object? sender, RoutedEventArgs e) => Owner?.ShowTab("settings");

        /// <summary>ShowTab("fyp"), never FypHostService.Launch() - that would be a second, ungated
        /// launch path. ponytail: "fyp" is one of the shell's WindowKeys, so the call is a
        /// documented no-op until OpenFypFeed exists (MainShellWindow.TabNavigation.cs).</summary>
        private void BtnPlayFyp_Click(object? sender, RoutedEventArgs e) => Owner?.ShowTab("fyp");

        /// <summary>ShowTab("justdrop"), which owns the withheld refusal. ponytail: also a
        /// WindowKey on this head, so it no-ops until the shop host lands.</summary>
        private void BtnPlayJustDrop_Click(object? sender, RoutedEventArgs e) => Owner?.ShowTab("justdrop");

        private void BtnPlayLockdown_Click(object? sender, RoutedEventArgs e) => Owner?.ShowTab("lockdown");

        // ---- MORE ------------------------------------------------------------------------

        /// <summary>ponytail: needs ArcademyHostService.Launch, which owns the door, the T2 check
        /// and the AudioOnlySession refusal. A launch, not navigation - the Arcademy has no tab.</summary>
        private void BtnStartArcademy_Click(object? sender, RoutedEventArgs e) { }

        /// <summary>Loom NAVIGATES; a Launch() here would be a second editor. WPF calls
        /// <c>OpenStudioModule("spiral")</c> = ShowTab("studio") + StudioTab.FocusRackEntry("spiral").
        /// ponytail: OpenStudioModule is one of the helpers stubbed out of
        /// CCP.Avalonia/Views/Windows/MainShellWindow.Presets.cs, so this lands on the Studio rack's
        /// default module instead of the Spiral one.</summary>
        private void BtnPlayLoom_Click(object? sender, RoutedEventArgs e) => Owner?.ShowTab("studio");
    }
}
