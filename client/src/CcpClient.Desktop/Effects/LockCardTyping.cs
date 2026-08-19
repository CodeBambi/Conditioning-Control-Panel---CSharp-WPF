namespace CcpClient.Desktop.Effects;

/// <summary>What one keystroke did to the card's state.</summary>
public enum LockCardStep
{
    /// <summary>Nothing observable — a key with no character, or a control character.</summary>
    Ignored,

    /// <summary>The answer changed and the card should repaint.</summary>
    Typed,

    /// <summary>The answer matched the phrase and this repeat is banked.</summary>
    RepeatAccepted,

    /// <summary>The phrase appeared in the box without being typed. Refused, box wiped.</summary>
    UntypedMatchRefused,

    /// <summary>Every repeat is done.</summary>
    Solved,

    /// <summary>The user asked to leave and the card honours it.</summary>
    Cancelled,

    /// <summary>The user asked to leave and this card does not honour it.</summary>
    CancelRefused,
}

/// <summary>
/// The lock card's typing rules, ported from <c>Windows/LockCardWindow.xaml.cs</c> and kept pure so
/// every one of them is pinnable without a window, a keyboard or a human.
///
/// <para><b>Upstream's anti-cheat has two halves and the port keeps both, but only one of them
/// survives as CODE.</b> The mechanical half — cancel every paste command, disable the undo stack,
/// swallow Ctrl+C/V/X/A/Z/Y and Shift+Insert/Delete (<c>:246-264</c>, <c>:196-234</c>) — exists
/// because upstream's card is a WPF <c>TextBox</c>, and a <c>TextBox</c> has a clipboard, an undo
/// stack and a context menu. <b>This port's card has no edit control at all</b>: it is a raw window
/// whose window procedure receives <c>WM_CHAR</c>, so there is no paste route to close and Ctrl+V
/// arrives as the control character 0x16 rather than as text. The hardening therefore reduces to one
/// rule — <i>a control character is not typing</i> — which is enforced in the presence's window
/// procedure, and the SEMANTIC half below, which is ported verbatim because it is what actually
/// decides whether a repeat counts.</para>
/// </summary>
public static class LockCardTyping
{
    /// <summary>
    /// The semantic half of the anti-cheat, verbatim: a repeat only counts once the user has produced
    /// at least as many characters as the phrase is long
    /// (<c>Windows/LockCardWindow.xaml.cs:272</c>). Any route that fills the box in one shot leaves
    /// the credit far short of this, so the match is refused.
    /// </summary>
    public static bool HasTypedEnough(int keystrokes, int phraseLength) => keystrokes >= phraseLength;

