using System.Collections.Generic;
using System.ComponentModel;
using System.Windows.Input;

namespace ConditioningControlPanel.Views.Controls.Companion
{
    /// <summary>
    /// Z2 — Talk to her. A <i>threshold</i> surface, not a chat app: it proves the thread is real,
    /// lets you say one thing right here, and hands off to the tube chat for long sessions.
    ///
    /// <para>The four states are mutually exclusive and every one of them ships copy:</para>
    /// <list type="bullet">
    ///   <item><b>Live</b> — the real thread, send box enabled.</item>
    ///   <item><b>Dormant</b> (pre-Train 1) — one bubble plus the honest "she forgets every
    ///   conversation the moment it ends… that's about to change." line.</item>
    ///   <item><b>Disabled</b> (provider Off) — dimmed input and a jump link into the Engine Room.</item>
    ///   <item><b>Locked</b> (no AI entitlement) — the flagship teaser: a blurred staged thread
    ///   under a Velvet-Vault veil, a personal sell line, and one CTA chip. The veil is decoration;
    ///   only the chip takes clicks, so the card never blocks tab navigation.</item>
    /// </list>
    /// </summary>
    public interface IChatThresholdVm : INotifyPropertyChanged
    {
        CompanionZoneState State { get; }

        /// <summary>The last ~3 real turns, oldest first. Empty in the locked state.</summary>
        IReadOnlyList<IChatBubbleVm> Turns { get; }

        /// <summary>Static fake bubbles rendered under the veil. Never live content — the blur
        /// runs on this tiny static panel only, never over a scrolling surface.</summary>
        IReadOnlyList<IChatBubbleVm> TeaserTurns { get; }

        /// <summary>Two-way: the send box.</summary>
        string Draft { get; set; }

        /// <summary>A send is in flight — the three-dot pulse runs, the box is read-only.</summary>
        bool IsThinking { get; }

        /// <summary>False dims the input row (provider Off, or locked).</summary>
        bool CanSend { get; }

        /// <summary>"she last heard from you 2h ago" — memory-flavored, shown in the header.</summary>
        string LastHeardCopy { get; }

        /// <summary>Right-aligned footer line ("she remembers this conversation now.").</summary>
        string FooterCopy { get; }

        /// <summary>The in-character copy for whichever non-live state is active.</summary>
        string StateCopy { get; }

        /// <summary>Veil sell line — personalised from the interview profile when one exists.</summary>
        string LockCopy { get; }
        string LockCtaLabel { get; }

        string InputPlaceholder { get; }

        ICommand SendCommand { get; }
        ICommand OpenFullChatCommand { get; }
        /// <summary>Opens the read-only persisted transcript viewer.</summary>
        ICommand HistoryCommand { get; }
        /// <summary>Veil CTA → the Patreon tab.</summary>
        ICommand UnlockCommand { get; }
        /// <summary>"turn her brain on in the Engine Room below." jump link.</summary>
        ICommand OpenEngineRoomCommand { get; }
    }
}
