// PORTED from ConditioningControlPanel/Services/Possession/Effects/RewriteEffect.cs and
// GlyphRotEffect.cs (7.1.5, wave A). Both only ever change what a label SAYS, through the overlay in
// PossessionEffectBase: the control's own text is never written. The WPF rewrite also takes a button's
// string content; buttons are not enrolled on this head, so only the TextBlock road is here.

using System;
using System.Collections.Generic;
using System.Text;
using Avalonia.Controls;
using Avalonia.Threading;

namespace ConditioningControlPanel.Services.Possession.Effects;

/// <summary>R1, the workhorse. A label briefly says something else, from the mod-voiced pools
/// (Core RewritePools). Big from Melt up, so the warden names it there.</summary>
internal sealed class RewriteEffect : PossessionEffectBase
{
    private static readonly PossessionRole[] _roles =
        { PossessionRole.Label, PossessionRole.Title, PossessionRole.TabHeader, PossessionRole.Button };

    public override string Id => "rewrite";
    public override PossessionRung MinRung => PossessionRung.Drift;
    public override PossessionIntensity MinIntensity => PossessionIntensity.Gentle;
    public override bool IsBig => (Ctx?.Rung ?? PossessionRung.Settle) >= PossessionRung.Melt;
    public override double Weight => 4;
    public override TimeSpan HoldFor => TimeSpan.FromSeconds(3);
    public override IReadOnlyList<PossessionRole> Roles => _roles;

    protected override bool CanApplyCore(PossessionContext ctx, PossessionTarget? target)
    {
        if (target?.Role == PossessionRole.Timer) return false;   // the timer VALUE is never touched
        var tb = PossessionTree.FindTextBlock(target?.Element);
        if (!PossessionTree.IsRewritable(tb, 2)) return false;
        return RewritePools.Rewrite(tb!.Text, RewritePools.ActiveModId, ctx.Rng) != null;
    }

    protected override void ApplyCore(PossessionContext ctx, PossessionTarget? target)
    {
        var tb = PossessionTree.FindTextBlock(target?.Element);
        if (!PossessionTree.IsRewritable(tb, 2)) return;
        var text = tb!.Text;
        var rewritten = RewritePools.Rewrite(text, RewritePools.ActiveModId, ctx.Rng);
        if (rewritten == null || string.Equals(rewritten, text, StringComparison.Ordinal)) return;
        Overlay(tb, TextBlock.TextProperty, rewritten);
    }
}

/// <summary>R2. One word rots into box glyphs and look-alikes a letter at a time (60 ms a step),
/// holds, then heals in reverse. A step is a text change, not a flash: photosafe only holds longer.</summary>
internal sealed class GlyphRotEffect : PossessionEffectBase
{
    private static readonly PossessionRole[] _roles = { PossessionRole.Label, PossessionRole.Title, PossessionRole.TabHeader };
    private static readonly char[] Boxes = { '▯', '▮', '□', '▪', '◻', '▉', '▒' };
    private static readonly Dictionary<char, char[]> LookAlikes = new()
    {
        ['a'] = new[] { 'а', '@' },
        ['e'] = new[] { 'е', '3' },
        ['o'] = new[] { 'о', '0' },
        ['i'] = new[] { 'і', '1' },
        ['s'] = new[] { 'ѕ', '5' },
        ['c'] = new[] { 'с' },
        ['n'] = new[] { 'п' },
        ['t'] = new[] { 'т', '7' },
        ['r'] = new[] { 'г' },
        ['y'] = new[] { 'у' },
        ['h'] = new[] { 'һ' },
        ['m'] = new[] { 'м' },
    };
    private static readonly char[] Marks = { '̀', '́', '̰', '̶' };
    internal const double StepMs = 60, RotHoldMs = 1200;

    private TextBlock? _tb;
    private string? _original;
    private string[]? _cells;
    private List<int>? _order;
    private IDisposable? _face;
    private int _step, _holdSteps;

