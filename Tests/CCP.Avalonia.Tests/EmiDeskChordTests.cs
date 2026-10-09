using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Controls.AppSettings;
using ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.EmiDesk;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>WPF EmiDeskSettingsSection hotkey capture (:173-:240) and EmiDeskService.ApplyHotkey
/// (:1633): Settings -> Emi Desk -> click the chord button, press a chord; the Core rules refuse,
/// the service arms it, and the armed chord toggles her. The X grab is a seam.</summary>
public sealed class EmiDeskChordTests
{
    private sealed record Grab(ChordMods Mods, string Key, Action OnPress);

    private static Task Run(Func<EmiDeskService, List<Grab>, Func<bool>, Action<bool>, Task> body) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var oldSettings = CoreSettings.ServiceProvider;
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        var svc = EmiDeskService.Instance;
        var (oldReg, oldUnreg, oldAsk) = (svc.RegisterChord, svc.UnregisterChord, svc.AskMute);
        var grabs = new List<Grab>();
        int unregs = 0;
        bool accept = true;
        try
        {
            var s = CoreSettings.Current;
            s.EmiDeskEnabled = true;
            s.EmiDeskMuteAvatar = false;
            s.EmiDeskHotkey = EmiDeskChord.DefaultHotkey;
            s.PanicKeyEnabled = true;
            s.PanicKey = "Escape";
            svc.RegisterChord = (m, k, a) => { grabs.Add(new Grab(m, k, a)); return accept; };
            svc.UnregisterChord = () => unregs++;
            svc.AskMute = () => Task.FromResult(EmiMuteChoice.Keep);
            await body(svc, grabs, () => unregs > 0, v => accept = v);
        }
        finally
        {
            if (svc.IsOut) svc.Dismiss();
            (svc.RegisterChord, svc.UnregisterChord, svc.AskMute) = (oldReg, oldUnreg, oldAsk);
            CoreSettings.ServiceProvider = oldSettings;
        }
    });

    [Fact]
    public Task CapturedChordIsValidatedPersistedAndArmed() => Run((svc, grabs, _, accept) =>
    {
        var section = new EmiDeskSettingsSection();
        var host = new Window { Width = 700, Height = 700, Content = section };
        host.Show();
        section.SyncFromSettings();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var btn = section.FindControl<Button>("BtnHotkey")!;
            var hint = section.FindControl<TextBlock>("TxtHotkeyHint")!;

            btn.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(Loc.Get("emi_desk_hotkey_capturing"), btn.Content);

            // A bare key is refused and capture stays open (WPF: "Stay in capture").
            host.KeyPress(Key.K, RawInputModifiers.None, PhysicalKey.K, "k");
            Assert.Equal(Loc.Get("emi_desk_hotkey_err_bare"), hint.Text);
            // Ctrl+Alt+G is Quick Recal.
            host.KeyPress(Key.G, RawInputModifiers.Control | RawInputModifiers.Alt, PhysicalKey.G, "");
            Assert.Equal(Loc.Get("emi_desk_hotkey_err_quickrecal"), hint.Text);
            Assert.Equal(EmiDeskChord.DefaultHotkey, CoreSettings.Current.EmiDeskHotkey);
            Assert.Empty(grabs);

            host.KeyPress(Key.K, RawInputModifiers.Control | RawInputModifiers.Alt, PhysicalKey.K, "");
            Assert.Equal("Ctrl+Alt+K", CoreSettings.Current.EmiDeskHotkey);
            Assert.Equal("Ctrl+Alt+K", btn.Content);
            Assert.Equal(Loc.Get("set2_emi_desk_hotkey_hint"), hint.Text);
            var g = Assert.Single(grabs);
            Assert.Equal((ChordMods.Ctrl | ChordMods.Alt, "K"), (g.Mods, g.Key));
            Assert.True(svc.HotkeyArmed);

            // The armed chord's press (listener thread in the app) toggles her out.
            g.OnPress();
            Dispatcher.UIThread.RunJobs();
            Assert.True(svc.IsOut);

            // A combo another client holds: saved, but the hint says it is taken.
            accept(false);
            btn.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            host.KeyPress(Key.J, RawInputModifiers.Control | RawInputModifiers.Shift, PhysicalKey.J, "");
            Assert.False(svc.HotkeyArmed);
            Assert.Equal(Loc.GetF("emi_desk_hotkey_err_taken", "Ctrl+Shift+J"), hint.Text);
        }
        finally { host.Close(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task ApplyHotkeyRefusesOffAndPanicClash() => Run((svc, grabs, unregistered, _) =>
    {
        // A chord on the panic key's base key would summon her and stop everything at once.
        CoreSettings.Current.PanicKey = "E";
        svc.ApplyHotkey();
        Assert.Empty(grabs);
        Assert.False(svc.HotkeyArmed);
        Assert.True(unregistered());

        CoreSettings.Current.PanicKey = "Escape";
        svc.ApplyHotkey();
        Assert.Single(grabs);
        Assert.True(svc.HotkeyArmed);

        // Switched off: freed, never grabbed again.
        CoreSettings.Current.EmiDeskEnabled = false;
        svc.ApplyHotkey();
        Assert.Single(grabs);
        Assert.False(svc.HotkeyArmed);
        return Task.CompletedTask;
    });
}
