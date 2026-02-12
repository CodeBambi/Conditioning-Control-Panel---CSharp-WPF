using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using NAudio.Wave;

namespace ConditioningControlPanel.Services;

/// <summary>
/// Bubble popping game - bubbles float up from bottom of screen, user pops them by clicking
/// </summary>
public class BubbleService : IDisposable
{
    private const int MAX_BUBBLES = 3;
    private readonly List<Bubble> _bubbles = new();
    private readonly Random _random = new();
    private DispatcherTimer? _spawnTimer;
    private DispatcherTimer? _animationTimer; // Single shared animation timer for all bubbles
    private bool _isRunning;
    private BitmapImage? _bubbleImage;
    private string _assetsPath = "";
    // Per-screen DPI is now computed on demand via Bubble.GetDpiForScreen()

    public bool IsRunning => _isRunning;
    public bool IsPaused => _isPaused;
    public int ActiveBubbles => _bubbles.Count;

    private bool _isPaused;

    public event Action? OnBubblePopped;
    public event Action? OnBubbleMissed;

    public void Start(bool bypassLevelCheck = false)
    {
        if (_isRunning) return;

        var settings = App.Settings.Current;

        // Check level requirement unless bypassed (e.g., during sessions)
        if (!bypassLevelCheck && !settings.IsLevelUnlocked(20))
        {
            App.Logger?.Information("BubbleService: Level {Level} is below 20, bubbles not available", settings.PlayerLevel);
            return;
        }

        _isRunning = true;

        _assetsPath = App.UserAssetsPath;
        
        // Pre-load bubble image
        LoadBubbleImage();

        // Start spawning bubbles based on frequency setting
        var intervalMs = 60000.0 / Math.Max(1, settings.BubblesFrequency); // frequency per minute
        
        _spawnTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(intervalMs)
        };
        _spawnTimer.Tick += (s, e) => SpawnBubble();
        _spawnTimer.Start();

