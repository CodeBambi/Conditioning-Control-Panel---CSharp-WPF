using System;
using System.IO;

namespace ConditioningControlPanel.Services.Awareness
{
    /// <summary>
    /// The seam between the observer and the trust surface — and the one place that knows how to erase
    /// everything awareness has ever created.
    ///
    /// <para><b>Why a seam at all.</b> The privacy panel has to show the user the LAST FRAME THAT WAS
    /// ACTUALLY CUT, rendered as the actual cloud projection, and it must be able to erase the ledger
    /// the observer is holding open. Both need a reference to state the panel does not own and must not
    /// duplicate: a second <see cref="ActivityLedger"/> over the same file would corrupt it, and a
    /// panel that re-derived "what she can see" from its own guesses would be showing the user a
    /// reconstruction while calling it the wire format.</para>
    ///
    /// <para><b>What the observer package does with it.</b> Set <see cref="Ledger"/> and
    /// <see cref="Memory"/> once when they are constructed, and call <see cref="Publish"/> at the end of
    /// the pipeline with every frame it cuts. Nothing here starts, stops or drives anything; it is a
    /// notice board with a delete button.</para>
    ///
    /// <para>Everything is null-safe and never throws: before the observer package lands,
    /// <see cref="LastFrame"/> is null and the panel says so out loud rather than inventing a frame.</para>
    /// </summary>
    public static class AwarenessLive
    {
        private static readonly object Lock = new();
        private static ContextFrame? _lastFrame;
        private static DateTime? _lastFrameAt;

        /// <summary>The live ledger, set by whoever owns the observer. Null before it is constructed.</summary>
        public static ActivityLedger? Ledger { get; set; }

        /// <summary>The habit/reaction store behind her callbacks. Null until the memory owner sets it.</summary>
        public static ICompanionMemory? Memory { get; set; }

        /// <summary>
        /// Clears the observer's in-RAM state; set by whoever owns the observer, to
        /// <c>AwarenessObserver.ResetTransientState</c>.
        ///
        /// <para><b>Why the wipe is incomplete without it.</b> The ledger and the memory live on disk;
        /// the observer additionally holds a pending candidate, the committed app and cluster, the
        /// churn counter, the fullscreen/wake flags, the media loop counter and its own last frame —
        /// none of which are files. Erasing only the files would leave her able to say something about
        /// the very thing the user just erased, which is the one moment this feature cannot survive.</para>
        /// </summary>
        public static Action? ResetObserverState { get; set; }

        /// <summary>
        /// Clears the PACING state — the arbiter's cooldown ledger and the worthiness scorer's
        /// floating threshold and per-app repetition counters. Set by whoever owns the arbiter.
        ///
        /// <para>Small, in RAM and self-expiring, but <see cref="WipeEverything"/> claims to erase
        /// every artifact this feature creates, and these are two of them: after a wipe the ledger
        /// would still be holding "you were told about this app nine minutes ago" for up to ten more
        /// minutes, and the scorer a decaying counter keyed by that same app id for up to thirty.</para>
        /// </summary>
        public static Action? ResetPacingState { get; set; }

        /// <summary>
        /// Drops one app from the pacing state. The per-app half of <see cref="ResetPacingState"/>,
        /// used by <see cref="Forget"/>.
        /// </summary>
        public static Action<string>? ForgetPacingState { get; set; }

        /// <summary>The last frame that was actually cut, or null when none has been.</summary>
        public static ContextFrame? LastFrame
        {
            get { lock (Lock) return _lastFrame; }
        }

        /// <summary>When <see cref="LastFrame"/> was published (local time), or null.</summary>
        public static DateTime? LastFrameAt
        {
            get { lock (Lock) return _lastFrameAt; }
        }

        /// <summary>Raised after a frame is published, so an open panel can re-read without polling harder.</summary>
        public static event EventHandler? FramePublished;

        /// <summary>Records the frame the observer just cut. Null is ignored rather than clearing.</summary>
        public static void Publish(ContextFrame? frame)
        {
            if (frame == null) return;

            lock (Lock)
            {
                _lastFrame = frame;
                _lastFrameAt = frame.CutAt == default ? DateTime.Now : frame.CutAt;
            }

            try { FramePublished?.Invoke(null, EventArgs.Empty); }
            catch (Exception ex) { App.Logger?.Debug("AwarenessLive: a FramePublished subscriber threw: {E}", ex.Message); }
        }

        /// <summary>Forgets the last frame. Part of <see cref="WipeEverything"/>; separate so tests can be blunt.</summary>
        public static void Clear()
        {
            lock (Lock)
            {
                _lastFrame = null;
                _lastFrameAt = null;
            }
        }

