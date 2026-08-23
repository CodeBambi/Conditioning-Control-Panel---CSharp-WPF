using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;

namespace ConditioningControlPanel.Services.Possession.Effects;

// =====================================================================================================
//  POSSESSION - the shared spine of every effect. Read Services/Possession/POSSESSION.md first.
//
//  THE GRAMMAR (non-negotiable, "clarity in front"):
//      1. charge   - await ctx.Attribution.ChargeAsync(target.Element, ct)   BEFORE anything moves
//      2. name     - big effects call ctx.Name(Id, target name) exactly once per Apply
//      3. possess  - hold an ctx.Attribution.Possess(element) handle for as long as it stays moved
//      4. undo     - dispose the handle, put the control back EXACTLY as it was
//
//  This base runs 1-3 for the common case (charge on Apply, one target) and hands deferred-charge
//  effects (dodge, melt, dissolve fire on hover) the same sequence through ChargeAndPossessAsync().
//  UndoAsync is guarded: safe before Apply, twice, mid-cancellation, and after the window died.
// =====================================================================================================

public abstract class PossessionEffectBase : IPossessionEffect
{
    /// <summary>Possession's ember. Only the attribution layer paints it - EXCEPT debris (ash, tiles,
    /// fallen glyphs), which is tinted ember on purpose so the rubble is obviously Possession's.</summary>
    public static readonly Color EmberColor = Color.FromRgb(0xFF, 0x8A, 0x5C);
    public static readonly SolidColorBrush EmberBrush = Freeze(new SolidColorBrush(EmberColor));

    private static SolidColorBrush Freeze(SolidColorBrush b) { b.Freeze(); return b; }

    // ---- contract ------------------------------------------------------------------------------
    public abstract string Id { get; }
    public abstract PossessionRung MinRung { get; }
    public virtual PossessionIntensity MinIntensity => PossessionIntensity.Gentle;
    public virtual bool IsBig => false;
    public virtual bool UsesFlicker => false;
    public abstract double Weight { get; }
    public abstract TimeSpan HoldFor { get; }
    public virtual IReadOnlyList<PossessionRole> Roles => Array.Empty<PossessionRole>();
    public bool IsLive => _live;

    // ---- state ---------------------------------------------------------------------------------
    protected PossessionContext? Ctx;
    protected PossessionTarget? Target;
    protected FrameworkElement? Element => Target?.Element;
    protected CancellationTokenSource? Cts;
    protected TransformLease? Lease;

    private IDisposable? _handle;
    private readonly List<IDisposable> _extraHandles = new();
    private bool _live;
    private bool _started;
    private bool _undone = true;   // "nothing to undo" until Apply runs
    private bool _charged;
    private bool _named;

    /// <summary>Name the warden uses instead of the target's DisplayName (a swap names BOTH buttons).</summary>
    protected string? NameOverrideText;

    /// <summary>False for effects that charge lazily (on first hover / first dodge) or have no target.</summary>
    protected virtual bool ChargeOnApply => true;

    // ---- CanApply ------------------------------------------------------------------------------
    public bool CanApply(PossessionContext ctx, PossessionTarget? target)
    {
        try
        {
            if (_live) return false;
            if (ctx == null) return false;

            if (Roles.Count == 0)
            {
                // Targetless effect (crack, doki dialog): a target may still be handed in, we ignore it.
                return CanApplyCore(ctx, null);
            }

            if (target == null) return false;
            if (target.IsLive) return false;
            if (!Roles.Contains(target.Role)) return false;

            var el = target.Element;
            if (el == null) return false;
            if (!el.IsVisible) return false;
            if (el.ActualWidth <= 0 || el.ActualHeight <= 0) return false;

            return CanApplyCore(ctx, target);
        }
        catch (Exception ex)
        {
            App.Logger?.Warning("Possession {Id}.CanApply failed: {Error}", Id, ex.Message);
            return false;
        }
    }

    /// <summary>Per-effect extra checks (needs a partner, needs an unbound TextBlock, and so on).</summary>
    protected virtual bool CanApplyCore(PossessionContext ctx, PossessionTarget? target) => true;

