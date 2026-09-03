using System;
using System.Diagnostics;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Controls.AppSettings
{
    /// <summary>
    /// SETTINGS ▸ DATA, ported from the WPF head: offline mode, phrase backup, the cloud-backup
    /// signpost and the Danger Zone.
    ///
    /// On WPF the first three rows forward to MainWindow partials; here the logic lives in the
    /// section, against Core. Offline mode prompts for a username through the ported
    /// <see cref="Dialogs.OfflineUsernameDialog"/> and reverts on cancel, as WPF does. The phrase
    /// backup goes through <see cref="PhraseBackupService"/> (now in Core) and Avalonia's
    /// StorageProvider. The factory reset keeps the WPF contract exactly: two unskippable
    /// confirmations, settings only, sealed service, detached relaunch, hard exit — see
    /// <see cref="PerformFactoryReset"/>.
    /// </summary>
    public partial class DataSettingsSection : UserControl
    {
        private bool _isLoading = true;
        private PhraseBackupService? _phraseBackupService;

        public DataSettingsSection()
        {
            // InitializeComponent, not AvaloniaXamlLoader.Load: only the generated one assigns
            // the x:Name fields, and the seed below reads them.
            InitializeComponent();
            SyncFromSettings();
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            if (CoreSettings.Service is { } svc) svc.CurrentReplaced += OnCurrentReplaced;
            SyncFromSettings();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            if (CoreSettings.Service is { } svc) svc.CurrentReplaced -= OnCurrentReplaced;
            base.OnDetachedFromVisualTree(e);
        }

        private void OnCurrentReplaced() => Dispatcher.UIThread.Post(SyncFromSettings);

        internal void SyncFromSettings()
        {
            _isLoading = true;
            try { ChkOfflineMode.IsChecked = CoreSettings.Current.OfflineMode; }
            finally { _isLoading = false; }
        }

        // =====================================================================================
        //  offline mode + phrase backup
        // =====================================================================================

        private async void ChkOfflineMode_Changed(object? sender, RoutedEventArgs e)
        {
            if (_isLoading) return;

            var isEnabled = ChkOfflineMode.IsChecked ?? false;
            var s = CoreSettings.Current;

            if (isEnabled)
            {
                // Enabling offline mode — prompt for a username if not set, as WPF does. Without
                // a window to own the dialog (headless render) there is no way to ask, so revert
                // rather than enable a half-configured mode.
                if (string.IsNullOrWhiteSpace(s.OfflineUsername))
                {
                    if (TopLevel.GetTopLevel(this) is not Window owner)
                    {
                        Revert(false);
                        return;
                    }

                    var dialog = new Dialogs.OfflineUsernameDialog();
                    var ok = await dialog.ShowDialog<bool?>(owner);
                    if (ok == true && !string.IsNullOrWhiteSpace(dialog.Username))
                    {
                        s.OfflineUsername = dialog.Username;
                    }
                    else
                    {
                        Revert(false);   // user cancelled
                        return;
                    }
                }

                s.OfflineMode = true;
                // ponytail: WPF also disconnects the live network services and greys the online
                // rows (MainWindow.DisconnectNetworkServices / UpdateOfflineModeUI); those are the
                // shell's, wired when its network partials exist on this head.
                Log.Information("Offline mode enabled with username '{Username}'", s.OfflineUsername);
            }
            else
            {
                s.OfflineMode = false;
                Log.Information("Offline mode disabled");
            }

            CoreSettings.Save();

            void Revert(bool value)
            {
                _isLoading = true;
                try { ChkOfflineMode.IsChecked = value; }
                finally { _isLoading = false; }
            }
        }

        private async void BtnExportPhrases_Click(object? sender, RoutedEventArgs e)
        {
            var settings = CoreSettings.Current;
            if (TopLevel.GetTopLevel(this) is not { } top) return;
            _phraseBackupService ??= new PhraseBackupService();

            var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Export Phrases",
                SuggestedFileName = PhraseBackupService.GetExportFileName(),
                DefaultExtension = ".ccpphrases.json",
                FileTypeChoices = new[]
                {
                    new FilePickerFileType("Phrase backup") { Patterns = new[] { "*.ccpphrases.json" } },
                },
            });
            if (file?.TryGetLocalPath() is not { } path) return;

            try
            {
                var count = _phraseBackupService.Export(settings, path);
                Log.Information("Phrases exported: {Count} to {Path}", count, path);
                // ponytail: WPF confirms with ShowStyledDialog ("Phrases Exported"); no styled
                // message dialog on this head yet.
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to export phrases");
            }
        }

        private async void BtnImportPhrases_Click(object? sender, RoutedEventArgs e)
        {
            var settings = CoreSettings.Current;
            if (TopLevel.GetTopLevel(this) is not Window owner) return;
            _phraseBackupService ??= new PhraseBackupService();

            var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Import Phrases",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("Phrase backup") { Patterns = new[] { "*.ccpphrases.json" } },
                    new FilePickerFileType("All files") { Patterns = new[] { "*" } },
                },
            });
            if (files.Count != 1 || files[0].TryGetLocalPath() is not { } path) return;

            if (!_phraseBackupService.Validate(path, out var error))
            {
                Log.Warning("Phrase import rejected: {Error}", error);
                // ponytail: WPF explains with ShowStyledDialog ("Import Failed"); no styled
                // message dialog on this head yet.
                return;
            }

            // Importing replaces the current phrase pools — confirm first. WPF uses a two-button
            // styled confirm; the ported WarningDialog (acknowledge-gated) is the closest
            // equivalent on this head and errs stricter, never looser.
            var confirm = new Dialogs.WarningDialog(
                "Import Phrases?",
                "This replaces your current lock-card phrases, subliminals, mantras and other " +
                "custom text with the ones in the backup file. Continue?",
                "Replace my phrases with the backup");
            await confirm.ShowDialog(owner);
            if (!confirm.Confirmed) return;

            try
            {
                var count = _phraseBackupService.Import(settings, path);
                CoreSettings.Save();
                Log.Information("Phrases imported: {Count} from {Path}", count, path);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to import phrases");
            }
        }

        private void BtnGoToAccountBackup_Click(object? sender, RoutedEventArgs e)
        {
            this.FindAncestorOfType<Tabs.AppSettingsTabView>()?.FocusSection("account");
        }

        // =====================================================================================
        //  danger zone
        // =====================================================================================

        /// <summary>
        /// Two dialogs, in this order, and neither is skippable:
        /// <list type="number">
        ///   <item>a <see cref="Dialogs.WarningDialog"/> that lists what goes and what stays,
        ///   gated on its acknowledgement checkbox;</item>
        ///   <item>an <see cref="Dialogs.InputDialog"/> that only accepts the literal keyword.</item>
        /// </list>
        /// </summary>
        private async void BtnFactoryReset_Click(object? sender, RoutedEventArgs e)
        {
            if (TopLevel.GetTopLevel(this) is not Window owner) return;

            // ponytail: WPF refuses outright during Lockdown (App.Lockdown.IsActive) so the reset
            // cannot become a lockdown escape hatch; restore this check when a lockdown seam
            // exists on this head.

            // --- confirmation 1: what happens ---
            var warning = new Dialogs.WarningDialog(
                Loc.Get("set2_reset_dialog1_title"),
                Loc.Get("set2_reset_dialog1_body"),
                Loc.Get("set2_reset_dialog1_confirm"));
            await warning.ShowDialog(owner);
            if (!warning.Confirmed)
            {
                Log.Information("[RESET] Factory reset cancelled at the warning dialog");
                return;
            }

            // --- confirmation 2: type it out ---
            // A typed word, not a second button: the point is to make the last step impossible to
            // click through by muscle memory. Compared case-insensitively and trimmed so the
            // barrier is intent, not typing accuracy.
            var keyword = Loc.Get("set2_reset_keyword");
            var input = new Dialogs.InputDialog(
                Loc.Get("set2_reset_dialog2_title"),
                Loc.GetF("set2_reset_dialog2_prompt", keyword));
            var accepted = await input.ShowDialog<bool?>(owner);
            if (accepted != true ||
                !string.Equals(input.ResultText?.Trim(), keyword, StringComparison.OrdinalIgnoreCase))
            {
                Log.Information("[RESET] Factory reset cancelled at the keyword prompt");
                return;
            }

            PerformFactoryReset();
        }

        /// <summary>
        /// Resets <b>settings only</b> and restarts. The contract is the WPF head's, verbatim:
        /// settings.json is moved aside (never deleted), leftover save temps and the cloud-restore
        /// marker go with it, a factory-reset marker tells the next launch the missing file is
        /// deliberate, the service is sealed so a mid-flight debounced save cannot resurrect the
        /// file, the relaunch is a detached delayed shell (our single-instance handshake would
        /// otherwise ack the new process straight back out), and the exit is hard because the
        /// normal shutdown saves settings — which would undo the reset. What stays, deliberately:
        /// assets, packs, mods, achievements, the account, crash logs and the daily .bak rotation.
        /// </summary>
        private void PerformFactoryReset()
        {
            var dir = CorePaths.UserData;
            var settingsPath = Path.Combine(dir, "settings.json");
            var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var backupPath = Path.Combine(dir, $"settings.pre-reset-{stamp}.json");

            Log.Warning("[RESET] Factory reset confirmed. Settings only - assets, packs, mods " +
                        "and the account are untouched. Data folder: {Dir}", dir);

            // BEFORE anything touches the file: flush the user's true final state (so the
            // moved-aside backup is what they actually had), then seal the service so a debounced
            // or async save cannot re-create settings.json between the move and the exit.
            try { CoreSettings.Service?.SaveImmediate(); } catch (Exception ex) { Log.Warning("[RESET] final flush failed: {E}", ex.Message); }
            CoreSettings.Service?.SealForReset();

            // Relaunch FIRST so the window between "settings gone" and "process gone" is as small
            // as possible: if anything below throws, the user still gets an app back.
            var relaunched = TryScheduleRelaunch();

            try
            {
                if (File.Exists(settingsPath))
                {
                    File.Move(settingsPath, backupPath, overwrite: true);
                    Log.Warning("[RESET] settings.json moved aside to {Backup}", backupPath);
                }
                else
                {
                    Log.Warning("[RESET] no settings.json to move aside (already at defaults)");
                }

                foreach (var temp in SafeGlob(dir, "settings.json*.tmp"))
                {
                    try { File.Delete(temp); Log.Information("[RESET] deleted stale temp {Temp}", temp); }
                    catch (Exception ex) { Log.Warning("[RESET] could not delete temp {Temp}: {E}", temp, ex.Message); }
                }

                var marker = settingsPath + ".restored";
                if (File.Exists(marker))
                {
                    try { File.Delete(marker); Log.Information("[RESET] cleared cloud-restore marker"); }
                    catch (Exception ex) { Log.Warning("[RESET] could not clear restore marker: {E}", ex.Message); }
                }

                // Tell the next launch that the missing settings.json is DELIBERATE, so it does
                // not offer to restore the cloud backup of the settings the user just asked to be
                // rid of. The marker is consumed there.
                try
                {
                    File.WriteAllText(settingsPath + ".factory-reset", stamp);
                    Log.Information("[RESET] dropped factory-reset marker for the next launch");
                }
                catch (Exception ex) { Log.Warning("[RESET] could not drop factory-reset marker: {E}", ex.Message); }

                Log.Warning("[RESET] Factory reset complete - exiting{Relaunch}",
                            relaunched ? " and relaunching" : " (relaunch unavailable, start the app again manually)");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[RESET] Factory reset failed while clearing the settings file");
                // The app keeps running on this path, so give it its saves back - a sealed service
                // would silently stop persisting anything for the rest of the session.
                CoreSettings.Service?.UnsealAfterFailedReset();
                // ponytail: WPF also tells the user (MessageBox, set2_reset_failed_*); no styled
                // message dialog on this head yet.
                return;
            }

            // Belt and braces: a temp created by a save that was already mid-flight when the sweep
            // above ran would be promoted to settings.json on the next launch, undoing the reset.
            foreach (var temp in SafeGlob(dir, "settings.json*.tmp"))
            {
                try { File.Delete(temp); Log.Warning("[RESET] deleted late temp {Temp}", temp); }
                catch (Exception ex) { Log.Warning("[RESET] could not delete late temp {Temp}: {E}", temp, ex.Message); }
            }

            // Hard exit on purpose - see the method docs. Serilog buffers, so flush first or the
            // whole [RESET] trail above is lost exactly when it matters.
            try { Log.Information("[RESET] flushing log before exit"); Serilog.Log.CloseAndFlush(); } catch { }
            Environment.Exit(0);
        }

        private static string[] SafeGlob(string dir, string pattern)
        {
            try { return Directory.Exists(dir) ? Directory.GetFiles(dir, pattern) : Array.Empty<string>(); }
            catch { return Array.Empty<string>(); }
        }

        /// <summary>
        /// Schedules "wait a few seconds, then start this exe again" in a detached shell, per
        /// platform. The wait is what makes it work: the single-instance handshake would otherwise
        /// have the new process ack against the old one and exit. Returns false if it could not be
        /// scheduled - the reset still proceeds, the user just starts the app themselves.
        /// </summary>
        private static bool TryScheduleRelaunch()
        {
            try
            {
                var exe = Environment.ProcessPath;
                if (string.IsNullOrEmpty(exe) || !File.Exists(exe))
                {
                    Log.Warning("[RESET] cannot relaunch: process path unavailable");
                    return false;
                }

                var psi = OperatingSystem.IsWindows()
                    ? new ProcessStartInfo
                    {
                        FileName = "cmd.exe",
                        Arguments = $"/c timeout /t 4 /nobreak >nul & start \"\" \"{exe}\"",
                    }
                    : new ProcessStartInfo
                    {
                        FileName = "/bin/sh",
                        Arguments = $"-c 'sleep 4; exec \"{exe}\"'",
                    };
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.WorkingDirectory = Path.GetDirectoryName(exe) ?? Environment.CurrentDirectory;

                Process.Start(psi);
                Log.Information("[RESET] relaunch scheduled for {Exe}", exe);
                return true;
            }
            catch (Exception ex)
            {
                Log.Warning("[RESET] relaunch could not be scheduled: {E}", ex.Message);
                return false;
            }
        }
    }
}