        /// <summary>
        /// THE wipe. Erases every artifact this feature creates, because a purge that misses one of them
        /// is a purge that failed:
        /// <list type="number">
        ///   <item><c>awareness_ledger.json</c> — the counters, streaks and histograms;</item>
        ///   <item>its <c>.tmp</c> sibling, the full copy an interrupted atomic write leaves behind and
        ///   which no other code path deletes;</item>
        ///   <item>the ledger's in-memory state: per-app days, the night histogram, open and suspended
        ///   visits, the one-shot trend guards and the session ring of transitions;</item>
        ///   <item>the pending debounced save, cancelled — otherwise a queued write resurrects all of it;</item>
        ///   <item>the last published frame held here for the panel's wire view;</item>
        ///   <item>the recent-reaction ban list and any habits, via
        ///   <see cref="ICompanionMemory.ForgetAsync"/> with a null app id;</item>
        ///   <item>the observer's in-RAM state — pending candidate, committed app and cluster, churn
        ///   counter, fullscreen/wake flags, media loop counter, its own last frame — via
        ///   <see cref="ResetObserverState"/>. None of these are files, so nothing above reaches them;</item>
        ///   <item>the pacing state — the arbiter's cooldown ledger (per-app last-spoke times, the
        ///   hourly line list) and the scorer's threshold bump and repetition counters — via
        ///   <see cref="ResetPacingState"/>. Also not files, and also keyed by the erased app ids.</item>
        /// </list>
        ///
        /// <para>Deliberately NOT in scope, and the panel's copy says so rather than implying otherwise:
        /// chat memory and the conversation log, which the memory diary owns and wipes with its own
        /// button.</para>
        ///
        /// <para>When no ledger has been constructed yet — awareness has never run this session — the
        /// files are still removed from disk directly, because "there is nothing loaded" is not the same
        /// as "there is nothing there".</para>
        /// </summary>
        public static void WipeEverything()
        {
            var ledger = Ledger;
            if (ledger != null)
            {
                try { ledger.Wipe(); }
                catch (Exception ex) { App.Logger?.Warning(ex, "AwarenessLive: ledger wipe failed"); }
            }
            else
            {
                WipeFilesDirectly();
            }

            var memory = Memory;
            if (memory != null)
            {
                try { _ = memory.ForgetAsync(null); }
                catch (Exception ex) { App.Logger?.Warning(ex, "AwarenessLive: memory forget failed"); }
            }

            // The in-RAM half of the erasure. Without this the observer still holds the app it had
            // committed to and the candidate it was about to cut, and could speak about what was just
            // wiped on its very next tick.
            try { ResetObserverState?.Invoke(); }
            catch (Exception ex) { App.Logger?.Warning(ex, "AwarenessLive: observer state reset failed"); }

            // The other in-RAM half: the cooldown ledger's per-app last-spoke times and the scorer's
            // repetition counters, both keyed by the app ids that were just erased.
            try { ResetPacingState?.Invoke(); }
            catch (Exception ex) { App.Logger?.Warning(ex, "AwarenessLive: pacing state reset failed"); }

            Clear();
            App.Logger?.Information("Awareness: everything she noticed has been erased");
        }

        /// <summary>
        /// Forgets one app everywhere: the ledger's counters and ring, the memory's habits and lines
        /// about it, and the panel's last frame when it happens to be that app (so the wire view cannot
        /// keep showing what was just forgotten).
        /// </summary>
        public static void Forget(string? appId)
        {
            if (string.IsNullOrWhiteSpace(appId)) return;
            var id = AwarenessText.SanitizeId(appId);

            try { Ledger?.Forget(id); }
            catch (Exception ex) { App.Logger?.Warning(ex, "AwarenessLive: ledger forget failed"); }

            try { _ = Memory?.ForgetAsync(id); }
            catch (Exception ex) { App.Logger?.Warning(ex, "AwarenessLive: memory forget failed"); }

            try { ForgetPacingState?.Invoke(id); }
            catch (Exception ex) { App.Logger?.Warning(ex, "AwarenessLive: pacing forget failed"); }

            lock (Lock)
            {
                if (_lastFrame != null &&
                    string.Equals(AwarenessText.SanitizeId(_lastFrame.AppId), id, StringComparison.OrdinalIgnoreCase))
                {
                    _lastFrame = null;
                    _lastFrameAt = null;
                }
            }
        }

        /// <summary>
        /// Removes the ledger file and its <c>.tmp</c> sibling when no ledger object is holding them.
        /// Same two paths <see cref="ActivityLedger.Wipe"/> deletes, resolved the same way.
        /// </summary>
        private static void WipeFilesDirectly()
        {
            string path;
            try { path = ActivityLedger.DefaultLedgerPath; }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "AwarenessLive: could not resolve the ledger path");
                return;
            }

            foreach (var file in new[] { path, path + ".tmp" })
            {
                try
                {
                    if (File.Exists(file)) File.Delete(file);
                }
                catch (Exception ex)
                {
                    App.Logger?.Warning(ex, "AwarenessLive: failed to delete {File}", file);
                }
            }
        }
    }
}
