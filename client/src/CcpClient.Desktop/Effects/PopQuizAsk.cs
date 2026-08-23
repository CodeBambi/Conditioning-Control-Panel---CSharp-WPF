using System.Text;

namespace CcpClient.Desktop.Effects;

/// <summary>What one delivered keystroke did to the quiz card.</summary>
public enum PopQuizStep
{
    /// <summary>Nothing observable — a key that is not one of the four answer keys and not Escape,
    /// or any key at all after the question has already been answered
    /// (<c>Windows/PopQuizWindow.xaml.cs:138</c>, <c>if (_answered) return;</c>).</summary>
    Ignored,

    /// <summary>An answer was chosen. <b>There is no other kind of pick:</b> every answer is correct
    /// (<c>Services/Quiz/PopQuizService.cs:12</c>).</summary>
    Picked,

    /// <summary>Escape, before an answer. Upstream closes the window and awards nothing
    /// (<c>PopQuizWindow.xaml.cs:128-134</c>).</summary>
    Skipped,
}

/// <summary>
/// One question as it is actually being asked: the shuffle that decided what sits in which slot, the
/// key that picks each slot, and the affirmation that comes back. Ported from
/// <c>Windows/PopQuizWindow.xaml.cs</c> and kept pure, so every rule is pinnable with no window, no
/// keyboard and no human — the same shape and the same reason as <see cref="LockCardAttempt"/> and
/// <see cref="BubbleCountAnswer"/>.
///
/// <para><b>The shuffle is behaviour, not presentation.</b> Upstream permutes the four slots with a
/// Fisher-Yates walk (<c>:54-60</c>) and stores the ORIGINAL index on each slot's <c>.Tag</c>
/// (<c>:122-125</c>), so the affirmation it shows is looked up by the answer the user picked and not
/// by where that answer happened to be on screen (<c>:171</c>). Get that wrong and every user of
/// every question gets a reply to something they did not say — which is the one thing a
/// reinforcement question cannot afford. <see cref="Order"/> is that mapping and
/// <see cref="Affirmation"/> is that lookup.</para>
///
/// <para><b>The four slots are picked with the keys 1-4, and that is a DIVERGENCE with a
/// reason.</b> Upstream's slots are clickable borders
/// (<c>Windows/PopQuizWindow.xaml:45-107</c>, <c>MouseLeftButtonDown="Answer_Click"</c>). This
/// port's card is the shared input capability's window, which exists to TAKE the keyboard and
/// deliver keystrokes (<see cref="Input.IInputPresence"/>); the pointer capability is a different
/// window with the opposite job, and asking through it would put a second thing in front of the user
/// that is NOT mutually exclusive with the Lock Card — losing exactly the cross-check upstream added
/// in its own #763 (<c>PopQuizService.cs:189-193</c>).</para>
/// </summary>
public sealed class PopQuizAsk
{
    /// <summary>The first answer key. Upstream has no keyboard path at all, so the four keys are the
    /// port's own — see the type remarks.</summary>
    public const char FirstAnswerKey = '1';

    /// <summary>The exit line, upstream's own wording
    /// (<c>Windows/PopQuizWindow.xaml:111</c>, <c>label_esc_to_skip</c>; <c>en.json:2710</c> =
    /// "ESC to skip"), with the port's key legend in front of it because the port's slots are keyed
    /// rather than clicked.</summary>
    public const string Hint = "1-4 to answer   ESC to skip";

    /// <summary>What the card says under the affirmation when twenty-five XP really reached the
    /// ledger — upstream's own line (<c>Windows/PopQuizWindow.xaml:124</c>, <c>label_25_xp</c>;
    /// <c>en.json:2711</c> = "+25 XP"). It is shown ONLY on a grant that banked; see
    /// <see cref="PopQuizEffect"/>.</summary>
    public const string XpLine = "+25 XP";

    private readonly int[] _order;
    private int _picked = -1;

