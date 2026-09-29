using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using CCP.Tests.Shared;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

/// <summary>
/// The one mod-switching path (WPF MainWindow ApplyActiveModChange / ActivateChosenMod /
/// PendingModActivation) and the first-run mod step (WPF FirstRunWizard PrepareModStep /
/// CommitModChoice), against a loopback fake release server only.
/// </summary>
public sealed class ModChoiceTests
{
    [Fact]
    public Task HeaderComboSwitchesTheModSavesAndRethemes() => Run(async (shell, svc, server) =>
    {
        var combo = shell.FindControl<ComboBox>("ModSelectorCombo")!;
        Assert.Equal(BuiltInMods.CCPDefaultId, ((ModSelectorItem)combo.SelectedItem!).Id);
        var before = (Color)Application.Current!.Resources["PinkColor"]!;
        PendingModChoice.Record(BuiltInMods.BambiSleepId, BuiltInMods.CCPDefaultId);

        combo.SelectedItem = shell.AvailableMods.Single(i => i.Id == BuiltInMods.DronificationId);
        Dispatcher.UIThread.RunJobs();   // the repaint is posted out of SelectionChanged

        Assert.Equal(BuiltInMods.DronificationId, AvApp.Mods!.ActiveModId);
        Assert.Equal(BuiltInMods.DronificationId, CoreSettings.Current.ActiveModId);   // saved
        Assert.True(CoreSettings.Current.ModChosen);
        var after = (Color)Application.Current.Resources["PinkColor"]!;
        Assert.Equal(Color.Parse(AvApp.Mods.GetAccentColorHex()), after);                 // re-themed live
        Assert.NotEqual(before, after);
        Assert.Null(PendingModChoice.Pending);                                           // manual switch outranks it
        Assert.Equal(BuiltInMods.DronificationId, ((ModSelectorItem)combo.SelectedItem!).Id);
        Assert.Equal(BuiltInMods.DronificationId, (combo.SelectionBoxItem as ModSelectorItem)?.Id);   // chip not blank
        await Task.CompletedTask;
    });

    [Fact]
    public Task UninstallingANonActiveModRefreshesTheComboAndTheChipKeepsItsName() => Run(async (shell, svc, server) =>
    {
        // A user mod, installed before the shell is built, so it is one of the combo's rows.
        shell.Close();
        var scratch = Directory.CreateTempSubdirectory("ccp-choice-").FullName;
        var ccpmod = Path.Combine(scratch, "t.ccpmod");
        using (var zip = ZipFile.Open(ccpmod, ZipArchiveMode.Create))
        using (var w = new StreamWriter(zip.CreateEntry("mod.json").Open()))
            w.Write(Newtonsoft.Json.JsonConvert.SerializeObject(new ModManifest { Id = "choice-test-mod", Name = "Choice Test", Version = "1.0.0", Author = "tests" }));
        Assert.True((await AvApp.Mods!.InstallModAsync(ccpmod)).Success);
        shell = new MainShellWindow();
        shell.Show();
        Assert.Contains(shell.AvailableMods, i => i.Id == "choice-test-mod");

        shell.FindControl<Button>("BtnManageMods")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var manager = await Owned<ConditioningControlPanel.Avalonia.Views.Dialogs.ModManagerDialog>(shell);
        var list = manager.FindControl<ListBox>("ModList")!;
        list.SelectedItem = list.Items.OfType<ListBoxItem>().Single(i => (string)i.Tag! == "choice-test-mod");
        manager.FindControl<Button>("BtnUninstall")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var confirm = await Owned<ConditioningControlPanel.Avalonia.Views.Dialogs.MessageDialog>(manager);
        confirm.FindControl<Button>("BtnOk")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await WaitFor(() => !AvApp.Mods.InstalledMods.ContainsKey("choice-test-mod"));
        manager.Close();
        await WaitFor(() => shell.AvailableMods.All(i => i.Id != "choice-test-mod"));   // refreshed on close

        var combo = shell.FindControl<ComboBox>("ModSelectorCombo")!;
        combo.SelectedItem = shell.AvailableMods.Single(i => i.Id == BuiltInMods.DronificationId);
        await WaitFor(() => AvApp.Mods.ActiveModId == BuiltInMods.DronificationId);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(BuiltInMods.DronificationId, (combo.SelectionBoxItem as ModSelectorItem)?.Id);   // chip not blank
        shell.Close();
    });

