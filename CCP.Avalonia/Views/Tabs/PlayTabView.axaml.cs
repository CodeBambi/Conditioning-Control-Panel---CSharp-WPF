using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.Windows;
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
        }

        private void OnPlayTabAttached(object? sender, EventArgs e)
        {
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

        /// <summary>ponytail: needs the Chaos slot picker behind MainWindow.BtnStartChaos_Click
        /// (TierGate.DemandLab + ChaosHostService); neither is on this head.</summary>
        private void BtnStartChaos_Click(object? sender, RoutedEventArgs e) { }

        /// <summary>ponytail: needs MainWindow.BtnQuickStartChaos_Click (straight into a saved run,
        /// same ChaosHostService).</summary>
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
        /// ConditioningControlPanel/Services/Webcam/GazeFocusService.cs, WebcamTrackingService and
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
