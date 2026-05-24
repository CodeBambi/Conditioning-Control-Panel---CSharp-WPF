using System.Globalization;
using ConditioningControlPanel.Models.Deeper;

namespace ConditioningControlPanel.Views.Deeper
{
    // Mission 1 commit 4 — lane chrome bookkeeping.
    // Updates the per-lane item counts in the header column. The actual
    // shapes still render onto the single TimelineCanvas with the existing
    // y-band layout; a follow-up mission will split rendering into per-lane
    // canvases with independent collapse + resize.
    public partial class DeeperEditorWindow
    {
        private void RefreshLaneCounts()
        {
            try
            {
                int rules = _enhancement?.Rules?.Count ?? 0;
                int regions = _enhancement?.Regions?.Count ?? 0;
                int haptics = 0;
                if (_enhancement?.HapticTracks != null)
                {
                    foreach (var t in _enhancement.HapticTracks)
                        if (t?.Events != null) haptics += t.Events.Count;
                }
                int effects = 0;
                if (_enhancement?.TimelineItems != null)
                {
                    foreach (var ti in _enhancement.TimelineItems)
                        if (ti != null && ti.Kind == TimelineItemKind.Effect &&
                            ti.EffectType != EffectTypes.Haptic) effects++;
                }

                if (TxtRulesLaneCount != null)
                    TxtRulesLaneCount.Text = rules == 0 ? "" : rules.ToString(CultureInfo.InvariantCulture);
                if (TxtRegionsLaneCount != null)
                    TxtRegionsLaneCount.Text = regions == 0 ? "" : regions.ToString(CultureInfo.InvariantCulture);
                if (TxtHapticsLaneCount != null)
                    TxtHapticsLaneCount.Text = haptics == 0 ? "" : haptics.ToString(CultureInfo.InvariantCulture);
                if (TxtEffectsLaneCount != null)
                    TxtEffectsLaneCount.Text = effects == 0 ? "" : effects.ToString(CultureInfo.InvariantCulture);
            }
            catch { }
        }
    }
}
