using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>WPF PersistOwnCosmetics on this head: a Customize save writes settings, repaints the card and pushes the
/// loadout on /v2/user/sync - only that push carries <c>cosmetics</c>, the empty save is the explicit clear, and a
/// cooldown-skipped save still rides the next push. Same class as the other sync tests (shared statics).</summary>
public sealed partial class AccountSeedTests
{
    [Fact]
    public void CustomizeSave_SavesRepaintsAndPushesCosmetics_OnlyOnThatPush_EmptyIsTheClear() => WithFreshInstall(async () =>
    {
        var s = CoreSettings.Current;
        var old = s.ProfileCosmetics;
        var (wire, sync, _) = SignIn("u1", L40Profile("u1"));
        Assert.True(await AccountSeed.LoadProfileAsync());
        Assert.Null(Assert.Single(wire.Syncs)["cosmetics"]);          // an ordinary push never carries it

        async Task<JObject> NextSync(int count)
        {
            for (var i = 0; i < 100 && wire.Syncs.Count() < count; i++) await Task.Delay(20);
            return wire.Syncs.ElementAt(count - 1);
        }

        try
        {
            await AvaloniaTestDispatcher.RunAsync(async () =>
            {
                if (global::Avalonia.Application.Current is null)
                    global::Avalonia.AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                        .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                        .SetupWithoutStarting();
                var shell = new MainShellWindow();
                shell.Show();
                try
                {
                    sync.UtcNow = () => T0.AddMinutes(1);
                    shell.PersistOwnCosmetics(new ProfileCosmetics { AvatarDeco = "bambi_silk_bow", Charms = { "bambi_plush_bunny" } });
                    Assert.Equal("bambi_silk_bow", s.ProfileCosmetics.AvatarDeco);              // saved
                    Assert.NotNull(shell.ProfilePage!.ProfileCharmSlot1.Source);                  // repainted
                    var body = await NextSync(2);
                    Assert.Equal("bambi_silk_bow", (string?)body["cosmetics"]!["avatar_deco"]);
                    Assert.Equal(new[] { "bambi_plush_bunny" }, body["cosmetics"]!["charms"]!.Values<string>());

                    sync.UtcNow = () => T0.AddMinutes(2);
                    Assert.True(await sync.PushAsync("level-up"));
                    Assert.Null(wire.Syncs.ElementAt(2)["cosmetics"]);                            // delivered once

                    // Unequip everything: the empty loadout goes up as WPF's explicit clear.
                    sync.UtcNow = () => T0.AddMinutes(3);
                    shell.PersistOwnCosmetics(new ProfileCosmetics());
                    var clear = (JObject)(await NextSync(4))["cosmetics"]!;
                    Assert.Equal(JTokenType.Null, clear["avatar_deco"]!.Type);
                    Assert.Empty(clear["charms"]!);
                    Assert.False(shell.ProfilePage!.ProfileCharmSlot1.IsVisible);

                    // Inside the cooldown the save is skipped, then rides the next push.
                    Assert.False(await sync.PushCosmeticsAsync(new ProfileCosmetics { AvatarDeco = "bambi_silk_bow" }));
                    sync.UtcNow = () => T0.AddMinutes(4);
                    Assert.True(await sync.PushAsync("level-up"));
                    Assert.Equal("bambi_silk_bow", (string?)wire.Syncs.Last()["cosmetics"]!["avatar_deco"]);
                }
                finally { shell.Close(); Dispatcher.UIThread.RunJobs(); }
            });
        }
        finally { s.ProfileCosmetics = old; }
    });
}