    // ---- Apply ---------------------------------------------------------------------------------
    public async Task ApplyAsync(PossessionContext ctx, PossessionTarget? target, CancellationToken ct)
    {
        if (_live) return;

        _live = true;
        _started = true;
        _undone = false;
        _charged = false;
        _named = false;
        Ctx = ctx;
        Target = Roles.Count == 0 ? null : target;

        try { Cts?.Dispose(); } catch { }
        Cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var token = Cts.Token;

        try
        {
            if (Target != null) Target.IsLive = true;

            if (ChargeOnApply)
            {
                var el = Target?.Element ?? ctx.Host.Window?.Content as FrameworkElement;
                if (el != null) await ChargeAndPossessAsync(el, token).ConfigureAwait(true);
                else NameOnce();
            }

            if (token.IsCancellationRequested) return;
            await ApplyCoreAsync(ctx, Target, token).ConfigureAwait(true);
        }
        catch (OperationCanceledException) { /* undo is already running */ }
        catch (Exception ex)
        {
            App.Logger?.Warning("Possession {Id} failed to apply: {Error}", Id, ex.Message);
            try { await UndoAsync(TimeSpan.Zero).ConfigureAwait(true); } catch { }
        }
    }

    protected abstract Task ApplyCoreAsync(PossessionContext ctx, PossessionTarget? target, CancellationToken ct);

    /// <summary>The grammar in one call: ember charge, then the name (big effects only), then the
    /// possessed outline handle. Idempotent - deferred-charge effects call it on first interaction.</summary>
    protected async Task ChargeAndPossessAsync(FrameworkElement element, CancellationToken ct)
    {
        if (_charged || element == null) return;
        _charged = true;
        try
        {
            if (Ctx != null) await Ctx.Attribution.ChargeAsync(element, ct).ConfigureAwait(true);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { App.Logger?.Warning("Possession {Id} charge failed: {Error}", Id, ex.Message); }

        if (ct.IsCancellationRequested) return;
        NameOnce();
        TakeHandle(element);
    }

    /// <summary>Extra possessed outline (a second victim, e.g. the other half of a swap).</summary>
    protected void PossessAlso(FrameworkElement? element)
    {
        if (element == null || Ctx == null) return;
        try
        {
            var h = Ctx.Attribution.Possess(element);
            if (h != null) _extraHandles.Add(h);
        }
        catch (Exception ex) { App.Logger?.Warning("Possession {Id} extra possess failed: {Error}", Id, ex.Message); }
    }

    private void TakeHandle(FrameworkElement element)
    {
        if (_handle != null || Ctx == null) return;
        try { _handle = Ctx.Attribution.Possess(element); }
        catch (Exception ex) { App.Logger?.Warning("Possession {Id} possess failed: {Error}", Id, ex.Message); }
    }

    /// <summary>The warden names the big ones, once per Apply. Micro-tics stay silent.</summary>
    protected void NameOnce(string? overrideName = null)
    {
        if (_named || !IsBig || Ctx == null) return;
        _named = true;
        var name = overrideName ?? NameOverrideText;
        if (string.IsNullOrWhiteSpace(name)) name = string.IsNullOrWhiteSpace(Target?.DisplayName) ? null : Target!.DisplayName;
        try { Ctx.Name(Id, name); }
        catch (Exception ex) { App.Logger?.Warning("Possession {Id} name failed: {Error}", Id, ex.Message); }
    }

    // ---- Undo ----------------------------------------------------------------------------------
    public async Task UndoAsync(TimeSpan duration)
    {
        if (_undone) return;
        _undone = true;

        try { Cts?.Cancel(); } catch { }

        if (_started)
        {
            try { await UndoCoreAsync(duration).ConfigureAwait(true); }
            catch (Exception ex) { App.Logger?.Warning("Possession {Id} failed to undo: {Error}", Id, ex.Message); }
        }

        try { _handle?.Dispose(); } catch { }
        _handle = null;
        foreach (var h in _extraHandles) { try { h.Dispose(); } catch { } }
        _extraHandles.Clear();

        try
        {
            if (Lease != null)
            {
                if (duration <= TimeSpan.Zero) Lease.ReleaseImmediate();
                else Lease.Release(TimeSpan.Zero);   // UndoCore already animated us home
            }
        }
        catch { }
        Lease = null;

        if (Target != null) { try { Target.IsLive = false; } catch { } }
        Target = null;
        _started = false;
        _charged = false;
        _named = false;
        NameOverrideText = null;
        _live = false;

        try { Cts?.Dispose(); } catch { }
        Cts = null;
    }

    /// <summary>Put the control back EXACTLY. Must tolerate a half-finished Apply.</summary>
    protected abstract Task UndoCoreAsync(TimeSpan duration);

    // ---- shared helpers ------------------------------------------------------------------------

    /// <summary>
    /// How long an Undo may animate. TimeSpan.Zero means the caller is on the SYNCHRONOUS path
    /// (UndoAll on crash / dispose): no animation at all, the restore happens on the spot.
    /// </summary>
    protected static double UndoMs(TimeSpan duration, double min, double max)
        => duration <= TimeSpan.Zero ? 0 : Math.Clamp(duration.TotalMilliseconds, min, max);

    /// <summary>Photosafe halves every motion amplitude (and skips bounces / shakes at the call site).</summary>
    protected double Amp(double value) => (Ctx?.Photosafe == true) ? value * 0.5 : value;

    protected bool Photosafe => Ctx?.Photosafe == true;

    protected Random Rng => Ctx?.Rng ?? Random.Shared;

    /// <summary>Random double in [min, max).</summary>
    protected double Rand(double min, double max) => min + Rng.NextDouble() * (max - min);

    /// <summary>Coin flip, used for "which way does it lean".</summary>
    protected int Sign() => Rng.Next(2) == 0 ? -1 : 1;

    /// <summary>Take (or join) the transform lease for the current target.</summary>
    protected TransformLease? TakeLease(UIElement? el = null)
    {
        Lease = TransformLease.Take(el ?? Element);
        return Lease;
    }
}

/// <summary>Small visual-tree helpers the text effects lean on. Nothing here ever throws.</summary>
public static class PossessionVisual
{
    /// <summary>The element itself when it IS a TextBlock, else its first TextBlock descendant.</summary>
    public static TextBlock? FindTextBlock(DependencyObject? root, int depth = 0)
    {
        if (root == null || depth > 12) return null;
        try
        {
            if (root is TextBlock tb) return tb;
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                var found = FindTextBlock(VisualTreeHelper.GetChild(root, i), depth + 1);
                if (found != null) return found;
            }
        }
        catch { }
        return null;
    }

