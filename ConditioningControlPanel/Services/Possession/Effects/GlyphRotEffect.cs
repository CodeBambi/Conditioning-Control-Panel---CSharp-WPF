using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Controls;

namespace ConditioningControlPanel.Services.Possession.Effects;

/// <summary>
/// R2 "glyphrot" - one word goes bad. Its letters are replaced one at a time, 60 ms apart, with box
/// glyphs, look-alikes and combining marks, it sits there rotted for a beat, and then it heals letter
/// by letter in the same order.
///
/// <para>INVARIANT: the rot is the same CHARACTER COUNT as the word it ate, and every substitute is a
/// single base character (a combining mark rides on top of one, it does not take a slot of its own),
/// so the line never re-flows, never wraps and never pushes its neighbours around. That matters more
/// than the exact glyph: a label that jumps reads as a layout bug, and a layout bug is the one thing
/// the haunt may never look like.</para>
///
/// <para>No flicker of any kind, so it is photosafe as it stands (UsesFlicker false). Bound text is
/// refused: a binding tick would heal the word early and read as a glitch.</para>
/// </summary>
public sealed class GlyphRotEffect : PossessionEffectBase
{
    private static readonly PossessionRole[] _roles =
        { PossessionRole.Label, PossessionRole.Title, PossessionRole.TabHeader };

    /// <summary>Same-width stand-ins: a box where a letter used to be reads as rot, not as a typo.</summary>
    private static readonly char[] Boxes = { '▯', '▮', '□', '▪', '◻', '▉', '▒' };

    /// <summary>Look-alikes, so some of the word survives just enough to be unsettling.</summary>
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

    /// <summary>Combining marks compose onto the character in front of them, so they add rot without
    /// adding width. Kept to four so the line never turns into soup.</summary>
    private static readonly char[] Marks = { '̀', '́', '̰', '̶' };

    private const double StepMs = 60;
    private const double RotHoldMs = 1200;

    private TextBlock? _tb;
    private string? _originalText;
    /// <summary>The LATEST string we painted. This effect walks the label through a couple of dozen
    /// intermediate values, so "what we wrote" is a moving target; both Paint and the restore compare
    /// against this one to tell "still ours" from "someone else owns the label now".</summary>
    private string? _writtenText;
    private string[]? _cells;      // one display cell per original character
    private int _wordStart;
    private int _wordEnd;          // exclusive

    public override string Id => "glyphrot";
    public override PossessionRung MinRung => PossessionRung.Melt;
    public override PossessionIntensity MinIntensity => PossessionIntensity.Eerie;
    public override bool IsBig => false;
    public override bool UsesFlicker => false;
    public override double Weight => 2;
    public override TimeSpan HoldFor => TimeSpan.FromMilliseconds(3500);
    public override IReadOnlyList<PossessionRole> Roles => _roles;

    protected override bool CanApplyCore(PossessionContext ctx, PossessionTarget? target)
    {
        if (target?.Role == PossessionRole.Timer) return false;
        var tb = PossessionVisual.FindTextBlock(target?.Element);
        if (!PossessionVisual.IsRewritable(tb, 3)) return false;
        return FindWord(tb!.Text, ctx.Rng, out _, out _);
    }

    protected override Task ApplyCoreAsync(PossessionContext ctx, PossessionTarget? target, CancellationToken ct)
    {
        _tb = PossessionVisual.FindTextBlock(target?.Element);
        if (!PossessionVisual.IsRewritable(_tb, 3)) { _tb = null; return Task.CompletedTask; }

        _originalText = _tb!.Text;
        if (!FindWord(_originalText, ctx.Rng, out _wordStart, out _wordEnd))
        {
            _originalText = null;
            _writtenText = null;
            _tb = null;
            return Task.CompletedTask;
        }

        // Nothing painted yet, so what stands on screen is still the original - and that IS ours.
        _writtenText = _originalText;

        _cells = new string[_originalText.Length];
        for (int i = 0; i < _originalText.Length; i++) _cells[i] = _originalText[i].ToString();

        _ = RotAsync(Cts?.Token ?? CancellationToken.None);
        return Task.CompletedTask;
    }

