using System.Globalization;
using ConditioningControlPanel.Models.Deeper;
using static ConditioningControlPanel.Views.Deeper.DeeperEditorGeometry;

namespace ConditioningControlPanel.Views.Deeper
{
    // Mission 1 commit 4 — lane chrome bookkeeping.
    // Updates the per-lane item counts in the header column. The actual
    // shapes still render onto the single TimelineCanvas with the existing
    // y-band layout; a follow-up mission will split rendering into per-lane
    // canvases with independent collapse + resize.
    public partial class DeeperEditorWindow
    {
        // Lane geometry (TimelineLane / LaneBand / LaneBandInset) lives in
        // DeeperEditorGeometry so it can be unit-tested without a Window.

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
