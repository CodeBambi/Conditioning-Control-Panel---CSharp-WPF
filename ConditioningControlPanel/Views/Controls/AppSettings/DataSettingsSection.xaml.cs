using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel.Views.Controls.AppSettingsSections
{
    /// <summary>
    /// SETTINGS ▸ DATA. Offline mode, phrase backup, a signpost to the cloud backup on
    /// Settings ▸ Account, and the Danger Zone.
    ///
    /// <para>Offline mode and the phrase buttons are pure forwards: the controls MOVED out of the
    /// permanently-Collapsed LegacyDashboardHost keeping their x:Names, so the MainWindow partials
    /// that own their logic still address them - only <c>SettingsTab.</c> became
    /// <c>AppSettingsTab.</c>.</para>
    ///
    /// <para><b>The factory reset is the one piece of net-new logic in Phase 2</b> (PLAN §4, Phase 2
    /// "Data ... Danger Zone (net-new factory reset - small, explicit, double-confirmed)"). Its
    /// contract is deliberately narrow and is spelled out on <see cref="PerformFactoryReset"/>:
    /// settings only, nothing that costs the user bytes or money.</para>
    /// </summary>
    public partial class DataSettingsSection : UserControl
    {
        public DataSettingsSection()
        {
            InitializeComponent();
        }

        private MainWindow? Main => Window.GetWindow(this) as MainWindow ?? App.MainWindowRef;

        // =====================================================================================
        //  offline mode + phrase backup - pure forwards
        // =====================================================================================

        private void ChkOfflineMode_Changed(object sender, RoutedEventArgs e)
        {
            if (Main is { } mw) mw.ChkOfflineMode_Changed(sender, e);
        }

        private void BtnExportPhrases_Click(object sender, RoutedEventArgs e)
        {
            if (Main is { } mw) mw.BtnExportPhrases_Click(sender, e);
        }

        private void BtnImportPhrases_Click(object sender, RoutedEventArgs e)
        {
            if (Main is { } mw) mw.BtnImportPhrases_Click(sender, e);
        }

        private void BtnGoToAccountBackup_Click(object sender, RoutedEventArgs e)
        {
            Main?.AppSettingsTab?.FocusSection("account");
        }

        // =====================================================================================
        //  danger zone
        // =====================================================================================

        /// <summary>
        /// Two dialogs, in this order, and neither is skippable:
        /// <list type="number">
        ///   <item>a <see cref="WarningDialog"/> that lists what goes and what stays, gated on its
        ///   acknowledgement checkbox;</item>
        ///   <item>an <see cref="InputDialog"/> that only accepts the literal word RESET.</item>
        /// </list>
        /// Refused outright during Lockdown: the reset ends with a process exit, and Lockdown's
        /// whole promise is that there is no exit (<c>MainWindow.Settings.cs:BtnExit_Click</c> makes
        /// the same check). A factory reset must not become a lockdown escape hatch.
        /// </summary>
        private void BtnFactoryReset_Click(object sender, RoutedEventArgs e)
        {
            var owner = Window.GetWindow(this) ?? Application.Current?.MainWindow;

            if (App.Lockdown?.IsActive == true)
            {
                Tell(owner,
                     Loc.Get("msg_you_are_in_lockdown_mode_nthere_is_no_escape"),
                     Loc.Get("title_lockdown"),
                     MessageBoxImage.Stop);
                return;
            }

            // --- confirmation 1: what happens ---
            var warning = new WarningDialog(
                Loc.Get("set2_reset_dialog1_title"),
                Loc.Get("set2_reset_dialog1_body"),
                Loc.Get("set2_reset_dialog1_confirm"))
            {
                Owner = owner,
                Topmost = true,
            };
            if (warning.ShowDialog() != true || !warning.Confirmed)
            {
                App.Logger?.Information("[RESET] Factory reset cancelled at the warning dialog");
                return;
            }

            // --- confirmation 2: type it out ---
            // A typed word, not a second button: the point is to make the last step impossible to
            // click through by muscle memory. Compared case-insensitively and trimmed so the
            // barrier is intent, not typing accuracy.
            var keyword = Loc.Get("set2_reset_keyword");
            var input = new InputDialog(
                Loc.Get("set2_reset_dialog2_title"),
                Loc.GetF("set2_reset_dialog2_prompt", keyword))
            {
                Owner = owner,
                Topmost = true,
            };
            if (input.ShowDialog() != true ||
                !string.Equals(input.ResultText?.Trim(), keyword, StringComparison.OrdinalIgnoreCase))
            {
                App.Logger?.Information("[RESET] Factory reset cancelled at the keyword prompt");
                return;
            }

            PerformFactoryReset();
        }

        /// <summary>
        /// Resets <b>settings only</b> and restarts.
        ///
        /// <para><b>What goes:</b> <c>settings.json</c> (moved aside to
        /// <c>settings.pre-reset-&lt;stamp&gt;.json</c>, not deleted - a reset the user regrets has to
        /// be recoverable by hand), any leftover <c>settings.json*.tmp</c> from an interrupted save,
        /// and the <c>.restored</c> cloud-restore marker. The temps and the marker MUST go with it:
        /// <c>SettingsService.RecoverAndCleanTempFiles</c> promotes a surviving temp to
        /// <c>settings.json</c> when the real file is missing, which would quietly undo the reset.</para>
        ///
        /// <para><b>What stays, deliberately:</b> the assets folder, content packs, installed mods,
        /// achievements/progression on the server, the account and its stored tokens, crash logs,
        /// and the rolling <c>settings.bak-N.json</c> dailies (those are only ever read when a file
        /// is CORRUPT, never when it is missing, so they cannot resurrect the old settings).
        /// "Factory reset" here means the app's own preferences - never the user's media.</para>
        ///
        /// <para><b>Why the restart is a delayed relaunch and the exit is hard.</b> There is no
        /// existing in-app restart path (the updater restarts via Inno Setup's
        /// <c>/RESTARTAPPLICATIONS</c>, which is the installer's job, not ours). Launching the exe
        /// directly would hit our own single-instance handshake: the still-alive process would ack
        /// and the new one would exit, leaving the user staring at an un-reset app. So the relaunch
        /// is handed to a detached shell that waits for this process to be gone first. The exit is
        /// <see cref="Environment.Exit"/> rather than the normal shutdown because the normal
        /// shutdown calls <c>SaveSettings()</c> - which would write the in-memory settings straight
        /// back over the file we just moved aside.</para>
        /// </summary>
        private void PerformFactoryReset()
        {
            var dir = App.UserDataPath;
            var settingsPath = Path.Combine(dir, "settings.json");
            var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var backupPath = Path.Combine(dir, $"settings.pre-reset-{stamp}.json");

            App.Logger?.Warning("[RESET] Factory reset confirmed. Settings only - assets, packs, mods " +
                                "and the account are untouched. Data folder: {Dir}", dir);

            // Relaunch FIRST so the window between "settings gone" and "process gone" is as small
            // as possible: if anything below throws, the user still gets an app back.
            var relaunched = TryScheduleRelaunch();

            try
            {
                if (File.Exists(settingsPath))
                {
                    File.Move(settingsPath, backupPath, overwrite: true);
                    App.Logger?.Warning("[RESET] settings.json moved aside to {Backup}", backupPath);
                }
                else
                {
                    App.Logger?.Warning("[RESET] no settings.json to move aside (already at defaults)");
                }

                foreach (var temp in SafeGlob(dir, "settings.json*.tmp"))
                {
                    try { File.Delete(temp); App.Logger?.Information("[RESET] deleted stale temp {Temp}", temp); }
                    catch (Exception ex) { App.Logger?.Warning("[RESET] could not delete temp {Temp}: {E}", temp, ex.Message); }
                }

                var marker = settingsPath + ".restored";
                if (File.Exists(marker))
                {
                    try { File.Delete(marker); App.Logger?.Information("[RESET] cleared cloud-restore marker"); }
                    catch (Exception ex) { App.Logger?.Warning("[RESET] could not clear restore marker: {E}", ex.Message); }
                }

                App.Logger?.Warning("[RESET] Factory reset complete - exiting{Relaunch}",
                                    relaunched ? " and relaunching" : " (relaunch unavailable, start the app again manually)");
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "[RESET] Factory reset failed while clearing the settings file");
                Tell(Window.GetWindow(this),
                     Loc.GetF("set2_reset_failed_body", ex.Message),
                     Loc.Get("set2_reset_failed_title"),
                     MessageBoxImage.Error);
                return;
            }

            // Hard exit on purpose - see the method docs. Serilog buffers, so flush first or the
            // whole [RESET] trail above is lost exactly when it matters.
            try { App.Logger?.Information("[RESET] flushing log before exit"); Serilog.Log.CloseAndFlush(); } catch { }
            Environment.Exit(0);
        }

        /// <summary>
        /// MessageBox with an owner only when there is one. Passing a null Window to the owned
        /// overload throws, and this control can legitimately be off-window (render tests, a
        /// popped-out host), which would turn a warning into a crash.
        /// </summary>
        private static void Tell(Window? owner, string message, string title, MessageBoxImage icon)
        {
            if (owner != null) MessageBox.Show(owner, message, title, MessageBoxButton.OK, icon);
            else MessageBox.Show(message, title, MessageBoxButton.OK, icon);
        }

        private static string[] SafeGlob(string dir, string pattern)
        {
            try { return Directory.Exists(dir) ? Directory.GetFiles(dir, pattern) : Array.Empty<string>(); }
            catch { return Array.Empty<string>(); }
        }

        /// <summary>
        /// Schedules "wait a few seconds, then start this exe again" in a detached shell.
        /// The wait is what makes it work: our single-instance handshake would otherwise have the
        /// new process ack against the old one and exit. Returns false if it could not be
        /// scheduled - the reset still proceeds, the user just starts the app themselves.
        /// </summary>
        private static bool TryScheduleRelaunch()
        {
            try
            {
                var exe = Environment.ProcessPath;
                if (string.IsNullOrEmpty(exe) || !File.Exists(exe))
                {
                    App.Logger?.Warning("[RESET] cannot relaunch: process path unavailable");
                    return false;
                }

                Process.Start(new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c timeout /t 4 /nobreak >nul & start \"\" \"{exe}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = Path.GetDirectoryName(exe) ?? Environment.CurrentDirectory,
                });
                App.Logger?.Information("[RESET] relaunch scheduled for {Exe}", exe);
                return true;
            }
            catch (Exception ex)
            {
                App.Logger?.Warning("[RESET] relaunch could not be scheduled: {E}", ex.Message);
                return false;
            }
        }
    }
}
