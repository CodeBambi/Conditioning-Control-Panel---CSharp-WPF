using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using ConditioningControlPanel.Models;
using XamlAnimatedGif;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// Displays standalone, always-on corner-GIF overlays that live outside any session.
    /// Reads <see cref="AppSettings.CornerGifOverlays"/> and shows one transparent, click-through,
    /// topmost window per enabled slot. This mirrors SessionEngine's session-scoped corner GIF but
    /// is app-wide and can drive several corners at once (the Spiral card exposes two).
    /// </summary>
    public class CornerGifService
    {
        /// <summary>Hard cap on simultaneous overlay windows, independent of what the settings file
        /// contains - the config UI only ever writes CornerGifWindow.SlotCount entries, but a
        /// hand-edited / migrated settings.json must not be able to spawn an unbounded number of
        /// topmost layered windows.</summary>
        private const int MaxOverlays = 2;

        /// <summary>Diagonal inset (DIPs) applied per slot that lands in an already-claimed corner.</summary>
        private const double SameCornerNudge = 40;

        // Keyed by slot index so a single slot can be rebuilt without touching the others.
        private readonly Dictionary<int, Window> _windows = new();

        // Per-slot generation counter. Realization is deferred (see QueueShow), so a teardown or a
        // newer refresh has to be able to cancel a Show that is still sitting in the dispatcher
        // queue - otherwise it lands untracked and StopAll can never close it.
        private readonly Dictionary<int, int> _slotSeq = new();

        // Backup teardown, matching AttentionCheckService: these overlays deliberately have NO
        // Owner (an owned window is hidden by the OS whenever its owner is minimized, which would
        // kill the whole point of a corner GIF you watch while working elsewhere - same reasoning
        // as the bouncing-text overlay in MainWindow.OnClosing). Unowned topmost windows block
        // ShutdownMode=OnLastWindowClose, so closing them from MainWindow.Closing is what lets the
        // app actually exit. MainWindow raises Closing ONLY on a real exit - minimize-to-tray
        // cancels first and never raises it - so this does not kill overlays on the tray path.
        private Window? _subscribedWindow;

        /// <summary>
        /// Tears down every overlay and re-shows the ones currently enabled in settings.
        /// Safe to call from any thread; marshals to the UI thread. The teardown is synchronous but
        /// each re-show is deferred one dispatcher pass (see QueueShow). Call after any config
        /// change; startup restore goes through <see cref="RestoreOnStartup"/> instead.
        /// </summary>
        public void RefreshOverlays() => RefreshOverlays(null);

        private void RefreshOverlays(Action? onComplete)
        {
            var disp = Application.Current?.Dispatcher;
            if (disp == null) return;
            if (!disp.CheckAccess())
            {
                disp.BeginInvoke(new Action(() => RefreshOverlays(onComplete)));
                return;
            }

            StopAll();
            TrySubscribeMainWindowClosing();

            var overlays = App.Settings?.Current?.CornerGifOverlays;
            if (overlays == null)
            {
                onComplete?.Invoke();
                return;
            }

            int queued = 0;
            for (int i = 0; i < overlays.Count; i++)
            {
                var o = overlays[i];
                if (o == null || !o.Enabled) continue;
                if (queued >= MaxOverlays)
                {
                    App.Logger?.Warning("CornerGifService: settings list has more than {Max} enabled corner-GIF slots - ignoring the rest", MaxOverlays);
                    break;
                }
                queued++;
                QueueShow(disp, i, o);
            }

            // Same priority as the queued shows, so FIFO puts this after the last one.
            if (onComplete != null) disp.BeginInvoke(onComplete, DispatcherPriority.Background);
        }

        /// <summary>
        /// Rebuilds ONE slot's overlay instead of every slot. Slots are independent windows, so a
        /// live size/opacity edit on one leaves the other's hwnd - and its running animation -
        /// untouched. Callers that change something cross-slot (corner pick, enable toggle: both
        /// shift the same-corner nudge below) must use <see cref="RefreshOverlays()"/> instead.
        /// </summary>
        public void RefreshSlot(int index)
        {
            var disp = Application.Current?.Dispatcher;
            if (disp == null) return;
            if (!disp.CheckAccess())
            {
                disp.BeginInvoke(new Action(() => RefreshSlot(index)));
                return;
            }

            CloseSlot(index);
            TrySubscribeMainWindowClosing();

            var overlays = App.Settings?.Current?.CornerGifOverlays;
            if (overlays == null || index < 0 || index >= overlays.Count || index >= MaxOverlays) return;

            var setting = overlays[index];
            if (setting == null || !setting.Enabled) return;

            QueueShow(disp, index, setting);
        }

        /// <summary>
        /// Startup restore path (#709). Guarded by a flag file: if the previous launch armed the
        /// restore and never finished it, the persisted slots are what wedged that launch, so they
        /// are force-disabled rather than replayed into the same wedge on every subsequent start
        /// (the reported workaround was reinstalling to wipe settings).
        /// </summary>
        public void RestoreOnStartup()
        {
            var disp = Application.Current?.Dispatcher;
            if (disp == null) return;
            if (!disp.CheckAccess())
            {
                disp.BeginInvoke(new Action(RestoreOnStartup));
                return;
            }

            var overlays = App.Settings?.Current?.CornerGifOverlays;
            bool anyEnabled = false;
            if (overlays != null)
            {
                foreach (var o in overlays)
                    if (o != null && o.Enabled) { anyEnabled = true; break; }
            }

            if (ConsumeRestoreSentinel())
            {
                if (anyEnabled && overlays != null)
                {
                    foreach (var o in overlays)
                        if (o != null) o.Enabled = false;
                    App.Settings?.Save();
                }
                App.Logger?.Warning("CornerGifService: the previous launch died while restoring corner-GIF overlays - all slots force-disabled so this launch can start. Re-enable them from the Spiral card.");
                return;
            }

            if (!anyEnabled) return;

            ArmRestoreSentinel();
            RefreshOverlays(ClearRestoreSentinel);
        }

        /// <summary>Closes every active corner-GIF overlay window.</summary>
        public void StopAll()
        {
            var disp = Application.Current?.Dispatcher;
            if (disp != null && !disp.CheckAccess())
            {
                disp.Invoke(StopAll);
                return;
            }

            foreach (var w in new List<Window>(_windows.Values))
            {
                try { w.Close(); }
                catch { /* already gone */ }
            }
            _windows.Clear();

            // Cancel every deferred realization still in the queue, or a teardown that races one
            // leaves an untracked overlay the user has no way to close.
            foreach (var index in new List<int>(_slotSeq.Keys)) BumpSlot(index);
        }

        private void CloseSlot(int index)
        {
            BumpSlot(index); // cancels a pending realization for this slot
            if (_windows.TryGetValue(index, out var w))
            {
                _windows.Remove(index);
                try { w.Close(); }
                catch { /* already gone */ }
            }
        }

        private int BumpSlot(int index)
        {
            _slotSeq.TryGetValue(index, out var seq);
            seq++;
            _slotSeq[index] = seq;
            return seq;
        }

        private int CurrentSeq(int index)
        {
            _slotSeq.TryGetValue(index, out var seq);
            return seq;
        }

        /// <summary>
        /// Defers one overlay's realization by a dispatcher pass. Show() on a WS_EX_LAYERED
        /// (AllowsTransparency) window runs a synchronous HwndTarget.OnResize ->
        /// MediaContext.CompleteRender on FIRST realization; doing Close,Close,Show,Show in a
        /// single synchronous burst - two slots - fires that while the previous pair's render
        /// targets are still tearing down and the first GIF animator is driving the render thread.
        /// That is the freeze #494 shape, and the crash reported in #709. Background priority also
        /// lets the StopAll() closes above finish before the first Show.
        /// </summary>
        private void QueueShow(Dispatcher disp, int index, CornerGifOverlaySetting setting)
        {
            var seq = BumpSlot(index);
            disp.BeginInvoke(new Action(() =>
            {
                if (CurrentSeq(index) != seq) return; // superseded or torn down while queued
                try { ShowOne(index, setting); }
                catch (Exception ex) { App.Logger?.Error(ex, "CornerGifService: ShowOne failed"); }
            }), DispatcherPriority.Background);
        }

        private void TrySubscribeMainWindowClosing()
        {
            if (_subscribedWindow != null) return;
            // MainWindowRef first: Application.Current.MainWindow is whatever window happened to be
            // shown first, which is not always the real main window during startup.
            Window? main = App.MainWindowRef ?? Application.Current?.MainWindow;
            if (main == null) return;
            main.Closing += OnMainWindowClosing;
            _subscribedWindow = main;
        }

        private void OnMainWindowClosing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            if (_subscribedWindow != null)
            {
                try { _subscribedWindow.Closing -= OnMainWindowClosing; } catch { }
                _subscribedWindow = null;
            }
            StopAll();
        }

        private void ShowOne(int index, CornerGifOverlaySetting setting)
        {
            Uri? gifUri = null;
            System.Drawing.Image? img = null;
            System.IO.MemoryStream? imgBacking = null;   // must outlive img (see the resource branch)

            // Resolution order: this slot's explicit pick -> the Spiral Library's active
            // selection (App.Settings.SpiralPath, the "pool") -> built-in spiral resource.
            // So an enabled-but-unpicked slot draws whatever spiral the app is already using,
            // matching OverlayService.GetSpiralPath (the pool) rather than a separate file.
            string filePath = "";
            if (!string.IsNullOrEmpty(setting.GifPath) && System.IO.File.Exists(setting.GifPath))
            {
                filePath = setting.GifPath;
            }
            else
            {
                var poolPath = App.Settings?.Current?.SpiralPath;
                if (!string.IsNullOrEmpty(poolPath) && System.IO.File.Exists(poolPath))
                    filePath = poolPath;
            }

            if (!string.IsNullOrEmpty(filePath))
            {
                try
                {
                    gifUri = new Uri(filePath);
                    img = System.Drawing.Image.FromFile(filePath);
                }
                catch (Exception ex)
                {
                    App.Logger?.Warning("CornerGifService: failed to load GIF from file {Path}: {Error}", filePath, ex.Message);
                    gifUri = null;
                    img = null;
                }
            }

            // Built-in spiral resource fallback (same resource the pool's "Default" card uses).
            // Handles both pack:// (embedded) and file:// (active-mod override) URIs.
            if (img == null)
            {
                try
                {
                    gifUri = new Uri(ModResourceResolver.ResolveUri("spiral.gif"), UriKind.Absolute);
                    if (gifUri.IsFile)
                    {
                        img = System.Drawing.Image.FromFile(gifUri.LocalPath);
                    }
                    else
                    {
                        var resourceInfo = Application.GetResourceStream(gifUri);
                        if (resourceInfo?.Stream != null)
                        {
                            // GDI+ keeps reading the source stream for the Image's whole lifetime,
                            // so the resource stream cannot be disposed here - copy it into a buffer
                            // that outlives img and is released alongside it below.
                            imgBacking = new System.IO.MemoryStream();
                            using (var src = resourceInfo.Stream) src.CopyTo(imgBacking);
                            imgBacking.Position = 0;
                            img = System.Drawing.Image.FromStream(imgBacking);
                        }
                    }
                }
                catch (Exception ex)
                {
                    App.Logger?.Warning("CornerGifService: failed to load default spiral resource: {Error}", ex.Message);
                }
            }

            if (img == null || gifUri == null)
            {
                App.Logger?.Warning("CornerGifService: could not load any corner GIF image - skipping");
                img?.Dispose();
                imgBacking?.Dispose();
                return;
            }

            double gifWidth = img.Width;
            double gifHeight = img.Height;
            img.Dispose();
            imgBacking?.Dispose();

            // Bug #625: a decoded-but-degenerate image (0x0) makes the scale below divide by
            // zero, and assigning the resulting NaN/Infinity to Window.Width/Height throws
            // deep inside WPF layout - which, on the startup restore path, took the whole app
            // down. Bail out loudly instead of handing WPF non-finite geometry.
            if (gifWidth <= 0 || gifHeight <= 0)
            {
                App.Logger?.Warning("CornerGifService: GIF has degenerate size {W}x{H} ({Path}) - skipping overlay",
                    gifWidth, gifHeight, gifUri);
                return;
            }

            // Scale to the user's longest-edge size (default 300).
            var targetSize = setting.Size > 0 ? setting.Size : 300;
            double scale = targetSize / Math.Max(gifWidth, gifHeight);
            double windowWidth = gifWidth * scale;
            double windowHeight = gifHeight * scale;

            if (!double.IsFinite(scale) || !double.IsFinite(windowWidth) || !double.IsFinite(windowHeight)
                || windowWidth <= 0 || windowHeight <= 0)
            {
                App.Logger?.Warning("CornerGifService: computed non-finite overlay geometry (scale={Scale}, {W}x{H}) - skipping overlay",
                    scale, windowWidth, windowHeight);
                return;
            }

            var screen = System.Windows.Forms.Screen.PrimaryScreen;
            if (screen == null) return;

            double dpiScale;
            using (var g = System.Drawing.Graphics.FromHwnd(IntPtr.Zero))
            {
                dpiScale = g.DpiX / 96.0;
            }

            double screenWidth = screen.Bounds.Width / dpiScale;
            double screenHeight = screen.Bounds.Height / dpiScale;

            double left = 0, top = 0;
            switch (setting.Position)
            {
                case CornerPosition.TopLeft:
                    left = 0; top = 0;
                    break;
                case CornerPosition.TopRight:
                    left = screenWidth - windowWidth; top = 0;
                    break;
                case CornerPosition.BottomLeft:
                    left = 0; top = screenHeight - windowHeight;
                    break;
                case CornerPosition.BottomRight:
                    left = screenWidth - windowWidth; top = screenHeight - windowHeight;
                    break;
            }

            // Two slots may legitimately pick the same corner; stacking them on identical pixels
            // gives two topmost animating windows fighting for z-order (one of them looks "gone").
            // Nudge each later slot diagonally inward instead of refusing the user's pick. Counted
            // from settings rather than from live windows so the offset is the same regardless of
            // which slot realizes first.
            int stacked = CountEarlierSlotsInCorner(index, setting.Position);
            if (stacked > 0)
            {
                double nudge = SameCornerNudge * stacked;
                left += setting.Position is CornerPosition.TopLeft or CornerPosition.BottomLeft ? nudge : -nudge;
                top += setting.Position is CornerPosition.TopLeft or CornerPosition.TopRight ? nudge : -nudge;
            }

            var opacity = Math.Clamp(setting.Opacity, 1, 100) / 100.0;

            var window = new Window
            {
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = System.Windows.Media.Brushes.Transparent,
                Topmost = true,
                ShowInTaskbar = false,
                ShowActivated = false,
                Width = windowWidth,
                Height = windowHeight,
                Left = left,
                Top = top,
                Opacity = opacity,
                WindowStartupLocation = WindowStartupLocation.Manual
            };

            var imageElement = new Image
            {
                Stretch = System.Windows.Media.Stretch.Uniform
            };

            // Error handler FIRST: SetSourceUri kicks off an async load, so a fault raised before
            // the handler is attached has no subscriber.
            AnimationBehavior.AddErrorHandler(imageElement, (s, e) =>
            {
                App.Logger?.Warning("CornerGifService: GIF animation error ({Kind}): {Error}",
                    e.Kind, e.Exception?.Message);
            });
            AnimationBehavior.SetRepeatBehavior(imageElement, System.Windows.Media.Animation.RepeatBehavior.Forever);
            AnimationBehavior.SetSourceUri(imageElement, gifUri);

            window.Content = imageElement;
            window.SourceInitialized += (s, e) => MakeWindowClickThrough(window);

            // Track BEFORE Show(): a throwing Show() would otherwise leave a topmost,
            // taskbar-less, click-through window that StopAll can never reach - the user has no
            // way to close it either (#709).
            _windows[index] = window;
            try
            {
                window.Show();
            }
            catch (Exception ex)
            {
                _windows.Remove(index);
                try { window.Close(); } catch { }
                App.Logger?.Error(ex, "CornerGifService: Show() failed for slot {Index} - overlay discarded", index);
                return;
            }

            App.Logger?.Information("CornerGifService: overlay shown at {Position} ({Path}, {W}x{H}px, {Opacity}%)",
                setting.Position, gifUri, (int)windowWidth, (int)windowHeight, setting.Opacity);
        }

        private static int CountEarlierSlotsInCorner(int index, CornerPosition position)
        {
            var overlays = App.Settings?.Current?.CornerGifOverlays;
            if (overlays == null) return 0;

            int count = 0;
            for (int i = 0; i < index && i < overlays.Count; i++)
            {
                var o = overlays[i];
                if (o != null && o.Enabled && o.Position == position) count++;
            }
            return count;
        }

        // Startup-restore flag file, same mechanism as EngineCrashSentinel (armed while the risky
        // work is in flight, cleared once it lands, a survivor at the next launch means the process
        // died in between). Scoped to this feature rather than folded into the engine/chaos
        // sentinels because it has to be consumed before MainWindow settles, not at startup report
        // time, and because consuming it mutates settings.
        private static string RestoreSentinelPath =>
            System.IO.Path.Combine(App.UserDataPath, "logs", "cornergif_restore.active");

        private static void ArmRestoreSentinel()
        {
            try
            {
                var path = RestoreSentinelPath;
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
                System.IO.File.WriteAllText(path, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            }
            catch { /* a guard that throws is worse than no guard */ }
        }

        private static void ClearRestoreSentinel()
        {
            try
            {
                var path = RestoreSentinelPath;
                if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
            }
            catch { }
        }

        /// <summary>True if a previous launch armed the restore and never cleared it. Consumes the
        /// file either way, so the force-disable happens once and not on every subsequent launch.</summary>
        private static bool ConsumeRestoreSentinel()
        {
            try
            {
                if (!System.IO.File.Exists(RestoreSentinelPath)) return false;
                ClearRestoreSentinel();
                return true;
            }
            catch { return false; }
        }

        private static void MakeWindowClickThrough(Window window)
        {
            var hwnd = new System.Windows.Interop.WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return;
            var extendedStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE, extendedStyle | WS_EX_TRANSPARENT | WS_EX_LAYERED | WS_EX_TOOLWINDOW);
        }

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int WS_EX_LAYERED = 0x00080000;
        private const int WS_EX_TOOLWINDOW = 0x00000080;

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hwnd, int index);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);
    }
}
