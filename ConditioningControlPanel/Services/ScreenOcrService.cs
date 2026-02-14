using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using WinForms = System.Windows.Forms;

namespace ConditioningControlPanel.Services
{
    public class ScreenOcrService : IDisposable
    {
        private const int ConfirmationDelayMs = 200;

        private Timer? _timer;
        private bool _disposed;
        private bool _isRunning;
        private bool _scanInProgress;
        private OcrEngine? _ocrEngine;
        private readonly object _lock = new();

        public ScreenOcrService()
        {
            try
            {
                _ocrEngine = OcrEngine.TryCreateFromUserProfileLanguages();
                if (_ocrEngine == null)
                    App.Logger?.Warning("ScreenOcrService: No OCR language pack available");
            }
            catch (Exception ex)
            {
                App.Logger?.Error("ScreenOcrService: Failed to create OCR engine: {Error}", ex.Message);
            }
        }

        public void Start()
        {
            lock (_lock)
            {
                if (_isRunning || _ocrEngine == null) return;

                var intervalMs = App.Settings?.Current?.ScreenOcrIntervalMs ?? 3000;
                _timer = new Timer(OnTimerTick, null, intervalMs, intervalMs);
                _isRunning = true;
                App.Logger?.Information("ScreenOcrService started (interval: {Interval}ms)", intervalMs);
            }
        }

        public void Stop()
        {
            lock (_lock)
            {
                if (!_isRunning) return;
                _timer?.Dispose();
                _timer = null;
                _isRunning = false;
                App.Logger?.Information("ScreenOcrService stopped");
            }
        }

        public void UpdateInterval(int intervalMs)
        {
            lock (_lock)
            {
                if (!_isRunning || _timer == null) return;
                _timer.Change(intervalMs, intervalMs);
            }
        }

        private async void OnTimerTick(object? state)
        {
            if (_disposed || !_isRunning || _ocrEngine == null) return;
            if (_scanInProgress) return;
            _scanInProgress = true;

            try
            {
                // Discovery scan — runs at the regular interval
                var allWords = await ScanAllScreensAsync();
                if (_disposed) return;

                await DispatchOcrResultsAsync(allWords);

                // Quick confirmation scan: if the discovery scan found unconfirmed keyword
                // matches (first sighting), re-scan after a short delay to confirm stability
                // instead of waiting for the next full interval tick
                if (!_disposed && App.KeywordTriggers?.NeedsOcrConfirmation == true)
                {
                    await Task.Delay(ConfirmationDelayMs);
                    if (_disposed) return;

                    var confirmWords = await ScanAllScreensAsync();
                    if (_disposed) return;

                    await DispatchOcrResultsAsync(confirmWords);
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("ScreenOcrService: Scan error: {Error}", ex.Message);
            }
            finally
            {
                _scanInProgress = false;
            }
        }

        private async Task<List<OcrWordHit>> ScanAllScreensAsync()
        {
            var allWords = new List<OcrWordHit>();
            var screens = App.GetAllScreensCached();

            foreach (var screen in screens)
            {
                var (_, words) = await CaptureAndRecognizeAsync(screen);
                if (words != null)
                    allWords.AddRange(words);
            }

            return allWords;
        }

        private async Task DispatchOcrResultsAsync(List<OcrWordHit> words)
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.HasShutdownStarted) return;

            await dispatcher.InvokeAsync(() =>
            {
                if (_disposed) return;
                App.KeywordTriggers?.CheckOcrWords(words);
            });
        }

        private async System.Threading.Tasks.Task<(string? text, List<OcrWordHit>? words)> CaptureAndRecognizeAsync(WinForms.Screen screen)
        {
            Bitmap? bitmap = null;
            try
            {
                var bounds = screen.Bounds;
                bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);

                using (var g = Graphics.FromImage(bitmap))
                {
                    g.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bounds.Size);
                }

                // Convert System.Drawing.Bitmap → WinRT SoftwareBitmap via MemoryStream
                using var ms = new MemoryStream();
                bitmap.Save(ms, ImageFormat.Bmp);
                ms.Position = 0;

                var rasStream = ms.AsRandomAccessStream();
                var decoder = await Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(rasStream);
                var softwareBitmap = await decoder.GetSoftwareBitmapAsync(
                    BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);

                try
                {
                    var result = await _ocrEngine!.RecognizeAsync(softwareBitmap);
                    if (result == null)
                        return (null, null);

                    // Extract word-level bounding rects
                    var words = new List<OcrWordHit>();
                    foreach (var line in result.Lines)
                    {
                        foreach (var word in line.Words)
                        {
                            var br = word.BoundingRect;
                            words.Add(new OcrWordHit
                            {
                                Text = word.Text,
                                ScreenRect = new Rectangle(
                                    bounds.Left + (int)br.X,
                                    bounds.Top + (int)br.Y,
                                    (int)br.Width,
                                    (int)br.Height),
                                Screen = screen
                            });
                        }
                    }

                    return (result.Text, words);
                }
                finally
                {
                    softwareBitmap.Dispose();
                }
            }
            catch (Exception ex)
            {
                // Fails when desktop locked, DRM content visible, UAC prompt, etc.
                App.Logger?.Debug("ScreenOcrService: Capture failed for {Screen}: {Error}",
                    screen.DeviceName, ex.Message);
                return (null, null);
            }
            finally
            {
                bitmap?.Dispose();
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Stop();
        }
    }
}
