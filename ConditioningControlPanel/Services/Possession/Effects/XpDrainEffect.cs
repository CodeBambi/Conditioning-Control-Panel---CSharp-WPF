using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel.Services.Possession.Effects;

/// <summary>
/// R2 "xpdrain" - the XP bar empties and the level lies. A ghost fill the exact size, shape and colour
/// of the real bar is parked in the GhostLayer, the real bar fades to Opacity 0 underneath it, and the
/// ghost drains to nothing over 1.5 s while the level chip reads zero. After the hold the ghost refills
/// to the width it had, the real bar comes back and the chip says what it said before.
///
/// <para><b>The value is never touched.</b> ProgressionService, the profile, the server echo and the
/// XP the user actually has know nothing about this: it is a picture of a bar draining, painted over a
/// bar that is still full. Same rule as the lockdown timer - the digits may wobble, the number stays
/// true (POSSESSION.md).</para>
///
/// <para>The chip's text is put back only if it is still OURS. A real level-up during the six-second
/// hold would rewrite it, and restoring "the string we found" would then quietly undo the user's own
/// progress on screen - the one restore that would be a bug rather than a haunt.</para>
/// </summary>
public sealed class XpDrainEffect : PossessionEffectBase
{
    private static readonly PossessionRole[] _roles = { PossessionRole.Progress, PossessionRole.Label };

    private const double DrainMs = 1500;
    private const double FadeMs = 220;

    /// <summary>The fill Border in MainWindow.xaml. Named rather than inferred: the bar is a Border
    /// inside a track Border (not a ProgressBar), so there is nothing about its type to recognise.</summary>
    private const string BarName = "XPBar";
    private const string LabelName = "TxtLevelLabel";

    private FrameworkElement? _bar;
    private TextBlock? _chip;

    private Border? _ghostFill;
    private double _ghostWidth;
    private double _barOpacity = 1;
    private bool _barFaded;

    private string? _chipOriginal;
    private string? _chipFake;

    public override string Id => "xpdrain";
    public override PossessionRung MinRung => PossessionRung.Melt;
    public override PossessionIntensity MinIntensity => PossessionIntensity.Eerie;
    public override bool IsBig => true;
    public override double Weight => 3;
    public override TimeSpan HoldFor => TimeSpan.FromSeconds(6);
    public override IReadOnlyList<PossessionRole> Roles => _roles;

    /// <summary>The deck may hand us either half of the pair, but the thing that MISBEHAVES is always
    /// the bar - so the ember lands there, not on whatever label happened to win the roll.</summary>
    protected override bool ChargeOnApply => false;

    protected override bool CanApplyCore(PossessionContext ctx, PossessionTarget? target)
    {
        // The warden names the BAR, whichever half of the pair the deck happened to hand us.
        NameOverrideText = "the XP bar";

        var bar = ResolveBar(ctx, target);
        if (bar == null || !bar.IsVisible) return false;

        var bounds = PossessionVisual.BoundsOf(ctx.Host, bar);
        return !bounds.IsEmpty && bounds.Width >= 6 && bounds.Height >= 2;
    }

    protected override async Task ApplyCoreAsync(PossessionContext ctx, PossessionTarget? target, CancellationToken ct)
    {
        _bar = ResolveBar(ctx, target);
        _chip = ResolveChip(ctx, target);
        if (_bar == null) return;

        await ChargeAndPossessAsync(_bar, ct).ConfigureAwait(true);
        if (ct.IsCancellationRequested) return;
        if (_chip != null && !ReferenceEquals(_chip, _bar)) PossessAlso(_chip);

        var layer = ctx.Host.GhostLayer;
        var bounds = PossessionVisual.BoundsOf(ctx.Host, _bar);
        if (layer == null || bounds.IsEmpty || bounds.Width <= 0) return;

        // The ghost is the bar's own brush at the bar's own size, so frame zero of the drain is
        // pixel-identical to the fill it replaces. A snapshot would work too, but a solid pill
        // stretched to a new width resamples badly and this one is going to change width a lot.
        _ghostWidth = bounds.Width;
        _ghostFill = new Border
        {
            Width = _ghostWidth,
            Height = bounds.Height,
            CornerRadius = (_bar as Border)?.CornerRadius ?? new CornerRadius(4),
            Background = (_bar as Border)?.Background ?? EmberBrush,
            IsHitTestVisible = false,
            SnapsToDevicePixels = true,
        };
        Canvas.SetLeft(_ghostFill, bounds.X);
        Canvas.SetTop(_ghostFill, bounds.Y);
        layer.Children.Add(_ghostFill);

        // Hand the bar over to its ghost. Opacity only: nothing about the real element's layout,
        // hit-testing or bound Width changes, so there is nothing to get wrong on the way back.
        _barOpacity = _bar.Opacity;
        _barFaded = true;
        PossAnim.To(_bar, UIElement.OpacityProperty, 0, FadeMs, PossAnim.EaseOut);

        // The lie: the chip reads zero for the length of the hold.
        LieAboutTheLevel();

        PossAnim.To(_ghostFill, FrameworkElement.WidthProperty, 0, DrainMs, PossAnim.EaseInOut);
        await PossAnim.DelayAsync(DrainMs + 30, ct).ConfigureAwait(true);
    }

