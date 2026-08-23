using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace ConditioningControlPanel.Services.Possession.Effects;

/// <summary>
/// R2 "melt" - the card goes soft under the cursor. Hover and it sags (skew + squash + a couple of
/// pixels of slump, with a whisper of blur); leave and it firms back up. It re-melts every time you
/// come back for the length of the hold. The control never stops working while it is soft.
/// </summary>
public sealed class MeltEffect : PossessionEffectBase
{
    private static readonly PossessionRole[] _roles = { PossessionRole.Card, PossessionRole.Button };

    private const double MeltMs = 900;
    private const double FirmMs = 600;

    private FrameworkElement? _el;
    private MouseEventHandler? _enter;
    private MouseEventHandler? _leave;
    private BlurEffect? _blur;
    private Effect? _priorEffect;
    private bool _priorEffectWasLocal;
    private bool _blurInstalled;

    public override string Id => "melt";
    public override PossessionRung MinRung => PossessionRung.Melt;
    public override PossessionIntensity MinIntensity => PossessionIntensity.Gentle;
    public override bool IsBig => true;
    public override double Weight => 4;
    public override TimeSpan HoldFor => TimeSpan.FromSeconds(25);
    public override IReadOnlyList<PossessionRole> Roles => _roles;

    /// <summary>Charged on the first hover, so the ember lands exactly when the card starts to sag.</summary>
    protected override bool ChargeOnApply => false;

    protected override Task ApplyCoreAsync(PossessionContext ctx, PossessionTarget? target, CancellationToken ct)
    {
        _el = target?.Element;
        if (_el == null) return Task.CompletedTask;

        _enter = (_, __) => { _ = MeltAsync(); };
        _leave = (_, __) => { _ = FirmAsync(); };
        _el.MouseEnter += _enter;
        _el.MouseLeave += _leave;

        // Already under the cursor when the ghost arrives: start soft right away.
        try { if (_el.IsMouseOver) _ = MeltAsync(); } catch { }
        return Task.CompletedTask;
    }

    private async Task MeltAsync()
    {
        try
        {
            var el = _el;
            if (el == null) return;
            var ct = Cts?.Token ?? CancellationToken.None;

            await ChargeAndPossessAsync(el, ct).ConfigureAwait(true);
            if (ct.IsCancellationRequested) return;

            var lease = Lease ?? TakeLease(el);
            if (lease == null) return;
            lease.SetOrigin(new Point(0.5, 1.0));   // sag from the bottom edge, like it is losing its footing

            PossAnim.To(lease.Skew, SkewTransform.AngleYProperty, Amp(6), MeltMs, PossAnim.EaseOut);
            PossAnim.To(lease.Scale, ScaleTransform.ScaleYProperty, 1.0 - Amp(0.08), MeltMs, PossAnim.EaseOut);
            PossAnim.To(lease.Translate, TranslateTransform.YProperty, Amp(6), MeltMs, PossAnim.EaseOut);
            InstallBlur(el, Amp(2));
        }
        catch (Exception ex) { App.Logger?.Warning("Possession melt failed: {Error}", ex.Message); }
    }

    private async Task FirmAsync()
    {
        try
        {
            var lease = Lease;
            if (lease == null) return;
            await Task.Yield();

            PossAnim.To(lease.Skew, SkewTransform.AngleYProperty, 0, FirmMs, PossAnim.EaseInOut);
            PossAnim.To(lease.Scale, ScaleTransform.ScaleYProperty, 1.0, FirmMs, PossAnim.EaseInOut);
            PossAnim.To(lease.Translate, TranslateTransform.YProperty, 0, FirmMs, PossAnim.EaseInOut);
            if (_blur != null) PossAnim.To(_blur, BlurEffect.RadiusProperty, 0, FirmMs, PossAnim.EaseInOut);
        }
        catch (Exception ex) { App.Logger?.Warning("Possession melt firm failed: {Error}", ex.Message); }
    }

    /// <summary>Only ever blur a control that has no Effect of its own; the prior Effect is restored
    /// exactly (including "there was no local value at all").</summary>
    private void InstallBlur(FrameworkElement el, double radius)
    {
        try
        {
            if (_blur == null)
            {
                if (el.Effect != null) return;
                _priorEffectWasLocal = el.ReadLocalValue(UIElement.EffectProperty) != DependencyProperty.UnsetValue;
                _priorEffect = el.Effect;
                _blur = new BlurEffect { Radius = 0, KernelType = KernelType.Gaussian, RenderingBias = RenderingBias.Performance };
                el.Effect = _blur;
                _blurInstalled = true;
            }
            PossAnim.To(_blur, BlurEffect.RadiusProperty, radius, MeltMs, PossAnim.EaseOut);
        }
        catch (Exception ex) { App.Logger?.Warning("Possession melt blur failed: {Error}", ex.Message); }
    }

    protected override async Task UndoCoreAsync(TimeSpan duration)
    {
        var el = _el;
        try
        {
            if (el != null)
            {
                if (_enter != null) el.MouseEnter -= _enter;
                if (_leave != null) el.MouseLeave -= _leave;
            }
        }
        catch { }
        _enter = null;
        _leave = null;

        double ms = UndoMs(duration, 300, 900);
        var lease = ms > 0 ? Lease : null;
        if (lease != null)
        {
            PossAnim.To(lease.Skew, SkewTransform.AngleYProperty, 0, ms, PossAnim.EaseInOut);
            PossAnim.To(lease.Scale, ScaleTransform.ScaleYProperty, 1.0, ms, PossAnim.EaseInOut);
            PossAnim.To(lease.Translate, TranslateTransform.YProperty, 0, ms, PossAnim.EaseInOut);
        }
        if (_blur != null && ms > 0) PossAnim.To(_blur, BlurEffect.RadiusProperty, 0, ms, PossAnim.EaseInOut);
        if (ms > 0 && (lease != null || _blur != null))
            await PossAnim.DelayAsync(ms + 20, CancellationToken.None).ConfigureAwait(true);

        try
        {
            if (el != null && _blurInstalled)
            {
                if (ReferenceEquals(el.Effect, _blur))
                {
                    if (_priorEffectWasLocal) el.Effect = _priorEffect;
                    else el.ClearValue(UIElement.EffectProperty);
                }
            }
        }
        catch (Exception ex) { App.Logger?.Warning("Possession melt effect restore failed: {Error}", ex.Message); }

        _blur = null;
        _blurInstalled = false;
        _priorEffect = null;
        _priorEffectWasLocal = false;
        _el = null;
    }
}
