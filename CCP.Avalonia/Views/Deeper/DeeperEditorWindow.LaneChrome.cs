using System.Globalization;
using ConditioningControlPanel.Models.Deeper;
using static ConditioningControlPanel.Views.Deeper.DeeperEditorGeometry;

namespace ConditioningControlPanel.Avalonia.Views.Deeper
{
    // PORTED near-verbatim from ConditioningControlPanel/Views/Deeper/DeeperEditorWindow.LaneChrome.cs.
    // Pure geometry + counters, no WPF types, so the only edit is Visibility -> IsVisible (none
    // needed here) and the empty-string convention for a zero count, unchanged.
    //
    // Lane chrome bookkeeping: the per-lane item counts in the header column. The shapes still
    // render onto the single TimelineCanvas with the y-band layout below.
    public partial class DeeperEditorWindow
    {
        // Lane geometry (TimelineLane / LaneBand / LaneBandInset) lives in
        // DeeperEditorGeometry so rendering and hit-testing share one tested source of truth.

        private void RefreshLaneCounts()
        {
            try
            {
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

                if (TxtRegionsLaneCount != null)
                    TxtRegionsLaneCount.Text = regions == 0 ? "" : regions.ToString(CultureInfo.InvariantCulture);
                if (TxtEffectsLaneCount != null)
                    TxtEffectsLaneCount.Text = effects == 0 ? "" : effects.ToString(CultureInfo.InvariantCulture);
                if (TxtHapticsLaneCount != null)
                    TxtHapticsLaneCount.Text = haptics == 0 ? "" : haptics.ToString(CultureInfo.InvariantCulture);
            }
            catch { }
        }
    }
}