    /// <summary>
    /// Does what is in the box so far match the phrase's opening? Upstream counts a mistake whenever
    /// it does not (<c>:727-733</c>): the comparison is against
    /// <c>_phrase.Substring(0, Math.Min(input.Length, _phrase.Length))</c> and it is
    /// case-INSENSITIVE, exactly as the completion test is.
    /// </summary>
    public static bool IsPrefixOf(string answer, string phrase)
    {
        ArgumentNullException.ThrowIfNull(answer);
        ArgumentNullException.ThrowIfNull(phrase);

        if (answer.Length == 0)
        {
            return true;
        }

        var expected = phrase[..Math.Min(answer.Length, phrase.Length)];
        return string.Equals(answer, expected, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Is the box a completed repeat? Upstream: <c>string.Equals(input.Trim(), _phrase,
    /// StringComparison.OrdinalIgnoreCase)</c> (<c>:740</c>). The <c>Trim</c> and the
    /// case-insensitivity are both upstream's and both are behaviour a user can feel.
    /// </summary>
    public static bool Matches(string answer, string phrase)
    {
        ArgumentNullException.ThrowIfNull(answer);
        ArgumentNullException.ThrowIfNull(phrase);
        return string.Equals(answer.Trim(), phrase, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// <b>Does Escape close THIS card?</b> Upstream's rule, verbatim
    /// (<c>Windows/LockCardWindow.xaml.cs:632</c>): <c>!strict || !panicEscapeIsLive</c>.
    ///
    /// <para>Non-strict cards: yes, Escape is the ordinary dismiss. Strict cards: only while a panic
    /// escape is genuinely live, because otherwise Escape is the sole remaining exit and <b>strict
    /// mode must never become an inescapable trap</b> — upstream's own words, and its reason for
    /// checking the panic HOOK rather than the panic SETTING is that a low-level keyboard hook can
    /// be silently un-registered by Windows with nothing reinstalling it (<c>:610-622</c>, the
    /// #616-#623 cluster). "Every failure mode here has to fall open, not shut."</para>
    ///
    /// <para><b>What that rule evaluates to in this port, stated where it cannot be missed.</b> The
    /// port has no panic-key hook at all, so <paramref name="panicEscapeIsLive"/> is always false and
    /// this predicate is always true: <b>every card here closes on Escape, including a strict
    /// one.</b> That is not strict mode being weakened — it is upstream's own fall-open rule applied
    /// to this build's facts, and it is what stops this packet from shipping a window that takes the
    /// keyboard and cannot be escaped. Strict mode still does the other thing it does upstream: it
    /// refuses the window's close request (<c>:678-681</c> blocks Alt+F4), so the card cannot be
    /// dismissed by the window manager, only by the exit the rule allows.</para>
    /// </summary>
    public static bool EscapeClosesCard(bool strict, bool panicEscapeIsLive) => !strict || !panicEscapeIsLive;
}

/// <summary>
/// One card's live typing state: what has been produced, how much of it was genuinely typed, how
/// many repeats are banked and how many mistakes were made.
///
/// <para>Mutable and single-threaded on purpose. Every keystroke arrives on the thread whose message
/// loop delivered it — the UI thread in the product — and the module owns exactly one of these per
/// card.</para>
/// </summary>
public sealed class LockCardAttempt
{
    private readonly string _phrase;
    private readonly int _requiredRepeats;
    private readonly bool _strict;
    private string _answer = string.Empty;
    private int _keystrokes;

    /// <param name="phrase">What must be produced.</param>
    /// <param name="requiredRepeats">How many times, clamped to WPF's 1..10
    /// (<c>AppSettings.cs:3349</c>).</param>
    /// <param name="strict">Whether this card refuses the ordinary exits
    /// (<c>LockCardWindow.xaml.cs:632</c>, <c>:678-681</c>).</param>
    public LockCardAttempt(string phrase, int requiredRepeats, bool strict)
    {
        ArgumentException.ThrowIfNullOrEmpty(phrase);
        _phrase = phrase;
        _requiredRepeats = Math.Clamp(requiredRepeats, LockCardSchedule.MinRepeats, LockCardSchedule.MaxRepeats);
        _strict = strict;
    }

    /// <summary>The phrase this card is asking for.</summary>
    public string Phrase => _phrase;

    /// <summary>How many repeats it wants.</summary>
    public int RequiredRepeats => _requiredRepeats;

    /// <summary>Whether it is a strict card.</summary>
    public bool Strict => _strict;

    /// <summary>What is in the box right now.</summary>
    public string Answer => _answer;

    /// <summary>Characters genuinely entered since the last accepted repeat — upstream's
    /// <c>_keystrokes</c> (<c>:44</c>), which its <c>RegisterSuccessfulRepeat</c> zeroes
    /// (<c>:702</c>) so the next repeat has to be earned from scratch.</summary>
    public int Keystrokes => _keystrokes;

    /// <summary>Repeats banked so far — upstream's <c>_completedRepeats</c> (<c>:771</c>).</summary>
    public int CompletedRepeats { get; private set; }

    /// <summary>Mistakes, counted upstream's way: once per changed box whose contents are not a
    /// prefix of the phrase (<c>:727-733</c>).</summary>
    public int Mistakes { get; private set; }

    /// <summary>Untyped full-phrase matches this card refused (<c>:746-758</c>).</summary>
    public int RefusedUntypedMatches { get; private set; }

    /// <summary>True once every repeat is banked.</summary>
    public bool Solved => CompletedRepeats >= _requiredRepeats;

    /// <summary>The progress line the card shows, upstream's counter in words.</summary>
    public string Progress => $"{Math.Min(CompletedRepeats + 1, _requiredRepeats)} of {_requiredRepeats}";

    /// <summary>
    /// Apply one delivered keystroke and say what it did. The whole of upstream's
    /// <c>TxtInput_TextChanged</c> (<c>:707-760</c>) plus its Escape branch (<c>:655-670</c>), in
    /// upstream's order: credit the keystroke, count a mistake if the box has left the phrase's
    /// prefix, then test for a full match, and only then apply the typed-enough gate.
    /// </summary>
    public LockCardStep Apply(char character, bool isCharacter, bool isBackspace, bool isCancel)
    {
        if (isCancel)
        {
            return LockCardTyping.EscapeClosesCard(_strict, panicEscapeIsLive: false)
                ? LockCardStep.Cancelled
                : LockCardStep.CancelRefused;
        }

        if (isBackspace)
        {
            if (_answer.Length == 0)
            {
                return LockCardStep.Ignored;
            }

            // Deliberately does NOT decrement the credit. Upstream's counter only ever counts
            // growth (CreditFailSafeGrowth, :289-290) and its PreviewTextInput never fires for
            // backspace, so a user who types and deletes has still typed.
            _answer = _answer[..^1];
            return LockCardStep.Typed;
        }

        if (!isCharacter)
        {
            return LockCardStep.Ignored;
        }

        _answer += character;
        _keystrokes++;

        if (!LockCardTyping.IsPrefixOf(_answer, _phrase))
        {
            Mistakes++;
        }

        if (!LockCardTyping.Matches(_answer, _phrase))
        {
            return LockCardStep.Typed;
        }

        // The gate lives at THIS call site only, exactly as upstream's does (:743-745).
        if (!LockCardTyping.HasTypedEnough(_keystrokes, _phrase.Length))
        {
            // A full-phrase match nobody typed: refuse it, wipe the box, make them type it for real
            // (:746-758).
            RefusedUntypedMatches++;
            ResetForNextRepeat();
            return LockCardStep.UntypedMatchRefused;
        }

        CompletedRepeats++;
        ResetForNextRepeat();
        return Solved ? LockCardStep.Solved : LockCardStep.RepeatAccepted;
    }

    private void ResetForNextRepeat()
    {
        _answer = string.Empty;
        _keystrokes = 0;
    }
}
