using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion.Runtime;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Newtonsoft.Json;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>
/// The Workshop's Community cell (WPF 7.1.5 CommunityPromptService + MainWindow.Patreon.cs
/// :2346-2493 + UpdateCommunityPromptsUI): import, activate behind the explicit-content gate,
/// deactivate, remove, browse and install, export. No network (a fake transport) and no real file
/// picker. Swaps the settings service and the cell's seams, so it runs alone.
/// </summary>
[Collection(RunsAloneCollection.Name)]
public sealed class CommunityPromptCellTests
{
    private sealed class FakeTransport : HttpMessageHandler
    {
        public readonly List<string> Asked = new();
        public Func<string, string?> Answer = _ => null;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var url = request.RequestUri!.ToString();
            Asked.Add(url);
            var body = Answer(url);
            return Task.FromResult(body == null
                ? new HttpResponseMessage(HttpStatusCode.NotFound)
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
        }
    }

    private sealed class Rig
    {
        public required string Dir;
        public required CommunityPromptLibrary Library;
        public required FakeTransport Net;
        public required AppSettings Settings;

        public string WritePromptFile(string fileName, string? slutPersonality = null, string personality = "Warm and patient.")
        {
            var path = Path.Combine(Dir, fileName);
            File.WriteAllText(path, JsonConvert.SerializeObject(new CommunityPrompt
            {
                Id = "from-file", Name = "ignored", Author = "Robin",
                PromptSettings = new CompanionPromptSettings { Personality = personality, SlutModePersonality = slutPersonality ?? "" },
            }));
            return path;
        }
    }

    private static void Run(Action<Rig> body)
    {
        AvaloniaTestDispatcher.Run(() =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            var dir = Path.Combine(Path.GetTempPath(), "ccp-community-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            var service = new SettingsService();
            var oldService = CoreSettings.ServiceProvider;
            var old = (WorkshopCommunityCell.Library, WorkshopCommunityCell.Notice, WorkshopCommunityCell.Ask,
                WorkshopCommunityCell.Acknowledge, WorkshopCommunityCell.PickImport, WorkshopCommunityCell.PickExport);
            CoreSettings.ServiceProvider = () => service;
            var net = new FakeTransport();
            var library = new CommunityPromptLibrary(Path.Combine(dir, "store"), net);
            try
            {
                WorkshopCommunityCell.Library = () => library;
                WorkshopCommunityCell.Notice = (_, _, _) => Task.CompletedTask;
                WorkshopCommunityCell.Ask = (_, _, _, _, _) => Task.FromResult(true);
                WorkshopCommunityCell.Acknowledge = _ => Task.FromResult(false);
                body(new Rig { Dir = dir, Library = library, Net = net, Settings = service.Current });
            }
            finally
            {
                (WorkshopCommunityCell.Library, WorkshopCommunityCell.Notice, WorkshopCommunityCell.Ask,
                    WorkshopCommunityCell.Acknowledge, WorkshopCommunityCell.PickImport, WorkshopCommunityCell.PickExport) = old;
                library.Dispose();
                CoreSettings.ServiceProvider = oldService;
                service.SealForReset();
                try { Directory.Delete(dir, true); } catch { }
            }
        });
    }

    private static (Window Window, WorkshopCommunityCell Cell) Host()
    {
        var cell = new WorkshopCommunityCell();
        var window = new Window { Content = cell, Width = 400, Height = 300 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, cell);
    }

    private static void Settle(Task t)
    {
        for (var i = 0; i < 400 && !t.IsCompleted; i++) { Dispatcher.UIThread.RunJobs(); if (!t.IsCompleted) Thread.Sleep(5); }
        Assert.True(t.IsCompleted);
        t.GetAwaiter().GetResult();
    }

    // Never block the dispatcher thread on a task that resumes on it: pump until it is done.
    private static T Wait<T>(Task<T> t)
    {
        Settle(t);
        return t.Result;
    }

    private static List<Button> RowButtons(WorkshopCommunityCell cell, string name) =>
        cell.FindControl<StackPanel>("InstalledPromptsPanel")!.Children.OfType<Grid>()
            .SelectMany(g => g.Children.OfType<StackPanel>()).SelectMany(p => p.Children.OfType<Button>())
            .Where(b => b.Name == name).ToList();

