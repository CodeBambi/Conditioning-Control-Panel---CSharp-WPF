using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Serialises every suite that mutates awareness's PROCESS-WIDE statics — <c>AwarenessLive</c>'s
/// ledger/memory/last-frame seam, <c>AwarenessPause</c>'s pause window, and
/// <c>AwarenessV2Routing</c>'s attached arbiter.
///
/// <para>These are deliberately static: the privacy panel has to reach the one live ledger, the
/// legacy mouth has to be able to ask "does v2 own speech right now" from a 1.5s poll, and a pause
/// has to be one pause. That design is right for the app and hostile to xUnit, which runs test
/// classes in parallel by default — so a pause set in one suite is visible to another suite's
/// unrelated privacy assertion, and it fails looking exactly like a flake.</para>
///
/// <para>Matches <see cref="CompanionWpfRenderCollection"/>, which exists for the same reason on the
/// WPF side. Everything else in the assembly still runs in parallel.</para>
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class AwarenessStaticsCollection
{
    public const string Name = "AwarenessStatics";
}
