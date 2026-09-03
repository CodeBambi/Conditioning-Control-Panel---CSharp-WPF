// PORTED from ConditioningControlPanel/MainWindow/MainWindow.AmbientFx.cs (57 lines).
//
// The registry is REAL here, not a stub: CCP.Avalonia/Controls/AmbientFxCanvas.cs is a full port
// with the same Pause()/Resume() contract, so the tab-level park/resume hook the WPF window uses
// works on this head unchanged. SwitchTabFx is called from ShowTab.
//
// THE HOOK NOW HAS CALLERS. WPF initialises each tab's FX from that tab's own IsVisibleChanged
// handler; the tab views on this head do not raise one into the shell (several load with
// AvaloniaXamlLoader and would need a second wiring pass each), so the lazy-init dispatch lives
// HERE, at the top of SwitchTabFx - the one place every tab arrival already funnels through.
// Same semantics as WPF: a tab a user never opens costs nothing, and each tab's FX is composed
// exactly once, by the partial that owns it.
//
// What is NOT here: AmbientFrameRate. It is a storyboard frame-rate cap
// (System.Windows.Media.Animation.Timeline.DesiredFrameRate) and this head has no storyboards -
// AmbientFxCanvas drives its own clock and gates it on reduced motion, the Performance tier and
// window activation itself (see its Evaluate()). A constant nothing reads would be a lie about a
// knob that does not exist here.
//
// Registered by this batch: "enhancements" (MainShellWindow.EnhancementsFx.cs) and
// "availablesubjects" (MainShellWindow.SubjectsFx.cs). "settings" (MosaicFx), "programs", "play"
// and "exclusives" belong to files this layer does not own and are still unregistered.

using System;
using System.Collections.Generic;
using ConditioningControlPanel.Avalonia.Controls;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        private readonly Dictionary<string, List<AmbientFxCanvas>> _tabFxCanvases =
            new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Registers a canvas against the tab key ShowTab uses. Call once, after the canvas exists.
        /// The canvas still self-parks on visibility/activation - this is the tab-level hook.
        /// </summary>
        internal void RegisterTabFx(string tab, AmbientFxCanvas? canvas)
        {
            if (string.IsNullOrEmpty(tab) || canvas == null) return;
            try
            {
                if (!_tabFxCanvases.TryGetValue(tab, out var list))
                    _tabFxCanvases[tab] = list = new List<AmbientFxCanvas>();
                if (!list.Contains(canvas)) list.Add(canvas);
            }
            catch (Exception ex) { Log.Debug("RegisterTabFx: {E}", ex.Message); }
        }

        /// <summary>
        /// Composes the incoming tab's FX the first time that tab is shown. Each case is one call
        /// into the tab's own FX partial, which owns its guard flag - a dispatch table, not a
        /// second registry. Wrapped as a whole: an entrance effect must never cost the navigation
        /// that asked for it.
        /// </summary>
        private void EnsureTabFx(string tab)
        {
            try
            {
                switch (tab)
                {
                    case "enhancements": EnsureEnhancementsFx(); break;
                    case "availablesubjects": EnsureSubjectsFx(); break;
                    case "deeper": EnsureDeeperFx(); break;
                }
            }
            catch (Exception ex) { Log.Debug("EnsureTabFx({T}): {E}", tab, ex.Message); }
        }

        /// <summary>Pauses every registered canvas that does not belong to the incoming tab, and
        /// resumes the ones that do. Must never throw - it runs inside ShowTab.</summary>
        private void SwitchTabFx(string tab)
        {
            // Both of these run BEFORE the early return below: the first visit to a tab is the
            // visit that registers its canvas, and it has to be resumed on that same pass. The
            // Deeper drift is not a canvas at all and is not in the registry.
            EnsureTabFx(tab);
            ApplyDeeperGlyphDrift(tab);

            if (_tabFxCanvases.Count == 0) return;
            try
            {
                foreach (var kvp in _tabFxCanvases)
                {
                    bool incoming = string.Equals(kvp.Key, tab, StringComparison.OrdinalIgnoreCase);
                    foreach (var canvas in kvp.Value)
                    {
                        if (incoming) canvas.Resume();
                        else canvas.Pause();
                    }
                }
            }
            catch (Exception ex) { Log.Debug("SwitchTabFx: {E}", ex.Message); }
        }
    }
}
