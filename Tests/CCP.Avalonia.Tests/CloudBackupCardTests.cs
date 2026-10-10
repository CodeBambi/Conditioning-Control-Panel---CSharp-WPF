using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Controls.AppSettings;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>
/// Ledger G7 / HA8: the cloud settings backup card on Settings &gt; Account & Plans. Signed out it is
/// shut; signed in the status line, Back up now and Restore reach the two /v2 routes through a fake
/// handler, and a restore keeps this machine's identity and safety floor.
/// </summary>
[Collection(RunsAloneCollection.Name)]   // swaps CoreSettings.ServiceProvider, the session probe and the client seam
public sealed class CloudBackupCardTests
{
    private sealed class Wire : HttpMessageHandler
    {
        public readonly List<string> Paths = new();
        public string Backup = "null";
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            Paths.Add(r.RequestUri!.AbsolutePath);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"success\":true,\"backup\":" + Backup + "}", Encoding.UTF8, "application/json"),
            });
        }
    }

    private static string Pack(JObject o)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            var bytes = Encoding.UTF8.GetBytes(o.ToString(Newtonsoft.Json.Formatting.None));
            gzip.Write(bytes, 0, bytes.Length);
        }
        return Convert.ToBase64String(output.ToArray());
    }

    [Fact]
    public Task TheCard_FollowsTheAccount_BacksUp_AndRestoresWithoutLooseningSafety() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();

        var (oldProvider, oldClient, oldSession) =
            (CoreSettings.ServiceProvider, AccountSettingsSection.NewBackupClient, CoreSession.IsSessionRunningProvider);
        var service = new SettingsService();
        var original = service.Current;
        var (oldId, oldStrict, oldPanic) = (original.UnifiedId, original.StrictLockEnabled, original.PanicKeyEnabled);
        var wire = new Wire();
        try
        {
            // No account services on a headless run: the default seam hands back no client, so no call.
            if (AccountSeed.Patreon == null) Assert.Null(AccountSettingsSection.NewBackupClient());

            CoreSettings.ServiceProvider = () => service;
            CoreSession.IsSessionRunningProvider = () => false;
            AccountSettingsSection.NewBackupClient =
                () => new CloudSettingsBackup(() => CoreSettings.Current, wire, () => "tok");

            original.UnifiedId = null;
            var section = new AccountSettingsSection();
            var card = section.FindControl<Border>("CloudSettingsBackupSection")!;
            Assert.False(card.IsVisible);                                     // WPF signed-out state: shut

            original.UnifiedId = "u-1";
            original.StrictLockEnabled = false;
            original.PanicKeyEnabled = true;
            section.RefreshProviderRows();
            Assert.True(card.IsVisible);

            // Status line: none yet, then the stored copy's date and version.
            var status = section.FindControl<TextBlock>("TxtCloudBackupStatus")!;
            await section.UpdateBackupStatusAsync();
            Assert.Equal(ConditioningControlPanel.Localization.Loc.Get("label_no_cloud_backup_found_back_up_your_settings_t"), status.Text);
            wire.Backup = "{\"app_version\":\"7.1.5\",\"backed_up_at\":\"2026-10-09T18:30:00Z\",\"size_bytes\":10}";
            await section.UpdateBackupStatusAsync();
            Assert.Contains("7.1.5", status.Text);

            // Back up now posts, and the button comes back.
            wire.Paths.Clear();
            await section.BackupNowAsync();
            Assert.Equal("/v2/user/backup-settings", wire.Paths[0]);
            var backupBtn = section.FindControl<Button>("BtnBackupSettingsNow")!;
            Assert.True(backupBtn.IsEnabled);
            Assert.Equal(ConditioningControlPanel.Localization.Loc.Get("btn_backup_now"), section.FindControl<TextBlock>("TxtBtnBackupNow")!.Text);

            // A running session refuses the restore before any call.
            var evil = JObject.FromObject(new AppSettings());
            evil["FlashFrequency"] = 9;
            evil["StrictLockEnabled"] = true;
            evil["PanicKeyEnabled"] = false;
            evil["UnifiedId"] = "someone-else";
            wire.Backup = "{\"app_version\":\"7.1.5\",\"settings_data\":\"" + Pack(evil) + "\"}";
            wire.Paths.Clear();
            CoreSession.IsSessionRunningProvider = () => true;
            await section.RestoreFromCloudAsync();
            Assert.Empty(wire.Paths);
            Assert.Same(original, service.Current);

            // Idle: the backup lands, this machine keeps its identity and its safety floor.
            CoreSession.IsSessionRunningProvider = () => false;
            await section.RestoreFromCloudAsync();
            Assert.Equal("/v2/user/settings-backup", wire.Paths[0]);
            var now = service.Current;
            Assert.NotSame(original, now);
            Assert.Equal(9, now.FlashFrequency);
            Assert.Equal("u-1", now.UnifiedId);
            Assert.False(now.StrictLockEnabled);
            Assert.True(now.PanicKeyEnabled);
            Assert.True(section.FindControl<Button>("BtnRestoreSettings")!.IsEnabled);
        }
        finally
        {
            (original.UnifiedId, original.StrictLockEnabled, original.PanicKeyEnabled) = (oldId, oldStrict, oldPanic);
            service.RestoreFrom(original);   // writes the pre-test settings back (SaveImmediate inside)
            CoreSettings.ServiceProvider = oldProvider;
            AccountSettingsSection.NewBackupClient = oldClient;
            CoreSession.IsSessionRunningProvider = oldSession;
        }
    });
}
