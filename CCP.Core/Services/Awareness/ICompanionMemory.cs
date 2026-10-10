using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ConditioningControlPanel.Services.Awareness
{
    /// <summary>
    /// A recurring pattern the companion has observed, promoted from ledger trends (doc 02 §3.3).
    ///
    /// <para><b>Train 4 owns promotion.</b> Train 2 never creates one of these; the record exists so
    /// the frame, the projection and the prompt can be written against the final shape now.</para>
    /// </summary>
    /// <param name="Pattern">"late_night_doomscroll", "shopping_no_purchase", "song_on_repeat".</param>
    /// <param name="LastReferenced">
    /// When she last joked about it. Enforces callback spacing (≥48h) so the deep cut does not become
    /// the new repetitive line.
    /// </param>
    /// <param name="Muted">
    /// User muted this habit in the privacy panel. Doc 02 §3.3 sketched this as a nullable label
    /// string; a bool is the honest contract for "the user said stop" and the panel's delete path
    /// covers the rest.
    /// </param>
    public sealed record HabitRecord(
        string Id,
        string AppId,
        string? Cluster,
        string Pattern,
        int OccurrenceCount,
        DateTime FirstSeen,
        DateTime LastSeen,
        DateTime? LastReferenced,
        bool Muted);

    /// <summary>
    /// The seam between awareness and the companion's durable memory (doc 02 §3.3, reconciled by
    /// MASTER-SCOPE §3.2: the store lives in CompanionBrain, awareness is its best customer).
    ///
    /// <para><b>Train 2 ships a stub.</b> <see cref="StubCompanionMemory"/> returns no habits, ever —
    /// promotion (trend seen on ≥3 distinct days → habit) is Train 4 work. Reactions ARE recorded,
    /// because the recent-reaction ban list is a Train 2 feature and needs somewhere to live.</para>
    ///
    /// <para>Running gags are deliberately absent: they are Train 4, and an interface member nobody
    /// can implement usefully is worse than one added later.</para>
    ///
    /// <para>Implementations must never throw. Awareness degrading to "no callbacks" is a worse joke;
    /// awareness degrading to an unhandled exception on a background timer is a crash log.</para>
    /// </summary>
    public interface ICompanionMemory
    {
        /// <summary>Habits matching an app id and/or its cluster, most recently seen first.</summary>
        Task<IReadOnlyList<HabitRecord>> GetHabitsAsync(string appId, string? cluster);

        /// <summary>The last <paramref name="count"/> delivered lines, newest first. The ban list.</summary>
        Task<IReadOnlyList<ReactionSummary>> GetRecentReactionsAsync(int count);

        /// <summary>
        /// Records a line the companion actually delivered. Callers MUST have taken it through
        /// <c>ModerationGuard.CheckOutput</c> first — a stored line is re-injected into later prompts,
        /// so anything the display path would refuse must never reach here.
        /// </summary>
        Task RecordReactionAsync(ReactionSummary line);

        /// <summary>
        /// Erases what this store holds about <paramref name="appId"/>, or everything when it is null.
        ///
        /// <para>The privacy panel's per-app forget and wipe-all buttons call this alongside
        /// <see cref="ActivityLedger.Forget"/> / <see cref="ActivityLedger.Wipe"/>. The ban list is
        /// awareness data too: "what she said about which app and when" is exactly the record a user
        /// pressing wipe means to be rid of, and a surviving ban list would also keep feeding those
        /// lines back into later prompts.</para>
        /// </summary>
        Task ForgetAsync(string? appId);
    }

    /// <summary>
    /// The Train 2 implementation: no habits, and a small in-process ring of delivered lines.
    ///
    /// <para>Nothing is persisted. That is a deliberate default rather than an omission — a file of
    /// "what she said about which app and when" is a behavioural log, and Train 2 has no privacy
    /// panel to show or wipe it with. The ban list only has to survive the session it bans within.</para>
    /// </summary>
    public sealed class StubCompanionMemory : ICompanionMemory
    {
        /// <summary>How many delivered lines the ban list remembers (doc 02 §3.1 item 4: "last ~10").</summary>
        public const int RingCapacity = 10;

        private static readonly Task<IReadOnlyList<HabitRecord>> NoHabits =
            Task.FromResult<IReadOnlyList<HabitRecord>>(Array.Empty<HabitRecord>());

        private readonly object _lock = new();
        private readonly LinkedList<ReactionSummary> _recent = new();

        /// <inheritdoc />
        public Task<IReadOnlyList<HabitRecord>> GetHabitsAsync(string appId, string? cluster) => NoHabits;

        /// <inheritdoc />
        public Task<IReadOnlyList<ReactionSummary>> GetRecentReactionsAsync(int count)
        {
            if (count <= 0) return Task.FromResult<IReadOnlyList<ReactionSummary>>(Array.Empty<ReactionSummary>());

            List<ReactionSummary> result;
            lock (_lock)
            {
                result = new List<ReactionSummary>(Math.Min(count, _recent.Count));
                foreach (var line in _recent)
                {
                    if (result.Count >= count) break;
                    result.Add(line);
                }
            }
            return Task.FromResult<IReadOnlyList<ReactionSummary>>(result);
        }

        /// <inheritdoc />
        public Task RecordReactionAsync(ReactionSummary line)
        {
            if (line == null || string.IsNullOrWhiteSpace(line.Text)) return Task.CompletedTask;
            lock (_lock)
            {
                _recent.AddFirst(line);
                while (_recent.Count > RingCapacity) _recent.RemoveLast();
            }
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task ForgetAsync(string? appId)
        {
            lock (_lock)
            {
                if (string.IsNullOrWhiteSpace(appId))
                {
                    _recent.Clear();
                }
                else
                {
                    var id = AwarenessText.SanitizeId(appId);
                    var node = _recent.First;
                    while (node != null)
                    {
                        var next = node.Next;
                        if (string.Equals(AwarenessText.SanitizeId(node.Value.AppId), id, StringComparison.OrdinalIgnoreCase))
                        {
                            _recent.Remove(node);
                        }
                        node = next;
                    }
                }
            }
            return Task.CompletedTask;
        }
    }
}
