using System;
using System.Collections.Generic;

namespace ConditioningControlPanel.Models;

/// <summary>
/// One intake that has been completed but whose drafted session has not been run yet - a
/// half-earned stamp. Kept as a list rather than a single slot because a patron can run
/// several intakes back to back and then work through the sessions afterwards; a single slot
/// would silently drop every draft but the newest.
/// </summary>
public class PendingIntakeDraft
{
    /// <summary>Id of the session the intake drafted. Matched against the running session, so
    /// only the session this intake actually produced can redeem the stamp.</summary>
    public string SessionId { get; set; } = "";

    /// <summary>When the intake was completed. Only used to evict the oldest entry when the
    /// list is full, and to age out drafts whose session file the user has since deleted.</summary>
    public DateTime DraftedUtc { get; set; }
}

/// <summary>
/// Persisted state for the intake punch card. Written to
/// <c>%LOCALAPPDATA%\ConditioningControlPanel\intake_punchcard.json</c>.
///
/// Deliberately NOT part of QuestProgress: quest progress is wiped on daily/weekly rollover,
/// and this card has to survive months of them.
/// </summary>
public class IntakePunchCardState
{
    /// <summary>Bumped only when a shape change needs migrating. QuestService has no versioning
    /// and recovers from drift by falling back to a fresh object; that is fine for quests, which
    /// regenerate in a day, and not fine here, where a reset costs the user six weeks. The field
    /// exists so a future migration has something to branch on instead of discarding the card.</summary>
    public int SchemaVersion { get; set; } = 1;

    /// <summary>When this card was opened. Null means no card exists yet.</summary>
    public DateTime? CardStartedUtc { get; set; }

    /// <summary>How many of the eight holes are punched.</summary>
    public int PunchedCount { get; set; }

    /// <summary>Timestamp per punched hole, oldest first. Display/history only -
    /// <see cref="PunchedCount"/> is the authority, so a truncated list can never
    /// silently un-punch the card.</summary>
    public List<DateTime> PunchedUtc { get; set; } = new();

    /// <summary>Completed intakes still waiting on their session to be run.</summary>
    public List<PendingIntakeDraft> PendingDrafts { get; set; } = new();

    /// <summary>When the eighth hole landed. Non-null means the card is full.</summary>
    public DateTime? CompletedUtc { get; set; }

    /// <summary>When the prize was handed over. Separate from <see cref="CompletedUtc"/> because
    /// the prize is still TBD: the card can be finishable and celebrated before anything is
    /// actually granted, and whatever the reward turns out to be, it is claimed exactly once.</summary>
    public DateTime? PrizeClaimedUtc { get; set; }
}
