using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Friends;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>Social &gt; Friends and Social &gt; Leash pages (WPF ca2997d30 / feda9c907) and the
/// rail chip's trimmed name (e9242c3d0), over the real Core FriendsService with its wire faked.</summary>
public sealed class SocialPagesTests
{
    private const string Empty = """
        {"ok":true,"code":"CCP-ABCDE","me":{"activity":"panel","lock_day":null,"shared":false},
         "friends":[],"incoming":[],"outgoing":[],"blocked":[]}
        """;

    private sealed class Wire : HttpMessageHandler
    {
        public string StateBody = Empty;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            var op = r.RequestUri!.AbsolutePath.Split('/').Last();
            var body = op == "state" ? StateBody : op == "poll" ? """{"ok":true,"online":[],"inbox":[],"receipts":[]}""" : """{"ok":true}""";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
        }
    }

    private static (FriendsService Svc, Wire Wire) Service()
    {
        var wire = new Wire();
        var shared = false;
        var api = new FriendsApi(new HttpClient(wire), () => ("u_me", "tok"), "http://127.0.0.1:9");
        return (new FriendsService(api, () => "u_me", () => true, null, () => shared, v => shared = v), wire);
    }

    private static T? Tagged<T>(Control root, string tag) where T : Control =>
        root.GetLogicalDescendants().OfType<T>().FirstOrDefault(c => c.Tag as string == tag);

    private static void Click(Button b) => b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    [Fact]
    public Task RailEntriesOpenTheFriendsAndLeashPages() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        var (svc, wire) = Service();
        var oldSvc = FriendsHead.Service;
        FriendsHead.Service = svc;
        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            Dispatcher.UIThread.RunJobs();

            // The Social door (WPF MainWindow.xaml:621), then its Friends pill.
            Click(shell.Named<Button>("DoorSocial")!);
            Assert.Equal("social", global::ConditioningControlPanel.Services.UI.NavSections.SectionForTab(shell.CurrentTab));
            Assert.Contains("active", shell.Named<Button>("DoorSocial")!.Classes);
            Click(shell.PageStrip!.PillFor("friends")!);
            Assert.Equal("friends", shell.CurrentTab);
            var page = shell.Named<FriendsTabView>("FriendsTab")!;
            Assert.True(page.IsVisible);
            Assert.True(page.Drawer.AsPage);
            Assert.True(double.IsNaN(page.Drawer.Width));                 // fills its column, not the 300px popup
            await svc.RefreshAsync();
            Dispatcher.UIThread.RunJobs();
            var empty = Tagged<StackPanel>(page, "friends-page-empty");
            Assert.NotNull(empty);                                         // "No friends yet." + Add one
            Assert.False(page.Drawer.AddBoxOpen);
            Click(Tagged<Button>(page, "friends-page-empty-add")!);
            Assert.True(page.Drawer.AddBoxOpen);

            // A page never claims Escape: it stays for the panic key.
            var esc = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape };
            page.Drawer.RaiseEvent(esc);
            Assert.False(esc.Handled);

            Click(shell.PageStrip!.PillFor("leash")!);
            Assert.Equal("leash", shell.CurrentTab);
            Assert.False(page.IsVisible);
            var leash = shell.Named<LeashTabView>("LeashTab")!;
            Assert.True(leash.IsVisible);
            Assert.True(leash.ShowingEmpty);

            // Hidden, the friends page folded and stopped listening: a new snapshot redraws nothing.
            wire.StateBody = Empty.Replace("\"friends\":[]",
                "\"friends\":[{\"id\":\"u_on\",\"name\":\"Mia\",\"tier\":0,\"online\":true,\"activity\":\"panel\",\"lock_day\":null,\"last_seen\":null,\"squelched\":false}]");
            await svc.RefreshAsync();
            Dispatcher.UIThread.RunJobs();
            Assert.Same(empty, Tagged<StackPanel>(page, "friends-page-empty"));

            // The empty Leash page's button goes to Friends, where offers start; showing it again lists Mia.
            Click(leash.FindControl<Button>("EmptyButton")!);
            Assert.Equal("friends", shell.CurrentTab);
            await svc.RefreshAsync();
            Dispatcher.UIThread.RunJobs();
            Assert.NotNull(Tagged<Border>(page, "friends-row:u_on"));
        }
        finally
        {
            shell.Close();
            FriendsHead.Service = oldSvc;
            settings.SaveImmediate(); CoreSettings.ServiceProvider = null;
        }
    });

    [Fact]
    public Task TheRailChipTrimsALongNameAndItsTooltipCarriesItWhole() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var old = CoreAccount.DisplayNameProvider;
        const string name = "CodeBambiWithAVeryLongDisplayName";
        CoreAccount.DisplayNameProvider = () => name;
        try
        {
            var chip = new FriendsRailChip(Service().Svc);
            var label = chip.Children.OfType<TextBlock>().Single(t => t.Text == name); // the rail name, not the drawer head
            Assert.Equal(TextTrimming.CharacterEllipsis, label.TextTrimming);
            Assert.Equal(name + "\n" + Loc.Get("friends_chip_tooltip"), ToolTip.GetTip(chip) as string);

            // A language switch rebuilds the code-set tooltip while the chip is in a window (WPF UpdateTooltip).
            var host = new Window { Content = chip };
            try
            {
                host.Show();
                var en = ToolTip.GetTip(chip) as string;
                LocalizationManager.Instance.SetLanguage("de");
                Assert.Equal(name + "\n" + Loc.Get("friends_chip_tooltip"), ToolTip.GetTip(chip) as string);
                Assert.NotEqual(en, ToolTip.GetTip(chip) as string);
            }
            finally { LocalizationManager.Instance.SetLanguage("en"); host.Close(); }
        }
        finally { CoreAccount.DisplayNameProvider = old; }
        return Task.CompletedTask;
    });
}
