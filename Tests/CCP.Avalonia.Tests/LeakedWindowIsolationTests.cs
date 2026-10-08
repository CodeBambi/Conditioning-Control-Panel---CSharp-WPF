using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Skia;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Xunit;
using Xunit.Sdk;
using Xunit.v3;

namespace CCP.Avalonia.Tests;

/// <summary>PLAYBOOK P52: a deliberately leaky pair. A leaves a feature card open and never closes
/// it; B, run right after it in the same testhost, must see no passive window up. Only the
/// assembly hook (<see cref="IsolateProcessStateAttribute"/>) closes A's card in between.</summary>
[TestCaseOrderer(typeof(ByMethodName))]
public sealed class LeakedWindowIsolationTests
{
    private static FeatureIntroPopup? _leaked;

    private static Task OnUi(Action body) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        body();
        return Task.CompletedTask;
    });

    [Fact]
    public Task A_LeavesAFeatureCardOpen() => OnUi(() =>
    {
        _leaked = new FeatureIntroPopup();
        _leaked.Show();
        StartupLadder.BeginFirstLaunchQuiet(TimeSpan.FromMinutes(10));   // ladder state only the hook's reset clears
        Assert.True(StartupLadder.PassiveWindowUp());
        Assert.True(StartupLadder.IsQuiet);
    });

    [Fact]
    public Task B_SeesNoPassiveWindowUp() => OnUi(() =>
    {
        Assert.False(_leaked?.IsVisible ?? false);
        Assert.False(StartupLadder.PassiveWindowUp());
        Assert.False(StartupLadder.IsQuiet);
    });
}

/// <summary>Fixed order for the pair above, whatever CCP_TEST_ORDER_SEED says.</summary>
internal sealed class ByMethodName : ITestCaseOrderer
{
    public IReadOnlyCollection<T> OrderTestCases<T>(IReadOnlyCollection<T> testCases) where T : notnull, ITestCase =>
        testCases.OrderBy(t => t.TestMethod?.MethodName, StringComparer.Ordinal).ToArray();
}
