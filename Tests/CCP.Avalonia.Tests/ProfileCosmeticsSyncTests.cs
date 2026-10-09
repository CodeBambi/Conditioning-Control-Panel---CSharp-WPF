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
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>WPF PersistOwnCosmetics + BuildCosmeticsPayload on this head (audit #1914): every push after the load
/// carries the sanitized settings loadout, so no cooldown, logout or restart can lose a save; an empty loadout goes
/// only as the explicit unequip-everything clear (this head does not adopt the cloud loadout, so an empty push from a
/// fresh install would wipe the account). Same class as the other sync tests (shared statics).</summary>
public sealed partial class AccountSeedTests
{
    [Fact]
    public void CustomizeSave_SavesRepaintsAndEveryPushCarriesTheLoadout_EmptyGoesOnlyAsTheClear() => WithFreshInstall(async () =>
    {
        var s = CoreSettings.Current;
        var old = s.ProfileCosmetics;
        var (wire, sync, _) = SignIn("u1", L40Profile("u1"));
        Assert.True(await AccountSeed.LoadProfileAsync());
        Assert.Null(Assert.Single(wire.Syncs)["cosmetics"]);          // (a) fresh install: the empty loadout is left out

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

                    // (c) an ordinary push carries the settings loadout too (WPF), sanitized: an unknown charm never goes up.
                    s.ProfileCosmetics.Charms.Add("not_a_real_charm");
                    sync.UtcNow = () => T0.AddMinutes(2);
                    Assert.True(await sync.PushAsync("level-up"));
                    Assert.Equal(new[] { "bambi_plush_bunny" }, wire.Syncs.ElementAt(2)["cosmetics"]!["charms"]!.Values<string>());

                    // (b) unequip everything: the empty loadout goes up once as WPF's explicit clear, then means "no change".
                    sync.UtcNow = () => T0.AddMinutes(3);
                    shell.PersistOwnCosmetics(new ProfileCosmetics());
                    Assert.True(JToken.DeepEquals(JObject.Parse(JsonConvert.SerializeObject(new ProfileCosmetics())),
                        (await NextSync(4))["cosmetics"]));                                      // golden clear body
                    Assert.False(shell.ProfilePage!.ProfileCharmSlot1.IsVisible);
                    sync.UtcNow = () => T0.AddMinutes(4);
                    Assert.True(await sync.PushAsync("level-up"));
                    Assert.False(sync.PendingCosmeticsClear);
                    Assert.Null(wire.Syncs.ElementAt(4)["cosmetics"]);

                    // (d) a clear skipped by the cooldown is dropped by logout, so the next account never gets it.
                    Assert.False(await sync.PushCosmeticsAsync(new ProfileCosmetics()));
                    Assert.True(sync.PendingCosmeticsClear);
                    sync.Reset();
                    Assert.False(sync.PendingCosmeticsClear);
                }
                finally { shell.Close(); Dispatcher.UIThread.RunJobs(); }
            });
        }
        finally { s.ProfileCosmetics = old; }
    });
}
