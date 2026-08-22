namespace CcpClient.Desktop.Views;

/// <summary>
/// What a page says when a launch THREW past the entitlement gate — the port's
/// counterpart of WPF's <c>MessageBox.Show("Couldn't start …:\n\n" + ex.Message, …,
/// MessageBoxImage.Warning)</c> (<c>MainWindow/MainWindow.Lab.cs:164-165</c> for Graded Intake,
/// <c>:269-270</c> and <c>:336-337</c> for Down the Rabbit Hole).
///
/// <para><b>The defect this closes is that the failure was INVISIBLE, not that it was
/// unhandled.</b> A catch that renders a band and drops the detail is the same bug in nicer
/// clothes, so the composed text keeps BOTH the exception type and its message — strictly WPF's
/// disclosure plus the type — and the caller's diagnostic keeps them too. Nothing here is a
/// substitute for the log line; it is the half the user can read.</para>
///
/// <para><b>And a failure is not a refusal.</b> The refusal bands on these same two cards mean
/// "we could not determine your entitlement" (<c>Features/Dtrh/DtrhGate.cs</c>,
/// <c>Features/Intake/IntakePassGate.cs</c>). If a fault rendered in the same words the user
/// would learn that a broken app and an unknown subscription are one event.
/// <see cref="NotADecisionLine"/> is the sentence that says the difference out loud; the amber
/// <c>fault-band</c> / <c>fault-plate</c> livery is the same statement in colour, carrying WPF's
/// <c>MessageBoxImage.Warning</c> severity across an idiom change.</para>
/// </summary>
public static class LaunchFaultText
{
    /// <summary>The DTRH headline, verbatim from <c>MainWindow.Lab.cs:269</c> minus its trailing
    /// colon (the colon joins headline to detail in <see cref="Compose"/>, exactly as WPF's
    /// concatenation does).</summary>
    public const string DtrhHeadline = "Couldn't start Down the Rabbit Hole";

    /// <summary>The Graded Intake headline, verbatim from <c>MainWindow.Lab.cs:164</c>.</summary>
    public const string IntakeHeadline = "Couldn't start Graded Intake";

    /// <summary>
    /// The line that makes a fault unmistakably not a refusal, in words as well as livery. It is
    /// the §10 D21 discipline read in the other direction: that rule keeps "I could not tell"
    /// from being rendered as "you are not a patron", and this keeps "the app broke" from being
    /// rendered as either.
    /// </summary>
    public const string NotADecisionLine =
        "This is a fault in the app, not a decision about your account: nothing here was refused to you. "
        + "The button still works, so you can try again.";

    /// <summary>
    /// The clamp on the exception's own message. WPF's <c>MessageBox</c> grows to fit its text and
    /// is then dismissed; this plate lives inside a card that stays on screen, and an unclamped
    /// multi-line message would push its own last line — and the sentence above it — out of the
    /// plate. That is the §10 D23 illegibility defect arriving by a second route. Divergence §13 D43.
    /// </summary>
    public const int MaxDetailLength = 400;

    /// <summary>The marker appended when a message really was clamped, so the user is told the
    /// text was cut rather than left to wonder whether the sentence just ends there.</summary>
    public const string ClampMarker = "… (truncated; the diagnostic log has the whole message)";

    /// <summary>
    /// The technical half: the exception TYPE and its message. The type is what survives a
    /// paraphrase in a bug report and is what the trap named at authoring requires be kept; the
    /// message is what WPF shows.
    /// </summary>
    public static string Detail(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        var name = exception.GetType().Name;
        // A message can be empty (plenty of framework exceptions carry only a type) — say the
        // type alone rather than emitting a dangling colon.
        var message = exception.Message;
        if (string.IsNullOrWhiteSpace(message))
        {
            return name;
        }

        // Newlines inside an exception message would break the plate's line rhythm and, in a
        // multi-line message, hide the "not a decision" line below the fold.
        message = message.ReplaceLineEndings(" ").Trim();
        var detail = name + ": " + message;
        return detail.Length <= MaxDetailLength
            ? detail
            : detail[..MaxDetailLength] + ClampMarker;
    }

    /// <summary>
    /// The separator between the body's three parts, and it is a SINGLE newline where WPF's
    /// <c>MessageBox</c> uses a blank line (<c>"…:\n\n" + ex.Message</c>,
    /// <c>MainWindow.Lab.cs:164</c>, <c>:269</c>).
    ///
    /// <para><b>Measured, not stylistic.</b> In Avalonia 12.1.1 a wrapped <c>TextBlock</c> inside
    /// these plates whose text contains an EMPTY LINE wedges the layout pass: the click that
    /// raised the band never returns. It reproduces with a two-character body (<c>"A:\n\nB"</c>),
    /// with and without <c>LineHeight</c>, and — the part that matters — it reproduces identically
    /// in the EXISTING refusal plate, so it is a property of the surface rather than of this
    /// feature. Single newlines are the shape the landed refusal copy already uses and are proven
    /// safe. Divergence and close condition at wpf-surface-reachability.md §13 D43;
    /// <c>LaunchFaultTextTests</c> holds the line over a DERIVED set - reflection across this class
    /// and both gates, plus every message the gates can produce - never a hand list.</para>
    /// </summary>
    public const string Separator = "\n";

    /// <summary>
    /// The whole user-facing body. WPF's shape — headline, colon, detail, then the port's own
    /// not-a-refusal line (<c>MainWindow.Lab.cs:164</c>, <c>:269</c>), joined by
    /// <see cref="Separator"/>.
    /// </summary>
    public static string Compose(string headline, Exception exception)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(headline);
        ArgumentNullException.ThrowIfNull(exception);
        return headline + ":" + Separator + Detail(exception) + Separator + NotADecisionLine;
    }
}