    public override string Id => "glyphrot";
    public override PossessionRung MinRung => PossessionRung.Melt;
    public override PossessionIntensity MinIntensity => PossessionIntensity.Eerie;
    public override bool IsBig => false;
    public override double Weight => 2;
    public override TimeSpan HoldFor => TimeSpan.FromMilliseconds(3500);
    public override IReadOnlyList<PossessionRole> Roles => _roles;

    /// <summary>True once the word has rotted and healed (tests).</summary>
    internal bool IsHealed => _order != null && _step >= 2 * _order.Count + _holdSteps;

    protected override bool CanApplyCore(PossessionContext ctx, PossessionTarget? target)
    {
        if (target?.Role == PossessionRole.Timer) return false;
        var tb = PossessionTree.FindTextBlock(target?.Element);
        return PossessionTree.IsRewritable(tb, 3) && FindWord(tb!.Text!, ctx.Rng, out _, out _);
    }

    protected override void ApplyCore(PossessionContext ctx, PossessionTarget? target)
    {
        var tb = PossessionTree.FindTextBlock(target?.Element);
        if (!PossessionTree.IsRewritable(tb, 3)) return;
        var text = tb!.Text!;
        if (!FindWord(text, ctx.Rng, out int start, out int end)) return;
        _tb = tb;
        _original = text;
        _cells = new string[text.Length];
        for (int i = 0; i < text.Length; i++) _cells[i] = text[i].ToString();
        _order = new List<int>();
        for (int i = start; i < end; i++) _order.Add(i);
        for (int i = _order.Count - 1; i > 0; i--)
        {
            int j = ctx.Rng.Next(i + 1);
            (_order[i], _order[j]) = (_order[j], _order[i]);
        }
        _step = 0;
        _holdSteps = (int)Math.Ceiling((ctx.Photosafe ? RotHoldMs * 1.4 : RotHoldMs) / StepMs);
        Every(StepMs, Step);
    }

    /// <summary>One 60 ms beat: rot the next letter, hold, or heal the last one rotted.</summary>
    internal void Step()
    {
        if (!IsLive || _cells == null || _order == null || _original == null || IsHealed) return;
        int n = _order.Count;
        if (_step < n) _cells[_order[_step]] = Rot(_original[_order[_step]]);
        else if (_step >= n + _holdSteps)
        {
            int i = _order[n - 1 - (_step - n - _holdSteps)];
            _cells[i] = _original[i].ToString();
        }
        _step++;
        if (_step <= n || _step > n + _holdSteps) Paint();
    }

    private void Paint()
    {
        if (_tb == null || _cells == null) return;
        var sb = new StringBuilder(_cells.Length + 8);
        foreach (var c in _cells) sb.Append(c);
        var next = OverlayHandle(_tb, TextBlock.TextProperty, sb.ToString());
        Drop(_face);   // the new face is already on top: no frame shows the real text in between
        _face = next;
    }

    protected override void RestoreCore()
    {
        _tb = null;
        _original = null;
        _cells = null;
        _order = null;
        _face = null;
    }

    private string Rot(char c)
    {
        char baseChar = LookAlikes.TryGetValue(char.ToLowerInvariant(c), out var options) && Rng.Next(100) < 55
            ? options[Rng.Next(options.Length)]
            : Boxes[Rng.Next(Boxes.Length)];
        return Rng.Next(100) < 30 ? baseChar.ToString() + Marks[Rng.Next(Marks.Length)] : baseChar.ToString();
    }

    /// <summary>A word of three letters or more, picked at random.</summary>
    internal static bool FindWord(string text, Random rng, out int start, out int end)
    {
        start = end = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var words = new List<(int S, int E)>();
        int i = 0;
        while (i < text.Length)
        {
            while (i < text.Length && !char.IsLetter(text[i])) i++;
            int s = i;
            while (i < text.Length && char.IsLetter(text[i])) i++;
            if (i - s >= 3) words.Add((s, i));
        }
        if (words.Count == 0) return false;
        (start, end) = words[(rng ?? Random.Shared).Next(words.Count)];
        return true;
    }
}
