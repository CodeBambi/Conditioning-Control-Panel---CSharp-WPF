using System;
using System.Reflection;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.AvatarTube;
using ConditioningControlPanel.Services.Companion.Asks;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>hunt3 IC3: a tube that showed an ask card hooked a lambda on the CompanionAskService
/// singleton and never took it off, so every closed tube stayed alive. Closing releases it.</summary>
[Collection(RunsAloneCollection.Name)]
public sealed class TubeAskHookReleaseTests
{
    private static int Subscribers()
    {
        var f = typeof(CompanionAskService).GetField("CardChanged", BindingFlags.Instance | BindingFlags.NonPublic)!;
        return (f.GetValue(CompanionAskService.Instance) as Delegate)?.GetInvocationList().Length ?? 0;
    }

    [Fact]
    public void ClosingTheTubeTakesItsAskHookOffTheSingleton() => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var before = Subscribers();
        var tube = new AvatarTubeWindow(null);
        try
        {
            tube.Show();
            Dispatcher.UIThread.RunJobs();
            tube.HookAskCards();
            tube.HookAskCards();                       // once per window, however many cards
            Assert.Equal(before + 1, Subscribers());
        }
        finally
        {
            tube.Close();
            Dispatcher.UIThread.RunJobs();
        }
        Assert.Equal(before, Subscribers());
    });
}
