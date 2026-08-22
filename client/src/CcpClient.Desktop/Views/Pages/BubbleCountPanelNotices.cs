using CcpClient.Desktop.Capabilities;
using CcpClient.Desktop.Effects;
using CcpClient.Desktop.Session;

namespace CcpClient.Desktop.Views.Pages;

/// <summary>
/// The Bubble Count panel's sentences, in their own file for the same reason
/// <see cref="InputPanelNotices"/>, <see cref="VideoPanelNotices"/> and
/// <see cref="AudioPanelNotices"/> are in theirs.
///
/// <para><b>This panel has to say something no other panel says: the row uses TWO capabilities, and
/// either can be missing on its own.</b> Every other row on this page reports one channel. A game
/// that can show its clip and cannot ask its question is not the same failure as one that can ask a
/// question it has nothing to ask about, and a single sentence covering both would be false for one
/// of them — the panel defect three earlier rows each shipped exactly once.</para>
/// </summary>
public static class BubbleCountPanelNotices
{
    /// <summary>
    /// The warning that leads the panel. Like the Lock Card's, it is a constant rather than inline
    /// AXAML text so a fact can pin the exact words — and it says both halves, because this module
    /// covers part of the screen AND then takes the keyboard.
    /// </summary>
    public const string InterruptionNotice =
        "This module INTERRUPTS you twice. First it plays one of your own clips on a panel over your "
        + "screen with bubbles drifting across it, and then it takes the keyboard and asks you how many "
        + "there were. Esc always closes the question in this build, and getting it wrong just ends the "
        + "game.";

    /// <summary>
    /// What is NOT ported, in the user's own words, rendered where they read it before enabling
    /// anything. Same discipline as the Brain Drain and Mandatory Video half-row notices.
    /// </summary>
    public const string ScopeNotice =
        "Scope: one bounded panel on the primary display, silent, with three tries at the answer. "
        + "Upstream covers every monitor, plays the clip's sound, and in strict mode makes you WATCH IT "
        + "AGAIN until you get it right or it grants mercy — none of that is in this build, so there is "
        + "no strict switch here to turn on. The bubbles are drawn INTO the picture rather than as "
        + "separate windows over it, which is what lets this build prove the operating system was really "
        + "holding them.";

    /// <summary>
    /// The live-state line, off the row's own dot plus its counters. The Armed arms are separate
    /// sentences for the reason the first such panel established and every module since has kept: "armed because no
    /// session is running" and "armed DURING a session because a channel is gone" are different
    /// situations, and telling the second to start a session they already started is the exact
    /// message that had to be split apart.
    /// </summary>
    public static string DescribeGameState(
        EffectDotState dot,
        int playedCount,
        BubbleCountEvent? last,
        bool sessionRunning,
        bool canReachADisplay,
        bool canReachAUser,
        bool playing,
        bool asking,
        BubbleCountResolution lastResolution)
    {
        if (dot == EffectDotState.Off)
        {
            return "Switched off. No game will start, session or no session.";
        }

        if (dot == EffectDotState.Armed && !sessionRunning)
        {
            return "Armed. No game starts until the session starts.";
        }

        if (dot == EffectDotState.Armed && !canReachADisplay && !canReachAUser)
        {
            return "Running, but neither half of this game can reach you: the operating system reports no "
                + "surface to play a clip on and no desktop to ask a question on. See below for what was "
                + "asked and what came back.";
        }

        if (dot == EffectDotState.Armed && !canReachADisplay)
        {
            return "Running, but nothing it plays can reach a screen: the operating system reports no "
                + "surface for a video. See below for what was asked and what came back.";
        }

        if (dot == EffectDotState.Armed && !canReachAUser)
        {
            return "Running, but it could never ask you the count: the operating system does not report a "
                + "desktop this process can put a window on. See below for what was asked and what came "
                + "back.";
        }

        if (dot == EffectDotState.Armed && playing)
        {
            // THE MOTION HALF OF THE DOT (the dot's seventh meaning), reached through this row.
            return "A clip is on screen and the operating system's own copy of it has STOPPED changing, so "
                + "there is nothing to count. The game ends and the next one comes round on the clock.";
        }

        if (dot == EffectDotState.Armed && asking)
        {
            // THE DEMAND HALF (the dot's sixth meaning), reached through this row.
            return "The question is on screen and the operating system has given the keyboard to another "
                + "window, so nothing you type reaches it. Click the question to give it the keyboard back.";
        }

        if (dot == EffectDotState.Armed)
        {
            return "This module is not scheduled right now, though the session is running. Switching it off "
                + "and on again re-arms it.";
        }

        var head = playing
            ? "Counting now: a clip is on screen and its picture is really moving."
            : asking
                ? "Asking you now: the question is on screen and the operating system is routing your "
                    + "keyboard to it."
                : "Running: the next game is on the clock.";

        if (playedCount == 0)
        {
            return head;
        }

        var ordinal = last is null ? string.Empty : $" The last one was #{last.Value.Ordinal}.";
        var ending = lastResolution switch
        {
            BubbleCountResolution.Counted => " You got the last count right.",
            BubbleCountResolution.Missed => " You used up every try on the last one.",
            BubbleCountResolution.Dismissed => " You closed the last question with Esc.",
            BubbleCountResolution.Withdrawn => " The last one was taken down when the session stopped.",
            BubbleCountResolution.Refused =>
                " The last question was taken straight back down: the operating system would not give it "
                + "the keyboard.",
            // ONE resolution value reached by several causes, and the honest sentence is the one they
            // share: nothing was ever asked, because there was nothing anybody could have counted.
            BubbleCountResolution.Abandoned =>
                " The last clip could not really be watched, so no question was asked and nothing was "
                + "scored.",
            _ => string.Empty,
        };

        return $"{head} {playedCount} game{(playedCount == 1 ? string.Empty : "s")} started so far."
            + $"{ordinal}{ending}";
    }

