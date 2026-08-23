using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Controls;

namespace ConditioningControlPanel.Services.Possession.Effects;

/// <summary>
/// R0 "typo" - one letter in a label slips into a look-alike (o to 0, l to 1) or trades places with
/// its neighbour, for a couple of seconds, then it is spelled right again and you are left wondering.
/// Never touches bound text (the binding would snap it back and read as a bug) and never the timer.
/// </summary>
public sealed class TypoEffect : PossessionEffectBase
{
    private static readonly PossessionRole[] _roles =
        { PossessionRole.Label, PossessionRole.TabHeader, PossessionRole.Title };

    private static readonly (char From, char To)[] _lookAlikes =
    {
        ('o', '0'), ('O', '0'), ('l', '1'), ('I', '1'), ('e', '3'), ('E', '3'),
        ('a', '4'), ('A', '4'), ('s', '5'), ('S', '5'), ('t', '7'), ('g', '9'),
    };

    private TextBlock? _tb;
    private string? _originalText;

    public override string Id => "typo";
    public override PossessionRung MinRung => PossessionRung.Settle;
    public override PossessionIntensity MinIntensity => PossessionIntensity.Gentle;
    public override bool IsBig => false;
    public override double Weight => 3;
    // Wave 2: 2.5 s was shorter than the time it takes to look away from what you were doing, glance
    // at the label and read it. Four seconds is "long enough that you are sure you read it right".
    public override TimeSpan HoldFor => TimeSpan.FromSeconds(4);
    public override IReadOnlyList<PossessionRole> Roles => _roles;

    protected override bool CanApplyCore(PossessionContext ctx, PossessionTarget? target)
    {
        if (target?.Role == PossessionRole.Timer) return false;   // the timer VALUE is never touched
        var tb = PossessionVisual.FindTextBlock(target?.Element);
        return PossessionVisual.IsRewritable(tb, 3) && CanMutate(tb!.Text);
    }

    protected override Task ApplyCoreAsync(PossessionContext ctx, PossessionTarget? target, CancellationToken ct)
    {
        _tb = PossessionVisual.FindTextBlock(target?.Element);
        if (!PossessionVisual.IsRewritable(_tb, 3)) return Task.CompletedTask;

        _originalText = _tb!.Text;
        var typo = Mutate(_originalText);
        if (typo == null || string.Equals(typo, _originalText, StringComparison.Ordinal))
        {
            _originalText = null;
            return Task.CompletedTask;
        }

        _tb.Text = typo;
        return Task.CompletedTask;
    }

    protected override Task UndoCoreAsync(TimeSpan duration)
    {
        try
        {
            if (_tb != null && _originalText != null) _tb.Text = _originalText;
        }
        catch (Exception ex) { App.Logger?.Warning("Possession typo restore failed: {Error}", ex.Message); }
        _tb = null;
        _originalText = null;
        return Task.CompletedTask;
    }

    /// <summary>True when the string has at least one letter we could swap or two we could trade.</summary>
    private static bool CanMutate(string text)
    {
        if (string.IsNullOrEmpty(text) || text.Length < 3) return false;
        for (int i = 1; i < text.Length; i++)
        {
            foreach (var (from, _) in _lookAlikes) if (text[i] == from) return true;
            if (i + 1 < text.Length && char.IsLetter(text[i]) && char.IsLetter(text[i + 1]) && text[i] != text[i + 1])
                return true;
        }
        return false;
    }

    private string? Mutate(string text)
    {
        // Collect every legal single-character slip, then pick one. Index 0 is left alone so the word
        // still reads at a glance - the wrongness should land a beat late.
        var swaps = new List<(int Index, char To)>();
        var trades = new List<int>();
        for (int i = 1; i < text.Length; i++)
        {
            foreach (var (from, to) in _lookAlikes)
                if (text[i] == from) swaps.Add((i, to));
            if (i + 1 < text.Length && char.IsLetter(text[i]) && char.IsLetter(text[i + 1]) && text[i] != text[i + 1])
                trades.Add(i);
        }

        bool preferSwap = swaps.Count > 0 && (trades.Count == 0 || Rng.Next(100) < 65);
        var sb = new StringBuilder(text);
        if (preferSwap)
        {
            var pick = swaps[Rng.Next(swaps.Count)];
            sb[pick.Index] = pick.To;
            return sb.ToString();
        }
        if (trades.Count > 0)
        {
            int i = trades[Rng.Next(trades.Count)];
            (sb[i], sb[i + 1]) = (sb[i + 1], sb[i]);
            return sb.ToString();
        }
        return null;
    }
}
