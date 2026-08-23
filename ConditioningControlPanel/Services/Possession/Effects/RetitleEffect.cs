using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Controls;

namespace ConditioningControlPanel.Services.Possession.Effects;

/// <summary>
/// "retitle" (Full Doki only) - the room stops pretending. A title stops naming a feature and starts
/// talking to you, and it stays that way until the lockdown ends. Bound titles are declined outright:
/// the binding would overwrite the line mid-sentence and read as a glitch instead of a message.
///
/// <para>MinRung moved R4 -> R3 (Collapse) in wave 2. The owner play-test verdict was that Full Doki
/// spent nine of its ten minutes indistinguishable from Eerie: R4 is the last 15 % of the timer, so on
/// a one-hour lockdown the single loudest thing the mode owns did not exist until minute 51, and on a
/// short one it often never fired at all. At Collapse it lands while there is still lockdown left to
/// live through, which is the point of opting in. It is still gated to Full Doki - Eerie caps at R3
/// but never reaches this effect, because MinIntensity, not MinRung, is what keeps a title talking
/// back out of the default experience.</para>
/// </summary>
public sealed class RetitleEffect : PossessionEffectBase
{
    private static readonly PossessionRole[] _roles = { PossessionRole.Title };

    private static readonly string[] _lines =
    {
        "still here?",
        "there is no exit",
        "i can see you reading this",
        "the timer is not the way out",
        "you already know how this ends",
    };

    private TextBlock? _tb;
    private string? _originalText;

    public override string Id => "retitle";
    public override PossessionRung MinRung => PossessionRung.Collapse;
    public override PossessionIntensity MinIntensity => PossessionIntensity.FullDoki;
    public override bool IsBig => true;
    public override double Weight => 3;
    /// <summary>Zero = it stays until the lockdown ends.</summary>
    public override TimeSpan HoldFor => TimeSpan.Zero;
    public override IReadOnlyList<PossessionRole> Roles => _roles;

    protected override bool CanApplyCore(PossessionContext ctx, PossessionTarget? target)
    {
        if (target?.Role == PossessionRole.Timer) return false;
        var tb = PossessionVisual.FindTextBlock(target?.Element);
        return PossessionVisual.IsRewritable(tb, 2);
    }

    protected override Task ApplyCoreAsync(PossessionContext ctx, PossessionTarget? target, CancellationToken ct)
    {
        _tb = PossessionVisual.FindTextBlock(target?.Element);
        if (!PossessionVisual.IsRewritable(_tb, 2)) return Task.CompletedTask;

        _originalText = _tb!.Text;
        var line = _lines[Rng.Next(_lines.Length)];
        if (string.Equals(line, _originalText, StringComparison.OrdinalIgnoreCase))
            line = _lines[(Array.IndexOf(_lines, line) + 1) % _lines.Length];

        _tb.Text = line;
        return Task.CompletedTask;
    }

    protected override Task UndoCoreAsync(TimeSpan duration)
    {
        try
        {
            if (_tb != null && _originalText != null) _tb.Text = _originalText;
        }
        catch (Exception ex) { App.Logger?.Warning("Possession retitle restore failed: {Error}", ex.Message); }
        _tb = null;
        _originalText = null;
        return Task.CompletedTask;
    }
}
