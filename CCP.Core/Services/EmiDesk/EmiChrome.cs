using System;

namespace ConditioningControlPanel.Services.EmiDesk;

/// <summary>Which piece of her chrome the pointer is sitting on.</summary>
/// <remarks>
/// A flag set rather than a single value on purpose. WPF can hand out an enter for a child before
/// the leave for its parent (and, under mouse capture, a leave that never gets its matching enter),
/// so "which one is it?" is not answerable; "how many are holding it open?" is.
/// </remarks>
[Flags]
public enum EmiChromePart
{
    None = 0,

    /// <summary>Her silhouette. <c>BodyRoot</c>, which is also the drag surface.</summary>
    Body = 1 << 0,

    /// <summary>The hover x, top right.</summary>
    Close = 1 << 1,

    /// <summary>The gear, top left. Her options.</summary>
    Gear = 1 << 2,

    /// <summary>The ?, under the gear. Her book.</summary>
    Help = 1 << 3,

    /// <summary>The corner resize handle, bottom right.</summary>
    Grip = 1 << 4,
}

/// <summary>
/// A reason her chrome must stay lit even though the pointer is nowhere near her.
/// </summary>
/// <remarks>
/// Every one of these is a gesture that TAKES THE POINTER OFF HER as part of doing its job. Growing
/// her by dragging the corner grip moves the cursor down and right, off the silhouette, in the
/// first ten pixels: without this, the handle the user is actively holding faded out from under
/// them. Same shape for the move drag, for a button held down, and for her options panel, which is
/// a sibling window the pointer has to travel to.
/// </remarks>
[Flags]
public enum EmiChromeHold
{
    None = 0,

    /// <summary>She is being moved.</summary>
    Drag = 1 << 0,

    /// <summary>The corner grip is being dragged.</summary>
    Resize = 1 << 1,

    /// <summary>A chrome button is held down and has not been released or cancelled yet.</summary>
    Press = 1 << 2,

    /// <summary>A surface she owns is open beside her (her options panel).</summary>
    Menu = 1 << 3,
}

/// <summary>
/// WHEN HER CHROME IS LIT. The whole of the decision, and none of the drawing.
///
/// <para><b>Why this exists.</b> Owner report, 2026-08-30: <i>"when we hover the buttons next to emi
/// ... they should show and be clickable. Right now I gotta hover EMI and be fast enough to catch
/// those buttons before they disappear."</i> Until now the rule was one element's
/// <c>MouseEnter</c>/<c>MouseLeave</c> straight onto a 140 ms fade: the instant the pointer left her
/// silhouette by a pixel - overshooting a corner chip, starting a resize drag, travelling toward a
/// panel - the fade began, and 140 ms is not enough time to come back. The chrome is a REGION now
/// (her body plus every chip), and leaving the region starts a grace timer instead of a fade.</para>
///
/// <para><b>Pure on purpose</b>, the way <see cref="EmiNudges"/> and <see cref="EmiRingLayout"/> are
/// pure: no timers, no dispatcher, no <c>App</c>, no visual tree. The window feeds it enters, leaves,
/// holds and one tick, and reads <see cref="Lit"/> back. That is what lets the whole state machine -
/// including the cases a play-test can never reproduce reliably, like a leave that arrives while the
/// grip is captured - be walked in a millisecond by <c>EmiChromeHoverTests</c>.</para>
///
/// <para><b>Every method returns whether <see cref="Lit"/> CHANGED</b>, so the caller starts an
/// animation on a transition and never on a mouse-move. Re-entering the region during the grace is
/// deliberately not a change: the chrome was still lit, so there is nothing to fade back in and no
/// flicker to see.</para>
/// </summary>
public sealed class EmiChromeHover
{
    /// <summary>
    /// How long her chrome stays lit after the pointer leaves the whole region, in milliseconds.
    ///
    /// <para><b>750 ms</b>, and the number is a travel budget, not a taste. The two chips sit in
    /// opposite top corners of a silhouette that is 152 to 420 DIPs wide, so a pointer heading from
    /// the middle of her to a corner - or, worse, arcing outside her and back - is on the move for
    /// somewhere around 250 to 500 ms at ordinary pointer speeds, and it only has to clip the edge
    /// once to have "left". 750 covers that with room to spare while staying under the ~1 s where
    /// chrome that outlives the pointer stops reading as forgiving and starts reading as stuck.
    /// It is also comfortably more than the 140 ms fade, so the fade never starts and reverses.</para>
    /// </summary>
    public const int DefaultGraceMs = 750;

