using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel.Services.Possession.Effects;

/// <summary>
/// R2 "relabel" - the button stops offering. Anything that reads Start or Stop says "Stay" for two and
/// a half seconds, then goes back to the EXACT object it was carrying (the same string instance, not a
/// copy: whatever else in the app compares that Content by reference is never any the wiser).
///
/// <para>Two shapes are eligible, and no third. A plain string Content is swapped and the ORIGINAL
/// OBJECT put back. Failing that, the one TextBlock inside the button that actually reads Start or
/// Stop gets its Text overridden with SetCurrentValue, which leaves its binding installed and
/// untouched: undo is then a single UpdateTarget() that re-pulls the real localized word from the
/// source. That second path is not scope creep, it is the only path that ever fires in this app -
/// every transport button here is an icon TextBlock plus a {loc:Str} TextBlock in a StackPanel, so a
/// string-Content-only relabel would have been dead code the day it shipped.</para>
///
/// <para>What is still refused: rebuilding a button's Content tree. "Restores EXACTLY" is not a thing
/// to be clever about (POSSESSION.md), and re-parenting somebody else's panel is exactly that.</para>
///
/// <para>The word itself is a loc key (lockdown_poss_stay) - unlike the warden barks, this one is
/// painted INTO the UI, so it has to speak the language the rest of the window is speaking.</para>
/// </summary>
public sealed class RelabelEffect : PossessionEffectBase
{
    private static readonly PossessionRole[] _roles = { PossessionRole.Button };

    private ContentControl? _cc;
    private object? _originalContent;

    private TextBlock? _tb;
    private string? _originalText;
    private bool _textWasBound;

    public override string Id => "relabel";
    public override PossessionRung MinRung => PossessionRung.Melt;
    public override PossessionIntensity MinIntensity => PossessionIntensity.Eerie;
    public override bool IsBig => true;
    public override double Weight => 3;
    public override TimeSpan HoldFor => TimeSpan.FromMilliseconds(2500);
    public override IReadOnlyList<PossessionRole> Roles => _roles;

    protected override bool CanApplyCore(PossessionContext ctx, PossessionTarget? target)
    {
        var stay = Stay();
        if (stay == null) return false;

        var el = target?.Element;
        if (el == null) return false;

        var content = RewriteEffect.StringContentOf(el);
        if (content != null && IsStartOrStop(content))
            return !string.Equals(stay, content, StringComparison.OrdinalIgnoreCase);

        var tb = FindStartStopTextBlock(el);
        return tb != null && !string.Equals(stay, tb.Text, StringComparison.OrdinalIgnoreCase);
    }

    protected override Task ApplyCoreAsync(PossessionContext ctx, PossessionTarget? target, CancellationToken ct)
    {
        try
        {
            var el = target?.Element;
            if (el == null) return Task.CompletedTask;

            var stay = Stay();
            if (stay == null) return Task.CompletedTask;

            // 1. The simple shape: Content IS the word.
            if (el is ContentControl cc && cc.Content is string text && IsStartOrStop(text))
            {
                if (string.Equals(stay, text, StringComparison.Ordinal)) return Task.CompletedTask;
                _cc = cc;
                _originalContent = cc.Content;   // the exact object, not a copy of the string
                cc.Content = stay;
                return Task.CompletedTask;
            }

            // 2. The shape this app actually ships: an icon plus a bound label in a panel.
            var tb = FindStartStopTextBlock(el);
            if (tb == null || string.Equals(stay, tb.Text, StringComparison.Ordinal)) return Task.CompletedTask;

            _tb = tb;
            _originalText = tb.Text;
            _textWasBound = System.Windows.Data.BindingOperations.IsDataBound(tb, TextBlock.TextProperty);

            // SetCurrentValue overrides what is DISPLAYED without disturbing the binding underneath,
            // which is what makes the restore a one liner instead of a re-binding exercise.
            tb.SetCurrentValue(TextBlock.TextProperty, stay);
        }
        catch (Exception ex) { App.Logger?.Warning("Possession relabel failed: {Error}", ex.Message); }

        return Task.CompletedTask;
    }

    protected override Task UndoCoreAsync(TimeSpan duration)
    {
        try
        {
            if (_cc != null && _originalContent != null) _cc.Content = _originalContent;
        }
        catch (Exception ex) { App.Logger?.Warning("Possession relabel restore failed: {Error}", ex.Message); }

        try
        {
            if (_tb != null)
            {
                if (_textWasBound)
                {
                    // Let the binding say what the word is - it is the authority, and it knows the
                    // current language even if it changed while the button was lying.
                    var expr = System.Windows.Data.BindingOperations.GetBindingExpression(_tb, TextBlock.TextProperty);
                    if (expr != null) expr.UpdateTarget();
                    else if (_originalText != null) _tb.SetCurrentValue(TextBlock.TextProperty, _originalText);
                }
                else if (_originalText != null)
                {
                    _tb.SetCurrentValue(TextBlock.TextProperty, _originalText);
                }
            }
        }
        catch (Exception ex) { App.Logger?.Warning("Possession relabel text restore failed: {Error}", ex.Message); }

        _cc = null;
        _originalContent = null;
        _tb = null;
        _originalText = null;
        _textWasBound = false;
        return Task.CompletedTask;
    }

    /// <summary>The one TextBlock inside this button that reads Start or Stop (never the icon glyph
    /// beside it, which is why this is a search and not FindTextBlock).</summary>
    private static TextBlock? FindStartStopTextBlock(FrameworkElement? root, int depth = 0)
    {
        if (root == null) return null;
        try
        {
            return FindTb(root, 0);
        }
        catch { return null; }

        static TextBlock? FindTb(DependencyObject node, int depth)
        {
            if (depth > 12) return null;
            if (node is TextBlock tb && IsStartOrStop(tb.Text)) return tb;
            int count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(node);
            for (int i = 0; i < count; i++)
            {
                var found = FindTb(System.Windows.Media.VisualTreeHelper.GetChild(node, i), depth + 1);
                if (found != null) return found;
            }
            return null;
        }
    }

    /// <summary>The localized word the button is made to say. Falls back to English rather than
    /// declining to run, so the effect survives a language file that has not been merged yet.</summary>
    private static string? Stay()
    {
        var s = RewritePools.LocOr("lockdown_poss_stay", "Stay");
        return string.IsNullOrWhiteSpace(s) ? null : s;
    }

    /// <summary>
    /// "Does this button offer to start or stop something." English first (the pools and most of the
    /// chrome are English), then whatever the current language calls those two buttons, so a German UI
    /// still gets its Starten relabelled.
    /// </summary>
    internal static bool IsStartOrStop(string? text)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            var t = text.Trim();

            if (StartsWithWord(t, "start") || StartsWithWord(t, "stop")) return true;

            foreach (var key in new[] { "btn_start", "btn_stop" })
            {
                var word = Loc.Get(key);
                if (string.IsNullOrWhiteSpace(word) || word.StartsWith("btn_", StringComparison.Ordinal)) continue;
                if (StartsWithWord(t, word.Trim())) return true;
            }
        }
        catch { }
        return false;
    }

    /// <summary>Whole-word prefix match, so "Start Flashes" counts and "Startup log" does not.</summary>
    private static bool StartsWithWord(string text, string word)
    {
        if (word.Length == 0 || text.Length < word.Length) return false;
        if (!text.StartsWith(word, StringComparison.OrdinalIgnoreCase)) return false;
        if (text.Length == word.Length) return true;
        var next = text[word.Length];
        return !char.IsLetterOrDigit(next);
    }
}
