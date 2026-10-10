// PORTED from ConditioningControlPanel/Services/Possession/Scenes/ (7.1.5): PossessionSceneBase (the
// part this head needs), TheCountScene, WhereYouAreScene. A scene is a haunt the director starts with
// no target (Core IPossessionScene, PossessionDirector.ElectScene): it takes free DISPLAY victims
// from the host registry, books them, plays a few timed beats and gives every one back on undo.
// Not here: RailSweepScene (its victims are the rail doors: buttons), the ember charge before each
// beat, and the pointer-proximity pick in "where you are" (any free card is taken instead).

using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using ConditioningControlPanel.Services.Possession.Effects;

namespace ConditioningControlPanel.Services.Possession.Scenes;

internal abstract class PossessionSceneBase : PossessionEffectBase, IPossessionScene
{
    private readonly List<PossessionTarget> _booked = new();

    public abstract int Beats { get; }
    public override PossessionRung MinRung => PossessionRung.Melt;
    public override PossessionIntensity MinIntensity => PossessionIntensity.Gentle;
    public override bool IsBig => true;      // a scene is named once by the warden
    public override double Weight => 1;

    /// <summary>The victims this scene holds right now (tests).</summary>
    internal IReadOnlyList<PossessionTarget> Booked => _booked;

    /// <summary>A free, visible display control of this role, or null. Booked when asked to.</summary>
    protected PossessionTarget? Pick(PossessionContext ctx, PossessionRole role, bool book)
    {
        if (!PossessionTree.IsDisplayRole(role)) return null;
        var free = new List<PossessionTarget>();
        foreach (var t in ctx.Host.Targets())
        {
            if (t == null || t.Role != role || t.IsLive || _booked.Contains(t)) continue;
            if (t.CooldownUntil > DateTime.Now) continue;
            if (t.Element is not Control c || !c.IsEffectivelyVisible || PossessionTree.IsOffLimits(c)) continue;
            free.Add(t);
        }
        if (free.Count == 0) return null;
        if (!book) return free[0];   // only asking: no roll is spent
        var pick = free[ctx.Rng.Next(free.Count)];
        pick.IsLive = true;
        _booked.Add(pick);
        return pick;
    }

    protected override bool SettleCore(double ms) => SettleLeases(ms);

    protected override void RestoreCore()
    {
        foreach (var t in _booked) t.IsLive = false;
        _booked.Clear();
    }
}

/// <summary>"The count": two labels slide off their marks in opposite directions, then the title
/// gains a typo and the edge pulses. Three beats.</summary>
internal sealed class TheCountScene : PossessionSceneBase
{
    public override string Id => "scene_the_count";
    public override int Beats => 3;
    public override TimeSpan HoldFor => TimeSpan.FromMilliseconds(3000);

    protected override bool CanApplyCore(PossessionContext ctx, PossessionTarget? target) =>
        target == null && (Pick(ctx, PossessionRole.Label, false) != null || Pick(ctx, PossessionRole.Title, false) != null);

    protected override void ApplyCore(PossessionContext ctx, PossessionTarget? target)
    {
        var labels = new List<PossessionTarget>();
        for (int i = 0; i < 2; i++)
            if (Pick(ctx, PossessionRole.Label, true) is { } t) labels.Add(t);
        var title = Pick(ctx, PossessionRole.Title, true);
        if (labels.Count == 0 && title == null) return;

        ctx.Name(Id, title?.DisplayName ?? (labels.Count > 0 ? labels[0].DisplayName : "the numbers"));
        double direction = ctx.Rng.Next(2) == 0 ? 1 : -1;
        double at = 220;
        foreach (var label in labels)
        {
            double dir = direction;
            At(at, () =>
            {
                if (label.Element is not Control c || !c.IsEffectivelyVisible || PossessionTree.IsOffLimits(c)) return;
                var lease = LeaseFor(c, RelativePoint.Center);
                Tween(lease.Translate, 900, false,
                    (0, TranslateTransform.XProperty, 0), (1, TranslateTransform.XProperty, Amp(7) * dir),
                    (0, TranslateTransform.YProperty, 0), (1, TranslateTransform.YProperty, Amp(3) * dir));
            });
            direction = -direction;
            at += 540;
        }
        At(at + 40, () =>
        {
            if (title?.Element is Control tc && !PossessionTree.IsOffLimits(tc)
                && PossessionTree.FindTextBlock(tc) is { } tb && PossessionTree.IsRewritable(tb, 3)
                && TypoEffect.Mutate(tb.Text!, ctx.Rng) is { } typo)
                Overlay(tb, TextBlock.TextProperty, typo);
            ctx.Host.EdgePulse(ctx.Photosafe ? 0.3 : 0.5);
        });
    }
}

/// <summary>"Where you are": a card swells once, like a breath taken, then leans and sags from its
/// bottom edge. Two beats. WPF takes the card nearest the pointer; this head takes any free one.</summary>
internal sealed class WhereYouAreScene : PossessionSceneBase
{
    public override string Id => "scene_where_you_are";
    public override int Beats => 2;
    public override TimeSpan HoldFor => TimeSpan.FromMilliseconds(3200);

    protected override bool CanApplyCore(PossessionContext ctx, PossessionTarget? target) =>
        target == null && Pick(ctx, PossessionRole.Card, false) != null;

    protected override void ApplyCore(PossessionContext ctx, PossessionTarget? target)
    {
        var card = Pick(ctx, PossessionRole.Card, true);
        if (card?.Element is not Control el) return;
        ctx.Name(Id, card.DisplayName);
        double peak = 1.0 + Amp(0.035);
        double lean = Amp(1.8) * (ctx.Rng.Next(2) == 0 ? -1 : 1);
        PossessionLease? lease = null;
        At(320, () =>
        {
            if (PossessionTree.IsOffLimits(el)) return;
            lease = LeaseFor(el, new RelativePoint(0.5, 1.0, RelativeUnit.Relative));
            Tween(lease.Scale, 1400, false,
                (0, ScaleTransform.ScaleXProperty, 1), (0.5, ScaleTransform.ScaleXProperty, peak), (1, ScaleTransform.ScaleXProperty, 1),
                (0, ScaleTransform.ScaleYProperty, 1), (0.5, ScaleTransform.ScaleYProperty, peak), (1, ScaleTransform.ScaleYProperty, 1));
        });
        At(320 + 1460, () =>
        {
            if (lease == null) return;
            StopTweens();
            Tween(lease.Skew, 1100, false, (0, SkewTransform.AngleXProperty, 0), (1, SkewTransform.AngleXProperty, lean));
            Tween(lease.Translate, 1100, false, (0, TranslateTransform.YProperty, 0), (1, TranslateTransform.YProperty, Amp(7)));
            Tween(lease.Scale, 1100, false,
                (0, ScaleTransform.ScaleXProperty, 1), (1, ScaleTransform.ScaleXProperty, 1),
                (0, ScaleTransform.ScaleYProperty, 1), (1, ScaleTransform.ScaleYProperty, 1.0 - Amp(0.03)));
        });
    }
}