        // Single shared animation timer for all bubbles (32ms = ~30 FPS, sufficient for floating bubbles)
        _animationTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(32)
        };
        _animationTimer.Tick += AnimateAllBubbles;
        _animationTimer.Start();

        // Spawn first bubble immediately
        SpawnBubble();

        // Update Discord presence
        App.DiscordRpc?.SetBubbleActivity();

        App.Logger?.Information("BubbleService started - {Freq} bubbles/min", settings.BubblesFrequency);
    }

    private void AnimateAllBubbles(object? sender, EventArgs e)
    {
        if (!_isRunning) return;

        // Animate all bubbles in a single pass - iterate by index to avoid allocation
        for (int i = _bubbles.Count - 1; i >= 0; i--)
        {
            if (i < _bubbles.Count)
                _bubbles[i].AnimateFrame();
        }
    }

    public void Stop()
    {
        if (!_isRunning) return;
        _isRunning = false;

        _spawnTimer?.Stop();
        _spawnTimer = null;

        _animationTimer?.Stop();
        _animationTimer = null;

        // Small delay to allow any pending animation ticks to complete
        // This prevents race conditions when cleaning up during video playback
        Thread.Sleep(50);

        // Pop all remaining bubbles
        PopAllBubbles();

        // Update Discord presence back to idle (unless another activity takes over)
        App.DiscordRpc?.SetIdleActivity();

        App.Logger?.Information("BubbleService stopped");
    }

    public void RefreshFrequency()
    {
        if (!_isRunning || _spawnTimer == null) return;

        _spawnTimer.Stop();

        var intervalMs = 60000.0 / Math.Max(1, App.Settings.Current.BubblesFrequency);
        _spawnTimer.Interval = TimeSpan.FromMilliseconds(intervalMs);

        _spawnTimer.Start();

        App.Logger?.Information("BubbleService frequency updated to {Freq} bubbles/min", App.Settings.Current.BubblesFrequency);
    }

    /// <summary>
    /// Pause bubble spawning and clear all active bubbles (for bubble count minigame)
    /// </summary>
    public void PauseAndClear()
    {
        if (!_isRunning) return;

        _isPaused = true;
        _spawnTimer?.Stop();
        PopAllBubbles();

        App.Logger?.Debug("BubbleService paused and cleared for minigame");
    }

    /// <summary>
    /// Resume bubble spawning after pause
    /// </summary>
    public void Resume()
    {
        if (!_isRunning || !_isPaused) return;

        _isPaused = false;
        _spawnTimer?.Start();

        App.Logger?.Debug("BubbleService resumed");
    }

            private void LoadBubbleImage()
            {
                try
                {
                    var resourceUri = new Uri("pack://application:,,,/Resources/bubble.png", UriKind.Absolute);
                    _bubbleImage = new BitmapImage();
                    _bubbleImage.BeginInit();
                    _bubbleImage.UriSource = resourceUri;
                    _bubbleImage.CacheOption = BitmapCacheOption.OnLoad;
                    _bubbleImage.EndInit();
                    _bubbleImage.Freeze();
                    App.Logger?.Debug("Bubble image loaded from embedded resource");
                }
                catch (Exception ex)
                {
                    App.Logger?.Error("Failed to load bubble image: {Error}", ex.Message);
                }
            }
    private void SpawnBubble()
    {
        if (!_isRunning) return;
        if (_bubbles.Count >= MAX_BUBBLES)
        {
            App.Logger?.Debug("Max bubbles reached, skipping spawn");
            return;
        }

        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            try
            {
                var settings = App.Settings.Current;
                var screens = settings.DualMonitorEnabled 
                    ? App.GetAllScreensCached() 
                    : new[] { System.Windows.Forms.Screen.PrimaryScreen! };
                
                var screen = screens[_random.Next(screens.Length)];
                // Outside sessions, bubbles are always clickable (no UI toggle exists for this setting)
                var isClickable = App.IsSessionRunning ? settings.BubblesClickable : true;
                var bubble = new Bubble(screen, _bubbleImage, _random, OnPop, OnMiss, OnDestroy, isClickable);
                _bubbles.Add(bubble);
                
                App.Logger?.Debug("Spawned bubble, total: {Count}", _bubbles.Count);
            }
            catch (Exception ex)
            {
                App.Logger?.Error("Failed to spawn bubble: {Error}", ex.Message);
            }
        });
    }

    private void OnPop(Bubble bubble)
    {
        // Roll for lucky bubble (5% chance for 10x XP if skill unlocked)
        var multiplier = App.SkillTree?.RollLuckyBubble() ?? 1;
        var isLucky = multiplier > 1;

        // Play appropriate sound
        PlayPopSound(isLucky);

        // Don't remove here - let the pop animation play, removal happens in OnDestroy
        OnBubblePopped?.Invoke();

        App.Progression?.AddXP(2 * multiplier, XPSource.Bubble);

        // Track for achievement
        App.Achievements?.TrackBubblePopped();

        // Haptic feedback with combo system
        _ = App.Haptics?.BubblePopAsync();
    }

    private void OnMiss(Bubble bubble)
    {
        // Bubble floated off screen - remove immediately (no animation needed)
        _bubbles.Remove(bubble);
        OnBubbleMissed?.Invoke();
    }

    private void OnDestroy(Bubble bubble)
    {
        // Called when bubble is fully destroyed (after pop animation completes)
        _bubbles.Remove(bubble);
    }

    private void PlayPopSound(bool isLucky = false)
    {
        try
        {
            var soundsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "sounds", "bubbles");

            // If lucky bubble, play special Burst easter egg sound
            if (isLucky)
            {
                var burstPath = Path.Combine(soundsPath, "Burst.mp3");
                if (File.Exists(burstPath))
                {
                    var masterVolume = App.Settings.Current.MasterVolume / 100f;
                    var bubblesVolume = App.Settings.Current.BubblesVolume / 100f;
                    var volume = (float)Math.Pow(masterVolume * bubblesVolume, 1.5);
                    PlaySoundAsync(burstPath, volume);
                    App.Logger?.Information("🎉 Lucky Bubble! 10x XP!");
                    return;
                }
            }

            // Normal pop sound
            var popFiles = new[] { "Pop.mp3", "Pop2.mp3", "Pop3.mp3" };
            var chosenPop = popFiles[_random.Next(popFiles.Length)];
            var popPath = Path.Combine(soundsPath, chosenPop);

            if (File.Exists(popPath))
            {
                var masterVolume = App.Settings.Current.MasterVolume / 100f;
                var bubblesVolume = App.Settings.Current.BubblesVolume / 100f;
                var volume = (float)Math.Pow(masterVolume * bubblesVolume, 1.5);

                PlaySoundAsync(popPath, volume);
            }
        }
        catch (Exception ex)
        {
            App.Logger?.Debug("Failed to play pop sound: {Error}", ex.Message);
        }
    }

    // Performance: Pool of audio devices to avoid creating new ones for each sound
    private static readonly Queue<WaveOutEvent> _audioDevicePool = new();
    private static readonly object _audioPoolLock = new();
    private const int MAX_POOLED_DEVICES = 4;

    private WaveOutEvent GetPooledAudioDevice()
    {
        lock (_audioPoolLock)
        {
            if (_audioDevicePool.Count > 0)
            {
                return _audioDevicePool.Dequeue();
            }
        }
        return new WaveOutEvent();
    }

    private void ReturnAudioDevice(WaveOutEvent device)
    {
        lock (_audioPoolLock)
        {
            if (_audioDevicePool.Count < MAX_POOLED_DEVICES)
            {
                _audioDevicePool.Enqueue(device);
            }
            else
            {
                device.Dispose();
            }
        }
    }

    private void PlaySoundAsync(string path, float volume)
    {
        Task.Run(() =>
        {
            WaveOutEvent? outputDevice = null;
            AudioFileReader? audioFile = null;
            try
            {
                audioFile = new AudioFileReader(path);
                audioFile.Volume = volume;

                outputDevice = GetPooledAudioDevice();  // Performance: Reuse pooled device
                outputDevice.Init(audioFile);
                outputDevice.Play();

                while (outputDevice.PlaybackState == PlaybackState.Playing)
                {
                    Thread.Sleep(50);
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("Audio playback failed: {Error}", ex.Message);
            }
            finally
            {
                audioFile?.Dispose();
                if (outputDevice != null)
                {
                    try { outputDevice.Stop(); } catch { }
                    ReturnAudioDevice(outputDevice);  // Performance: Return to pool
                }
            }
        });
    }

    public void PopAllBubbles()
    {
        try
        {
            // Safety check for shutdown scenarios
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.HasShutdownStarted)
            {
                // Direct cleanup without dispatcher - force destroy
                foreach (var bubble in _bubbles.ToArray())
                {
                    try { bubble.ForceDestroy(); } catch { }
                }
                _bubbles.Clear();
                return;
            }

            // Take a copy of bubbles to close
            var bubblesToClose = _bubbles.ToArray();
            _bubbles.Clear();

            // Close on UI thread - use Invoke for synchronous cleanup during stop
            // Since animation timer is stopped, we need to force destroy (no animation)
            dispatcher.Invoke(() =>
            {
                foreach (var bubble in bubblesToClose)
                {
                    try
                    {
                        bubble.ForceDestroy();
                    }
                    catch (Exception ex)
                    {
                        App.Logger?.Debug("Error destroying bubble: {Error}", ex.Message);
                    }
                }
            }, System.Windows.Threading.DispatcherPriority.Send); // High priority to complete quickly
        }
        catch (Exception ex)
        {
            App.Logger?.Debug("PopAllBubbles error during shutdown: {Error}", ex.Message);
            // Force clear the list even if popup failed
            _bubbles.Clear();
        }
    }

    public void Dispose()
    {
        Stop();
    }
}

