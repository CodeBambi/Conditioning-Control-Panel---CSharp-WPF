using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Skia;
using Avalonia.Threading;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Awareness;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace CCP.Avalonia.Consent.Tests;

internal static class ConsentTestProfile
{
    internal static readonly string DirectoryPath = Path.Combine(
        Path.GetTempPath(), "ccp-consent-tests-" + Environment.ProcessId);

    [ModuleInitializer]
    internal static void Initialize()
    {
        Directory.CreateDirectory(DirectoryPath);
        Environment.SetEnvironmentVariable("CCP_USERDATA_DIR", DirectoryPath);
    }
}

/// <summary>
/// Headless, process-isolated regression coverage for the Avalonia awareness door. The tests drive
/// the real shell method and the real modal dialog; they do not reproduce its consent algorithm.
/// </summary>
public sealed class AwarenessConsentFlowTests
{
    private static SettingsService? _settings;

    [Fact]
    public async Task EnablingWithoutEntitlementDoesNotOpenOrWrite()
    {
        var settings = Prepare();
        var previousProvider = CoreEntitlement.HasPremiumProvider;
        try
        {
            CoreEntitlement.HasPremiumProvider = () => false;
            await OnUiAsync(async () =>
            {
                var shell = OpenShell();
                try
                {
                    var enabled = await shell.SetAwarenessEnabled(true);

                    Assert.False(enabled);
                    Assert.False(settings.AwarenessModeEnabled);
                    Assert.False(settings.AwarenessConsentGiven);
                    Assert.Empty(shell.OwnedWindows);
                }
                finally { CloseShell(shell); }
            });
        }
        finally { Restore(previousProvider); }
    }

    [Fact]
    public async Task DecliningTheRealDialogLeavesAwarenessOff()
    {
        var settings = Prepare();
        var previousProvider = CoreEntitlement.HasPremiumProvider;
        try
        {
            CoreEntitlement.HasPremiumProvider = () => true;
            await OnUiAsync(async () =>
            {
                var shell = OpenShell();
                try
                {
                    var pending = shell.SetAwarenessEnabled(true);
                    var dialog = ConsentDialog(shell);
                    Click(dialog, "BtnDecline");
                    var enabled = await pending;

                    Assert.False(enabled);
                    Assert.False(settings.AwarenessModeEnabled);
                    Assert.False(settings.AwarenessConsentGiven);
                    Assert.False(settings.AwarenessConsentShownV2);
                    Assert.Empty(settings.AwarenessDenyList);
                }
                finally { CloseShell(shell); }
            });
        }
        finally { Restore(previousProvider); }
    }

    [Fact]
    public async Task DismissingTheRealDialogIsADecline()
    {
        var settings = Prepare();
        var previousProvider = CoreEntitlement.HasPremiumProvider;
        try
        {
            CoreEntitlement.HasPremiumProvider = () => true;
            await OnUiAsync(async () =>
            {
                var shell = OpenShell();
                try
                {
                    var pending = shell.SetAwarenessEnabled(true);
                    var dialog = ConsentDialog(shell);
                    var closed = false;
                    dialog.Closed += (_, _) => closed = true;
                    dialog.Close(); // title-bar/window dismissal has no affirmative result
                    Dispatcher.UIThread.RunJobs();
                    Assert.True(closed, "dismissing the consent window did not close it");
                    var enabled = await pending;

                    Assert.False(enabled);
                    Assert.False(settings.AwarenessModeEnabled);
                    Assert.False(settings.AwarenessConsentGiven);
                    Assert.False(settings.AwarenessConsentShownV2);
                }
                finally { CloseShell(shell); }
            });
        }
        finally { Restore(previousProvider); }
    }

    [Fact]
    public async Task AcceptingTheRealDialogSeedsPrivacyAndMigratesBeforeEnabling()
    {
        var settings = Prepare(s => s.AwarenessReactionCooldownSeconds = 15);
        var previousProvider = CoreEntitlement.HasPremiumProvider;
        try
        {
            CoreEntitlement.HasPremiumProvider = () => true;
            await OnUiAsync(async () =>
            {
                var shell = OpenShell();
                try
                {
                    var pending = shell.SetAwarenessEnabled(true);
                    var dialog = ConsentDialog(shell);
                    Click(dialog, "BtnAccept");
                    var enabled = await pending;

                    Assert.True(enabled);
                    Assert.True(settings.AwarenessModeEnabled);
                    Assert.True(settings.AwarenessConsentGiven);
                    Assert.True(settings.AwarenessConsentShownV2);
                    Assert.True(settings.AwarenessDenySeeded);
                    Assert.Contains(AwarenessPrivacyRules.GroupPasswordManagers, settings.AwarenessDenyList);
                    Assert.Contains(AwarenessPrivacyRules.GroupBanking, settings.AwarenessDenyList);
                    Assert.Contains(AwarenessPrivacyRules.GroupEmailTitles, settings.AwarenessDenyList);
                    Assert.True(settings.AwarenessIntensityMigrated);
                    Assert.Equal(AwarenessIntensity.Unhinged, settings.AwarenessIntensity);
                }
                finally { CloseShell(shell); }
            });
        }
        finally { Restore(previousProvider); }
    }

