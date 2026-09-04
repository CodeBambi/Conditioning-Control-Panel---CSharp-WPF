using System;

namespace ConditioningControlPanel
{
    /// <summary>
    /// The lock-card <b>surface</b> seam: put a card on screen. The other half of the split -
    /// when a card is due and which phrase it carries - is portable and lives in
    /// <see cref="Services.LockCardScheduler"/>.
    ///
    /// <para>Showing one is not portable in any part. A lock card is an ownerless topmost cover on
    /// every monitor with input synced across them; on the WPF head it must additionally clear the
    /// interaction queue and a visible pop quiz, and its defer-and-replay policy is written against
    /// <c>App.InteractionQueue</c>, <c>LockCardWindow</c> and <c>PopQuizWindow</c>. So this seam is
    /// one delegate and the head supplies all of it - repeats, strict, voice and the phrase draw
    /// included, because each head resolves those at the moment it actually shows the card.</para>
    ///
    /// <para><b>Unseeded is honest, not degraded.</b> A head with no lock-card window shows no lock
    /// card; nothing is gated on this, nothing is unlocked by its absence, and the scheduler goes on
    /// keeping correct time behind it. That is strictly the same outcome as the WPF original when
    /// <c>App.LockCard</c> was null, which every call site there already writes as
    /// <c>App.LockCard?.</c>.</para>
    /// </summary>
    public static class CoreLockCard
    {
        /// <summary>
        /// Show a lock card now. <c>isTest</c> is the dashboard's Test button: the head's window
        /// treats a test card as dismissible and awards nothing for it.
        ///
        /// <para>The head is expected to hop to its own UI thread, refuse to stack a second card,
        /// draw the phrase through <see cref="Services.LockCardScheduler.PickPhrase"/> and read
        /// repeats/strict/voice from <see cref="CoreSettings"/>.</para>
        /// </summary>
        public static volatile Action<bool>? ShowHandler;

        /// <summary>Ask the head to show a card. Never throws; a head that is tearing down must not
        /// take the scheduler with it.</summary>
        public static void Show(bool isTest = false)
        {
            try { ShowHandler?.Invoke(isTest); } catch { }
        }
    }
}
