using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Avalonia.Views.Controls.Leash;
using ConditioningControlPanel.Services.Leash;
using Newtonsoft.Json.Linq;
using Xunit;
using AppA = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

/// <summary>Wave 4 r8: the WPF 7.1.5 LeashDrawerSection on this head (offer row, own card with the
/// one-click cut, holder cards) and its single mount into the friends drawer.</summary>
[Collection(RunsAloneCollection.Name)]
public sealed class LeashDrawerSectionTests
{
    private const string Block = """
    {"me":{"holder":{"id":"h1","name":"Vex"},"intensity":"standard","since":"2026-10-01T00:00:00Z","day":3,
      "pending":[{"pid":"p1","kind":"lines","size":5,"from":{"id":"h1","name":"Vex"},"at":"2026-10-09T00:00:00Z","expires_at":"2099-10-12T00:00:00Z"}],
      "pardons":1,"stickers":[]},
     "holding":[{"who":{"id":"k9","name":"Kit"},"online":true,"intensity":"soft","since":"2026-10-02T00:00:00Z","day":2}],
     "offers":[{"id":"o1","from":{"id":"f2","name":"Ash"},"at":"2026-10-09T00:00:00Z","expires_at":"2099-10-12T00:00:00Z"}]}
    """;

    private sealed class Api : ILeashApi
    {
        public readonly List<string> Ops = new();
        public Task<JObject?> CallAsync(string op, JObject body, CancellationToken ct = default)
        {
            Ops.Add(op);
            return Task.FromResult<JObject?>(new JObject { ["ok"] = true });
        }
    }

    private static void Setup()
    {
        if (Application.Current is null)
            AppBuilder.Configure<AppA>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
    }

    private static LeashService Service(Api api, Action cutSafety)
    {
        var svc = new LeashService(api, () => "me", cutSafety: cutSafety);
        svc.ApplyBlock(JObject.Load(new Newtonsoft.Json.JsonTextReader(new System.IO.StringReader(Block)) { DateParseHandling = Newtonsoft.Json.DateParseHandling.None }));
        return svc;
    }

    private static void Click(Button b) => b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static Button ButtonTagged(Control root, string tag) =>
        root.GetLogicalDescendants().OfType<Button>().First(b => (b.Tag as string) == tag);

    [Fact]
    public async Task Section_DrawsOfferSelfAndHolder_InWpfOrder_AndTheCutIsOneClick()
    {
        await AvaloniaTestDispatcher.RunAsync(async () =>
        {
            Setup();
            int safety = 0;
            var api = new Api();
            var svc = Service(api, () => safety++);
            var section = new LeashDrawerSection(() => svc, () => null);

            Assert.True(section.IsVisible);
            var tags = section.Children.Select(c => c.Tag as string).ToList();
            Assert.Equal(new[] { "leash-offer-row:f2", "leash-self-card", "leash-holder-card:k9" }, tags);

            // Own card: the scissors are the first button under the name, then the level and quiet switches.
            var self = section.SelfCard!;
            var buttons = self.GetLogicalDescendants().OfType<Button>().Select(b => b.Tag as string).ToList();
            Assert.Equal("leash-cut", buttons.First(t => t?.StartsWith("leash-help") != true));   // the "?" sits in the head, as WPF
            Assert.Contains("leash-level:1:on", buttons);
            Assert.Contains("leash-dnd:0:on", buttons);
            Assert.Contains(self.GetLogicalDescendants().OfType<Control>(), c => (c.Tag as string) == "leash-self-line:pending");
            Assert.Contains(self.GetLogicalDescendants().OfType<Control>(), c => (c.Tag as string) == "leash-self-hold");

            // One click, no confirm: the service cut runs its local safety first and the card drops.
            await self.CutAsync();
            Assert.Equal(1, safety);
            Assert.Null(svc.Snapshot.Me);
            section.Render();
            Assert.DoesNotContain(section.Children, c => (c.Tag as string) == "leash-self-card");
        });
    }

    [Fact]
    public async Task Look_OpensTheAskCard_AndLetGo_AsksFirst()
    {
        await AvaloniaTestDispatcher.RunAsync(async () =>
        {
            Setup();
            var api = new Api();
            var svc = Service(api, () => { });
            var section = new LeashDrawerSection(() => svc, () => null);
            LeashAskCard? shown = null;
            string? confirmTag = null;
            Action? yes = null;
            LeashAskCard.ShowOverride = c => shown = c;
            LeashCutConfirmWindow.ShowOverride = (_, tag, onYes) => { confirmTag = tag; yes = onYes; };
            try
            {
                Click(ButtonTagged(section, "leash-offer-look"));
                Assert.NotNull(shown);
                Assert.Equal("leash-ask:f2", shown!.Tag);
                var askButtons = shown.GetLogicalDescendants().OfType<Button>().Select(b => b.Tag as string).ToList();
                Assert.Contains("leash-ask-yes", askButtons);
                Assert.Contains("leash-ask-no", askButtons);
                shown.SetLevel(LeashIntensity.Strict);
                Assert.Contains("leash-ask-level:2:on", shown.GetLogicalDescendants().OfType<Button>().Select(b => b.Tag as string));
                await shown.AnswerAsync(true);
                Assert.Contains(api.Ops, o => o.Contains("answer", StringComparison.OrdinalIgnoreCase) || o.Contains("accept", StringComparison.OrdinalIgnoreCase));

                var holder = section.HolderCards.Single();
                var menu = holder.Menu();
                var release = menu.Items.OfType<MenuItem>().Single();
                Assert.Equal("leash-menu-release", release.Tag);
                release.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                Assert.Equal("leash-release-confirm", confirmTag);
                Assert.DoesNotContain("release", api.Ops);
                yes!();
                await Task.Delay(10);
                Assert.Contains("release", api.Ops);
            }
            finally
            {
                LeashAskCard.ShowOverride = null;
                LeashCutConfirmWindow.ShowOverride = null;
            }
        });
    }

    [Fact]
    public async Task MountLeash_AddsTheSectionOnce()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            Setup();
            var drawer = new FriendsDrawer();
            var slot = new StackPanel();
            var mount = typeof(FriendsDrawer).GetMethod("MountLeash", BindingFlags.Instance | BindingFlags.NonPublic)!;
            mount.Invoke(drawer, new object[] { slot });
            mount.Invoke(drawer, new object[] { slot });
            Assert.Single(slot.Children);
            Assert.IsType<LeashDrawerSection>(slot.Children[0]);
            Assert.Same(drawer.LeashSection, slot.Children[0]);
            return Task.CompletedTask;
        });
    }
}