/// <summary>
/// Individual bubble that floats upward and can be popped
/// </summary>
internal class Bubble
{
    private readonly Window _window;
    private readonly Random _random;
    private readonly Action<Bubble>? _onPop;
    private readonly Action<Bubble>? _onMiss;
    private readonly Action<Bubble>? _onDestroy;
    private readonly bool _isClickable;

    private double _posX, _posY;
    private double _startX;
    private double _speed;
    private double _timeAlive;
    private double _wobbleOffset;
    private double _angle;
    private double _scale = 1.0;
    private double _fadeAlpha = 1.0;
    private int _animType;
    private bool _isPopping;
    private bool _isAlive = true;
    private bool _isDestroyed = false;

    private readonly Image _bubbleImage;
    private readonly int _size;
    private readonly double _screenTop;

    public bool IsAlive => _isAlive && !_isDestroyed;

    public Bubble(System.Windows.Forms.Screen screen, BitmapImage? image, Random random,
                  Action<Bubble>? onPop, Action<Bubble>? onMiss, Action<Bubble>? onDestroy, bool isClickable = true)
    {
        _random = random;
        _onPop = onPop;
        _onMiss = onMiss;
        _onDestroy = onDestroy;
        _isClickable = isClickable;
        
        // Random properties
        _size = random.Next(150, 250);
        _speed = 1.0 + random.NextDouble() * 1.0; // 1.0 to 2.0 pixels per frame (scaled for 30fps)
        _animType = random.Next(4);
        _wobbleOffset = random.NextDouble() * 100;
        _angle = random.Next(360);

        // Get DPI scale for this specific screen
        var dpiScale = GetDpiForScreen(screen);

        // Position - start at bottom of screen
        var area = screen.WorkingArea;
        _startX = (area.X + random.Next(50, Math.Max(100, area.Width - _size - 50))) / dpiScale;
        _posX = _startX;
        _posY = (area.Y + area.Height) / dpiScale;
        _screenTop = area.Y / dpiScale - _size - 50;

        // Create bubble image (hit-testing disabled — the Ellipse behind handles clicks)
        _bubbleImage = new Image
        {
            Width = _size,
            Height = _size,
            Stretch = Stretch.Uniform,
            RenderTransformOrigin = new Point(0.5, 0.5),
            Cursor = _isClickable ? Cursors.Hand : Cursors.Arrow,
            IsHitTestVisible = false
        };

        if (image != null)
        {
            _bubbleImage.Source = image;
        }
        else
        {
            // Fallback - create simple ellipse
            var drawing = new DrawingGroup();
            using (var ctx = drawing.Open())
            {
                var gradientBrush = new RadialGradientBrush(
                    Color.FromArgb(180, 200, 220, 255),
                    Color.FromArgb(80, 255, 255, 255));
                ctx.DrawEllipse(gradientBrush, new Pen(Brushes.White, 2), 
                    new Point(_size / 2, _size / 2), _size / 2 - 5, _size / 2 - 5);
            }
            _bubbleImage.Source = new DrawingImage(drawing);
        }

        // Transform for rotation and scale
        var transformGroup = new TransformGroup();
        transformGroup.Children.Add(new ScaleTransform(1, 1));
        transformGroup.Children.Add(new RotateTransform(0));
        _bubbleImage.RenderTransform = transformGroup;

        // Create invisible hit area ellipse that covers the full bubble
        // This ensures clicks anywhere in the circular bubble area register
        var hitArea = new System.Windows.Shapes.Ellipse
        {
            Width = _size,
            Height = _size,
            Fill = Brushes.Transparent, // Invisible but captures hits
            IsHitTestVisible = _isClickable,
            Cursor = _isClickable ? Cursors.Hand : Cursors.Arrow
        };

        if (_isClickable)
        {
            hitArea.MouseLeftButtonDown += (s, e) =>
            {
                Pop();
                e.Handled = true;
            };
        }

        // Create container grid with hit area behind the bubble image
        var grid = new Grid
        {
            Width = _size,
            Height = _size,
            Background = Brushes.Transparent,
            IsHitTestVisible = _isClickable
        };
        grid.Children.Add(hitArea);      // Hit area first (behind)
        grid.Children.Add(_bubbleImage); // Image on top

        // Grid click as backup (only if clickable)
        if (_isClickable)
        {
            grid.MouseLeftButtonDown += (s, e) =>
            {
                Pop();
                e.Handled = true;
            };
        }

        // Single window - clickable or click-through based on setting
        _window = new Window
        {
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            Topmost = true,
            ShowInTaskbar = false,
            ShowActivated = false,
            Focusable = false,
            Width = _size + 40,
            Height = _size + 40,
            Left = _posX - 20,
            Top = _posY - 20,
            Content = grid,
            Cursor = _isClickable ? Cursors.Hand : Cursors.Arrow,
            IsHitTestVisible = _isClickable
        };

        // Window click as final backup (only if clickable)
        if (_isClickable)
        {
            _window.MouseLeftButtonDown += (s, e) => Pop();
        }

        // Show window
        _window.Show();

        // Hide from Alt+Tab
        HideFromAltTab();

        // Note: Animation is now driven by shared timer in BubbleService.AnimateAllBubbles()
    }

