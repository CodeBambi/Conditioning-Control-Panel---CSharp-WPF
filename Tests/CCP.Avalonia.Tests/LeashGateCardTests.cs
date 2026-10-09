using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Controls.Leash;
using ConditioningControlPanel.Services.Leash;
using Xunit;
using AppA = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

/// <summary>Wave 5 r12: the WPF 7.1.5 leash gate card on this head. Panic and Cut leash are on the
/// card always and never disabled, whatever the task is doing.</summary>
[Collection(RunsAloneCollection.Name)]
public sealed class LeashGateCardTests
{
    private static void Setup()
    {
        if (Application.Current is null)
            AppBuilder.Configure<AppA>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        LeashFx.ForceStill = true;
    }

    private static Punishment Lines(string pid = "p1", int size = 5) => new(pid, PunishKind.Lines, size, null,
        new LeashPerson("h1", "Vex", null), DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddHours(20));

    private static Button Btn(LeashGateCard gate, string tag) =>
        gate.GetLogicalDescendants().OfType<Button>().Single(b => (b.Tag as string) == tag);

    [Fact]
    public async Task Gate_ShowsTheTask_AndPanicAndCutAreAlwaysLive()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            Setup();
            try
            {
                var gate = new LeashGateCard();
                Assert.False(gate.IsUp);
                int panic = 0, cut = 0, start = 0, pardon = 0;
                gate.PanicRequested += () => panic++;
                gate.CutRequested += () => cut++;
                gate.StartRequested += _ => start++;
                gate.PardonRequested += _ => pardon++;

                gate.Present(Lines(), pardons: 1);
                Assert.True(gate.IsUp);
                Assert.Equal("p1", gate.Pid);
                Assert.Contains(gate.GetLogicalDescendants().OfType<Control>(), c => (c.Tag as string) == "leash-stamp");
                Assert.Contains(gate.GetLogicalDescendants().OfType<Control>(), c => (c.Tag as string) == "leash-gate-pips");

                Btn(gate, "leash-gate-go").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Btn(gate, "leash-gate-pardon").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal((1, 1), (start, pardon));

                // Running: the action greys, the two ways out do not.
                gate.SetProgress(2, 5, running: true);
                Assert.True(gate.Running);
                Assert.False(Btn(gate, "leash-gate-go").IsEnabled);
                Assert.Equal(2, gate.GetLogicalDescendants().OfType<Border>().Count(b => (b.Tag as string) == "leash-pip-done"));
                Assert.True(Btn(gate, "leash-gate-panic").IsEnabled);
                Assert.True(Btn(gate, "leash-gate-cut").IsEnabled);
                Btn(gate, "leash-gate-panic").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Btn(gate, "leash-gate-cut").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal((1, 1), (panic, cut));

                gate.StopRunning();
                Assert.True(Btn(gate, "leash-gate-go").IsEnabled);
                Assert.Equal((2, 5), gate.ProgressShown);

                // A video that will not play: no action, "Not now" and both exits stay.
                int later = 0;
                gate.LaterRequested += () => later++;
                gate.Present(Lines(), pardons: 0, unplayable: true);
                Assert.True(gate.ShowsUnplayable);
                Assert.DoesNotContain(gate.GetLogicalDescendants().OfType<Button>(), b => (b.Tag as string) == "leash-gate-go");
                Btn(gate, "leash-gate-later").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(1, later);
                Assert.True(Btn(gate, "leash-gate-panic").IsEnabled);
                Assert.True(Btn(gate, "leash-gate-cut").IsEnabled);

                gate.Dismiss();
                Assert.False(gate.IsUp);
            }
            finally { LeashFx.ForceStill = false; }
            return Task.CompletedTask;
        });
    }
}
