using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Controls.Leash;
using ConditioningControlPanel.Services.Leash;
using Newtonsoft.Json.Linq;
using Xunit;
using AppA = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

/// <summary>Wave 5 r11: the snap (WPF 7.1.5 LeashSnapCard + LeashSurfaces.ShowAsk/ShowSnap): Put it
/// on closes the ask card and shows the snap for this side; the replay seam shows it again.</summary>
[Collection(RunsAloneCollection.Name)]   // swaps the LeashOverlay seam
public sealed class LeashSnapTests
{
    private static void Setup()
    {
        if (Application.Current is null)
            AppBuilder.Configure<AppA>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
    }

    private sealed class Api : ILeashApi
    {
        public Task<JObject?> CallAsync(string op, JObject body, CancellationToken ct = default)
            => Task.FromResult<JObject?>(new JObject { ["ok"] = true, ["status"] = "on" });
    }

    private static readonly LeashPerson Vex = new("h1", "Vex", null);

    [Fact]
    public async Task PutItOn_ShowsTheSnap_AndOkClosesIt()
    {
        await AvaloniaTestDispatcher.RunAsync(async () =>
        {
            Setup();
            LeashFx.ForceStill = true;
            Control? up = null;
            LeashOverlay.ShowOverride = c => up = c;
            try
            {
                var svc = new LeashService(new Api(), () => "me");
                var offer = new LeashOffer(Vex, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(1));
                LeashAskCard.Show(offer, () => svc, null);
                var ask = Assert.IsType<LeashAskCard>(up);
                await ask.AnswerAsync(true);
                var snap = Assert.IsType<LeashSnapCard>(LeashOverlay.Current);
                Assert.Contains(snap.GetLogicalDescendants().OfType<Control>(), c => c.Tag as string == "leash-snap-title");
                Assert.Contains(snap.GetLogicalDescendants().OfType<Control>(), c => c.Tag as string == "leash-tag");
                snap.Close();
                Assert.Null(LeashOverlay.Current);

                LeashSnapCard.Replay(null, Vex);
                Assert.IsType<LeashSnapCard>(LeashOverlay.Current);
                LeashOverlay.Close();
            }
            finally { LeashOverlay.ShowOverride = null; }
        });
    }
}