    /// <summary>
    /// Called by BubbleService's shared animation timer (~30 FPS)
    /// </summary>
    public void AnimateFrame()
    {
        // Early exit checks - must be first to avoid any work on destroyed bubbles
        if (!_isAlive || _isDestroyed) return;

        if (_isPopping)
        {
            // Pop animation - expand and fade (scaled for 30fps)
            _scale += 0.04;
            _fadeAlpha -= 0.066;
            _angle += 2;

            if (_fadeAlpha <= 0)
            {
                Destroy();
                return;
            }
        }
        else
        {
            // Normal float animation (scaled for 30fps)
            _timeAlive += 0.02;
            _posY -= _speed;

            // Wobble based on animation type
            double offset = 0;
            switch (_animType)
            {
                case 0:
                    offset = Math.Sin(_timeAlive * 6) * 25;
                    _angle = (_angle + 0.34) % 360;
                    break;
                case 1:
                    offset = Math.Sin(_timeAlive * 7.5) * 30;
                    _angle = (_angle + 0.14) % 360;
                    break;
                case 2:
                    offset = Math.Cos(_timeAlive * 5.4) * 25;
                    _angle = (_angle - 0.66) % 360;
                    break;
                case 3:
                    offset = Math.Sin(_timeAlive * 3) * 30 + Math.Cos(_timeAlive * 6) * 15;
                    _angle = (_angle + 0.54) % 360;
                    break;
            }
            _posX = _startX + offset;

            // Check if floated off screen
            if (_posY < _screenTop)
            {
                _onMiss?.Invoke(this);
                Destroy();
                return;
            }
        }

        // Update visuals - wrapped in try-catch to handle disposed windows gracefully
        try
        {
            // Double-check we're still alive after calculations
            if (_isDestroyed || !_isAlive) return;

            // Update scale wobble (scaled for 30fps)
            var wobble = 0.06 * Math.Sin(_timeAlive * 7.5 + _wobbleOffset);
            var currentScale = _scale + wobble;

            if (_bubbleImage.RenderTransform is TransformGroup tg && tg.Children.Count >= 2)
            {
                if (tg.Children[0] is ScaleTransform st)
                {
                    st.ScaleX = currentScale;
                    st.ScaleY = currentScale;
                }
                if (tg.Children[1] is RotateTransform rt)
                {
                    rt.Angle = _angle;
                }
            }

            _window.Opacity = _fadeAlpha;
            _window.Left = _posX - 20;
            _window.Top = _posY - 20;
        }
        catch (Exception ex)
        {
            App.Logger?.Debug("Bubble animate error: {Error}", ex.Message);
            Destroy();
        }
    }