    /// <summary>
    /// A TextBlock we are allowed to rewrite: real text, not data-bound, not styled into a pile of
    /// inlines we would have to rebuild from scratch. Bound text snaps back the moment the source
    /// ticks, which would read as a bug rather than a haunt.
    /// </summary>
    public static bool IsRewritable(TextBlock? tb, int minLength = 3)
    {
        try
        {
            if (tb == null) return false;
            if (BindingOperations.GetBindingExpression(tb, TextBlock.TextProperty) != null) return false;
            if (BindingOperations.IsDataBound(tb, TextBlock.TextProperty)) return false;
            var text = tb.Text;
            if (string.IsNullOrWhiteSpace(text)) return false;
            if (text.Length < minLength) return false;
            if (tb.Inlines.Count > 1) return false;
            if (tb.Inlines.Count == 1 && tb.Inlines.FirstInline is not Run) return false;
            return true;
        }
        catch { return false; }
    }

    /// <summary>
    /// The element's rectangle in GhostLayer coordinates. MainWindow's content sits inside a Viewbox
    /// around a fixed design canvas while the ghost layer is a sibling OUTSIDE it, so ActualWidth is a
    /// design-space measurement and cannot be mixed with layer positions: this goes through the real
    /// transform chain instead.
    /// </summary>
    public static Rect BoundsOf(IPossessionHost host, FrameworkElement el)
        => Ghost.LayerBoundsOf(host, el);

    /// <summary>
    /// Layer units per design unit for this element, per axis. Divide by these before handing any
    /// layer-space distance to the element's own TransformLease (which is design space); multiply by
    /// them to take a design-space measurement (a FormattedText width, say) into the ghost layer.
    /// </summary>
    public static (double X, double Y) ScaleOf(IPossessionHost host, FrameworkElement el)
        => Ghost.LayerScaleOf(host, el);

    /// <summary>Never dodge / fall the window's real close or minimize chrome (POSSESSION.md hard rule).</summary>
    public static bool IsWindowChrome(FrameworkElement? el)
    {
        try
        {
            var name = el?.Name;
            if (string.IsNullOrEmpty(name)) return false;
            var n = name!.ToLowerInvariant();
            return n.Contains("close") || n.Contains("minimi") || n.Contains("maximi") || n.Contains("titlebar");
        }
        catch { return false; }
    }
}
