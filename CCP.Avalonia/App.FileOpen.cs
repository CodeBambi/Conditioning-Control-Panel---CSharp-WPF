// PORTED from WPF App.xaml.cs:1543-1600 (the show-signal callback) and :3187-3204 (the first
// instance's own --play / --edit), plus MainWindow.DeeperTab.cs:604-675 (HandlePendingFileOpen).
using System;
using System.Threading.Tasks;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services.Launcher;

namespace ConditioningControlPanel.Avalonia
{
    public partial class App
    {
        /// <summary>This launch's own "Open with CCP" request, opened once the shell is up.</summary>
        internal static (string action, string path)? PendingFileOpen;

        /// <summary>
        /// A second launch asked this instance to show (pipe from a port launch, WPF's named signal
        /// from a WPF launch). The payload is a <see cref="LauncherHandoff"/> surface, null for a
        /// bare relaunch, or the marker that says "read the handoff file".
        /// </summary>
        internal static Task RouteSecondLaunch(string? payload) =>
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                string? action = null, path = null;
                if (payload == WpfInstanceBridge.HandoffFileMarker)
                {
                    (action, path) = FileOpenHandoff.Consume(ConditioningControlPanel.CorePaths.UserData);
                    payload = action == LauncherHandoff.Action ? path : null;
                }
                var w = (Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
                if (w is not MainShellWindow shell) { w?.Activate(); return; }
                if (path != null && (action == FileOpenHandoff.PlayAction || action == FileOpenHandoff.EditAction))
                {
                    // WPF App.xaml.cs:1583: raise the panel, then open the file.
                    try { shell.ShowFromTray(); } catch (Exception ex) { Serilog.Log.Warning(ex, "ShowFromTray failed"); }
                    try { shell.HandlePendingFileOpen(action, path); } catch (Exception ex) { Serilog.Log.Warning(ex, "HandlePendingFileOpen failed"); }
                    return;
                }
                LauncherWindow.RouteHandoff(shell, payload);
            }).GetTask();

        /// <summary>Call once the shell exists: ends the single-instance startup phase when the UI
        /// thread first pumps, and replays this launch's own --play / --edit (WPF App.xaml.cs:3187).</summary>
        internal static void OnShellReadyForHandoffs(MainShellWindow shell)
        {
            Dispatcher.UIThread.Post(() =>
            {
                WpfInstanceBridge.StartupPhase = false;
                if (PendingFileOpen is not { } open) return;
                PendingFileOpen = null;
                try { shell.HandlePendingFileOpen(open.action, open.path); }
                catch (Exception ex) { Serilog.Log.Warning(ex, "HandlePendingFileOpen failed"); }
            }, DispatcherPriority.Background);
        }
    }
}

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        /// <summary>Test seam: what a play / edit request opened (action, path).</summary>
        internal static Action<string, string>? FileOpenProbe;

        /// <summary>WPF MainWindow.DeeperTab.cs:670. play = the Deeper player on the media file,
        /// edit = the Deeper editor on a blank enhancement for it.</summary>
        public void HandlePendingFileOpen(string action, string path)
        {
            if (string.IsNullOrEmpty(action) || string.IsNullOrEmpty(path)) return;
            if (FileOpenProbe is { } probe) { probe(action, path); return; }
            if (action == FileOpenHandoff.PlayAction)
            {
                // WPF OpenInDeeperPlayer: the one player window, then the media file.
                try { ShowOrActivateDeeperPlayer().OpenLocalMediaFile(path); }
                catch (Exception ex) { Serilog.Log.Error(ex, "Failed to open Deeper player for {Path}", path); }
            }
            else if (action == FileOpenHandoff.EditAction)
            {
                // WPF OpenInDeeperEditorForMedia: EnhancementLibrary.CreateBlank(mediaType, path).
                try
                {
                    OpenDeeperEditor(new Models.Deeper.Enhancement
                    {
                        MediaType = FileOpenHandoff.IsVideo(path) ? Models.Deeper.MediaTypes.Video : Models.Deeper.MediaTypes.Audio,
                        MediaSource = path,
                    }, null);
                }
                catch (Exception ex) { Serilog.Log.Error(ex, "Failed to open Deeper editor for media"); }
            }
        }
    }
}
