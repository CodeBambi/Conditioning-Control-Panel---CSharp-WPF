using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Controls.AppSettings;
using ConditioningControlPanel.Avalonia.Views.Features;
using ConditioningControlPanel.Localization;
using Xunit;

namespace CCP.Avalonia.GeneralSettings.Tests;

/// <summary>
/// Settings ▸ General: the Display card (content-monitor picker + session countdown,
/// GeneralSettingsSection.xaml:125) and the run-on-startup / start-hidden pair
/// (MainWindow.UiUpdates.cs:2825/2868, GeneralSettingsSection.xaml.cs OnSectionShown), driven
/// through the real controls with the OS registration redirected to a temp autostart folder.
/// </summary>
public sealed class GeneralDisplayStartupTests
{
    public GeneralDisplayStartupTests() => GeneralSettingsTestProfile.AssertOwned();

    [Fact]
    public async Task DisplayCard_MonitorPickerAndCountdownWriteTheirSettings()
    {
        await RunWithSection(async (section, _, _) =>
        {
            var s = CoreSettings.Current;
            var countdown = section.FindControl<CheckBox>("ChkShowSessionCountdown")!;
            Assert.True(countdown.IsChecked);   // seeded from the default (on)
            countdown.IsChecked = false;
            Assert.False(s.ShowSessionCountdown);

            var picker = section.FindControl<MonitorTargetPicker>("ContentMonitorPicker")!;
            var combo = picker.FindControl<ComboBox>("CmbContentMonitor")!;
            var tags = combo.Items.Cast<ComboBoxItem>().Select(i => (int)i.Tag!).ToList();
            Assert.Equal(new[] { MonitorTargetPicker.TagAllMonitors, MonitorTargetPicker.TagPrimaryOnly }, tags.Take(2));
            Assert.Equal(Loc.Get("monitor_target_all"), ((ComboBoxItem)combo.Items[0]!).Content);

            combo.SelectedIndex = 0;   // All monitors
            Assert.True(s.DualMonitorEnabled);
            Assert.Equal(-1, s.GlobalTargetMonitor);
            combo.SelectedIndex = 1;   // Primary only
            Assert.False(s.DualMonitorEnabled);
            Assert.Equal(-1, s.GlobalTargetMonitor);

            // An unplugged monitor's index matches no row: show Primary, never rewrite the choice.
            s.GlobalTargetMonitor = 7;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, combo.SelectedIndex);
            Assert.Equal(7, s.GlobalTargetMonitor);
            await Task.CompletedTask;
        });
    }

    [Fact]
    public async Task RunOnStartup_RegistersWarnsRevertsAndReconciles()
    {
        await RunWithSection(async (section, asked, host) =>
        {
            var s = CoreSettings.Current;
            var win = section.FindControl<CheckBox>("ChkWinStart")!;
            var hidden = section.FindControl<CheckBox>("ChkStartHidden")!;
            Assert.False(XdgAutostart.IsRegistered());

            // Plain enable: the entry is written with WPF's --startup argument and the flag saved.
            Click(host, win);   // a real pointer click: IsChecked flips, then the Click handler
            Assert.True(win.IsChecked);
            Assert.True(XdgAutostart.IsRegistered());
            Assert.Contains("--startup", File.ReadAllText(XdgAutostart.EntryPath));
            Assert.True(s.RunOnStartup);
            Assert.Empty(asked);

            // Hidden while startup is on: warned; "No" reverts the hidden box.
            Click(host, hidden);
            Assert.Single(asked);
            Assert.Equal(Loc.Get("msg_startup_hidden_warning"), asked[0]);
            Assert.False(hidden.IsChecked);
            Assert.False(s.StartMinimized);

            // Disable, then enable with hidden on and decline: box reverts, nothing registered.
            Click(host, win);
            Assert.False(XdgAutostart.IsRegistered());
            Click(host, hidden);   // startup off: no warning
            Assert.True(s.StartMinimized);
            asked.Clear();
            Click(host, win);
            Assert.Single(asked);
            Assert.False(win.IsChecked);
            Assert.False(XdgAutostart.IsRegistered());
            Assert.False(s.RunOnStartup);

            // Reconcile on show: an externally added entry is adopted ...
            XdgAutostart.SetStartupState(true);
            section.OnSectionShown();
            Assert.True(s.RunOnStartup);
            Assert.True(win.IsChecked);
            // ... and a stored ON whose entry vanished is re-created, not erased.
            File.Delete(XdgAutostart.EntryPath);
            section.OnSectionShown();
            Assert.True(XdgAutostart.IsRegistered());
            Assert.True(s.RunOnStartup);
        });
    }

    [Fact]
    public void AutostartEntryOutsideATestResolvesUnderTheTestSandbox()
    {
        Assert.Null(XdgAutostart.DirectoryOverride);
        Assert.StartsWith(Path.GetFullPath(TestXdgConfigSandbox.Root) + Path.DirectorySeparatorChar,
            Path.GetFullPath(XdgAutostart.EntryPath));
    }

    /// <summary>Headless pointer press+release at the control's centre - what a user's click does.</summary>
    private static void Click(Window host, Control control)
    {
        var p = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), host)!.Value;
        host.MouseDown(p, MouseButton.Left);
        host.MouseUp(p, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    private static async Task RunWithSection(Func<GeneralSettingsSection, List<string>, Window, Task> body)
    {
        await AvaloniaTestDispatcher.RunAsync(async () =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            LocalizationManager.Instance.Initialize("en");

            var previousProvider = CoreSettings.ServiceProvider;
            var previousDialog = GeneralSettingsSection.DialogOverride;
            var previousDir = XdgAutostart.DirectoryOverride;
            var dir = Path.Combine(Path.GetTempPath(), "ccp-autostart-" + Guid.NewGuid().ToString("N"));
            var asked = new List<string>();
            Window? host = null;
            try
            {
                CoreSettings.ServiceProvider = null;
                CoreSettings.Current.RunOnStartup = false;
                CoreSettings.Current.StartMinimized = false;
                CoreSettings.Current.ShowSessionCountdown = true;
                CoreSettings.Current.DualMonitorEnabled = false;
                CoreSettings.Current.GlobalTargetMonitor = -1;
                XdgAutostart.DirectoryOverride = dir;
                GeneralSettingsSection.DialogOverride = (_, message, confirm) =>
                {
                    asked.Add(message);
                    return Task.FromResult(false);   // "No" / OK on an error
                };

                var section = new GeneralSettingsSection();
                host = new Window { Width = 760, Height = 900, Content = section };
                host.Show();
                Dispatcher.UIThread.RunJobs();
                await body(section, asked, host);
            }
            finally
            {
                try { host?.Close(); } catch { }
                GeneralSettingsSection.DialogOverride = previousDialog;
                XdgAutostart.DirectoryOverride = previousDir;
                CoreSettings.Current.RunOnStartup = false;
                CoreSettings.Current.StartMinimized = false;
                CoreSettings.Current.ShowSessionCountdown = true;
                CoreSettings.Current.GlobalTargetMonitor = -1;
                CoreSettings.ServiceProvider = previousProvider;
                try { Directory.Delete(dir, true); } catch { }
            }
        });
    }
}
