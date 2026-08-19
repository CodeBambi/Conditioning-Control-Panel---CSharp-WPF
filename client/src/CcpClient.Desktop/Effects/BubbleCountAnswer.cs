namespace CcpClient.Desktop.Effects;

/// <summary>What one keystroke did to the counting card.</summary>
public enum BubbleCountStep
{
    /// <summary>Nothing observable — a key that is not a digit, not Backspace, not Enter and not
    /// Escape.</summary>
    Ignored,

    /// <summary>The typed number changed and the card should repaint.</summary>
    Typed,

    /// <summary>Enter was pressed with nothing usable in the box. The card says so and stays up, and
    /// <b>no attempt is spent</b> — upstream's own behaviour
    /// (<c>Windows/BubbleCountResultWindow.xaml.cs:190-195</c>: it shows "Please enter a number!",
    /// clears the box and RETURNS before the attempt counter).</summary>
    NotANumber,

    /// <summary>Wrong, and there are attempts left. The card carries the too-low/too-high hint
    /// (<c>:247</c>).</summary>
    Missed,

    /// <summary>The count was right.</summary>
    Correct,

    /// <summary>Wrong, and that was the last attempt (<c>:227-241</c>).</summary>
    Exhausted,

    /// <summary>The user pressed Escape and this card honours it.</summary>
    GaveUp,
}

/// <summary>
/// The counting card's own rules: three attempts at a number, digits only, Enter submits, and a
/// too-low/too-high hint in between. Ported from
/// <c>Windows/BubbleCountResultWindow.xaml.cs</c> and kept pure, so every rule is pinnable with no
/// window, no keyboard and no human — the same shape and the same reason as
/// <see cref="LockCardAttempt"/>.
///
/// <para><b>Escape always ends the card here, and that is D112 applied a second time rather than a
/// new decision.</b> Upstream gates Escape on strict mode alone
/// (<c>:171-178</c>, <c>if (e.Key == Key.Escape &amp;&amp; !_strictMode …)</c>) and then needs a
/// 120-second inactivity watchdog to rescue the user it strands — its own #633 comment says so:
/// "If an idle user is stranded on the fullscreen/topmost result window (strict mode has no Esc),
/// auto-complete after this timeout" (<c>:28-32</c>). This port has no panic-key hook, so
/// upstream's own fall-open rule leaves Escape live on every card (<c>LockCardWindow.xaml.cs:632</c>,
/// D112) — and with Escape live the watchdog has no user to rescue, so it is absent rather than
/// ported into a build where its cause cannot occur.</para>
/// </summary>
public sealed class BubbleCountAnswer
{
    /// <summary>Upstream's attempt budget (<c>Windows/BubbleCountResultWindow.xaml.cs:24</c>,
    /// <c>_attemptsRemaining = 3</c>).</summary>
    public const int DefaultAttempts = 3;

    /// <summary>Upstream's Enter handler (<c>:166-170</c>). Delivered by the input capability as
    /// <c>InputKeystrokeKind.Key</c> with this virtual-key code — the capability names Backspace,
    /// Escape and characters, and every other key arrives raw, so the SUBMIT key is the caller's to
    /// recognise.</summary>
    public const int SubmitVirtualKey = 0x0D;

    /// <summary>Upstream's title (<c>en.json:2443</c>, <c>dialog_how_many_bubbles</c>). The port
    /// takes the plain string rather than the panel's emoji-wrapped label
    /// (<c>label_how_many_bubbles</c>), because the card is drawn with GDI and an emoji there is a
    /// font question rather than a wording one.</summary>
    public const string Question = "How Many Bubbles?";

    /// <summary>Upstream's non-numeric answer (<c>:192</c>).</summary>
    public const string NotANumberMessage = "Please enter a number!";

    /// <summary>Upstream's hint when the guess is under (<c>:247</c>).</summary>
    public const string TooLowMessage = "Too low! Try higher.";

    /// <summary>Upstream's hint when the guess is over (<c>:247</c>).</summary>
    public const string TooHighMessage = "Too high! Try lower.";

    private readonly int _correct;
    private string _typed = string.Empty;

