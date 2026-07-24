using ConditioningControlPanel.Services;
using Xunit;
using static ConditioningControlPanel.Services.LockCardService;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// #676 — the decision half of the lock-card defer-and-replay fix. The full ShowLockCard path creates
/// WPF windows and can't run headlessly, but its policy is extracted into the pure
/// <see cref="LockCardService.ResolveBlockedCardAction"/>: when a card is already open, defer once via the
/// interaction queue; if the dequeued replay STILL finds a card open, drop it (one re-defer cap) so a
/// close/hide race can't bounce forever and wedge the slot until the 5-minute stuck backstop.
/// </summary>
public class LockCardDeferTests
{
    [Fact]
    public void NoCardOpen_Proceeds()
        => Assert.Equal(BlockedCardAction.Proceed,
            ResolveBlockedCardAction(cardAlreadyOpen: false, isDeferredReplay: false, hasInteractionQueue: true));

    [Fact]
    public void NoCardOpen_ProceedsEvenAsAReplay()
        => Assert.Equal(BlockedCardAction.Proceed,
            ResolveBlockedCardAction(cardAlreadyOpen: false, isDeferredReplay: true, hasInteractionQueue: true));

    [Fact]
    public void CardOpen_FirstRequest_DefersToQueue()
        => Assert.Equal(BlockedCardAction.Defer,
            ResolveBlockedCardAction(cardAlreadyOpen: true, isDeferredReplay: false, hasInteractionQueue: true));

    [Fact]
    public void CardOpen_ReplayStillBlocked_DropsAfterOneReDefer()
        => Assert.Equal(BlockedCardAction.DropAfterReDefer,
            ResolveBlockedCardAction(cardAlreadyOpen: true, isDeferredReplay: true, hasInteractionQueue: true));

    [Fact]
    public void ReDeferCap_IgnoresQueuePresence()
        // Once we're the replay, we drop-and-release regardless of the queue (Complete is null-safe).
        => Assert.Equal(BlockedCardAction.DropAfterReDefer,
            ResolveBlockedCardAction(cardAlreadyOpen: true, isDeferredReplay: true, hasInteractionQueue: false));

    [Fact]
    public void CardOpen_NoQueue_Drops()
        => Assert.Equal(BlockedCardAction.DropNoQueue,
            ResolveBlockedCardAction(cardAlreadyOpen: true, isDeferredReplay: false, hasInteractionQueue: false));
}
