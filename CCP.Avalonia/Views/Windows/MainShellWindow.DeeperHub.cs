// PORTED from ConditioningControlPanel/MainWindow/MainWindow.DeeperHub.cs (row actions, keyboard
// selection, reload) and the library half of MainWindow.DeeperTab.cs (OpenDeeperFile,
// OpenDeeperEditor, the player entry points, open folder, two-step delete with undo, the welcome
// card's demo/tour buttons). The index, filter and sort live in DeeperTabViewModel over Core
// DeeperLocalLibrary; this partial owns the windows, dialogs, timers and files.
//
// Still out (see docs/avalonia-parity.md shell-deeper-hub): import (EnhancementLibrary
// FindDuplicateOf/PromoteToLibrary + RevealDeeperLibraryRow's flash), the bundled-demo seeding, the
// library FileSystemWatcher (LibraryChanged), the duration probe, and the Deeper tutorial.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Views.Deeper;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Deeper;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        /// <summary>WPF BtnDeeperCatalogue_Click (MainWindow.DeeperHub.cs:477).</summary>
        internal const string DeeperCatalogueUrl = "https://app.cclabs.app/catalogue";

        /// <summary>WPF DeeperDeleteGraceMs: the row is gone at once, the file after this.</summary>
        internal static readonly TimeSpan DeeperDeleteGrace = TimeSpan.FromMilliseconds(6000);
        internal static TimeProvider DeeperDeleteTime = TimeProvider.System;

        /// <summary>WPF ShowStyledDialog(title, msg, Delete, Cancel); tests answer it.</summary>
        internal static Func<Window, string, string, Task<bool>> DeeperConfirm = (owner, title, message) =>
            MessageDialog.ConfirmAsync(owner, title, message, okText: Loc.Get("btn_delete"), cancelText: Loc.Get("btn_cancel"));

        private readonly Dictionary<string, DeeperEditorWindow> _deeperOpenEditors = new(StringComparer.OrdinalIgnoreCase);
        private DispatcherTimer? _deeperDeleteTimer;

        private DeeperTabViewModel? DeeperModel => Named<DeeperTabView>("DeeperTab")?.DataContext as DeeperTabViewModel;

        /// <summary>Reloads the local index when the Deeper view is attached or the tab opens.</summary>
        internal void InitializeDeeperHub() => ReloadDeeperLibraryFromDisk();

        private void ReloadDeeperLibraryFromDisk() => DeeperModel?.ReloadLibrary();

        /// <summary>WPF ApplyDeeperFilterAndSort after a submission record changed: repaint badges.</summary>
        internal void RefreshDeeperLibraryRows() => DeeperModel?.Refresh();

        /// <summary>WPF OpenDeeperFile (MainWindow.DeeperTab.cs:748): load, then the editor.</summary>
        internal async void OpenDeeperFile(string path)
        {
            try
            {
                var enhancement = EnhancementSerializer.LoadFromFile(path);
                DeeperEditorWindow.TouchRecent(path);   // EnhancementLibrary.Open records it
                OpenDeeperEditor(enhancement, path);
            }
            catch (Exception ex)
            {
                if (ex is not EnhancementLoadException) Log.Warning(ex, "Failed to open Deeper file");
                if (IsVisible) await MessageDialog.ShowAsync(this, "Deeper", ex.Message);
            }
        }

        /// <summary>WPF OpenDeeperEditor: one editor per file (re-activated, not duplicated); closing
        /// it refreshes the hub list.</summary>
        internal void OpenDeeperEditor(Models.Deeper.Enhancement enhancement, string? filePath)
        {
            string? key = filePath is null ? null : DeeperTabViewModel.FullPath(filePath);
            if (key != null && _deeperOpenEditors.TryGetValue(key, out var existing))
            {
                if (existing.WindowState == WindowState.Minimized) existing.WindowState = WindowState.Normal;
                existing.Activate();
                return;
            }
            var window = new DeeperEditorWindow(enhancement, filePath);
            if (key != null) _deeperOpenEditors[key] = window;
            window.Closed += (_, _) =>
            {
                if (key != null) _deeperOpenEditors.Remove(key);
                ReloadDeeperLibraryFromDisk();
            };
            if (IsVisible) window.Show(this); else window.Show();
        }

        /// <summary>WPF EnhancementPlayerWindow.ShowOrActivate: one player window, reused.</summary>
        // The one shared player (the editor's Preview opens the same window), never a second one.
        private EnhancementPlayerWindow ShowOrActivateDeeperPlayer() => EnhancementPlayerWindow.ShowOrActivate(this);

        /// <summary>WPF BtnDeeperOpenPlayer_Click (MainWindow.DeeperTab.cs:202).</summary>
        internal void BtnDeeperOpenPlayer_Click()
        {
            CoreBark.NotifyUiAction("deeper_player");
            try { ShowOrActivateDeeperPlayer(); }
            catch (Exception ex) { Log.Error(ex, "Failed to open Deeper player"); }
        }

        /// <summary>WPF DeeperRowPlay_Click (MainWindow.DeeperHub.cs:577): a player that already has
        /// this file is only brought forward - reloading would restart playback from the top.</summary>
        internal void PlayDeeperLibraryEntry(string path)
        {
            try
            {
                var already = EnhancementPlayerWindow.Open.LastOrDefault() is { } open && DeeperTabViewModel.PathsEqual(open.LoadedFilePath, path);
                var player = ShowOrActivateDeeperPlayer();
                if (!already) player.LoadEnhancementFile(path);
            }
            catch (Exception ex) { Log.Error(ex, "Failed to open Deeper player for enhancement"); }
        }

        /// <summary>WPF BtnDeeperOpenLibraryFolder_Click: create it if needed, then the file manager.</summary>
        internal void OpenDeeperLibraryFolder()
        {
            try
            {
                Directory.CreateDirectory(DeeperLocalLibrary.DefaultFolder);
                Platform.ExternalOpener.Open(DeeperLocalLibrary.DefaultFolder);
            }
            catch (Exception ex) { Log.Warning(ex, "Failed to open Deeper library folder"); }
        }

        /// <summary>WPF DeeperRowMenuReveal_Click (explorer /select). Linux file managers have no
        /// portable "select this file", so the containing folder opens.</summary>
        internal static void RevealDeeperFile(string path)
        {
            try
            {
                if (File.Exists(path)) Platform.ExternalOpener.Open(Path.GetDirectoryName(Path.GetFullPath(path)));
            }
            catch (Exception ex) { Log.Warning(ex, "Deeper: reveal in folder failed"); }
        }

        // ---- the welcome card (WPF MainWindow.DeeperTab.cs:71-118) ----

        internal void BtnDeeperWelcomeTour_Click()
        {
            CoreBark.NotifyUiAction("deeper_tour");
            DismissDeeperWelcomeCard();
            // WPF StartDeeperTabTutorial: the tab, then the tour.
            ShowTab("deeper");
            CoreTutorial.Start("Deeper");
        }

        internal void BtnDeeperWelcomeDemo_Click()
        {
            DismissDeeperWelcomeCard();
            OpenDeeperBundledDemo();
        }

        /// <summary>The bundled demo is matched by file name, so a moved library still finds it.</summary>
        private async void OpenDeeperBundledDemo()
        {
            try
            {
                var match = DeeperLocalLibrary.Scan().Entries.FirstOrDefault(e =>
                    string.Equals(Path.GetFileName(e.FilePath), "welcome.ccpenh.json", StringComparison.OrdinalIgnoreCase));
                if (match != null) { OpenDeeperFile(match.FilePath); return; }
                if (IsVisible) await MessageDialog.ShowAsync(this, Loc.Get("deeper_dialog_title"), Loc.Get("deeper_demo_missing_text"));
            }
            catch (Exception ex) { Log.Debug("Open bundled Deeper demo failed: {Error}", ex.Message); }
        }

        // ---- two-step delete (WPF MainWindow.DeeperTab.cs:926-1020) ----

        /// <summary>Confirm, hide the row at once, recycle the file after the grace; the toast's
        /// Undo cancels it. Pending paths stay out of every rescan.</summary>
        internal async void DeleteDeeperLibraryEntry(EnhancementLibraryEntry entry)
        {
            try
            {
                var label = string.IsNullOrEmpty(entry.Name) ? Path.GetFileName(entry.FilePath) : entry.Name;
                if (!await DeeperConfirm(this, Loc.Get("deeper_library_delete_title"),
                        string.Format(Loc.Get("deeper_library_delete_confirm_fmt"), label))) return;
                if (DeeperModel is not { } model) return;

                var key = DeeperTabViewModel.FullPath(entry.FilePath);
                if (model.PendingDeletes.ContainsKey(key)) return;
                model.PendingDeletes[key] = DeeperDeleteTime.GetTimestamp();
                _deeperDeleteTimer ??= new DispatcherTimer(TimeSpan.FromMilliseconds(250), DispatcherPriority.Background,
                    (_, _) => CommitDueDeeperDeletes());
                _deeperDeleteTimer.Start();
                ReloadDeeperLibraryFromDisk();

                App.Notifications.Show(string.Format(Loc.Get("deeper_library_deleted_toast_fmt"), label),
                    Helpers.NotificationType.Info, DeeperDeleteGrace, Loc.Get("btn_undo"), () =>
                    {
                        if (!model.PendingDeletes.Remove(key)) return;
                        ReloadDeeperLibraryFromDisk();
                    });
            }
            catch (Exception ex) { Log.Warning(ex, "Deeper: delete failed"); }
        }

        /// <summary>The grace timer's tick: recycles every pending delete whose grace has run out.</summary>
        internal void CommitDueDeeperDeletes()
        {
            if (DeeperModel is not { } model) return;
            foreach (var (key, started) in model.PendingDeletes.ToList())
            {
                if (DeeperDeleteTime.GetElapsedTime(started) < DeeperDeleteGrace) continue;
                model.PendingDeletes.Remove(key);
                CommitDeeperDelete(key);
            }
            if (model.PendingDeletes.Count == 0) _deeperDeleteTimer?.Stop();
        }

        /// <summary>WPF: called on a real exit so a confirmed delete is not lost with the timer.</summary>
        private void CommitPendingDeeperDeletesOnExit()
        {
            _deeperDeleteTimer?.Stop();
            if (DeeperModel is not { } model || model.PendingDeletes.Count == 0) return;
            foreach (var key in model.PendingDeletes.Keys.ToList())
            {
                model.PendingDeletes.Remove(key);
                try { CommitDeeperDelete(key); } catch { }
            }
        }

        private void CommitDeeperDelete(string fullPath)
        {
            try
            {
                if (File.Exists(fullPath)) Platform.TrashBin.Move(fullPath);
                // The submission record is keyed by this path; a later file of the same name must
                // not inherit its badge.
                if (CoreSettings.Current.DeeperSubmissions.Remove(CanonicalCataloguePathKey(fullPath)))
                    CoreSettings.Save();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to delete Deeper library entry");
                App.Notifications.Show(ex.Message, Helpers.NotificationType.Error);
            }
            ReloadDeeperLibraryFromDisk();
        }
    }
}
