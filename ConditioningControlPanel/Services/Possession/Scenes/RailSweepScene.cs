using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using ConditioningControlPanel.Services.Possession.Effects;

namespace ConditioningControlPanel.Services.Possession.Scenes;

/// <summary>
/// "Rail sweep" - something walks along the nav rail. The ember charge lands on each door in turn,
/// left to right, and each one it touches sags a few pixels and loses a letter as it passes. The edge
/// pulses when the sweep reaches the end, and the warden names the rail once at the top.
///
/// <para>Why the doors: they are the one row of controls that is on screen on EVERY tab, which makes
/// this the scene that always has somewhere to happen. It also reads as DIRECTIONAL - the single
/// clearest way to say "this is a thing moving through the room" rather than "three controls glitched".</para>
/// </summary>
public sealed class RailSweepScene : PossessionSceneBase
{
    public override string Id => "scene_rail_sweep";
    public override PossessionRung MinRung => PossessionRung.Melt;
    public override int Beats => 3;

    private const int MaxDoors = 5;

    protected override async Task RunCoreAsync(PossessionContext ctx, IPossessionHost host,
                                               Func<PossessionRole, PossessionTarget?> pick, CancellationToken ct)
    {
        // Claim what we can, then order by screen position: the picker hands out whatever is free, and
        // a sweep that jumps around is just three glitches again.
        var doors = new List<PossessionTarget>();
        for (int i = 0; i < MaxDoors; i++)
        {
            var t = pick(PossessionRole.TabHeader);
            if (t?.Element == null) break;
            doors.Add(t);
        }
        if (doors.Count < 2)
        {
            App.Logger?.Debug("Possession scene {Id}: only {Count} doors free, skipping", Id, doors.Count);
            return;
        }

        doors.Sort((a, b) => CentreX(host, a.Element).CompareTo(CentreX(host, b.Element)));
        NameOnce("the doors");

        foreach (var door in doors)
        {
            if (ct.IsCancellationRequested) return;
            var el = door.Element;
            if (el == null || !el.IsVisible) continue;

            if (!await ChargeAsync(el, ct, 200).ConfigureAwait(true)) return;

            var lease = Lease(el);
            if (lease != null)
            {
                // Down and a touch back, like the door flinched away from whatever just brushed past.
                var dy = Amp(4.5);
                PossAnim.Pulse(lease.Translate, TranslateTransform.YProperty, dy * 1.4, 120, 220, 0, dy);
                PossAnim.To(lease.Rotate, RotateTransform.AngleProperty, Amp(1.2) * (Rng.Next(2) == 0 ? -1 : 1), 260, PossAnim.EaseOut);
            }
            TypoInto(el);

            if (!await Beat(Rng.Next(180, 300), ct).ConfigureAwait(true)) return;
        }

        EdgePulse(Photosafe ? 0.35 : 0.55);
        await Beat(900, ct).ConfigureAwait(true);
    }
}
