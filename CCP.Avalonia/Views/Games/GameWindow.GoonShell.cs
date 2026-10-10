using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.GoonGame;
using Newtonsoft.Json.Linq;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Games
{
    /// <summary>
    /// The Goon window's dealings with the rest of the desktop (WPF GoonHostService): the recap's match
    /// card (share-card, GoonShareCard.cs), the control panel ducked out of the way and brought back
    /// (:373, :393), and the boot-error box (:1838).
    /// </summary>
    internal sealed partial class GameWindow
    {
        // ============================ share-card ============================

        /// <summary>Test seams: the clipboard and the save dialog. Copy answers true when the card is on the
        /// clipboard; save answers "" on success, "cancelled" when the user backed out.</summary>
        internal Func<GameWindow, byte[], Task<bool>> GoonCopyCard { get; set; } = CopyGoonCardAsync;
        internal Func<GameWindow, byte[], string, Task<string>> GoonSaveCard { get; set; } = SaveGoonCardAsync;

        /// <summary>WPF GoonShareCard.Handle: the page hands over BYTES and nothing else. They must be a real
        /// PNG within the size and pixel caps before they touch the clipboard, and a save goes only where the
        /// user points the system dialog (the page's file name is a suggestion, tamed and forced to .png).
        /// No path, no url, no shell open. Every request answers exactly one share-card-result.</summary>
        private async void OnGoonShareCard(JObject o)
        {
            var req = GoonShareCard.Parse(o, out var error);
            var id = req?.Id ?? ((string?)o["id"] ?? "");
            void Reply(bool ok, string err) => Post(new { type = "share-card-result", id, ok, error = err });
            if (req == null)
            {
                Log.Debug("[Goon] share-card refused ({E})", error);
                Reply(false, error);
                return;
            }
            try
            {
                if (!GoonShareCard.PixelsOk(req.Png)) { Reply(false, "bad-format"); return; }
                if (req.Action == "copy")
                {
                    var ok = await GoonCopyCard(this, req.Png);
                    Reply(ok, ok ? "" : "clipboard-busy");
                }
                else
                {
                    var r = await GoonSaveCard(this, req.Png, req.FileName);
                    Reply(r == "", r);
                }
            }
            catch (Exception ex)
            {
                Log.Warning("[Goon] share-card {A} failed: {E}", req.Action, ex.Message);
                Reply(false, "failed");
            }
        }

        private static async Task<bool> CopyGoonCardAsync(GameWindow w, byte[] png)
        {
            if (w.Clipboard is not { } clip) return false;
            for (int attempt = 0; attempt < 4; attempt++)
            {
                try
                {
                    using var ms = new MemoryStream(png);
                    await clip.SetBitmapAsync(new Bitmap(ms));   // decoded here: bytes that are not a picture never land
                    return true;
                }
                catch (Exception ex) when (attempt < 3)
                {
                    // The clipboard is a shared lock; another app holding it for a moment is normal.
                    Log.Debug("[Goon] clipboard busy: {E}", ex.Message);
                    await Task.Delay(60);
                }
            }
            return false;
        }

        private static async Task<string> SaveGoonCardAsync(GameWindow w, byte[] png, string fileName)
        {
            IStorageFolder? pictures = null;
            try { pictures = await w.StorageProvider.TryGetWellKnownFolderAsync(WellKnownFolder.Pictures); } catch { }
            var file = await w.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                SuggestedFileName = fileName,
                DefaultExtension = ".png",
                ShowOverwritePrompt = true,
                SuggestedStartLocation = pictures,
                FileTypeChoices = new[] { new FilePickerFileType(Loc.Get("goon_share_png_filter")) { Patterns = new[] { "*.png" } } },
            });
            if (file == null) return "cancelled";
            await using (var s = await file.OpenWriteAsync()) await s.WriteAsync(png);
            Log.Information("[Goon] saved a match card ({N} bytes)", png.Length);
            return "";
        }

        // ============================ ducking the control panel ============================

        private bool _goonDuckedMain;
        private WindowState _goonMainStateBeforeDuck = WindowState.Normal;

        /// <summary>WPF DuckMainWindow: a plain MINIMIZE of the control panel, never a tray tuck. No-op when
        /// it is already minimized or hidden: the user's own last word on the window stands and no restore
        /// is owed.</summary>
        private void DuckGoonMain()
        {
            try
            {
                var main = Windows.MainShellWindow.Current;
                if (main == null || !main.IsVisible || main.WindowState == WindowState.Minimized) return;
                _goonMainStateBeforeDuck = main.WindowState;
                main.WindowState = WindowState.Minimized;
                _goonDuckedMain = true;
                // Minimizing the panel hands activation to whatever is next; take it back for the duel.
                try { Activate(); Web.Focus(); } catch { }
                Log.Information("[Goon] ducked the panel (was {S})", _goonMainStateBeforeDuck);
            }
            catch (Exception ex) { Log.Debug("[Goon] duck: {E}", ex.Message); }
        }

        /// <summary>WPF RestoreMainWindow, from the one close funnel: a tool that minimizes the app and leaves
        /// it minimized is a worse bug than the one the duck fixes.</summary>
        private void RestoreGoonMain()
        {
            if (!_goonDuckedMain) return;
            _goonDuckedMain = false;
            try
            {
                var main = Windows.MainShellWindow.Current;
                if (main == null || !main.IsVisible || main.WindowState != WindowState.Minimized) return;
                main.WindowState = _goonMainStateBeforeDuck == WindowState.Maximized ? WindowState.Maximized : WindowState.Normal;
                try { main.Activate(); } catch { }
            }
            catch (Exception ex) { Log.Debug("[Goon] restore: {E}", ex.Message); }
        }

        // ============================ boot-error ============================

        /// <summary>Test seam: the box that names a boot failure (WPF OnBootError's MessageBox).</summary>
        internal static Func<string, Task> GoonBootErrorBox { get; set; } = async text =>
        {
            if (Windows.MainShellWindow.Current is not { IsVisible: true } shell) return;   // nowhere to show it: the log has it
            await Dialogs.MessageDialog.ShowAsync(shell, GoonHostService.ProductName, text);
        };

        /// <summary>WPF OnBootError: the window goes first (the shell closes it), then the box says why.</summary>
        private static void ShowGoonBootError(string? msg)
        {
            var text = Loc.GetF("arcademy_boot_error_body", GoonHostService.ProductName, msg ?? "");
            Dispatcher.UIThread.Post(async () =>
            {
                try { await GoonBootErrorBox(text); }
                catch (Exception ex) { Log.Debug("[Goon] boot-error box: {E}", ex.Message); }
            });
        }
    }
}
