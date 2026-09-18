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
    /// same handler object the old button ran — <c>BtnGazeMinigame_Click</c>,
    /// <c>ChkFocusGaze_Changed</c>, <c>BtnStartBureau_Click</c>, <c>BtnStartIntake_Click</c>, and
    /// <c>ShowTab</c> for everything that is a page rather than a window. Nothing here
    /// re-implements a launch, and nothing here decides a tier: the lockbands are decoration and
    /// <c>TierGate</c> does the refusing inside the handler.</para>
    ///
    /// <para><b>No ambient loop.</b> The Rabbit Hole hero carried this surface's one focal
    /// canvas until 2026-09-18, when the games moved to the CC Labs launcher; the wall has no
    /// registered canvas now, and <c>SwitchTabFx</c> simply finds nothing under "play".</para>
    /// </summary>
    public partial class PlayTabView : UserControl
    {
        public PlayTabView()
        {
            InitializeComponent();

            // Nothing composed here since the games left the wall (2026-09-18): the only
            // ambient canvas this view ever owned sat behind the Rabbit Hole hero.
        }
    }
}
