// PORTED-IN-PART from ConditioningControlPanel/MainWindow/MainWindow.EnhancementsFx.cs (256 lines).
//
// All three of this file's effects are live; the hero ambient is here. EnhancementsTabView.axaml
// already carries the <fx:AmbientFxCanvas x:Name="SkillTreeFx"/> the WPF tab has, and nothing was
// starting it - the tree drew on bare background paint. EnsureEnhancementsFx composes it with the
// WPF tuning (DustField at 0.55) and registers it with RegisterTabFx, so it parks on the way out
// of the tab and resumes on the way back in. Called from EnsureTabFx (MainShellWindow.AmbientFx.cs)
// on the first ShowTab("enhancements"), which is the same laziness as WPF calling it from
// RefreshEnhancementsUI: a user who never opens the tab never pays for it.
//
// The canvas gates itself on the performance tier, window activation and its own visibility, so
// none of WPF's Activated/Deactivated/StateChanged subscriptions are needed for it - that whole
// funnel existed for the owned-node AnimationClock, which the tab now gates itself. See below.
//
// The two micro effects live on the tab that draws the nodes (EnhancementsTabView.axaml.cs):
// the owned-node breath (one 24fps clock for every owned glow, 0.38<->0.72 over 3.8s, also
// drifting the two CreateAnimatedSkillTreeBrush gradients) parks when the tab is hidden or the
// window is inactive/minimised and rests at the static glow under Off/Reduced or the Performance
// tier, as ApplyOwnedNodeBreath; the node hover pop (1.25, 250ms in / 200ms out, z-lift) is a
// TransformOperationsTransition, snapped when MotionLevel is Off, as ApplySkillNodeHover.
// Deviation: Avalonia's BackEaseOut has a fixed amplitude, WPF's pop used 0.4.

using System;
using Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Controls;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        /// <summary>Dust alpha multiplier, verbatim from WPF. Low: the tree art is the subject,
        /// this is the air.</summary>
        private const double SkillTreeFxIntensity = 0.55;

        private bool _enhancementsFxInitialized;

        /// <summary>
        /// Composes the skill tree's ambient dust, once, on the first arrival at the tab.
        /// </summary>
        private void EnsureEnhancementsFx()
        {
            if (_enhancementsFxInitialized) return;
            _enhancementsFxInitialized = true;
            try
            {
                // FindControl, never the generated field: this window loads with
                // AvaloniaXamlLoader.Load, so EnhancementsTab is permanently null (see the header
                // of MainShellWindow.TabNavigation.cs).
                var canvas = Named<Tabs.EnhancementsTabView>("EnhancementsTab")
                    ?.FindControl<AmbientFxCanvas>("SkillTreeFx");
                if (canvas == null) return;

                canvas.StartLayers(new AmbientFxConfig
                {
                    Layers = AmbientFxLayers.DustField,
                    Intensity = SkillTreeFxIntensity,
                });
                // ShowTab parks it on the way out and resumes it on the way in, for free.
                RegisterTabFx("enhancements", canvas);
            }
            catch (Exception ex) { Log.Warning(ex, "EnsureEnhancementsFx failed"); }
        }
    }
}
