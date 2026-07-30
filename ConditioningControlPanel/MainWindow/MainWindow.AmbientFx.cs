using System;
using System.Collections.Generic;
using ConditioningControlPanel.Controls;

namespace ConditioningControlPanel
{
    // Ambient FX canvas registry. ShowTab() historically had to remember to stop each new ambient
    // loop by hand (it still only knows about three); every AmbientFxCanvas registered here gets
    // parked on the way out of its tab and resumed on the way in, for free.
    public partial class MainWindow
    {
        private readonly Dictionary<string, List<AmbientFxCanvas>> _tabFxCanvases =
            new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Registers a canvas against the tab key ShowTab uses. Call once, after the canvas exists.
        /// The canvas still self-parks on visibility/activation — this is the tab-level hook.
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
            catch (Exception ex) { App.Logger?.Debug("RegisterTabFx: {E}", ex.Message); }
        }

        /// <summary>Pauses every registered canvas that does not belong to the incoming tab, and
        /// resumes the ones that do. Must never throw — it runs inside ShowTab.</summary>
        private void SwitchTabFx(string tab)
        {
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
            catch (Exception ex) { App.Logger?.Debug("SwitchTabFx: {E}", ex.Message); }
        }
    }
}
