using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Serializes every suite that realizes a Companion WPF tree on its own STA thread.
///
/// <para><b>Why.</b> These suites all build controls whose XAML merges
/// <c>Themes/CompanionTheme.xaml</c> from a pack URI. WPF caches a <see cref="System.Windows.ResourceDictionary"/>
/// per Source and hands the same instance to every consumer — and a ResourceDictionary is not
/// thread-safe while it is being loaded. With xUnit running test classes in parallel, two STA
/// threads can hit that first load at the same moment and one of them gets a dictionary that is not
/// finished yet, which surfaces as an occasional render failure in whichever suite lost the race.
/// It reads exactly like a flake and is not one.</para>
///
/// <para>The wiring pass added two more of these suites, which made a rare collision a regular one.
/// Putting them in one non-parallel collection costs a couple of seconds and removes the class of
/// failure entirely. Everything else in the assembly still runs in parallel.</para>
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class CompanionWpfRenderCollection
{
    public const string Name = "CompanionWpfRender";
}
