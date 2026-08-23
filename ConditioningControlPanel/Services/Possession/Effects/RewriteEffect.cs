using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace ConditioningControlPanel.Services.Possession.Effects;

/// <summary>
/// R1 "rewrite" - the label briefly says something else. Home becomes hole, Start becomes stay, the
/// level readout stops being yours. Where the pools (see <see cref="RewritePools"/>) have nothing to
/// say, the words stay put and only the ownership changes ("Sessions (mine)").
///
/// <para>Three seconds, then the EXACT original string comes back - same object for a Button's
/// Content, same string for a TextBlock - so nothing downstream can tell it was ever holding
/// something else. Bound text is refused outright (see PossessionVisual.IsRewritable): a binding
/// would snap it back on its own and read as a bug rather than a haunt.</para>
/// </summary>
public sealed class RewriteEffect : PossessionEffectBase
{
    private static readonly PossessionRole[] _roles =
        { PossessionRole.Label, PossessionRole.Title, PossessionRole.TabHeader, PossessionRole.Button };

    private TextBlock? _tb;
    private string? _originalText;

    private ContentControl? _cc;
    private object? _originalContent;

    public override string Id => "rewrite";
    public override PossessionRung MinRung => PossessionRung.Drift;
    public override PossessionIntensity MinIntensity => PossessionIntensity.Gentle;

    /// <summary>Named from R2 up. At R1 a word quietly changing its mind is a micro-tic: charge and
    /// tint carry the attribution, the warden stays quiet.</summary>
    public override bool IsBig => (Ctx?.Rung ?? PossessionRung.Settle) >= PossessionRung.Melt;

    public override double Weight => 4;
    public override TimeSpan HoldFor => TimeSpan.FromSeconds(3);
    public override IReadOnlyList<PossessionRole> Roles => _roles;

    protected override bool CanApplyCore(PossessionContext ctx, PossessionTarget? target)
    {
        if (target?.Role == PossessionRole.Timer) return false;   // the timer VALUE is never touched
        var el = target?.Element;
        if (el == null) return false;

        // A Button carrying a plain string is rewritten through Content (its ContentPresenter has no
        // TextBlock we may keep a handle on across a content change).
        if (StringContentOf(el) is string s)
            return RewritePools.Rewrite(s, RewritePools.ActiveModId, ctx.Rng) != null;

        var tb = PossessionVisual.FindTextBlock(el);
        if (!PossessionVisual.IsRewritable(tb, 2)) return false;
        return RewritePools.Rewrite(tb!.Text, RewritePools.ActiveModId, ctx.Rng) != null;
    }

    protected override Task ApplyCoreAsync(PossessionContext ctx, PossessionTarget? target, CancellationToken ct)
    {
        var el = target?.Element;
        if (el == null) return Task.CompletedTask;

        try
        {
            if (el is ContentControl cc && cc.Content is string s)
            {
                var line = RewritePools.Rewrite(s, RewritePools.ActiveModId, ctx.Rng);
                if (line == null || string.Equals(line, s, StringComparison.Ordinal)) return Task.CompletedTask;
                _cc = cc;
                _originalContent = cc.Content;   // the exact object, not a copy of the string
                cc.Content = line;
                return Task.CompletedTask;
            }

            _tb = PossessionVisual.FindTextBlock(el);
            if (!PossessionVisual.IsRewritable(_tb, 2)) { _tb = null; return Task.CompletedTask; }

            var text = _tb!.Text;
            var rewritten = RewritePools.Rewrite(text, RewritePools.ActiveModId, ctx.Rng);
            if (rewritten == null || string.Equals(rewritten, text, StringComparison.Ordinal))
            {
                _tb = null;
                return Task.CompletedTask;
            }

            _originalText = text;
            _tb.Text = rewritten;
        }
        catch (Exception ex) { App.Logger?.Warning("Possession rewrite failed: {Error}", ex.Message); }

        return Task.CompletedTask;
    }

    protected override Task UndoCoreAsync(TimeSpan duration)
    {
        try
        {
            if (_cc != null && _originalContent != null) _cc.Content = _originalContent;
        }
        catch (Exception ex) { App.Logger?.Warning("Possession rewrite content restore failed: {Error}", ex.Message); }

        try
        {
            if (_tb != null && _originalText != null) _tb.Text = _originalText;
        }
        catch (Exception ex) { App.Logger?.Warning("Possession rewrite text restore failed: {Error}", ex.Message); }

        _cc = null;
        _originalContent = null;
        _tb = null;
        _originalText = null;
        return Task.CompletedTask;
    }

    /// <summary>The element's Content when it is a plain string worth rewriting, else null.</summary>
    internal static string? StringContentOf(FrameworkElement? el)
    {
        try
        {
            if (el is ContentControl cc && cc.Content is string s && !string.IsNullOrWhiteSpace(s)) return s;
        }
        catch { }
        return null;
    }
}