    [Fact]
    public Task FirstRunPackChoiceRecordsDownloadsThenActivatesOnArrival() => Run(async (shell, svc, server) =>
    {
        var wizard = Wizard(shell);
        wizard.Select(Card(wizard, BuiltInMods.BambiSleepId));
        Next(wizard);   // step 2 -> CommitModChoice

        Assert.Equal(BuiltInMods.BambiSleepId, PendingModChoice.Pending);   // recorded before the bytes land
        Assert.Equal(BuiltInMods.CCPDefaultId, AvApp.Mods!.ActiveModId);
        Assert.True(CoreSettings.Current.ModPickerShown);                    // offer spent at open
        await wizard.ChosenPackDownload!;
        await WaitFor(() => AvApp.Mods.ActiveModId == BuiltInMods.BambiSleepId);   // PackArrived

        Assert.True(svc.IsInstalled("mod-bambi"));
        Assert.Equal(BuiltInMods.BambiSleepId, CoreSettings.Current.ActiveModId);
        Assert.Null(PendingModChoice.Pending);
        Assert.Equal(1, server.PackGets);
        wizard.Close();
    });

    [Fact]
    public Task FirstRunChoiceOnDiskActivatesNow() => Run(async (shell, svc, server) =>
    {
        shell.ActivateChosenMod(BuiltInMods.DronificationId, MainShellWindow.ModChoiceTrigger.Immediate);
        var wizard = Wizard(shell);
        wizard.Select(Card(wizard, BuiltInMods.CCPDefaultId));   // ships in the box
        Next(wizard);

        Assert.Equal(BuiltInMods.CCPDefaultId, AvApp.Mods!.ActiveModId);
        Assert.Null(PendingModChoice.Pending);
        Assert.Equal(0, server.PackGets);
        wizard.Close();
        await Task.CompletedTask;
    });

    [Fact]
    public Task FirstRunOfflineLatchesTheOfferAndRecordsNothing() => Run(async (shell, svc, server) =>
    {
        CoreSettings.Current.OfflineMode = true;
        var wizard = Wizard(shell);
        wizard.Select(Card(wizard, BuiltInMods.BambiSleepId));
        Next(wizard);

        Assert.Equal(1, CoreSettings.Current.ModPickerOfflineOffers);
        Assert.True(CoreSettings.Current.ModPickerShown);
        Assert.Null(PendingModChoice.Pending);
        Assert.Null(wizard.ChosenPackDownload);
        Assert.Equal(BuiltInMods.CCPDefaultId, AvApp.Mods!.ActiveModId);
        wizard.Close();
        await Task.CompletedTask;
    });

    [Fact]
    public Task ReturningUserStartupOpensNoPickerAndFetchesNothing() => Run(async (shell, svc, server) =>
    {
        // Run's profile is a returning user (Welcomed, age accepted, ModPickerShown=false). WPF 6.11.x
        // removed the standalone popup (MainWindow.xaml.cs:610-611), so nothing opens or fetches.
        for (var i = 0; i < 10; i++) { Dispatcher.UIThread.RunJobs(); await Task.Delay(50); }
        Assert.Empty(shell.OwnedWindows.OfType<ModPickerDialog>());
        Assert.False(CoreSettings.Current.ModPickerShown);
        Assert.Empty(server.Requests);
    });

    [Fact]
    public Task PickerDialogDownloadsShowsProgressAndActivates() => Run(async (shell, svc, server) =>
    {
        // No caller on either head (see ShowIfNeeded); the ported flow is exercised directly.
        var picker = new ModPickerDialog(null);
        _ = picker.ShowDialog(shell);
        var card = picker.FindControl<ItemsControl>("CardsList")!.ItemsSource!.Cast<ModPickerCard>()
            .Single(c => c.PackId == "mod-bambi");
        await WaitFor(() => card.SizeText != "" && picker.FindControl<Button>("BtnDownload")!.IsEnabled);
        var seen = new System.Collections.Generic.List<double>();
        card.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(ModPickerCard.Percent)) seen.Add(card.Percent); };
        card.IsSelected = true;

        await picker.BtnDownload_Click();
        await WaitFor(() => AvApp.Mods!.ActiveModId == BuiltInMods.BambiSleepId);