    // ---- the library ----

    [Fact]
    public void Import_TakesTheFileNameAsTheName_AndAFreshId() => Run(rig =>
    {
        var prompt = rig.Library.ImportFromFile(rig.WritePromptFile("Soft Voice.json"))!;
        Assert.Equal("Soft Voice", prompt.Name);
        Assert.NotEqual("from-file", prompt.Id);
        Assert.Equal(new[] { prompt.Id }, rig.Settings.InstalledCommunityPromptIds);
        Assert.Equal("Robin", rig.Library.GetInstalledPrompt(prompt.Id)!.Author);
        Assert.Null(rig.Library.ImportFromFile(Path.Combine(rig.Dir, "missing.json")));
    });

    [Fact]
    public void Activate_AppliesThePrompt_Deactivate_HandsTheWireBack_Remove_ForgetsIt() => Run(rig =>
    {
        var id = rig.Library.ImportFromFile(rig.WritePromptFile("One.json"))!.Id;
        Assert.True(rig.Library.ActivatePrompt(id));
        Assert.Equal(id, rig.Settings.ActiveCommunityPromptId);
        Assert.True(rig.Settings.CompanionPrompt.UseCustomPrompt);
        Assert.Equal("Warm and patient.", rig.Settings.CompanionPrompt.Personality);

        rig.Library.DeactivatePrompt();
        Assert.Null(rig.Settings.ActiveCommunityPromptId);
        Assert.False(rig.Settings.CompanionPrompt.UseCustomPrompt);

        Assert.True(rig.Library.ActivatePrompt(id));
        rig.Library.RemovePrompt(id);
        Assert.Null(rig.Settings.ActiveCommunityPromptId);
        Assert.Empty(rig.Settings.InstalledCommunityPromptIds);
        Assert.Null(rig.Library.GetInstalledPrompt(id));
        Assert.False(rig.Library.ActivatePrompt(id));
    });

    [Fact]
    public void AnExplicitPrompt_IsRefusedUntilTheAcknowledgementIsOnRecord() => Run(rig =>
    {
        rig.Settings.SlutModeEnabled = true;
        var id = rig.Library.ImportFromFile(rig.WritePromptFile("Two.json", slutPersonality: "explicit variant"))!.Id;
        Assert.False(rig.Library.ActivatePrompt(id));
        Assert.Null(rig.Settings.ActiveCommunityPromptId);

        ExplicitContentGate.MarkAcknowledged(rig.Settings.CompanionPrompt);
        Assert.True(rig.Library.ActivatePrompt(id));
    });

    [Fact]
    public void Install_ComesOffTheTransport_NeverInOfflineMode_AndNeverWithAPathForAnId() => Run(rig =>
    {
        rig.Net.Answer = url => url.EndsWith("/prompts/abc", StringComparison.Ordinal)
            ? JsonConvert.SerializeObject(new CommunityPrompt { Id = "abc", Name = "Net", Author = "Sam", PromptSettings = new CompanionPromptSettings() })
            : null;

        rig.Settings.OfflineMode = true;
        Assert.Null(Wait(rig.Library.InstallPromptAsync("abc")));
        Assert.Empty(rig.Net.Asked);

        rig.Settings.OfflineMode = false;
        Assert.Null(Wait(rig.Library.InstallPromptAsync("../abc")));
        Assert.Empty(rig.Net.Asked);

        var prompt = Wait(rig.Library.InstallPromptAsync("abc"))!;
        Assert.Equal("Net", prompt.Name);
        Assert.Contains("abc", rig.Settings.InstalledCommunityPromptIds);
        Assert.False(CommunityPromptLibrary.IsSafeId("a/b"));
        Assert.True(CommunityPromptLibrary.IsSafeId("0f3a9c"));
    });

    // ---- the cell ----

