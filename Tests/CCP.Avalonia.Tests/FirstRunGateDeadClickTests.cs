using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Features;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>WPF's 18+ gate on the first run, and the dashboard/Library clicks that used to do nothing.</summary>
public sealed class FirstRunGateDeadClickTests
{
    [Fact]
    public Task ClosingTheGateUnticked_HandsBackAndExits() => Run(async shell =>
    {
        var run = FirstRunWizard.Run(shell);
        var wizard = Wizard(shell);
        var next = wizard.FindControl<Button>("BtnNext")!;
        var box = wizard.FindControl<CheckBox>("ChkAgeConfirm")!;
        Assert.False(next.IsEnabled);
        box.IsChecked = true;
        Assert.True(next.IsEnabled);
        box.IsChecked = false;
        Assert.False(next.IsEnabled);

        var closed = false;
        shell.Closed += (_, _) => closed = true;
        wizard.Close();
        await run;
        Assert.True(closed);
        Assert.False(CoreSettings.Current.HasAcceptedAgeVerification);
        Assert.False(CoreSettings.Current.Welcomed);   // handed back to the next launch
    });

    [Fact]
    public Task EnterWithTheBoxTicked_RecordsAcceptanceAndCarriesOn() => Run(async shell =>
    {
        var run = FirstRunWizard.Run(shell);
        var wizard = Wizard(shell);
        wizard.FindControl<CheckBox>("ChkAgeConfirm")!.IsChecked = true;
        wizard.FindControl<Button>("BtnNext")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.True(CoreSettings.Current.HasAcceptedAgeVerification);
        Assert.True(wizard.FindControl<Grid>("Step2")!.IsVisible);

        var closed = false;
        shell.Closed += (_, _) => closed = true;
        wizard.Close();
        await run;
        Assert.False(closed);
    });

    [Fact]
    public Task DashboardAndLibraryClicksGoWhereWpfGoes() => Run(shell =>
    {
        var dash = shell.GetLogicalDescendants().OfType<SettingsTabView>().First();
        FeatureCard Card(string n) => dash.FindControl<FeatureCard>(n)!;

        Card("CardFlash").RaiseEvent(new RoutedEventArgs(FeatureCard.ClickEvent));
        Assert.Equal("studio", shell.CurrentTab);

        var was = MainShellWindow.IsWallFeatureOn("flash");
        Card("CardFlash").RaiseEvent(new RoutedEventArgs(FeatureCard.ToggleRequestedEvent));
        Assert.NotEqual(was, MainShellWindow.IsWallFeatureOn("flash"));

        var pink = MainShellWindow.IsWallFeatureOn("pinkfilter");
        dash.FindControl<SplitFeatureCard>("ComboSpiralPink")!.RaiseEvent(new RoutedEventArgs(SplitFeatureCard.ToggleBEvent));
        Assert.NotEqual(pink, MainShellWindow.IsWallFeatureOn("pinkfilter"));

        shell.ShowTab("settings");
        Card("CardVault").RaiseEvent(new RoutedEventArgs(FeatureCard.ClickEvent));
        Assert.Equal("exclusives", shell.CurrentTab);

        shell.ShowTab("settings");
        shell.FindControl<Button>("BtnPatreonExclusives")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal("exclusives", shell.CurrentTab);

        shell.ShowTab("settings");
        shell.FindControl<Button>("BtnNavMediaLog")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal("settings", shell.CurrentTab);   // a window, not the Assets tab
        Assert.Contains(shell.OwnedWindows, w => w is MediaHistoryWindow);
        return Task.CompletedTask;
    });

    private static FirstRunWizard Wizard(MainShellWindow shell)
    {
        Dispatcher.UIThread.RunJobs();
        return shell.OwnedWindows.OfType<FirstRunWizard>().Single();
    }

    private static Task Run(System.Func<MainShellWindow, Task> body) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        MainShellWindow? shell = null;
        try
        {
            CoreSettings.Current.Welcomed = true;   // claimed, as MainShellWindow.FirstRun does
            CoreSettings.Current.HasAcceptedAgeVerification = false;
            shell = new MainShellWindow();
            shell.Show();
            await body(shell);
        }
        finally
        {
            foreach (var w in shell?.OwnedWindows.ToList() ?? new()) w.Close();
            shell?.RequestExit();
            CoreSettings.ServiceProvider = null;
        }
    });
}
