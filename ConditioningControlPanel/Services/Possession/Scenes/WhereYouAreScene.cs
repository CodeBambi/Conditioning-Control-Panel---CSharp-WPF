using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using ConditioningControlPanel.Services.Possession.Effects;

namespace ConditioningControlPanel.Services.Possession.Scenes;

/// <summary>
/// "Where you are" - the card under the cursor notices you. It breathes twice, then the breath does
/// not come back: it sags, leans, and settles a little wrong, and the warden names it.
///
/// <para>This is the scene that answers the owner's actual complaint. A haunt that fires on a control
/// at the other end of the window is a tree falling in an empty forest; this one only ever happens to
/// the thing the pointer is resting on, which is why the director hands it a proximity-filtered pick.</para>
///
/// <para>It sags in-scene rather than borrowing MeltEffect: melt is HOVER-DRIVEN (it arms a MouseEnter
/// handler and only bites when the pointer arrives - see Services/Dev/PossessionPreview.cs, which has
/// to fake the hover to photograph it). Inside a scene that would produce a beat that mostly does
/// nothing, and reusing the catalog's shared instance would fight the director's IsLive bookkeeping.
/// The sag below is the same visual grammar (skew + drop + lean), owned by the scene.</para>
/// </summary>
public sealed class WhereYouAreScene : PossessionSceneBase
{
    public override string Id => "scene_where_you_are";
    public override PossessionRung MinRung => PossessionRung.Melt;
    public override int Beats => 2;

    protected override async Task RunCoreAsync(PossessionContext ctx, IPossessionHost host,
                                               Func<PossessionRole, PossessionTarget?> pick, CancellationToken ct)
    {
        var card = pick(PossessionRole.Card) ?? pick(PossessionRole.Button);
        var el = card?.Element;
        if (el == null)
        {
            App.Logger?.Debug("Possession scene {Id}: nothing near the pointer is free, skipping", Id);
            return;
        }

        if (!await ChargeAsync(el, ct, 320).ConfigureAwait(true)) return;
        NameOnce(card!.DisplayName);

        var lease = Lease(el);
        if (lease == null) return;
        lease.SetOrigin(new Point(0.5, 1.0));   // hinge at the bottom edge, so it sags rather than slides

        // Beat 1-2: two breaths, so the sag lands as the thing STOPPING rather than as a random slump.
        var peak = 1.0 + Amp(0.035);
        PossAnim.To(lease.Scale, ScaleTransform.ScaleXProperty, peak, 700, PossAnim.Sine);
        PossAnim.To(lease.Scale, ScaleTransform.ScaleYProperty, peak, 700, PossAnim.Sine);
        if (!await Beat(700, ct).ConfigureAwait(true)) return;
        PossAnim.To(lease.Scale, ScaleTransform.ScaleXProperty, 1.0, 700, PossAnim.Sine);
        PossAnim.To(lease.Scale, ScaleTransform.ScaleYProperty, 1.0, 700, PossAnim.Sine);
        if (!await Beat(760, ct).ConfigureAwait(true)) return;

        // Beat 3: the sag. Slow, heavy easing - a card giving up, not a card being shoved.
        var lean = Amp(1.8) * (Rng.Next(2) == 0 ? -1 : 1);
        PossAnim.To(lease.Skew, SkewTransform.AngleXProperty, lean, 1100, PossAnim.EaseInOut);
        PossAnim.To(lease.Translate, TranslateTransform.YProperty, Amp(7), 1100, PossAnim.EaseInOut);
        PossAnim.To(lease.Scale, ScaleTransform.ScaleYProperty, 1.0 - Amp(0.03), 1100, PossAnim.EaseInOut);

        await Beat(1400, ct).ConfigureAwait(true);
    }
}
