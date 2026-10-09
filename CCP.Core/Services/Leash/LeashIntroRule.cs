using System;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services.Leash;

/// <summary>Which end of the leash the explainer is written for.</summary>
public enum LeashIntroSide { Holder, Leashed }

/// <summary>What just happened on a surface that carried the first-time explainer.</summary>
public enum LeashIntroMoment
{
    /// <summary>The holder pressed Offer on the explainer and the offer went out.</summary>
    OfferSent,
    /// <summary>The holder closed the explainer without offering.</summary>
    OfferDismissed,
    /// <summary>The leashed side answered the ask card (put it on or said no).</summary>
    AskAnswered,
    /// <summary>The ask card closed without an answer (app closed, card dismissed).</summary>
    AskDismissed,
}

/// <summary>
/// The first-time rule for the leash explainer (owner, 2026-09-26: "show it the first time they
/// use or receive the leash, so they can make an informed decision"). Pure: the flags come in,
/// a decision goes out.
///
/// <para>Leashed side: the first ask card carries the explainer above the intensity switch, and
/// "Put it on" stays disabled for <see cref="AskReadMs"/> after the explainer is on screen, so
/// nobody can say yes before the picture has had a glance. Holder side: the first offer opens the
/// explainer with an Offer button; the offer is sent only from there.</para>
///
/// <para>A flag is set only when the decision was actually made with the explainer in view
/// (an answer, or an offer sent). Closing without deciding leaves it unset, so the next time
/// shows it again.</para>
/// </summary>
public static class LeashIntroRule
{
    /// <summary>How long "Put it on" waits on a first ask card. Long enough to take in four
    /// pictures and two lines, short enough not to read as a punishment.</summary>
    public const int AskReadMs = 2500;

    /// <summary>Does this side still owe a look at the explainer?</summary>
    public static bool ShouldExplain(LeashIntroSide side, bool seenHolder, bool seenLeashed)
        => side == LeashIntroSide.Holder ? !seenHolder : !seenLeashed;

    /// <summary>May "Put it on" be pressed yet? <paramref name="explainerOnScreen"/> is how long the
    /// explainer has been visible on this card (zero when it has not been shown).</summary>
    public static bool PutItOnEnabled(bool seenLeashed, TimeSpan explainerOnScreen)
        => seenLeashed || explainerOnScreen >= TimeSpan.FromMilliseconds(AskReadMs);

    /// <summary>How much longer "Put it on" waits. Zero when it may be pressed now.</summary>
    public static TimeSpan PutItOnWait(bool seenLeashed, TimeSpan explainerOnScreen)
    {
        if (PutItOnEnabled(seenLeashed, explainerOnScreen)) return TimeSpan.Zero;
        var left = TimeSpan.FromMilliseconds(AskReadMs) - (explainerOnScreen < TimeSpan.Zero ? TimeSpan.Zero : explainerOnScreen);
        return left < TimeSpan.Zero ? TimeSpan.Zero : left;
    }

    /// <summary>Which flag a moment sets, if any.</summary>
    public static LeashIntroSide? MarksSeen(LeashIntroMoment moment) => moment switch
    {
        LeashIntroMoment.OfferSent => LeashIntroSide.Holder,
        LeashIntroMoment.AskAnswered => LeashIntroSide.Leashed,
        _ => null,
    };

    // ---- the settings adapters (thin, so the surfaces never touch the flags by hand) ----

    /// <summary>Should <paramref name="side"/> see the explainer now? Null settings = yes (a
    /// fresh install and a broken settings file both deserve the explanation).</summary>
    public static bool ShouldExplain(AppSettings? s, LeashIntroSide side)
        => s == null || ShouldExplain(side, s.LeashIntroSeenHolder, s.LeashIntroSeenLeashed);

    /// <summary>Record a moment. Returns true when a flag flipped (the caller saves settings).</summary>
    public static bool Record(AppSettings? s, LeashIntroMoment moment)
    {
        if (s == null) return false;
        switch (MarksSeen(moment))
        {
            case LeashIntroSide.Holder when !s.LeashIntroSeenHolder:
                s.LeashIntroSeenHolder = true;
                return true;
            case LeashIntroSide.Leashed when !s.LeashIntroSeenLeashed:
                s.LeashIntroSeenLeashed = true;
                return true;
            default:
                return false;
        }
    }
}