    /// <param name="question">The question drawn from the pool.</param>
    /// <param name="random">The draw behind the shuffle. Injected for the reason every module's
    /// <see cref="Random"/> is: the PERMUTATION is what a fact makes deterministic, and the walk it
    /// feeds stays this class's own rather than a permutation a test double hands back.</param>
    public PopQuizAsk(PopQuizQuestion question, Random random)
    {
        ArgumentNullException.ThrowIfNull(question);
        ArgumentNullException.ThrowIfNull(random);
        if (question.Answers.Count != PopQuizQuestion.AnswerCount
            || question.Affirmations.Count != PopQuizQuestion.AnswerCount)
        {
            throw new ArgumentException(
                $"a pop quiz question carries {PopQuizQuestion.AnswerCount} answers and "
                + $"{PopQuizQuestion.AnswerCount} affirmations",
                nameof(question));
        }

        Question = question;

        // Upstream's walk, verbatim (PopQuizWindow.xaml.cs:55-60): indices 0..3, i from 3 down to 1,
        // j = random.Next(i + 1), swap. The inclusive upper bound (i + 1, not i) is what makes it a
        // uniform Fisher-Yates rather than the subtly biased variant.
        _order = [0, 1, 2, 3];
        for (var i = PopQuizQuestion.AnswerCount - 1; i > 0; i--)
        {
            var j = random.Next(i + 1);
            (_order[i], _order[j]) = (_order[j], _order[i]);
        }
    }

    /// <summary>The question this card is asking.</summary>
    public PopQuizQuestion Question { get; }

    /// <summary>Display slot to the answer's index in <see cref="PopQuizQuestion.Answers"/> —
    /// upstream's <c>indices</c> array, the thing it hangs on each slot's <c>.Tag</c>
    /// (<c>PopQuizWindow.xaml.cs:122-125</c>).</summary>
    public IReadOnlyList<int> Order => _order;

    /// <summary>The four answers in the order they are shown (<c>:116-119</c>).</summary>
    public IReadOnlyList<string> Options => [.. _order.Select(i => Question.Answers[i])];

    /// <summary>Which slot was picked, or null while the question stands.</summary>
    public int? PickedSlot => _picked < 0 ? null : _picked;

    /// <summary>True once an answer has been given. Upstream's <c>_answered</c>
    /// (<c>PopQuizWindow.xaml.cs:23</c>), which gates every later keystroke and mouse event.</summary>
    public bool Answered => _picked >= 0;

    /// <summary>
    /// The reply to the answer the user actually picked — <c>_question.Affirmations[answerIndex]</c>
    /// where <c>answerIndex</c> is the slot's stored ORIGINAL index
    /// (<c>PopQuizWindow.xaml.cs:171</c>). Null until something is picked.
    /// </summary>
    public string? Affirmation => _picked < 0 ? null : Question.Affirmations[_order[_picked]];

    /// <summary>
    /// The card's question face: the question, then the four answers one to a line with the key that
    /// picks each.
    ///
    /// <para><b>Why it is one block and not four.</b> The input capability's content record carries
    /// four slots — question, progress, answer, hint — and only the QUESTION slot is drawn wrapped
    /// and multi-line (<c>Input/Win32InputPresence.cs:817</c>, <c>CentredWrapped</c>); the other
    /// three are single-line with end-ellipsis (<c>:814</c>, <c>:820-821</c>). Four answers on a
    /// single ellipsised line would silently HIDE an option on a narrow display, and an option the
    /// user cannot read is one they cannot pick. So the question and its answers travel together in
    /// the wrapped slot, which is also upstream's own stacking — question above, four rows below
    /// (<c>Windows/PopQuizWindow.xaml:33-108</c>). It is the same fold
    /// <see cref="BubbleCountAnswer.Progress"/> already made for the same record rather than growing
    /// the capability for its third consumer.</para>
    /// </summary>
    public string Face
    {
        get
        {
            var text = new StringBuilder(Question.Text).Append('\n');
            var options = Options;
            for (var slot = 0; slot < options.Count; slot++)
            {
                text.Append('\n').Append((char)(FirstAnswerKey + slot)).Append("  ").Append(options[slot]);
            }

            return text.ToString();
        }
    }

    /// <summary>
    /// Apply one delivered keystroke and say what it did.
    /// </summary>
    /// <param name="character">The character, when <paramref name="isCharacter"/>.</param>
    /// <param name="isCharacter">The capability delivered a character.</param>
    /// <param name="isCancel">The capability delivered Escape (or a close request).</param>
    public PopQuizStep Apply(char character, bool isCharacter, bool isCancel)
    {
        // Upstream's first line in both handlers: once answered, nothing else counts
        // (PopQuizWindow.xaml.cs:130 for Escape, :138 for a click). The window is already on its way
        // out at that point and a second answer would re-award and re-affirm.
        if (Answered)
        {
            return PopQuizStep.Ignored;
        }

        if (isCancel)
        {
            return PopQuizStep.Skipped;
        }

        if (!isCharacter)
        {
            return PopQuizStep.Ignored;
        }

        var slot = character - FirstAnswerKey;
        if (slot < 0 || slot >= PopQuizQuestion.AnswerCount)
        {
            return PopQuizStep.Ignored;
        }

        _picked = slot;
        return PopQuizStep.Picked;
    }
}