    public void Pop()
    {
        if (!_isAlive || _isPopping) return;
        _isPopping = true;
        _onPop?.Invoke(this);
        // Don't call Destroy() here - let AnimateFrame() handle the burst animation
        // The animation will expand and fade the bubble, then call Destroy() when done
    }

    /// <summary>
    /// Force destroy the bubble immediately without animation.
    /// Used during cleanup when animation timer is stopped.
    /// </summary>
    public void ForceDestroy()
    {
        Destroy();
    }

    private void Destroy()
    {
        if (_isDestroyed) return;
        _isDestroyed = true;
        _isAlive = false;

        try { _window.Close(); } catch { }

        // Notify service to remove from list (after animation completed)
        try { _onDestroy?.Invoke(this); } catch { }
    }

    #region Win32

    private static double GetDpiForScreen(System.Windows.Forms.Screen screen)
    {
        try
        {
            uint dpiX = 96, dpiY = 96;
            var hMonitor = MonitorFromPoint(new POINT { X = screen.Bounds.X + 1, Y = screen.Bounds.Y + 1 }, 2);

            if (hMonitor != IntPtr.Zero)
            {
                var result = GetDpiForMonitor(hMonitor, 0, out dpiX, out dpiY);
                if (result == 0)
                {
                    return dpiX / 96.0;
                }
            }

            using var g = System.Drawing.Graphics.FromHwnd(IntPtr.Zero);
            return g.DpiX / 96.0;
        }
        catch
        {
            return 1.0;
        }
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

    [System.Runtime.InteropServices.DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);

    private void HideFromAltTab()
    {
        try
        {
            var hwnd = new System.Windows.Interop.WindowInteropHelper(_window).Handle;
            var exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
        }
        catch { }
    }

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hwnd, int index);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);

    #endregion
}