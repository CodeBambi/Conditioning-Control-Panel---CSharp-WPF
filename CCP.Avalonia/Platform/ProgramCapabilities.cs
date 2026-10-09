using System.Collections.Generic;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Models.Program;

namespace ConditioningControlPanel.Avalonia.Platform
{
    /// <summary>
    /// Which program task signals this head can raise (programs-3a decision, docs/avalonia-decisions.md).
    /// A static table, not runtime registration: a signal that simply has not fired yet must not look
    /// unavailable. Each category here has a reachable QuestService.Track* call in the head or Core
    /// (ProgramCapabilitiesTests scans for it). Seeded into CoreProgram.TaskAvailableProvider at startup.
    /// </summary>
    internal static class ProgramCapabilities
    {
        internal static readonly IReadOnlySet<QuestCategory> Raised = new HashSet<QuestCategory>
        {
            QuestCategory.Flash, QuestCategory.Video, QuestCategory.Spiral, QuestCategory.PinkFilter,
            QuestCategory.Bubbles, QuestCategory.LockCard, QuestCategory.BubbleCount, QuestCategory.Mantra,
            QuestCategory.Autonomy, QuestCategory.Lockdown, QuestCategory.BlinkTrainer,
            // Not raised: KeywordTrigger (engine not ported), Remote/RemoteIssue, Session, Streak, Combined.
        };

        /// <summary>Ritual tasks need SubmitRitualTask (programs slice 3b flips this).</summary>
        internal static bool RitualsAvailable = false;

        internal static bool IsAvailable(ProgramTask task) =>
            task.Kind == ProgramTaskKind.Ritual ? RitualsAvailable
            : task.Verifier is { } category && Raised.Contains(category);
    }
}
