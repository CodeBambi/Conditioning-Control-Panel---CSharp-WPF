using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Serialises every suite that reads or swaps <see cref="ConditioningControlPanel.Services.Safety.GameSurfaces.All"/>,
/// the PROCESS-WIDE list of game surfaces the panic ladder closes. It is static because the host
/// services behind it are static singletons in the app, which is right there and hostile to xUnit's
/// default parallelism: GameSurfacePanicTests swaps the list for fakes, and PanicPolicyTests reads
/// the real one, so in parallel the second would see the first's stand-ins. Same collar as
/// <see cref="InteractionQueueSchedulerCollection"/>.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class GameSurfacesCollection
{
    public const string Name = "GameSurfaces";
}
