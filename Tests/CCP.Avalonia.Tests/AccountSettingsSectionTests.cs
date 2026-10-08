using System;
using System.Collections.Generic;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Avalonia.Views.Controls.AppSettings;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Newtonsoft.Json;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>SETTINGS · ACCOUNT on this head, driven from the shell the way a user reaches it:
/// fake providers (in-memory secrets, no keyring, no server), Settings door, the card buttons.</summary>
public sealed class AccountSettingsSectionTests
{
    private static void Run(Action<MainShellWindow, AccountSettingsSection, AppSettings, Dictionary<string, string?>> body) =>
        AvaloniaTestDispatcher.Run(() =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            var oldGet = CoreSecrets.RetrieveProvider;
            var oldSet = CoreSecrets.StoreProvider;
            var oldSettings = CoreSettings.ServiceProvider;
            var settingsPath = Path.Combine(CorePaths.UserData, "settings.json");
            var settingsBefore = File.Exists(settingsPath) ? File.ReadAllBytes(settingsPath) : null;
            var secrets = new Dictionary<string, string?>();
            CoreSecrets.RetrieveProvider = n => secrets.GetValueOrDefault(n);
            CoreSecrets.StoreProvider = (n, v) => secrets[n] = v;
            var service = new SettingsService();
            CoreSettings.ServiceProvider = () => service;
            var s = CoreSettings.Current;
            MainShellWindow? shell = null;
            try
            {
                Assert.True(AccountSeed.Seed(p => new ProviderSubscription(p, () => s)));
                shell = new MainShellWindow();
                shell.Show();
                shell.ShowTab("appsettings");
                Dispatcher.UIThread.RunJobs();
                var section = shell.AppSettingsPage!.FindControl<AccountSettingsSection>("SectionAccount")!;
                body(shell, section, s, secrets);
            }
            finally
            {
                shell?.Close();
                Dispatcher.UIThread.RunJobs();
                CoreAccount.IsLoggedInProvider = CoreAccount.HasPremiumAccessProvider = CoreAccount.HasLabAccessProvider = CoreAccount.IsWhitelistedProvider = null;
                CoreAccount.DisplayNameProvider = null;
                CoreAccount.ChangeDisplayNameProvider = null;
                CoreAccount.DeleteAccountProvider = null;
                CoreAccount.UnifiedUserId = null;
                CoreEntitlement.HasPremiumProvider = CoreEntitlement.HasLabProvider = null;
                CoreProgram.HasPremiumProvider = null;
                CoreSecrets.RetrieveProvider = oldGet;
                CoreSecrets.StoreProvider = oldSet;
                CoreSettings.ServiceProvider = oldSettings;
                if (settingsBefore != null) File.WriteAllBytes(settingsPath, settingsBefore); else File.Delete(settingsPath);
            }
        });

    private static string Text(Control c, string name) => c.FindControl<TextBlock>(name)!.Text ?? "";
    private static bool Shown(Control c, string name) => c.FindControl<Control>(name)!.IsVisible;
    private static void Click(Control c, string name)
    {
        c.FindControl<Button>(name)!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
    }

    private static void SignInPatreon(Dictionary<string, string?> secrets, AppSettings s)
    {
        secrets["patreon_auth"] = JsonConvert.SerializeObject(new PatreonTokenData
        { AccessToken = "a", RefreshToken = "r", ExpiresAt = DateTime.UtcNow.AddDays(10) });
        secrets["patreon_cache"] = JsonConvert.SerializeObject(new PatreonCachedState
        { Tier = PatreonTier.Level2, IsActive = true, CacheExpiresAt = DateTime.UtcNow.AddHours(1), DisplayName = "Bambi" });
        s.UnifiedId = "u-test";
        s.UserDisplayName = "Bambi";
        s.HasLinkedPatreon = true;
    }

    [Fact]
    public void SignedInPatronSeesHerCardsAndTheProviderButtonSignsHerOut()
    {
        Run((shell, section, s, secrets) =>
        {
            SignInPatreon(secrets, s);
            Assert.True(AccountSeed.Seed(p => new ProviderSubscription(p, () => s)));
            shell.ShowTab("dashboard");
            shell.ShowTab("appsettings");          // the door repaints the section (WPF IsVisibleChanged)
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(Loc.GetF("label_welcome", "Bambi"), Text(section, "TxtPatreonStatus"));
            Assert.Equal(Loc.Get("label_patreon_tier_level2"), Text(section, "TxtPatreonTier"));
            Assert.Equal(Loc.Get("btn_logout"), Text(section, "TxtBtnPatreonLogin"));
            Assert.Equal(Loc.Get("btn_link_discord"), Text(section, "TxtBtnDiscordLogin"));
            Assert.Equal(Loc.Get("btn_login"), Text(section, "TxtBtnSubscribeStarLogin"));
            Assert.True(Shown(section, "AccountLinkingSection"));   // Discord not linked yet
            Assert.True(Shown(section, "BtnLinkDiscord"));
            Assert.False(Shown(section, "BtnLinkPatreon"));         // the grant works
            Assert.True(Shown(section, "DataPrivacySection"));
            Assert.False(Shown(section, "BtnExportData"));          // not ported: hidden, not inert

            Click(section, "BtnPatreonLogin");     // the only provider: the whole account goes

            Assert.Null(s.UnifiedId);
            Assert.False(AccountSeed.Patreon!.IsAuthenticated);
            Assert.Equal(Loc.Get("label_not_connected"), Text(section, "TxtPatreonStatus"));
            Assert.Equal(Loc.Get("btn_login"), Text(section, "TxtBtnPatreonLogin"));
            Assert.Equal(Loc.Get("btn_login"), Text(section, "TxtBtnDiscordLogin"));
            Assert.False(Shown(section, "AccountLinkingSection"));
            Assert.False(Shown(section, "DataPrivacySection"));
        });
    }

    [Fact]
    public void APatronWithADeadGrantIsOfferedReconnect()
    {
        Run((shell, section, s, secrets) =>
        {
            // Linked server-side, no token on this PC, premium off because of it (PatreonReconnectRule).
            s.UnifiedId = "u-test";
            s.HasLinkedPatreon = true;
            s.HasLinkedDiscord = true;
            section.OnSectionShown();

            Assert.True(Shown(section, "AccountLinkingSection"));
            Assert.True(Shown(section, "BtnLinkPatreon"));
            Assert.Equal(Loc.Get("btn_reconnect_patreon"), Text(section, "TxtBtnLinkPatreon"));
            Assert.True(Shown(section, "TxtPatreonReconnectHint"));
            Assert.False(Shown(section, "BtnLinkDiscord"));
        });
    }

    [Fact]
    public void PresenceSwitchAndChasterRowWork()
    {
        Run((shell, section, s, secrets) =>
        {
            var oldHead = FriendsHead.Service;
            var oldChaster = ChasterHead.Service;
            var oldMark = PresenceAsk.MarkAsked;
            var asked = false;
            PresenceAsk.MarkAsked = () => asked = true;
            try
            {
                FriendsHead.Service = null;
                ChasterHead.Service = null;
                s.FriendsPresenceShared = false;
                section.OnSectionShown();
                var chk = section.FindControl<CheckBox>("ChkFriendsPresence")!;
                Assert.False(chk.IsChecked);
                chk.IsChecked = true;                   // no friends service: the setting carries it
                Assert.True(s.FriendsPresenceShared);
                Assert.True(asked);

                Assert.Equal(Loc.Get("chaster_account_name") + " · " + Loc.Get("label_not_connected"), Text(section, "TxtChasterStatus"));
                Assert.Equal(Loc.Get("chaster_account_hint"), Text(section, "TxtChasterInfo"));
                Assert.Equal(Loc.Get("chaster_link"), Text(section, "TxtBtnChasterLink"));
                Assert.False(section.FindControl<Button>("BtnChasterLink")!.IsEnabled); // no service, no link
                Click(section, "BtnChasterOpen");
                Assert.Equal("chaster", shell.CurrentTab);
            }
            finally
            {
                FriendsHead.Service = oldHead;
                ChasterHead.Service = oldChaster;
                PresenceAsk.MarkAsked = oldMark;
            }
        });
    }
}
