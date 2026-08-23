using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using ConditioningControlPanel.Services.Possession.Effects;

namespace ConditioningControlPanel.Services.Possession.Scenes;

/// <summary>
/// "The count" - the numbers stop agreeing with themselves. The version tag and the level readout
/// drift out of true, the title above them mis-spells itself, the window edge pulses once, and the
/// warden names the title.
///
/// <para>The version tag and the level label are the two pieces of chrome a returning user reads
/// without thinking, which is exactly why they are the ones worth making unreliable: the joke is not
/// that a label moved, it is that the app's own bookkeeping has stopped being trustworthy. It never
/// touches the lockdown timer's VALUE (POSSESSION.md hard rule) - those live under names the host's
/// auto-tagger refuses to enrol.</para>
/// </summary>
public sealed class TheCountScene : PossessionSceneBase
{
    public override string Id => "scene_the_count";
    public override PossessionRung MinRung => PossessionRung.Melt;
    public override int Beats => 3;

    protected override async Task RunCoreAsync(PossessionContext ctx, IPossessionHost host,
                                               Func<PossessionRole, PossessionTarget?> pick, CancellationToken ct)
    {
        var labels = new List<PossessionTarget>();
        for (int i = 0; i < 2; i++)
        {
            var t = pick(PossessionRole.Label);
            if (t?.Element != null) labels.Add(t);
        }
        var title = pick(PossessionRole.Title);

        if (labels.Count == 0 && title?.Element == null)
        {
            App.Logger?.Debug("Possession scene {Id}: no free chrome to miscount, skipping", Id);
            return;
        }

        NameOnce(title?.DisplayName ?? (labels.Count > 0 ? labels[0].DisplayName : "the numbers"));

        // Beats 1-2: the readouts drift apart. Opposite directions, so they visibly disagree.
        var direction = Rng.Next(2) == 0 ? 1 : -1;
        foreach (var label in labels)
        {
            if (ct.IsCancellationRequested) return;
            var el = label.Element;
            if (el == null || !el.IsVisible) continue;

            if (!await ChargeAsync(el, ct, 220).ConfigureAwait(true)) return;
            var lease = Lease(el);
            if (lease != null)
            {
                PossAnim.To(lease.Translate, TranslateTransform.XProperty, Amp(7) * direction, 900, PossAnim.EaseInOut);
                PossAnim.To(lease.Translate, TranslateTransform.YProperty, Amp(3) * direction, 900, PossAnim.EaseInOut);
            }
            direction = -direction;
            if (!await Beat(320, ct).ConfigureAwait(true)) return;
        }

        // Beat 3: the heading above them mis-spells itself, which is the beat that says the disagreement
        // is not arithmetic, it is the room.
        if (title?.Element != null)
        {
            if (!await ChargeAsync(title.Element, ct, 260).ConfigureAwait(true)) return;
            TypoInto(title.Element);
        }

        EdgePulse(Photosafe ? 0.3 : 0.5);
        await Beat(1600, ct).ConfigureAwait(true);
    }
}