    private readonly int _graceMs;
    private EmiChromePart _over;
    private EmiChromeHold _holds;
    private DateTime? _graceUntil;

    /// <summary>Build the region. The grace is injectable so the tests do not have to wait 750 ms.</summary>
    public EmiChromeHover(int graceMs = DefaultGraceMs)
    {
        _graceMs = Math.Max(0, graceMs);
    }

    /// <summary>Should her chrome be showing right now?</summary>
    public bool Lit { get; private set; }

    /// <summary>The region is empty and the grace is running down. The caller's timer is armed off this.</summary>
    public bool GracePending => _graceUntil.HasValue;

    /// <summary>The configured grace, in milliseconds.</summary>
    public int GraceMs => _graceMs;

    /// <summary>Which parts currently hold the pointer. Exposed for the tests and the log.</summary>
    public EmiChromePart Over => _over;

    /// <summary>Which sticky reasons are currently holding the chrome open.</summary>
    public EmiChromeHold Holds => _holds;

    /// <summary>How long the grace has left, in milliseconds. Zero when none is running.</summary>
    public double GraceRemainingMs(DateTime nowUtc)
    {
        if (!_graceUntil.HasValue) return 0;
        double ms = (_graceUntil.Value - nowUtc).TotalMilliseconds;
        return ms > 0 ? ms : 0;
    }

    /// <summary>The pointer arrived on one of her parts.</summary>
    public bool Enter(EmiChromePart part, DateTime nowUtc)
    {
        if (part == EmiChromePart.None) return false;
        _over |= part;
        return Settle(nowUtc);
    }

    /// <summary>The pointer left one of her parts. The others may still be holding the region open.</summary>
    public bool Leave(EmiChromePart part, DateTime nowUtc)
    {
        if (part == EmiChromePart.None) return false;
        _over &= ~part;
        return Settle(nowUtc);
    }

    /// <summary>Set or clear a sticky reason. Idempotent: the same hold twice is still one hold.</summary>
    public bool Hold(EmiChromeHold reason, bool on, DateTime nowUtc)
    {
        if (reason == EmiChromeHold.None) return false;
        if (on) _holds |= reason;
        else _holds &= ~reason;
        return Settle(nowUtc);
    }

    /// <summary>The grace timer fired. Expires it if it really is up; a no-op if it is not.</summary>
    public bool Tick(DateTime nowUtc) => Settle(nowUtc);

    /// <summary>
    /// Forget everything: she is going away, or coming back from a dismiss.
    ///
    /// <para>Load-bearing on the summon path. A leave that never arrived because the window was
    /// hidden out from under the pointer would otherwise leave <see cref="EmiChromePart.Body"/>
    /// latched forever, and she would come back next summon with her chrome already lit and no
    /// pointer anywhere near her.</para>
    /// </summary>
    public bool Reset()
    {
        _over = EmiChromePart.None;
        _holds = EmiChromeHold.None;
        _graceUntil = null;
        bool changed = Lit;
        Lit = false;
        return changed;
    }

    /// <summary>
    /// The one place the answer is computed, so no caller can invent a fourth rule.
    ///
    /// <para>Order matters: an occupied region CANCELS a running grace rather than sitting alongside
    /// it, which is what makes a leave-and-return cost nothing. A grace is armed only on the edge
    /// where an occupied region empties while lit - never from a cold start, or a stray leave for a
    /// part that was not held would arm 750 ms of chrome on an idle widget.</para>
    /// </summary>
    private bool Settle(DateTime nowUtc)
    {
        bool occupied = _over != EmiChromePart.None || _holds != EmiChromeHold.None;

        if (occupied)
        {
            _graceUntil = null;
        }
        else if (Lit && !_graceUntil.HasValue && _graceMs > 0)
        {
            _graceUntil = nowUtc.AddMilliseconds(_graceMs);
        }
        else if (_graceUntil.HasValue && nowUtc >= _graceUntil.Value)
        {
            _graceUntil = null;
        }

        bool lit = occupied || _graceUntil.HasValue;
        bool changed = lit != Lit;
        Lit = lit;
        return changed;
    }
}