    /// <summary>Rot in, hold, heal out. Every step re-reads the fields, so an Undo landing mid-rot
    /// simply stops the loop and the restore in UndoCore wins.</summary>
    private async Task RotAsync(CancellationToken ct)
    {
        try
        {
            var order = new List<int>();
            for (int i = _wordStart; i < _wordEnd; i++) order.Add(i);
            Shuffle(order);

            foreach (var i in order)
            {
                if (!await PossAnim.DelayAsync(StepMs, ct).ConfigureAwait(true)) return;
                if (_cells == null || _originalText == null) return;
                _cells[i] = Rot(_originalText[i]);
                Paint();
            }

            if (!await PossAnim.DelayAsync(Photosafe ? RotHoldMs * 1.4 : RotHoldMs, ct).ConfigureAwait(true)) return;

            order.Reverse();
            foreach (var i in order)
            {
                if (!await PossAnim.DelayAsync(StepMs, ct).ConfigureAwait(true)) return;
                if (_cells == null || _originalText == null) return;
                _cells[i] = _originalText[i].ToString();
                Paint();
            }
        }
        catch (Exception ex) { App.Logger?.Warning("Possession glyphrot loop failed: {Error}", ex.Message); }
    }

    private void Paint()
    {
        try
        {
            var tb = _tb;
            var cells = _cells;
            if (tb == null || cells == null) return;

            // Someone else wrote this label since our last step (a level-up, a counter tick): it is
            // theirs now. Stop painting rather than fighting them, and leave _writtenText where it is
            // so the restore below stands down too.
            if (_writtenText != null && !string.Equals(tb.Text, _writtenText, StringComparison.Ordinal)) return;

            var sb = new StringBuilder(cells.Length + 8);
            foreach (var c in cells) sb.Append(c);
            var painted = sb.ToString();
            tb.Text = painted;
            _writtenText = painted;
        }
        catch (Exception ex) { App.Logger?.Warning("Possession glyphrot paint failed: {Error}", ex.Message); }
    }

    /// <summary>
    /// The exact original string, but only while the label still says what we last painted. The labels
    /// this effect can take are code-driven ones ({loc:Str} is a Binding and IsRewritable declines
    /// those), so they are exactly the ones that change underneath us; putting our snapshot back over a
    /// fresher value would lose it. Same guard as XpDrainEffect.RestoreTheLevel.
    /// </summary>
    protected override Task UndoCoreAsync(TimeSpan duration)
    {
        try
        {
            if (_tb != null && _originalText != null && _writtenText != null
                && string.Equals(_tb.Text, _writtenText, StringComparison.Ordinal))
                _tb.Text = _originalText;
        }
        catch (Exception ex) { App.Logger?.Warning("Possession glyphrot restore failed: {Error}", ex.Message); }

        _tb = null;
        _originalText = null;
        _writtenText = null;
        _cells = null;
        return Task.CompletedTask;
    }

    // ---- glyph plumbing ----------------------------------------------------------------------

    /// <summary>One rotted display cell for <paramref name="c"/>: a look-alike or a box, sometimes
    /// wearing a combining mark. Always exactly one visible column wide.</summary>
    private string Rot(char c)
    {
        char baseChar;
        var lower = char.ToLowerInvariant(c);
        if (LookAlikes.TryGetValue(lower, out var options) && Rng.Next(100) < 55)
        {
            baseChar = options[Rng.Next(options.Length)];
        }
        else
        {
            baseChar = Boxes[Rng.Next(Boxes.Length)];
        }

        if (Rng.Next(100) < 30) return baseChar.ToString() + Marks[Rng.Next(Marks.Length)];
        return baseChar.ToString();
    }

    /// <summary>Pick a word of 3+ letters to eat. Returns its half-open range in the string.</summary>
    private static bool FindWord(string text, Random rng, out int start, out int end)
    {
        start = 0;
        end = 0;
        try
        {
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
            var pick = words[(rng ?? Random.Shared).Next(words.Count)];
            start = pick.S;
            end = pick.E;
            return true;
        }
        catch { return false; }
    }

    private void Shuffle(List<int> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}
