// PORTED from ConditioningControlPanel/Services/ScreenOcrService.cs (7.1.5). Same numbers: the user's
// interval, a 200 ms confirmation delay, at most 3 quick confirm scans a tick, own-window exclusion.
// The reader is a platform seam: Windows = Windows.Media.Ocr (WinRtScreenReader, compiled under
// CCP_WINRT); Linux has no reader (see ReasonUnavailable), so the switches stay greyed there.
//
// Privacy, as WPF: a scan lives in memory for one tick. No word read from the screen is logged, stored
// or sent; only counts are logged, at Debug. The timer exists only while the master switch, the screen
// read switch and access are all on (Sync), and a tick that finds one of them off stops it.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.KeywordTriggers;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>Reads every screen into words with virtual-desktop pixel rectangles.</summary>
internal interface IScreenTextReader
{
    /// <summary>False when the OS has no usable engine (Windows: no OCR language pack).</summary>
    bool IsAvailable { get; }
    Task<List<OcrWordHit>> ReadAllScreensAsync();
    /// <summary>This app's own visible, non-fullscreen windows (WPF App.GetCcpWindowRectsCached).</summary>
    IReadOnlyList<(int X, int Y, int Width, int Height)> OwnWindowRects();
}

internal static class ScreenOcrService
{
    internal const int ConfirmationDelayMs = 200;
    internal const int MaxQuickConfirmScans = 3;
    internal const int MinIntervalMs = 500;

    private static readonly object Gate = new();
    private static Timer? _timer;
    private static int _scanInProgress;
    private static IScreenTextReader? _reader;
    private static bool _readerTried;

    /// <summary>Test seam: a fake reader (set before the first use), and the engine it feeds.</summary>
    internal static IScreenTextReader? ReaderForTest
    {
        get => _reader;
        set { _reader = value; _readerTried = value != null; }
    }

    /// <summary>Set once by the test assembly's module initializer: no test may read the real screen
    /// (a test drives <see cref="TickAsync"/> with a fake reader instead).</summary>
    internal static bool Disabled;

    internal static KeywordTriggerEngine Engine { get; set; } = KeywordTriggerHead.Engine;

    /// <summary>Test seam: how a checked scan reaches the UI thread.</summary>
    internal static Func<Action, Task> OnUi { get; set; } = a => Dispatcher.UIThread.InvokeAsync(a).GetTask();

    private static IScreenTextReader? Reader
    {
        get
        {
            if (_readerTried) return _reader;
            _readerTried = true;
            try { _reader = CreateReader(); }
            catch (Exception ex) { Log.Warning("Screen OCR: reader unavailable: {Error}", ex.Message); }
            return _reader;
        }
    }

    private static IScreenTextReader? CreateReader()
    {
#if CCP_WINRT
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041)) return new WinRtScreenReader();
#endif
        return null;
    }

    /// <summary>True when this OS can read the screen at all (the switches are live).</summary>
    internal static bool IsSupported
    {
        get
        {
            if (_reader != null) return true;
#if CCP_WINRT
            return OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041);
#else
            return false;
#endif
        }
    }

    /// <summary>True when a reader exists AND has an engine (WPF IsOcrAvailable).</summary>
    internal static bool IsOcrAvailable => Reader?.IsAvailable == true;

    internal static bool IsRunning { get { lock (Gate) return _timer != null; } }

    /// <summary>WPF's three start conditions (MainWindow.Patreon.cs:515, MainWindow.xaml.cs:1186).</summary>
    internal static bool ShouldRun(AppSettings? s, bool hasAccess) =>
        s is { ScreenOcrEnabled: true, KeywordTriggersEnabled: true } && hasAccess;

    /// <summary>Start or stop to match the settings. Call after any of the three conditions moves.</summary>
    internal static void Sync()
    {
        bool run;
        try { run = ShouldRun(CoreSettings.Current, Engine.HasAccess()); }
        catch { run = false; }
        if (run) Start(); else Stop();
    }

    internal static void Start()
    {
        if (!IsSupported || Disabled) return;
        lock (Gate)
        {
            if (_timer != null) return;
            if (!IsOcrAvailable) { Log.Warning("Screen OCR: no OCR language pack available"); return; }
            var interval = Math.Max(MinIntervalMs, CoreSettings.Current?.ScreenOcrIntervalMs ?? 3000);
            _timer = new Timer(_ => _ = TickAsync(), null, interval, interval);
            Log.Information("Screen OCR started (interval: {Interval}ms)", interval);
        }
    }

    internal static void Stop()
    {
        lock (Gate)
        {
            if (_timer == null) return;
            _timer.Dispose();
            _timer = null;
        }
        try { Engine.ResetOcrTracking(); } catch { }
        Log.Debug("Screen OCR stopped");
    }

    /// <summary>WPF UpdateInterval (the Scan interval slider).</summary>
    internal static void UpdateInterval(int intervalMs)
    {
        lock (Gate) _timer?.Change(Math.Max(MinIntervalMs, intervalMs), Math.Max(MinIntervalMs, intervalMs));
    }

    /// <summary>One tick: a discovery scan, then up to three quick confirmation scans while a candidate
    /// is still building its streak. Internal so a test drives it without a timer.</summary>
    internal static async Task TickAsync()
    {
        var reader = Reader;
        if (reader == null) return;
        // An entitlement or a switch can lapse while the timer is live: the tick ends the timer itself.
        bool run;
        try { run = ShouldRun(CoreSettings.Current, Engine.HasAccess()); } catch { run = false; }
        if (!run) { Stop(); return; }
        if (Interlocked.Exchange(ref _scanInProgress, 1) == 1) return;
        try
        {
            await ScanOnceAsync(reader);
            var quick = 0;
            while (StillWanted() && Engine.NeedsOcrConfirmation && quick < MaxQuickConfirmScans)
            {
                quick++;
                await Task.Delay(ConfirmationDelayMs);
                await ScanOnceAsync(reader);
            }
        }
        catch (Exception ex) { Log.Debug("Screen OCR: scan error: {Error}", ex.Message); }
        finally { Interlocked.Exchange(ref _scanInProgress, 0); }
    }

    private static bool StillWanted()
    {
        try { return ShouldRun(CoreSettings.Current, Engine.HasAccess()); } catch { return false; }
    }

    private static async Task ScanOnceAsync(IScreenTextReader reader)
    {
        var words = await reader.ReadAllScreensAsync();
        IReadOnlyList<OcrWordHit> filtered = words;
        if (words.Count > 0 && CoreSettings.Current?.AwarenessIgnoreOwnUi == true)
        {
            var own = reader.OwnWindowRects();
            if (own.Count > 0)
            {
                var kept = KeywordTriggerEngine.ExcludeOwnWindows(words, own);
                if (kept.Count != words.Count)
                    Log.Debug("OCR self-exclusion: dropped {Dropped}/{Total} words inside {N} own window(s)",
                        words.Count - kept.Count, words.Count, own.Count);
                filtered = kept;
            }
        }
        await OnUi(() => Engine.CheckOcrWords(filtered));
    }

    /// <summary>Why the screen-read switches are greyed, or null when they work. Linux: nothing in the
    /// tree can read text from a screen grab (no Tesseract, no portal screenshot path), so there is no
    /// reader. Windows without the projection or before Windows 10 2004 reads the same way.</summary>
    internal static string? ReasonUnavailable => IsSupported ? null : "exclusives_not_on_this_build";
}
