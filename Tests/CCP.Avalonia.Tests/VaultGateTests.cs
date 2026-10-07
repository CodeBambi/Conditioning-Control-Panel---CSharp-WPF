using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Vault;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>Main fbe161de2 et al.: a padlock opens the vault gate card, not Settings · Account.</summary>
public sealed class VaultGateTests
{
    [Fact]
    public Task PadlockOpensTheCardAndItsLinksClick() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        EnsureAvalonia();
        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            Dispatcher.UIThread.RunJobs();
            var remote = shell.Named<RemoteControlTabView>("RemoteControlTab")!;
            var unlock = remote.GetLogicalDescendants().OfType<Button>()
                .Single(b => b.Content is TextBlock t && t.Text == Loc.Get("gate_unlock_with_patreon"));
            unlock.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();

            var card = VaultGateDialog.Open;
            Assert.NotNull(card);
            Assert.Contains(Loc.GetF("vaultgate_title_vault", Loc.Get("tab_remote_control")), Texts(card!));
            Assert.Contains(Loc.Get("vaultgate_basic_8"), Texts(card!));
            Assert.NotEqual("appsettings", shell.CurrentTab);   // the old jump to Settings - Account is gone

            // "Sign in" is the account door, as WPF's ShowAppInfoPopup.
            Click(card!, Loc.Get("vaultgate_signin"));
            Assert.Equal("appsettings", shell.CurrentTab);
            Assert.Null(VaultGateDialog.Open);
            unlock.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            card = VaultGateDialog.Open;

            // 99f48bc22: the links are buttons and click; Compare -> Yearly -> Back -> Close.
            Click(card!, Loc.Get("vaultgate_compare"));
            Assert.Contains(Loc.Get("vaultgate_compare_title"), Texts(card!));
            Assert.Contains(Loc.Get("vaultgate_prime_7"), Texts(card!));
            Click(card!, Loc.Get("vaultgate_billing_yearly"));
            Assert.Contains(Loc.Get("vaultgate_yearly_note"), Texts(card!));
            Click(card!, Loc.Get("vaultgate_back"));
            Assert.Contains(Loc.Get("vaultgate_signin"), Texts(card!));
            Click(card!, Loc.Get("vaultgate_close"));
            Assert.Null(VaultGateDialog.Open);
        }
        finally { VaultGateDialog.Open?.Close(); shell.Close(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task SaleStrikesTheMonthlyPriceOnTheCoveredTier() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        EnsureAvalonia();
        try
        {
            VaultGateDialog.Sale = new VaultSaleInfo(20, null, new[] { 1 });
            var card = VaultGateDialog.ShowOffer(null, null, 2, () => { });
            Dispatcher.UIThread.RunJobs();
            // Lab card: the sale covers Basic only, so no strike and the Prime chip.
            Assert.Contains(Loc.Get("vaultgate_chip_lab"), Texts(card));
            Assert.DoesNotContain(card.GetVisualDescendants().OfType<TextBlock>(), t => "struck".Equals(t.Tag));

            card = VaultGateDialog.ShowOffer(null, "lockdown", 1, () => { });
            Dispatcher.UIThread.RunJobs();
            var cur = VaultOffer.LocalCurrency();
            var struck = card.GetVisualDescendants().OfType<TextBlock>().Single(t => "struck".Equals(t.Tag));
            Assert.Equal(VaultOffer.Money(VaultOffer.PriceFor(1, cur).MonthlyCents, cur), struck.Text);
            Assert.Contains(Loc.Get("vaultgate_sale_first"), Texts(card));
        }
        finally { VaultGateDialog.Sale = null; VaultGateDialog.Open?.Close(); }
        return Task.CompletedTask;
    });

    private static void EnsureAvalonia()
    {
        if (global::Avalonia.Application.Current is not null) return;
        global::Avalonia.AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
            .UseHeadless(new global::Avalonia.Headless.AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    }

    private static string?[] Texts(Window w) => w.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToArray();

    private static void Click(Window w, string text)
    {
        var button = w.GetVisualDescendants().OfType<Button>()
            .First(b => b.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == text));
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
    }
}
