using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using ConditioningControlPanel.Avalonia.Controls;

namespace ConditioningControlPanel.Avalonia.Views.Tabs
{
    /// <summary>
    /// PORTED from ConditioningControlPanel/Views/Tabs/PlayTabView.xaml.cs.
    ///
    /// The Play door (tab key <c>play</c>): a card wall over the game-shaped features.
    ///
    /// <para><b>This file is the host only.</b> It owns the page frame and the one ambient loop.
    /// It owns no card content, no launch, and no tier decision. On WPF every card's click is a
    /// one-line shim in a <c>PlayTabView.&lt;Area&gt;.cs</c> partial that forwards to the
    /// MainWindow handler of the same name; MainWindow is a WPF type, so on this head every shim
    /// is a stub instead. The handler NAMES are kept verbatim - they are the launch-parity
    /// contract, and the wiring is a rename away once those handlers reach Core.</para>
    ///
    /// <para><b>The one loop.</b> <c>RabbitHoleFx</c> is this surface's single focal ambient
    /// canvas (FX_OVERHAUL_PLAN: one per surface). It is composed once, on first attach, and on
    /// WPF is then handed to <c>MainWindow.RegisterTabFx("play", …)</c> - simultaneously the
    /// park/resume hook and the motion kill-switch's reach. Starting the layers is portable and
    /// carried over for real; the registration is the stub.</para>
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

        // The compiled-XAML x:Name fields are only populated by a generated InitializeComponent();
        // this head loads its views with AvaloniaXamlLoader.Load, so the one control the code
        // touches is resolved by name here, as in LockdownTabView and AdornedAvatar.
        private readonly AmbientFxCanvas? _rabbitHoleFx;

        public PlayTabView()
        {
            AvaloniaXamlLoader.Load(this);
            _rabbitHoleFx = this.FindControl<AmbientFxCanvas>("RabbitHoleFx");

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
                if (_fxComposed || _rabbitHoleFx == null) return;
                _fxComposed = true;

                // Embers, not weather: a DustField alone. The card already carries a three-stop
                // gradient of its own and a fog layer on top would just wash it out. Colour is
                // FxTheme's particle slot, so this is an ember on a Bambi build and a green mote
                // on Dronification - no orange is hard-coded into the Play door.
                _rabbitHoleFx.StartLayers(new AmbientFxConfig
                {
                    Layers = AmbientFxLayers.DustField,
                    Intensity = RabbitHoleFxIntensity,
                });

                // ponytail: needs MainWindow.RegisterTabFx(TabKey, _rabbitHoleFx) - the park/resume
                // hook and the motion kill-switch's reach. MainWindow is a WPF type; wired when
                // the ambient registry moves to Core. Until then the canvas parks itself on
                // detach (AmbientFxCanvas.Evaluate), which is why running it here is safe.
            }
            catch (Exception)
            {
                // ponytail: needs App.Logger. Swallowed rather than crashing the tab, same as WPF.
            }
        }

        // ==================================================================================
        // Launch shims. Every name below is the MainWindow handler the WPF card forwards to -
        // `if (Window.GetWindow(this) is MainWindow mw) mw.<Handler>(sender, e);` - and launch
        // parity is the contract, so the names are kept even though the bodies are empty here.
        // Nothing on this surface may re-implement a launch, and nothing here decides a tier:
        // the lockbands are decoration and TierGate does the refusing inside the handler.
        // ==================================================================================

        /// <summary>ponytail: needs MainWindow.BtnStartChaos_Click (TierGate.DemandLab + the hub).</summary>
        private void BtnStartChaos_Click(object? sender, RoutedEventArgs e) { }

        /// <summary>ponytail: needs MainWindow.BtnQuickStartChaos_Click (straight into a saved run).</summary>
        private void BtnQuickStartChaos_Click(object? sender, RoutedEventArgs e) { }

        /// <summary>ponytail: needs MainWindow.BtnStartGoon_Click (GoonHostService).</summary>
        private void BtnStartGoon_Click(object? sender, RoutedEventArgs e) { }

        /// <summary>ponytail: needs MainWindow.ShowTab("remotecontrol").</summary>
        private void BtnPlayRemoteControl_Click(object? sender, RoutedEventArgs e) { }

        /// <summary>ponytail: needs MainWindow.ShowTab("settings") + the Devices section.</summary>
        private void BtnOpenDeviceSettings_Click(object? sender, RoutedEventArgs e) { }

        /// <summary>ponytail: needs MainWindow.BtnGazeMinigame_Click.</summary>
        private void BtnGazeMinigame_Click(object? sender, RoutedEventArgs e) { }

        /// <summary>ponytail: needs MainWindow.ChkFocusGaze_Changed - it reads ChkPlayFocusGaze and
        /// writes TxtPlayFocusGazeStatus. WPF's Checked + Unchecked pair is one event here.</summary>
        private void ChkFocusGaze_Changed(object? sender, RoutedEventArgs e) { }

        /// <summary>ponytail: needs MainWindow.BtnLabBlinkTrainerOpenNew_Click.</summary>
        private void BtnLabBlinkTrainerOpenNew_Click(object? sender, RoutedEventArgs e) { }

        /// <summary>ponytail: needs MainWindow.ShowTab("gradedintake") - every gate state navigates.</summary>
        private void BtnPlayGradedIntake_Click(object? sender, RoutedEventArgs e) { }

        /// <summary>ponytail: needs MainWindow.ShowTab("home") - the pass comes from the logo tile.</summary>
        private void BtnPlayIntakePassHome_Click(object? sender, RoutedEventArgs e) { }

        /// <summary>ponytail: needs MainWindow.ShowTab("fyp"), which intercepts into OpenFypFeed.
        /// Never FypHostService.Launch() - that would be a second, ungated launch path.</summary>
        private void BtnPlayFyp_Click(object? sender, RoutedEventArgs e) { }

        /// <summary>ponytail: needs MainWindow.ShowTab("justdrop"), which owns the withheld refusal.</summary>
        private void BtnPlayJustDrop_Click(object? sender, RoutedEventArgs e) { }

        /// <summary>ponytail: needs MainWindow.ShowTab("lockdown").</summary>
        private void BtnPlayLockdown_Click(object? sender, RoutedEventArgs e) { }

        /// <summary>ponytail: needs MainWindow.BtnStartArcademy_Click (ArcademyHostService.Launch,
        /// which owns the door, the T2 check and the AudioOnlySession refusal).</summary>
        private void BtnStartArcademy_Click(object? sender, RoutedEventArgs e) { }

        /// <summary>ponytail: needs MainWindow.OpenStudioModule("spiral") - ShowTab("studio") plus
        /// FocusRackEntry("spiral"). Loom NAVIGATES; a Launch() here would be a second editor.</summary>
        private void BtnPlayLoom_Click(object? sender, RoutedEventArgs e) { }
    }
}
