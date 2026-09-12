using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Serialises every suite that swaps <see cref="ConditioningControlPanel.Services.InteractionQueueService.TriggerScheduler"/>,
/// the PROCESS-WIDE seam that decides how a dequeued replay is scheduled. It is static because the
/// queue is a singleton in the app and the replay path has no instance to hang a hook on; that is
/// right for the app and hostile to xUnit's default parallelism. Two suites now use the seam
/// (InteractionQueueTests, from the queue's own regression set, and ChaosVideoStartWindowTests,
/// which drives the real queue), and in parallel the first to finish restored the DEFAULT
/// dispatcher scheduler under the other, whose replays then silently no-opped with no WPF
/// Application present. One collection, no race.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class InteractionQueueSchedulerCollection
{
    public const string Name = "InteractionQueueScheduler";
}
