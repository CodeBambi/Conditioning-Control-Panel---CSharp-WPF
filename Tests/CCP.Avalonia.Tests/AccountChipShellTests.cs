using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>
/// Parity ledger shell#17 / shell#18 / social#21 (wave A): the header account chip, the profile
/// bubble face and its menu paint the signed-in identity (WPF RefreshAccountChip /
/// RefreshProfileBubble / RefreshProfileMenu) instead of "Sign in" and "?" forever.
/// </summary>
public sealed class AccountChipShellTests
{
    [Fact]
    public Task ChipBubbleAndMenuPaintTheSignedInIdentity() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        var (oldIn, oldName, oldPrem, oldLab) = (CoreAccount.IsLoggedInProvider, CoreAccount.DisplayNameProvider,
            CoreAccount.HasPremiumAccessProvider, CoreAccount.HasLabAccessProvider);
        CoreAccount.IsLoggedInProvider = null;
        CoreAccount.DisplayNameProvider = null;
        CoreAccount.HasPremiumAccessProvider = null;
        CoreAccount.HasLabAccessProvider = null;
        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            Dispatcher.UIThread.RunJobs();
            T Find<T>(string n) where T : Control => shell.FindControl<T>(n)!;

            shell.RefreshAccountIdentity();
            Assert.Equal("?", Find<TextBlock>("ProfileBubbleInitials").Text);
            Assert.False(Find<TextBlock>("TxtAccountChipBadge").IsVisible);

            CoreAccount.IsLoggedInProvider = () => true;
            CoreAccount.DisplayNameProvider = () => "Ada Lovelace";
            CoreAccount.HasPremiumAccessProvider = () => true;
            shell.RefreshAccountIdentity();
            Assert.Equal("Ada Lovelace", Find<TextBlock>("TxtAccountChipName").Text);
            Assert.True(Find<TextBlock>("TxtAccountChipBadge").IsVisible);
            Assert.Equal("🔒", Find<TextBlock>("TxtAccountChipBadge").Text);
            Assert.Same(MainShellWindow.AccountChipTier1Brush, Find<Button>("BtnAccountChip").BorderBrush);
            Assert.Equal("AL", Find<TextBlock>("ProfileBubbleInitials").Text);

            CoreAccount.HasLabAccessProvider = () => true;
            shell.RefreshAccountIdentity();
            Assert.Equal("🧪", Find<TextBlock>("TxtAccountChipBadge").Text);
            Assert.Same(MainShellWindow.AccountChipTier2Brush, Find<Button>("BtnAccountChip").BorderBrush);

            // The menu: name, badge and the Log out row are SHOWN now (they were hidden stubs).
            var popup = Find<global::Avalonia.Controls.Primitives.Popup>("ProfileBubblePopup");
            popup.IsOpen = true;
            shell.RefreshAccountIdentity();
            Assert.True(Find<TextBlock>("ProfileMenuName").IsVisible);
            Assert.Equal("Ada Lovelace", Find<TextBlock>("ProfileMenuName").Text);
            Assert.Equal("🧪", Find<TextBlock>("ProfileMenuBadge").Text);
            var account = Find<Button>("ProfileMenuAccountBtn");
            Assert.True(account.IsVisible);
            Assert.Equal(ConditioningControlPanel.Localization.Loc.Get("btn_logout"), account.Content);
            popup.IsOpen = false;
        }
        finally
        {
            (CoreAccount.IsLoggedInProvider, CoreAccount.DisplayNameProvider,
             CoreAccount.HasPremiumAccessProvider, CoreAccount.HasLabAccessProvider) = (oldIn, oldName, oldPrem, oldLab);
            shell.Close();
            Dispatcher.UIThread.RunJobs();
            service.SealForReset();
            CoreSettings.ServiceProvider = null;
        }
        return Task.CompletedTask;
    });
}