    /// <param name="correctAnswer">How many bubbles were really drawn into pictures the operating
    /// system was holding.</param>
    /// <param name="attempts">The attempt budget; upstream's three unless a fact wants another.</param>
    public BubbleCountAnswer(int correctAnswer, int attempts = DefaultAttempts)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(correctAnswer);
        ArgumentOutOfRangeException.ThrowIfLessThan(attempts, 1);
        _correct = correctAnswer;
        AttemptsRemaining = attempts;
    }

    /// <summary>The count the card is asking about. Never rendered on the card.</summary>
    public int CorrectAnswer => _correct;

    /// <summary>What is in the box right now.</summary>
    public string Typed => _typed;

    /// <summary>How many guesses are left (<c>:24</c>, <c>:222</c>).</summary>
    public int AttemptsRemaining { get; private set; }

    /// <summary>How many guesses have been submitted and judged.</summary>
    public int GuessesMade { get; private set; }

    /// <summary>True once the count was guessed right.</summary>
    public bool Solved { get; private set; }

    /// <summary>
    /// The line under the question: upstream shows the attempt counter (<c>:280</c>,
    /// "Attempts remaining: n") and its feedback line (<c>:249</c>) as two separate blocks. <b>The
    /// input capability's card carries FOUR slots — question, progress, answer, hint — so this port
    /// folds those two into the progress slot</b> rather than growing the capability's content
    /// record for its second consumer. They change together (a judged guess spends an attempt and
    /// produces the hint in the same instant), which is what makes the fold honest rather than
    /// merely cheap.
    /// </summary>
    public string Progress => Feedback.Length == 0
        ? $"Attempts remaining: {AttemptsRemaining}"
        : $"{Feedback}   Attempts remaining: {AttemptsRemaining}";

    /// <summary>The last thing the card said about a guess, or empty before one was made.</summary>
    public string Feedback { get; private set; } = string.Empty;

    /// <summary>
    /// Apply one delivered keystroke and say what it did. Upstream's own rules, in upstream's own
    /// order: digits only (<c>:98-101</c>, <c>e.Handled = !char.IsDigit</c>), Enter submits
    /// (<c>:166-170</c>), a box that will not parse spends nothing (<c>:190-195</c>), a wrong guess
    /// spends an attempt and clears the box (<c>:222-250</c>).
    /// </summary>
    /// <param name="character">The character, when <paramref name="isCharacter"/>.</param>
    /// <param name="isCharacter">The capability delivered a character.</param>
    /// <param name="isBackspace">The capability delivered Backspace.</param>
    /// <param name="isCancel">The capability delivered Escape (or a close request).</param>
    /// <param name="virtualKey">The raw virtual key, which is how Enter arrives.</param>
    public BubbleCountStep Apply(
        char character, bool isCharacter, bool isBackspace, bool isCancel, int virtualKey)
    {
        if (isCancel)
        {
            return BubbleCountStep.GaveUp;
        }

        if (isBackspace)
        {
            if (_typed.Length == 0)
            {
                return BubbleCountStep.Ignored;
            }

            _typed = _typed[..^1];
            Feedback = string.Empty;
            return BubbleCountStep.Typed;
        }

        if (!isCharacter)
        {
            return virtualKey == SubmitVirtualKey ? Submit() : BubbleCountStep.Ignored;
        }

        // Digits only. Upstream refuses everything else at the TextBox's PreviewTextInput (:98-101),
        // so a letter never reaches its box at all; here the capability delivers every printable
        // character and the refusal is this line.
        if (!char.IsAsciiDigit(character))
        {
            return BubbleCountStep.Ignored;
        }

        _typed += character;
        Feedback = string.Empty;
        return BubbleCountStep.Typed;
    }

    private BubbleCountStep Submit()
    {
        if (!int.TryParse(_typed.Trim(), out var guess))
        {
            // Upstream shows "Please enter a number!", clears the box and RETURNS — before the
            // attempt counter (:190-195). An empty Enter costs nothing.
            Feedback = NotANumberMessage;
            _typed = string.Empty;
            return BubbleCountStep.NotANumber;
        }

        GuessesMade++;
        if (guess == _correct)
        {
            Solved = true;
            Feedback = string.Empty;
            return BubbleCountStep.Correct;
        }

        AttemptsRemaining--;
        _typed = string.Empty;
        if (AttemptsRemaining <= 0)
        {
            Feedback = string.Empty;
            return BubbleCountStep.Exhausted;
        }

        Feedback = guess < _correct ? TooLowMessage : TooHighMessage;
        return BubbleCountStep.Missed;
    }
}