    [Fact]
    public async Task AlreadyAcceptedConsentOpensTheDoorWithoutASecondDialog()
    {
        var settings = Prepare(s => s.AwarenessConsentShownV2 = true);
        var previousProvider = CoreEntitlement.HasPremiumProvider;
        try
        {
            CoreEntitlement.HasPremiumProvider = () => true;
            await OnUiAsync(async () =>
            {
                var shell = OpenShell();
                try
                {
                    var enabled = await shell.SetAwarenessEnabled(true);

                    Assert.True(enabled);
                    Assert.True(settings.AwarenessModeEnabled);
                    Assert.True(settings.AwarenessConsentGiven);
                    Assert.Empty(shell.OwnedWindows);
                }
                finally { CloseShell(shell); }
            });
        }
        finally { Restore(previousProvider); }
    }

    [Fact]
    public async Task DisablingRemainsUngatedWhenEntitlementIsDenied()
    {
        var settings = Prepare(s =>
        {
            s.AwarenessModeEnabled = true;
            s.AwarenessConsentGiven = true;
            s.AwarenessConsentShownV2 = true;
        });
        var previousProvider = CoreEntitlement.HasPremiumProvider;
        try
        {
            CoreEntitlement.HasPremiumProvider = () => false;
            await OnUiAsync(async () =>
            {
                var shell = OpenShell();
                try
                {
                    var enabled = await shell.SetAwarenessEnabled(false);

                    Assert.False(enabled);
                    Assert.False(settings.AwarenessModeEnabled);
                    Assert.False(settings.AwarenessConsentGiven);
                    Assert.Empty(shell.OwnedWindows);
                }
                finally { CloseShell(shell); }
            });
        }
        finally { Restore(previousProvider); }
    }

    private static AppSettings Prepare(Action<AppSettings>? configure = null)
    {
        EnsureAvalonia();
        _settings ??= new SettingsService();
        CoreSettings.ServiceProvider = () => _settings;
        LocalizationManager.Instance.SetLanguage("en");

        var settings = new AppSettings
        {
            Welcomed = true,
            Language = "en",
            UseAwarenessV2 = true,
        };
        configure?.Invoke(settings);
        _settings.RestoreFrom(settings);
        return settings;
    }

    private static void Restore(Func<bool>? previousProvider)
    {
        CoreEntitlement.HasPremiumProvider = previousProvider;
        CoreSettings.ServiceProvider = null;
    }

    private static void EnsureAvalonia()
    {
        if (Application.Current is null)
        {
            AppBuilder.Configure<App>()
                .UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        }
    }

    private static MainShellWindow OpenShell()
    {
        var shell = new MainShellWindow();
        shell.Show();
        Dispatcher.UIThread.RunJobs();
        Assert.True(shell.IsVisible, "the headless owner window did not open");
        return shell;
    }

    private static void CloseShell(MainShellWindow shell)
    {
        foreach (var child in shell.OwnedWindows.ToArray()) child.Close();
        shell.Close();
        Dispatcher.UIThread.RunJobs();
    }

    private static AwarenessConsentDialog ConsentDialog(MainShellWindow owner)
    {
        Dispatcher.UIThread.RunJobs();
        var dialog = owner.OwnedWindows.OfType<AwarenessConsentDialog>().SingleOrDefault();
        Assert.NotNull(dialog);
        Assert.True(dialog!.IsVisible, "the consent dialog did not open");
        return dialog;
    }

    private static void Click(AwarenessConsentDialog dialog, string name)
    {
        Dispatcher.UIThread.RunJobs();
        var button = dialog.FindControl<Button>(name);
        Assert.NotNull(button);
        Assert.True(button!.Bounds.Width > 0 && button.Bounds.Height > 0, $"{name} was not laid out");

        var point = button.TranslatePoint(
            new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), dialog);
        Assert.True(point.HasValue, $"could not translate {name} into the dialog");

        dialog.MouseMove(point!.Value, RawInputModifiers.None);
        dialog.MouseDown(point.Value, MouseButton.Left, RawInputModifiers.None);
        dialog.MouseUp(point.Value, MouseButton.Left, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
    }

    private static Task OnUiAsync(Func<Task> action)
    {
        if (Dispatcher.UIThread.CheckAccess()) return action();

        var completion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                await action();
                completion.SetResult(null);
            }
            catch (Exception ex) { completion.SetException(ex); }
        });
        return completion.Task;
    }
}
