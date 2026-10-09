using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>
/// Tests that change something process-wide (the storage provider binding) or that read process-wide
/// state (which shells are still alive) while they yield. Every Avalonia test shares one UI thread, so
/// another class's test runs in that gap: a shell opened there picked up the fake storage provider
/// (WindowDropTests, "NotSupportedException: TryGetFileFromPathAsync"), or read as a leaked shell.
/// xunit runs this collection on its own, after the parallel ones.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class RunsAloneCollection
{
    public const string Name = "Runs alone";
}
