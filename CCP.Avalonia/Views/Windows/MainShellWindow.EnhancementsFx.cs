// PORTED-IN-PART from ConditioningControlPanel/MainWindow/MainWindow.EnhancementsFx.cs (256 lines).
//
// ONE of this file's three effects is live here: the hero ambient. EnhancementsTabView.axaml
// already carries the <fx:AmbientFxCanvas x:Name="SkillTreeFx"/> the WPF tab has, and nothing was
// starting it - the tree drew on bare background paint. EnsureEnhancementsFx composes it with the
// WPF tuning (DustField at 0.55) and registers it with RegisterTabFx, so it parks on the way out
// of the tab and resumes on the way back in. Called from EnsureTabFx (MainShellWindow.AmbientFx.cs)
// on the first ShowTab("enhancements"), which is the same laziness as WPF calling it from
// RefreshEnhancementsUI: a user who never opens the tab never pays for it.
//
// The canvas gates itself on the performance tier, window activation and its own visibility, so
// none of WPF's Activated/Deactivated/StateChanged subscriptions are needed for it - that whole
// funnel existed for the owned-node AnimationClock, which is not here. See below.
//
// The two micro effects stay notes, because both attach to nodes that do not exist on this head:
//
//   * the owned-node breath (a single AnimationClock shared by every owned node's
//     DropShadowEffect, 0.38<->0.72 over 3.8s). Its enrolment point is RegisterOwnedNodeGlow,
//     called from MainWindow.Enhancements.cs's DrawSkillTree - and the tree on this head is a
//     SAMPLE drawn by EnhancementsTabView itself (it needs Services/SkillTreeService.cs and
//     Models/SkillTree.cs, both still in the WPF head). There is no owned node to enrol. When the
//     tree becomes real, the Avalonia twin is one Animation with IterationCount.Infinite and
//     PlaybackDirection.Alternate over DropShadowEffect.OpacityProperty, plus the window
//     Activated/Deactivated/PropertyChanged(WindowState) hooks WPF used to park it.
//   * the node hover pop (1.25 over 250ms in, 200ms out). Lives on the node visual and belongs to
//     whoever draws it, i.e. EnhancementsTabView, which this layer does not own.
//
// Also not here: EnhancementsAmbientAllowed / EnhancementsFxOnScreen. Both are re-expressed
// inside AmbientFxCanvas.Evaluate() for the one loop that survived; a second copy of the gate
// with no second consumer would be a lie about what it gates.
//
// Members of the WPF file still dropped (12):
//   private const double OwnedNodeGlowMinOpacity / MaxOpacity / GlowSeconds
//   private const double SkillNodeHoverScale, private const int SkillNodeHoverInMs / OutMs
//   private readonly List<DropShadowEffect> _ownedNodeGlows
//   private AnimationClock? _ownedNodeGlowClock
//   private void ApplyEnhancementsFxLoops / ResetOwnedNodeGlows / RegisterOwnedNodeGlow /
//   ApplyOwnedNodeBreath / StopOwnedNodeGlowClock / ApplySkillNodeHover

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