    /// <summary>
    /// What a game will ask for, from the dial that decides it. The rate is upstream's own
    /// arithmetic, stated in the units a user thinks in.
    /// </summary>
    public static string DescribeDifficulty(BubbleCountDifficulty difficulty) =>
        $"{difficulty}: about {BubbleCountArithmetic.BaseRate(difficulty):0.#} bubbles every "
        + $"{BubbleCountArithmetic.RateWindowSeconds:0} seconds of clip, give or take a fifth. Three tries "
        + "at the answer, with a higher/lower hint after each wrong one.";

    /// <summary>
    /// <b>The line that quotes the operating system about BOTH capabilities</b>, because this row
    /// needs both and either can refuse on its own. Each half reports the capability's own last typed
    /// outcome, never a sentence this page made up about a platform.
    /// </summary>
    public static string DescribeBothCapabilities(CapabilityState? playback, CapabilityState? prompt)
    {
        var video = playback switch
        {
            null => "Video: nothing has been asked of the operating system yet.",
            CapabilityState.Available a => $"Video: the operating system is holding the picture. {a.Detail}",
            CapabilityState.Unavailable u => $"Video: no clip played. {u.Reason.Detail}",
            CapabilityState.DependencyMissing m => $"Video: no clip played. {m.Reason.Detail}",
            CapabilityState.Degraded d => $"Video: partly available: {d.SurvivingSemantics}. {d.Reason.Detail}",
            CapabilityState.PermissionRequired p => $"Video: no clip played. {p.Reason.Detail}",
            CapabilityState.Faulted f => $"Video: no clip played. {f.Reason.Detail}",
            _ => playback.ToString() ?? string.Empty,
        };

        var input = prompt switch
        {
            null => "Question: nothing has been asked of the operating system yet.",
            CapabilityState.Available a => $"Question: the operating system gave it the keyboard. {a.Detail}",
            CapabilityState.Unavailable u => $"Question: none was shown. {u.Reason.Detail}",
            CapabilityState.DependencyMissing m => $"Question: none was shown. {m.Reason.Detail}",
            CapabilityState.Degraded d =>
                $"Question: partly available: {d.SurvivingSemantics}. {d.Reason.Detail}",
            CapabilityState.PermissionRequired p => $"Question: none was shown. {p.Reason.Detail}",
            CapabilityState.Faulted f => $"Question: none was shown. {f.Reason.Detail}",
            _ => prompt.ToString() ?? string.Empty,
        };

        return $"{video} {input}";
    }

    /// <summary>Where the clips come from — the SAME folder Mandatory Video reads, which is
    /// upstream's own arrangement and worth saying out loud so a user does not go looking for a
    /// second one.</summary>
    public static string DescribeClipPool(int clipCount, string folder)
    {
        ArgumentException.ThrowIfNullOrEmpty(folder);
        if (clipCount == 0)
        {
            return $"No clip to play, so no game will start. Drop an .mp4/.mov/.avi/.wmv/.mkv/.webm into "
                + $"{folder} (subfolders count) — it is picked up while the session runs, without "
                + "restarting. This is the same folder Mandatory Video plays from.";
        }

        return $"{clipCount} clip{(clipCount == 1 ? string.Empty : "s")} in {folder} — the same folder "
            + "Mandatory Video plays from. Each row deals its own shuffled order.";
    }
}
