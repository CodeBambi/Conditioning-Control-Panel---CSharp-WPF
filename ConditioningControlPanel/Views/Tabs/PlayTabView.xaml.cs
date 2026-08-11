using System;
using System.Windows;
using System.Windows.Controls;
using ConditioningControlPanel.Controls;

namespace ConditioningControlPanel.Views.Tabs
{
    /// <summary>
    /// The Play door (tab key <c>play</c>): a card wall over the game-shaped features that used to
    /// be scattered across the Lab tab, the Exclusives shelf and two orphaned windows.
    ///
    /// <para><b>This file is the host only.</b> It owns the page frame and the one ambient loop.
    /// It owns no card content, no launch, and no tier decision. Every card's markup goes in a
    /// <c>Slot*</c> Grid in PlayTabView.xaml, and every card's click shim goes in a partial file
    /// of its own — <c>Views\Tabs\PlayTabView.&lt;Area&gt;.cs</c>, holding nothing but
    /// <c>if (Window.GetWindow(this) is MainWindow mw) mw.&lt;ExistingHandler&gt;(sender, e);</c>
    /// passthroughs. Same convention as <see cref="AppSettingsTabView"/> (Phase 2) and
    /// <see cref="StudioTabView"/> (Phase 4), for the same reason: parallel agents must never
    /// share one file.</para>
    ///
    /// <para><b>Launch parity is the contract.</b> A shim means an entitled user's click runs the
    /// same handler object the old button ran — <c>BtnStartChaos_Click</c>,
    /// <c>BtnQuickStartChaos_Click</c>, <c>BtnStartGoon_Click</c>, <c>BtnGazeMinigame_Click</c>,
    /// <c>ChkFocusGaze_Changed</c>, <c>BtnStartBureau_Click</c>, <c>BtnStartIntake_Click</c>, and
    /// <c>ShowTab</c> for everything that is a page rather than a window. Nothing here
    /// re-implements a launch, and nothing here decides a tier: the lockbands are decoration and
    /// <c>TierGate</c> does the refusing inside the handler.</para>
    ///
    /// <para><b>The one loop.</b> <c>RabbitHoleFx</c> is this surface's single focal ambient
    /// canvas (FX_OVERHAUL_PLAN: one per surface). It is composed once, on first show, and then
    /// handed to <c>MainWindow.RegisterTabFx("play", …)</c> — which is simultaneously the
    /// park/resume hook (<c>SwitchTabFx</c> on every navigation) and the motion kill-switch's
    /// reach (<c>CmbMotionLevel_SelectionChanged</c> parks every registered canvas via
    /// <c>SwitchTabFx("")</c>). The registration key must stay the key <c>ShowTab</c> passes for
    /// this view; register it under anything else and the canvas runs forever off-screen.</para>
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
        /// <c>"lab"</c> is a permanent alias that routes here; it is NEVER the registry key —
        /// SwitchTabFx compares against the key ShowTab was called with, and the Play door's
        /// canonical key is this one.</summary>
        private const string TabKey = "play";

        private bool _fxComposed;

        public PlayTabView()
        {
            InitializeComponent();

            // Composed on first show rather than in the constructor: the canvas needs a live visual
            // tree to size its layers against, and the window is not up yet at construction time.
            // Views stay instantiated for the app's life (ground rule §2.3), so this fires exactly
            // once and never again.
            IsVisibleChanged += OnPlayTabVisibleChanged;
        }

        private void OnPlayTabVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            try
            {
                if (_fxComposed || !IsVisible) return;
                if (Window.GetWindow(this) is not MainWindow mw) return;   // retry on the next show
                if (RabbitHoleFx == null) return;

                _fxComposed = true;

                // Embers, not weather: a DustField alone. The card already carries a three-stop
                // gradient of its own and a fog layer on top of that would just wash it out.
                // Colour is FxTheme's particle slot, so this is an ember on a Bambi build and a
                // green mote on Dronification — no orange is hard-coded into the Play door.
                RabbitHoleFx.StartLayers(new AmbientFxConfig
                {
                    Layers = AmbientFxLayers.DustField,
                    Intensity = RabbitHoleFxIntensity,
                });

                // Park/resume for free from here on, and reachable from the motion kill-switch.
                mw.RegisterTabFx(TabKey, RabbitHoleFx);
            }
            catch (Exception ex) { App.Logger?.Debug("PlayTabView FX compose: {E}", ex.Message); }
        }
    }
}
