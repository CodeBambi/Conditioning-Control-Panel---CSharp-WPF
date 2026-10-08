using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
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
        await RunWithSection(async (section, _) =>
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
        await RunWithSection(async (section, asked) =>
        {
            var s = CoreSettings.Current;
            var win = section.FindControl<CheckBox>("ChkWinStart")!;
            var hidden = section.FindControl<CheckBox>("ChkStartHidden")!;
            Assert.False(XdgAutostart.IsRegistered());

            // Plain enable: the entry is written with WPF's --startup argument and the flag saved.
            win.IsChecked = true;
            await section.ApplyWinStartAsync();
            Assert.True(XdgAutostart.IsRegistered());
            Assert.Contains("--startup", File.ReadAllText(XdgAutostart.EntryPath));
            Assert.True(s.RunOnStartup);
            Assert.Empty(asked);

            // Hidden while startup is on: warned; "No" reverts the hidden box.
            hidden.IsChecked = true;
            await section.ApplyStartHiddenAsync();
            Assert.Single(asked);
            Assert.Equal(Loc.Get("msg_startup_hidden_warning"), asked[0]);
            Assert.False(hidden.IsChecked);
            Assert.False(s.StartMinimized);

            // Disable, then enable with hidden on and decline: box reverts, nothing registered.
            win.IsChecked = false;
            await section.ApplyWinStartAsync();
            Assert.False(XdgAutostart.IsRegistered());
            hidden.IsChecked = true;
            await section.ApplyStartHiddenAsync();   // startup off: no warning
            Assert.True(s.StartMinimized);
            asked.Clear();
            win.IsChecked = true;
            await section.ApplyWinStartAsync();
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

    private static async Task RunWithSection(Func<GeneralSettingsSection, List<string>, Task> body)
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
                await body(section, asked);
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
