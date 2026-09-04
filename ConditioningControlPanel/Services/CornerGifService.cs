using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using ConditioningControlPanel.Models;

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
        /// <summary>Hard cap on simultaneous overlay windows, the same-corner nudge and the
        /// realization stagger all live in <see cref="CornerGifPlanner"/> now - they are arithmetic
        /// over settings, not window code, so every head gets one answer rather than a copy each.
        /// This head keeps only what needs a Win32 surface.</summary>
        private const int MaxOverlays = CornerGifPlanner.MaxOverlays;

        /// <summary>How many times a slot may be pushed back because a display change is in flight
        /// before it is realized anyway. 8 x <see cref="SpawnRetryMs"/> covers a monitor drag.</summary>
        private const int SpawnDeferMaxAttempts = 8;
        private const int SpawnRetryMs = 250;

        // The oversize hazard threshold now lives in CornerGifMedia.OversizeSourcePixels, shared with
        // the session-scoped overlay. It was 4 MP here and in SessionEngine - 4% ABOVE the built-in
        // spiral's 3.84 MP, i.e. above the one asset both paths defaulted to - so the warning that
        // was meant to name this freeze never once fired.

        // Keyed by slot index so a single slot can be rebuilt without touching the others.
        private readonly Dictionary<int, Window> _windows = new();

        // Slots with a realization still pending (queued or waiting out a display change). Kept
        // separate from _windows so the sentinel knows an overlay is *about* to exist.
        private readonly HashSet<int> _pending = new();

        // Mirrors the on-disk sentinel so a burst of refreshes does not hammer the file.
        private bool _sentinelArmed;

        // Monotonic tick the next slot may realize at, so two slots never realize back-to-back.
        private long _nextRealizeTick;

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
        /// How many overlay windows are live (+ how many realizations are still queued), for the
        /// hang report. Read by <see cref="HangContext"/> on the WATCHDOG thread while the UI thread
        /// may be wedged, so this must stay a pair of plain field reads: Dictionary/HashSet Count is
        /// an int field, safe to read concurrently (a stale value is fine, a lock would not be — the
        /// UI thread could be holding it, which is exactly the case we are diagnosing).
        /// </summary>
        internal string ActiveWindowCount
        {
            get
            {
                try { return _windows.Count + "+" + _pending.Count + "pending"; }
                catch { return "(unavailable)"; }
            }
        }

        /// <summary>
        /// TRUE when a standalone corner overlay is on screen OR still queued to realize.
        /// SessionEngine asks this before raising its own corner GIF so the two never stack
        /// (ticket 1539282547484139682 - "the session corner spiral AND my own one at once").
        /// The pending set counts: a queued realization is about to occupy the corner.
        /// </summary>
        internal bool HasActiveOverlays
        {
            get
            {
                try { return _windows.Count > 0 || _pending.Count > 0; }
                catch { return false; }
            }
        }

        /// <summary>
        /// Live overlay hwnds, for OverlayService's z-order sweep. UI thread only (Window and
        /// WindowInteropHelper are thread-affine); returns nothing off it rather than throwing.
        /// </summary>
        internal List<IntPtr> GetOverlayHandles()
        {
            var handles = new List<IntPtr>(_windows.Count);
            try
            {
                if (Application.Current?.Dispatcher?.CheckAccess() != true) return handles;
                foreach (var window in _windows.Values)
                {
                    var hwnd = new System.Windows.Interop.WindowInteropHelper(window).Handle;
                    if (hwnd != IntPtr.Zero) handles.Add(hwnd);
                }
            }
            catch { /* a diagnostic/reconciler accessor must never throw */ }
            return handles;
        }

        /// <summary>
        /// Tears down every overlay and re-shows the ones currently enabled in settings.
        /// Safe to call from any thread; marshals to the UI thread. The teardown is synchronous but
        /// each re-show is deferred one dispatcher pass (see QueueShow). Call after any config
        /// change; startup restore goes through <see cref="RestoreOnStartup"/> instead.
        /// </summary>
        public void RefreshOverlays()
        {
            var disp = Application.Current?.Dispatcher;
            if (disp == null) return;
            if (!disp.CheckAccess())
            {
                disp.BeginInvoke(new Action(RefreshOverlays));
                return;
            }

            StopAll();
            TrySubscribeMainWindowClosing();

            var overlays = App.Settings?.Current?.CornerGifOverlays;
            if (overlays == null) return;

            int queued = 0;
            for (int i = 0; i < overlays.Count; i++)
            {
                var o = overlays[i];
                // Mirror of SessionEngine's own admission check: while a session-scoped corner GIF
                // is on screen the standalone slots stand down rather than stacking a second spiral
                // in the same corner. SessionEngine calls RefreshOverlays again when its overlay
                // closes, so the slot comes straight back.
                if (o == null || !CornerGifMedia.AllowStandaloneCornerGif(
                        o.Enabled, SessionEngine.IsSessionCornerGifActive)) continue;
                if (queued >= MaxOverlays)
                {
                    App.Logger?.Warning("CornerGifService: settings list has more than {Max} enabled corner-GIF slots - ignoring the rest", MaxOverlays);
                    break;
                }
                queued++;
                QueueShow(disp, i, o);
            }

            NotifySessionAdmissionChanged();
        }

        /// <summary>
        /// Tells a running session to re-resolve the corner-GIF dedupe now that the STANDALONE side
        /// has changed. Without this the live master was one-directional: a session whose corner GIF
        /// was refused at start because a slot was up stayed refused for the whole run even after
        /// the user switched that slot off, because the per-second tick only ever re-raises overlays
        /// with CornerGifStartMinute > 0. The user turned their own corner GIF off expecting the
        /// program's to appear, and nothing happened.
        ///
        /// <para>Called after the slots have settled, so <see cref="HasActiveOverlays"/> (which
        /// counts queued realizations) already reflects the new state.
        /// <c>SessionEngine.RefreshCornerGifPolicy</c> guards its own re-entrancy, so the handback
        /// it may trigger cannot bounce back here forever. Never throws: this rides on config
        /// changes and on the panic path's teardown.</para>
        /// </summary>
        private static void NotifySessionAdmissionChanged()
        {
            try { SessionEngine.Active?.RefreshCornerGifPolicy(); }
            catch (Exception ex)
            {
                try { App.Logger?.Warning(ex, "CornerGifService: session corner-GIF policy refresh failed"); } catch { }
            }
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
            if (overlays == null || index < 0 || index >= overlays.Count || index >= MaxOverlays)
            {
                NotifySessionAdmissionChanged();
                return;
            }

            var setting = overlays[index];
            // The SAME admission rule as RefreshOverlays above, read from the same helper on
            // purpose. This is the LIVE EDIT path (CornerGifWindow's debounced size/opacity
            // sliders call it), so a rule applied in only one of the two places lets one nudge of a
            // slider realise the very standalone overlay RefreshOverlays just suppressed - two
            // corner spirals at once, which is exactly ticket 1539282547484139682.
            if (setting == null || !CornerGifMedia.AllowStandaloneCornerGif(
                    setting.Enabled, SessionEngine.IsSessionCornerGifActive))
            {
                NotifySessionAdmissionChanged();
                return;
            }

            QueueShow(disp, index, setting);
            NotifySessionAdmissionChanged();
        }

        /// <summary>
        /// Startup restore path (#709/#954/#958). Guarded by a flag file that is armed for as long as
        /// an overlay is on screen: a surviving flag means the previous run ended - wedged, killed or
        /// crashed - while a corner GIF was live, so the persisted slots are force-disabled instead of
        /// being replayed into the same wedge on every subsequent start. Both reporters had to end the
        /// process and hand-edit settings.json (a reinstall keeps settings, so it did not help); after
        /// this, the next launch starts clean and says why.
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

            switch (ResolveRestoreAction(ConsumeRestoreSentinel(), anyEnabled))
            {
                case RestoreAction.ForceDisable:
                    if (overlays != null)
                    {
                        foreach (var o in overlays)
                            if (o != null) o.Enabled = false;
                        App.Settings?.Save();
                    }
                    App.Logger?.Warning("CornerGifService: the previous run ended with a corner-GIF overlay on screen - all slots force-disabled so this launch can start. Re-enable them from the Spiral card.");
                    NotifyForceDisabled();
                    return;

                case RestoreAction.Restore:
                    RefreshOverlays();
                    return;

                default:
                    return;
            }
        }

        /// <summary>What <see cref="RestoreOnStartup"/> does with the slots it found.</summary>
        internal enum RestoreAction { Nothing, Restore, ForceDisable }

        /// <summary>
        /// The startup rule for #954/#958, extracted so it can be unit-tested without a dispatcher.
        ///
        /// <para>A surviving sentinel means the previous run ended - wedged, killed or crashed - while
        /// a corner GIF was on screen. Those slots are what wedged it, so they are turned OFF rather
        /// than replayed; both reporters were otherwise stuck in a launch-freeze-kill loop that a
        /// reinstall could not break (it keeps settings.json) and had to hand-edit the file.</para>
        ///
        /// <para>A sentinel with nothing enabled is stale bookkeeping, not a hazard: the user already
        /// turned the slots off, so there is nothing to disable and nothing to warn about.</para>
        /// </summary>
        internal static RestoreAction ResolveRestoreAction(bool sentinelSurvived, bool anyEnabled)
        {
            if (!anyEnabled) return RestoreAction.Nothing;
            return sentinelSurvived ? RestoreAction.ForceDisable : RestoreAction.Restore;
        }

        /// <summary>
        /// Tell the user their corner GIFs were turned off, and why. The log line alone left both
        /// reporters believing the app had eaten their settings.
        /// </summary>
        private static void NotifyForceDisabled()
        {
            try
            {
                App.Notifications?.Show(
                    "Corner GIFs were turned off - the last session ended while one was on screen. Re-enable them from the Spiral card.",
                    NotificationType.Warning, TimeSpan.FromSeconds(12));
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("CornerGifService: force-disable notice failed: {E}", ex.Message);
            }
        }

        /// <summary>
        /// Arms the sentinel if it is not already armed, and disarms it once no overlay is live or
        /// pending. The whole point of the rewrite: the old code cleared the flag one dispatcher pass
        /// after Show() returned, but the GIF load is asynchronous, so the render thread wedged AFTER
        /// the all-clear and every later launch replayed it (#954/#958).
        /// </summary>
        private void SyncSentinel()
        {
            bool wanted = _windows.Count > 0 || _pending.Count > 0;
            if (wanted == _sentinelArmed) return;
            _sentinelArmed = wanted;
            if (wanted) ArmRestoreSentinel(); else ClearRestoreSentinel();
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
                ReleaseAnimator(w);
                try { w.Close(); }
                catch { /* already gone */ }
            }
            _windows.Clear();

            // Cancel every deferred realization still in the queue, or a teardown that races one
            // leaves an untracked overlay the user has no way to close.
            foreach (var index in new List<int>(_slotSeq.Keys)) BumpSlot(index);
            _pending.Clear();
            // Nothing left to space the next realization against, and leaving the cursor in the
            // future would delay the rebuild that a RefreshOverlays teardown is about to queue.
            _nextRealizeTick = 0;
            SyncSentinel();
        }

        private void CloseSlot(int index)
        {
            BumpSlot(index); // cancels a pending realization for this slot
            _pending.Remove(index);
            if (_windows.TryGetValue(index, out var w))
            {
                _windows.Remove(index);
                ReleaseAnimator(w);
                try { w.Close(); }
                catch { /* already gone */ }
            }
            SyncSentinel();
        }

        /// <summary>
        /// Clear the GIF source before closing the overlay window. RepeatBehavior.Forever installs
        /// a clock that keeps handing the render thread native-size WriteableBitmap frames and pins
        /// the Image for as long as the source is set; Close() alone does not tear it down. These
        /// overlays are rebuilt on every settings edit (the size/opacity sliders refresh a slot per
        /// debounce rest, an enable toggle or a SpiralPath change rebuilds BOTH slots), so without
        /// this each edit leaves another live animator driving the render thread - a slow-onset
        /// render-thread saturation that ends in the UI thread blocking inside WriteableBitmap.Lock
        /// with no exception. Same teardown every other XamlAnimatedGif site in the app performs.
        /// </summary>
        private static void ReleaseAnimator(Window w)
        {
            try
            {
                if (w.Content is Image img)
                {
                    try { CornerGifMedia.Detach(img); } catch { }
                    try { img.Source = null; } catch { }
                }
                w.Content = null;
            }
            catch { /* teardown must never throw */ }
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
        /// Defers one overlay's realization. Show() on a WS_EX_LAYERED (AllowsTransparency) window
        /// runs a synchronous HwndTarget.OnResize -> MediaContext.CompleteRender on FIRST
        /// realization; doing Close,Close,Show,Show in a single synchronous burst - two slots -
        /// fires that while the previous pair's render targets are still tearing down and the first
        /// GIF animator is driving the render thread. That is the freeze #494 shape, the crash in
        /// #709, and the "turned on corner gif 2 and it froze" hang in #958.
        ///
        /// <para>Background priority alone was not enough, because it only orders the work inside
        /// one dispatcher drain. Each slot now also waits out a real <see cref="CornerGifPlanner.StaggerMs"/> gap
        /// after the previous one, and no slot realizes while a display change is in flight
        /// (<see cref="Services.UI.DisplayChangeCoordinator.SpawnsSuppressed"/>) - #954 is a
        /// dual-monitor report, and layered-surface creation during a DPI/topology change is the
        /// exact hazard that coordinator exists for. Every other layered-spawn path in the app
        /// already honours it; this one did not.</para>
        /// </summary>
        private void QueueShow(Dispatcher disp, int index, CornerGifOverlaySetting setting)
        {
            var seq = BumpSlot(index);
            _pending.Add(index);
            SyncSentinel();

            long delayMs = CornerGifPlanner.NextRealizeDelayMs(ref _nextRealizeTick, Environment.TickCount64);

            ScheduleRealize(disp, index, setting, seq, delayMs, 0);
        }

        private void ScheduleRealize(Dispatcher disp, int index, CornerGifOverlaySetting setting,
            int seq, long delayMs, int deferAttempts)
        {
            void Realize()
            {
                if (CurrentSeq(index) != seq) return; // superseded or torn down while queued

                if (Services.UI.DisplayChangeCoordinator.SpawnsSuppressed && deferAttempts < SpawnDeferMaxAttempts)
                {
                    ScheduleRealize(disp, index, setting, seq, SpawnRetryMs, deferAttempts + 1);
                    return;
                }

                _pending.Remove(index);
                // Admission is re-asked HERE, not only when the show was queued: realization is
                // deferred (a dispatcher pass, plus up to 8 display-change retries), and a session
                // can raise its own corner overlay in that window. Without this a slot queued the
                // instant before would land behind the session's spiral - two spirals in one
                // corner, which is ticket 1539282547484139682.
                if (!CornerGifMedia.AllowStandaloneCornerGif(
                        setting.Enabled, SessionEngine.IsSessionCornerGifActive))
                {
                    SyncSentinel();
                    return;
                }
                try { ShowOne(index, setting); }
                catch (Exception ex) { App.Logger?.Error(ex, "CornerGifService: ShowOne failed"); }
                SyncSentinel();
            }

            if (delayMs <= 0)
            {
                disp.BeginInvoke(new Action(Realize), DispatcherPriority.Background);
                return;
            }

            var timer = new DispatcherTimer(DispatcherPriority.Background, disp)
            {
                Interval = TimeSpan.FromMilliseconds(delayMs)
            };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                Realize();
            };
            timer.Start();
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

            // Resolution order: this slot's explicit pick -> the Spiral Library's active
            // selection (App.Settings.SpiralPath, the "pool") -> built-in corner spiral.
            // So an enabled-but-unpicked slot draws whatever spiral the app is already using,
            // matching OverlayService.GetSpiralPath (the pool) rather than a separate file.
            // The first two steps are CornerGifPlanner's (they are the same on every head); only
            // the built-in fallback below is this head's, because only it knows its pack:// art.
            var filePath = CornerGifPlanner.ResolveSourcePath(setting, App.Settings?.Current?.SpiralPath);

            if (!string.IsNullOrEmpty(filePath))
            {
                try { gifUri = new Uri(filePath); }
                catch (Exception ex)
                {
                    App.Logger?.Warning("CornerGifService: failed to load GIF from file {Path}: {Error}", filePath, ex.Message);
                    gifUri = null;
                }
            }

            // Built-in fallback. CornerGifMedia keeps an active mod's own spiral (branding) and
            // otherwise hands back the pre-scaled corner asset rather than the 2400x1600 fullscreen
            // spiral this used to reach for.
            if (gifUri == null)
            {
                try { gifUri = new Uri(CornerGifMedia.ResolveDefaultUriString(), UriKind.Absolute); }
                catch (Exception ex)
                {
                    App.Logger?.Warning("CornerGifService: failed to resolve default spiral resource: {Error}", ex.Message);
                }
            }

            if (gifUri == null)
            {
                App.Logger?.Warning("CornerGifService: could not load any corner GIF image - skipping");
                return;
            }

            // Header-only dimension read - this was a full GDI+ decode whose only product was
            // Width/Height, so every show decoded the file twice.
            //
            // Bug #625: a degenerate (0x0) image makes the scale below divide by zero, and
            // assigning the resulting NaN/Infinity to Window.Width/Height throws deep inside WPF
            // layout - which, on the startup restore path, took the whole app down. Bail out loudly
            // instead of handing WPF non-finite geometry.
            if (!CornerGifMedia.TryGetPixelSize(gifUri, out var gifWidth, out var gifHeight)
                || gifWidth <= 0 || gifHeight <= 0)
            {
                App.Logger?.Warning("CornerGifService: GIF has unreadable or degenerate size {W}x{H} ({Path}) - skipping overlay",
                    gifWidth, gifHeight, gifUri);
                return;
            }

            var screen = System.Windows.Forms.Screen.PrimaryScreen;
            if (screen == null) return;

            double dpiScale;
            using (var g = System.Drawing.Graphics.FromHwnd(IntPtr.Zero))
            {
                dpiScale = g.DpiX / 96.0;
            }

            // Everything from here to the window itself is CornerGifPlanner's, because none of it
            // needs a window: the longest-edge scale (default 300), the corner the user picked, the
            // same-corner nudge - two slots may legitimately pick ONE corner, and stacking them on
            // identical pixels gives two topmost animating windows fighting for z-order, so one of
            // them just looks "gone" - and the opacity clamp. Null means the numbers came out
            // degenerate or non-finite; #625 is what happens when that is handed to WPF layout
            // anyway, so bail out loudly instead. A zero DPI read lands here too.
            var placement = CornerGifPlanner.Place(
                setting, gifWidth, gifHeight,
                screen.Bounds.Width / dpiScale, screen.Bounds.Height / dpiScale,
                CornerGifPlanner.CountEarlierSlotsInCorner(
                    App.Settings?.Current?.CornerGifOverlays, index, setting.Position));

            if (placement is not { } place)
            {
                App.Logger?.Warning("CornerGifService: computed non-finite overlay geometry for slot {Index} (source {W}x{H}, size {Size}, dpi {Dpi}) - skipping overlay",
                    index, gifWidth, gifHeight, setting.Size, dpiScale);
                return;
            }

            double windowWidth = place.Width;
            double windowHeight = place.Height;
            double left = place.Left;
            double top = place.Top;
            var opacity = place.Opacity;

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

            // Per-frame downscale quality (#954/#958). Still set, but a belt to the braces now: the
            // frames CornerGifMedia hands over are already the overlay's size, so there is no
            // per-frame downscale left to filter.
            System.Windows.Media.RenderOptions.SetBitmapScalingMode(
                imageElement, System.Windows.Media.BitmapScalingMode.LowQuality);

            CornerGifMedia.WarnIfOversize("CornerGifService", gifUri, gifWidth, gifHeight, windowWidth, windowHeight);

            // Decode ONCE, OFF the UI thread, downscaled to the pixels this overlay actually
            // occupies (see CornerGifMedia). XamlAnimatedGif handed the render thread a
            // WriteableBitmap at the GIF's NATIVE size and WPF resampled it to the overlay's size on
            // EVERY frame, forever - on a layered window that saturates the render thread, and a
            // saturated render thread blocks the UI thread inside WriteableBitmap.Lock (the
            // CWGXBitmapLockState::LockRead wedge in UiHangWatchdog's dump notes). #221 only made
            // the filter cheaper; the source stayed full size.
            CornerGifMedia.Attach(imageElement, gifUri, windowWidth, windowHeight, dpiScale);

            window.Content = imageElement;
            window.SourceInitialized += (s, e) => MakeWindowClickThrough(window);

            // Track BEFORE Show(): a throwing Show() would otherwise leave a topmost,
            // taskbar-less, click-through window that StopAll can never reach - the user has no
            // way to close it either (#709).
            _windows[index] = window;
            try
            {
                // Show() on a layered window realizes its render target synchronously; if the render
                // thread wedges here the hang report must name this call rather than "(idle)".
                using (VideoDiag.UiScope($"CornerGifService.ShowOne(slot {index}, layered Show)"))
                using (HangContext.Scope($"cornerGif.show[{index}]"))
                {
                    window.Show();
                }
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

        /// <summary>Clean-shutdown hook, called from App.OnExit alongside the engine/chaos sentinels.
        /// The flag now lives for as long as an overlay is on screen, so an orderly exit that never
        /// raised MainWindow.Closing (update restart, tray quit) would otherwise look like the wedge
        /// it is meant to catch and disable the user's slots for no reason.</summary>
        public static void ClearSentinelOnCleanExit() => ClearRestoreSentinel();

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