        Assert.Contains(seen, p => p > 0 && p < 100);   // progress shown
        Assert.True(card.IsInstalled);
        Assert.True(svc.IsInstalled("mod-bambi"));
        Assert.Equal(BuiltInMods.BambiSleepId, CoreSettings.Current.ActiveModId);
        Assert.Null(PendingModChoice.Pending);
        Assert.Equal(1, server.PackGets);
        picker.Close();
    });

    [Fact]
    public Task ShowIfNeededFollowsTheLiveServiceRules() => Run(async (shell, svc, server) =>
    {
        Assert.True(ModPickerDialog.HasPackService);
        Assert.False(svc.IsFullInstall);

        CoreSettings.Current.ModPickerShown = false;
        server.Handle = c => FakeServer.Send(c, Array.Empty<byte>(), 404);   // manifest gone
        Assert.Null(await svc.FetchManifestAsync());
        Assert.True(svc.ManifestUnavailable);
        var shown = ModPickerDialog.ShowIfNeeded(shell);   // a regression opens a modal: fail, never hang
        await Task.Delay(300);
        Dispatcher.UIThread.RunJobs();
        Assert.True(shown.IsCompleted);
        Assert.False(await shown);   // guard 1: deferred, offer kept
        Assert.False(CoreSettings.Current.ModPickerShown);

        AvApp.ResetReleaseContent();
        Assert.False(ModPickerDialog.HasPackService);
        Assert.False(await ModPickerDialog.ShowIfNeeded(shell));   // no service
        Assert.Empty(shell.OwnedWindows.OfType<ModPickerDialog>());
    });

    // ---- helpers ----

    private static FirstRunWizard Wizard(MainShellWindow shell)
    {
        var wizard = new FirstRunWizard { ShellOwner = shell };
        wizard.Show();
        wizard.FindControl<CheckBox>("ChkAgeConfirm")!.IsChecked = true;
        Next(wizard);   // step 1 -> 2: PrepareModStep
        return wizard;
    }

    private static FirstRunModCard Card(FirstRunWizard w, string modId) =>
        w.FindControl<ItemsControl>("ModCardsList")!.ItemsSource!.Cast<FirstRunModCard>().Single(c => c.ModId == modId);

    private static void Next(FirstRunWizard w) =>
        w.FindControl<Button>("BtnNext")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static async Task<T> Owned<T>(Window owner) where T : Window
    {
        await WaitFor(() => owner.OwnedWindows.OfType<T>().Any());
        return owner.OwnedWindows.OfType<T>().Single();
    }

    private static async Task WaitFor(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(20);
        }
        Assert.True(condition(), "timed out");
    }

    private static byte[] BuildZip()
    {
        var payload = new byte[256 * 1024];
        new Random(7).NextBytes(payload);
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        using (var s = zip.CreateEntry("Resources/sounds/test/clip.mp3", CompressionLevel.NoCompression).Open())
            s.Write(payload);
        return ms.ToArray();
    }

    private static Task Run(Func<MainShellWindow, ReleaseContentService, FakeServer, Task> body) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Assert.Equal(TestUserDataProfile.Root, CorePaths.UserData);   // never the real profile
        if (Application.Current is null)
            AppBuilder.Configure<AvApp>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var zip = BuildZip();
        var sha = Convert.ToHexString(SHA256.HashData(zip)).ToLowerInvariant();
        var manifest = System.Text.Encoding.UTF8.GetBytes(
            "{\"packs\":[{\"id\":\"mod-bambi\",\"file\":\"mod-bambi.zip\",\"sizeBytes\":" + zip.Length +
            ",\"sha256\":\"" + sha + "\",\"contentVersion\":1,\"targetRoot\":\"\"}]}");
        using var server = new FakeServer();
        server.Handle = c => c.Request.Url!.AbsolutePath switch
        {
            "/v6.6.0/content-manifest.json" => FakeServer.Send(c, manifest),
            "/v6.6.0/mod-bambi.zip" => FakeServer.Send(c, zip),
            _ => FakeServer.Send(c, Array.Empty<byte>(), 404),
        };
        var handler = new LoopbackOnlyHandler();
        var oldSettings = CoreSettings.ServiceProvider;
        var oldVersion = CoreReleaseContent.AppVersionProvider;
        var mods = new CoreModsSnapshot();
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        CoreReleaseContent.AppVersionProvider = () => "6.6.3";   // cycle v6.6.0
        using var svc = new ReleaseContentService(server.Prefix + "{0}/", handler);
        var oldResources = Application.Current!.Resources.Keys.ToHashSet();
        MainShellWindow? shell = null;
        try
        {
            var s = settings.Current;
            s.InstalledContentPacks.Clear();
            s.OfflineMode = false;
            s.ModPickerShown = false;
            s.ModPickerOfflineOffers = 0;
            s.PendingModActivationId = "";
            s.ActiveModId = BuiltInMods.CCPDefaultId;
            s.Welcomed = true;
            s.HasAcceptedAgeVerification = true;
            AvApp.StartMods();
            AvApp.StartReleaseContent(svc);
            shell = new MainShellWindow();
            shell.Show();
            await body(shell, svc, server);
            Assert.Empty(handler.Violations);
        }
        finally
        {
            foreach (var w in shell?.OwnedWindows.ToList() ?? new()) w.Close();
            shell?.RequestExit();
            Dispatcher.UIThread.RunJobs();
            foreach (var key in Application.Current.Resources.Keys.Where(k => !oldResources.Contains(k)).ToList())
                Application.Current.Resources.Remove(key);   // the palette must not leak into later tests
            settings.SaveImmediate();
            settings.SealForReset();
            CoreSettings.ServiceProvider = oldSettings;
            CoreReleaseContent.AppVersionProvider = oldVersion;
            mods.Dispose();
            CoreReleaseContent.StampProvider = null;
            CoreReleaseContent.PackInfoProvider = null;
            CoreReleaseContent.UiInvoke = null;
            AvApp.ResetReleaseContent();
        }
    });
}