    protected override async Task UndoCoreAsync(TimeSpan duration)
    {
        double ms = UndoMs(duration, 260, 700);

        try
        {
            if (_ghostFill != null && ms > 0)
            {
                PossAnim.To(_ghostFill, FrameworkElement.WidthProperty, _ghostWidth, ms, PossAnim.EaseOut);
                await PossAnim.DelayAsync(ms + 30, CancellationToken.None).ConfigureAwait(true);
            }
        }
        catch (Exception ex) { App.Logger?.Warning("Possession xpdrain refill failed: {Error}", ex.Message); }

        try
        {
            if (_bar != null && _barFaded)
            {
                PossAnim.Settle(_bar, UIElement.OpacityProperty, _barOpacity);
                _bar.Opacity = _barOpacity;
            }
        }
        catch (Exception ex) { App.Logger?.Warning("Possession xpdrain bar restore failed: {Error}", ex.Message); }

        try
        {
            var layer = Ctx?.Host.GhostLayer;
            if (_ghostFill != null)
            {
                PossAnim.Settle(_ghostFill, FrameworkElement.WidthProperty, _ghostWidth);
                layer?.Children.Remove(_ghostFill);
            }
        }
        catch { }

        RestoreTheLevel();

        _ghostFill = null;
        _bar = null;
        _chip = null;
        _barFaded = false;
        _chipOriginal = null;
        _chipFake = null;
    }

    // ---- the level chip ----------------------------------------------------------------------

    private void LieAboutTheLevel()
    {
        try
        {
            var chip = _chip;
            if (chip == null || !PossessionVisual.IsRewritable(chip, 2)) return;

            _chipOriginal = chip.Text;
            _chipFake = Loc.Get("possession_level_zero");
            if (string.IsNullOrWhiteSpace(_chipFake) || _chipFake == "possession_level_zero") _chipFake = "LVL 0";
            chip.Text = _chipFake;
        }
        catch (Exception ex)
        {
            App.Logger?.Warning("Possession xpdrain level lie failed: {Error}", ex.Message);
            _chipOriginal = null;
        }
    }

    private void RestoreTheLevel()
    {
        try
        {
            var chip = _chip;
            if (chip == null || _chipOriginal == null) return;
            // Only if the chip still says what WE made it say. A real level-up during the hold owns
            // the label now, and stamping the old number back over it would be the one restore that
            // actually loses something.
            if (_chipFake != null && !string.Equals(chip.Text, _chipFake, StringComparison.Ordinal)) return;
            chip.Text = _chipOriginal;
        }
        catch (Exception ex) { App.Logger?.Warning("Possession xpdrain level restore failed: {Error}", ex.Message); }
    }

    // ---- finding the pair --------------------------------------------------------------------

    /// <summary>The fill: the Progress-role victim when the deck handed us one, else the window's own
    /// XP bar by name (the Label half of this effect's roles is the level chip, not the bar).</summary>
    private static FrameworkElement? ResolveBar(PossessionContext ctx, PossessionTarget? target)
    {
        try
        {
            if (target?.Role == PossessionRole.Progress && target.Element != null) return target.Element;
            return ctx.Host.Window?.FindName(BarName) as FrameworkElement;
        }
        catch { return null; }
    }

    private static TextBlock? ResolveChip(PossessionContext ctx, PossessionTarget? target)
    {
        try
        {
            // A Label victim is only OUR label when it is the level chip: the deck tags every label in
            // the room, and rewriting the version tag to read "LVL 0" is a different (and much worse)
            // effect than lying about the level.
            if (target?.Role == PossessionRole.Label
                && string.Equals(target.Element?.Name, LabelName, StringComparison.Ordinal))
            {
                var tb = PossessionVisual.FindTextBlock(target.Element);
                if (tb != null) return tb;
            }
            return ctx.Host.Window?.FindName(LabelName) as TextBlock;
        }
        catch { return null; }
    }
}