    [Fact]
    public void WithALibrary_TheFourButtonsShow_AndImportAddsARow() => Run(rig =>
    {
        var (window, cell) = Host();
        try
        {
            foreach (var name in new[] { "BtnBrowsePrompts", "BtnImportPrompt", "BtnExportPrompt", "BtnRefreshPrompts" })
                Assert.True(cell.FindControl<Button>(name)!.IsVisible, name);
            Assert.True(cell.FindControl<TextBlock>("TxtNoInstalledPrompts")!.IsVisible);

            var file = rig.WritePromptFile("Picked.json");
            WorkshopCommunityCell.PickImport = _ => Task.FromResult<string?>(file);
            Settle(cell.ImportAsync());
            Assert.Single(RowButtons(cell, "BtnActivatePrompt"));
            Assert.False(cell.FindControl<TextBlock>("TxtNoInstalledPrompts")!.IsVisible);

            // Activate from the row, then the row has no Activate left; remove empties the list.
            RowButtons(cell, "BtnActivatePrompt").Single().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(rig.Settings.InstalledCommunityPromptIds.Single(), rig.Settings.ActiveCommunityPromptId);
            Assert.Empty(RowButtons(cell, "BtnActivatePrompt"));

            RowButtons(cell, "BtnRemovePrompt").Single().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.Empty(rig.Settings.InstalledCommunityPromptIds);
            Assert.True(cell.FindControl<TextBlock>("TxtNoInstalledPrompts")!.IsVisible);
        }
        finally { window.Close(); Dispatcher.UIThread.RunJobs(); }
    });

    [Fact]
    public void ActivatingAnExplicitPrompt_AsksFirst_AndARefusalChangesNothing() => Run(rig =>
    {
        var (window, cell) = Host();
        try
        {
            rig.Settings.SlutModeEnabled = true;
            var id = rig.Library.ImportFromFile(rig.WritePromptFile("Three.json", slutPersonality: "explicit variant"))!.Id;
            var asked = 0;
            WorkshopCommunityCell.Acknowledge = _ => { asked++; return Task.FromResult(false); };
            Settle(cell.ActivateAsync(id));
            Assert.Equal(1, asked);
            Assert.Null(rig.Settings.ActiveCommunityPromptId);

            WorkshopCommunityCell.Acknowledge = _ => { asked++; return Task.FromResult(true); };
            Settle(cell.ActivateAsync(id));
            Assert.Equal(2, asked);
            Assert.Equal(id, rig.Settings.ActiveCommunityPromptId);
        }
        finally { window.Close(); Dispatcher.UIThread.RunJobs(); }
    });

    [Fact]
    public void Browse_InstallsTheFirstOneNotInstalled_AndExportWritesTheCurrentPersonality() => Run(rig =>
    {
        var (window, cell) = Host();
        try
        {
            var manifest = JsonConvert.SerializeObject(new CommunityPromptsManifest
            {
                Prompts = new[] { new CommunityPromptManifestEntry { Id = "p1", Name = "First", Author = "Sam", Description = "d" } },
            });
            rig.Net.Answer = url => url.EndsWith("/prompts/manifest", StringComparison.Ordinal) ? manifest
                : url.EndsWith("/prompts/p1", StringComparison.Ordinal)
                    ? JsonConvert.SerializeObject(new CommunityPrompt { Id = "p1", Name = "First", Author = "Sam", PromptSettings = new CompanionPromptSettings() })
                    : null;
            Settle(cell.BrowseAsync());
            Assert.Equal(new[] { "p1" }, rig.Settings.InstalledCommunityPromptIds);
            Assert.Single(RowButtons(cell, "BtnRemovePrompt"));

            rig.Settings.CompanionPrompt.Personality = "Mine.";
            var target = Path.Combine(rig.Dir, "out.json");
            WorkshopCommunityCell.PickExport = (_, _) => Task.FromResult<string?>(target);
            Settle(cell.ExportAsync());
            var exported = JsonConvert.DeserializeObject<CommunityPrompt>(File.ReadAllText(target))!;
            Assert.Equal("Mine.", exported.PromptSettings.Personality);
            Assert.True(exported.PromptSettings.UseCustomPrompt);
        }
        finally { window.Close(); Dispatcher.UIThread.RunJobs(); }
    });
}
