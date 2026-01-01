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
    private const int MAX_BUBBLES = 8;
    private readonly List<Bubble> _bubbles = new();
    private readonly Random _random = new();
    private DispatcherTimer? _spawnTimer;
    private bool _isRunning;
    private BitmapImage? _bubbleImage;
    private string _assetsPath = "";

    public bool IsRunning => _isRunning;
    public int ActiveBubbles => _bubbles.Count;

    public event Action? OnBubblePopped;
    public event Action? OnBubbleMissed;

    public void Start()
    {
        if (_isRunning) return;
        
        var settings = App.Settings.Current;
        
        // Check level requirement
        if (settings.PlayerLevel < 20)
        {
            App.Logger?.Information("BubbleService: Level {Level} is below 20, bubbles not available", settings.PlayerLevel);
            return;
        }
        
        _isRunning = true;

        _assetsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets");
        
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

        // Spawn first bubble immediately
        SpawnBubble();

        App.Logger?.Information("BubbleService started - {Freq} bubbles/min", settings.BubblesFrequency);
    }

    public void Stop()
    {
        if (!_isRunning) return;
        _isRunning = false;

        _spawnTimer?.Stop();
        _spawnTimer = null;

        // Pop all remaining bubbles
        PopAllBubbles();

        App.Logger?.Information("BubbleService stopped");
    }

    private void LoadBubbleImage()
    {
        try
        {
            var imagePath = Path.Combine(_assetsPath, "images", "bubble.png");
            if (File.Exists(imagePath))
            {
                _bubbleImage = new BitmapImage();
                _bubbleImage.BeginInit();
                _bubbleImage.UriSource = new Uri(imagePath);
                _bubbleImage.CacheOption = BitmapCacheOption.OnLoad;
                _bubbleImage.EndInit();
                _bubbleImage.Freeze();
                App.Logger?.Debug("Bubble image loaded from {Path}", imagePath);
            }
            else
            {
                App.Logger?.Warning("Bubble image not found at {Path}", imagePath);
            }
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
                    ? System.Windows.Forms.Screen.AllScreens 
                    : new[] { System.Windows.Forms.Screen.PrimaryScreen! };
                
                var screen = screens[_random.Next(screens.Length)];
                var bubble = new Bubble(screen, _bubbleImage, _random, OnPop, OnMiss);
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
        PlayPopSound();
        _bubbles.Remove(bubble);
        OnBubblePopped?.Invoke();
        App.Progression?.AddXP(2);
        
        // Track for achievement
        App.Achievements?.TrackBubblePopped();
    }

    private void OnMiss(Bubble bubble)
    {
        _bubbles.Remove(bubble);
        OnBubbleMissed?.Invoke();
    }

    private void PlayPopSound()
    {
        try
        {
            var soundsPath = Path.Combine(_assetsPath, "sounds");
            var popFiles = new[] { "Pop.mp3", "Pop2.mp3", "Pop3.mp3" };
            var chosenPop = popFiles[_random.Next(popFiles.Length)];
            var popPath = Path.Combine(soundsPath, chosenPop);

            if (File.Exists(popPath))
            {
                var volume = App.Settings.Current.BubblesVolume / 100f;
                volume = Math.Max(0.05f, (float)Math.Pow(volume, 1.5)); // Gentler curve, min 5%
                
                PlaySoundAsync(popPath, volume);
            }

            // Random bonus sounds
            var chance = _random.NextDouble();
            if (chance < 0.03) // 3% chance
            {
                var burstPath = Path.Combine(soundsPath, "burst.mp3");
                if (File.Exists(burstPath))
                {
                    var volume = App.Settings.Current.BubblesVolume / 100f;
                    PlaySoundAsync(burstPath, volume);
                }
            }
            else if (chance < 0.08) // 5% chance (8% - 3%)
            {
                var ggPath = Path.Combine(soundsPath, "GG.mp3");
                if (File.Exists(ggPath))
                {
                    var volume = App.Settings.Current.BubblesVolume / 100f;
                    PlaySoundAsync(ggPath, volume);
                }
            }
        }
        catch (Exception ex)
        {
            App.Logger?.Debug("Failed to play pop sound: {Error}", ex.Message);
        }
    }

    private void PlaySoundAsync(string path, float volume)
    {
        Task.Run(() =>
        {
            try
            {
                using var audioFile = new AudioFileReader(path);
                audioFile.Volume = volume;
                using var outputDevice = new WaveOutEvent();
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
        });
    }

    public void PopAllBubbles()
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            foreach (var bubble in _bubbles.ToArray())
            {
                bubble.Pop();
            }
            _bubbles.Clear();
        });
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
    private readonly DispatcherTimer _animTimer;
    private readonly Random _random;
    private readonly Action<Bubble> _onPop;
    private readonly Action<Bubble> _onMiss;
    
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
    
    private readonly Image _bubbleImage;
    private readonly int _size;
    private readonly double _screenTop;

    public Bubble(System.Windows.Forms.Screen screen, BitmapImage? image, Random random, 
                  Action<Bubble> onPop, Action<Bubble> onMiss)
    {
        _random = random;
        _onPop = onPop;
        _onMiss = onMiss;
        
        // Random properties
        _size = random.Next(150, 250);
        _speed = 1.5 + random.NextDouble() * 1.5; // 1.5 to 3.0 pixels per frame
        _animType = random.Next(4);
        _wobbleOffset = random.NextDouble() * 100;
        _angle = random.Next(360);

        // Get DPI scale
        var dpiScale = GetDpiScale();
        
        // Position - start at bottom of screen
        var area = screen.WorkingArea;
        _startX = (area.X + random.Next(50, Math.Max(100, area.Width - _size - 50))) / dpiScale;
        _posX = _startX;
        _posY = (area.Y + area.Height) / dpiScale;
        _screenTop = area.Y / dpiScale - _size - 50;

        // Create bubble image
        _bubbleImage = new Image
        {
            Width = _size,
            Height = _size,
            Stretch = Stretch.Uniform,
            RenderTransformOrigin = new Point(0.5, 0.5),
            Cursor = Cursors.Hand
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

        // Make bubble image clickable
        _bubbleImage.MouseLeftButtonDown += (s, e) => 
        {
            Pop();
            e.Handled = true;
        };

        // Create container grid with the bubble
        var grid = new Grid 
        { 
            Background = Brushes.Transparent,
            Children = { _bubbleImage }
        };
        
        // Grid click as backup
        grid.MouseLeftButtonDown += (s, e) => 
        {
            Pop();
            e.Handled = true;
        };

        // Single window - clickable, not click-through
        _window = new Window
        {
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            Topmost = true,
            ShowInTaskbar = false,
            Width = _size + 40,
            Height = _size + 40,
            Left = _posX - 20,
            Top = _posY - 20,
            Content = grid,
            Cursor = Cursors.Hand
        };

        // Window click as final backup
        _window.MouseLeftButtonDown += (s, e) => Pop();

        // Show window
        _window.Show();

        // Animation timer (~20 FPS)
        _animTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _animTimer.Tick += Animate;
        _animTimer.Start();
    }

    private void Animate(object? sender, EventArgs e)
    {
        if (!_isAlive) return;

        if (_isPopping)
        {
            // Pop animation - expand and fade
            _scale += 0.06;
            _fadeAlpha -= 0.1;
            _angle += 3;

            if (_fadeAlpha <= 0)
            {
                Destroy();
                return;
            }
        }
        else
        {
            // Normal float animation
            _timeAlive += 0.03;
            _posY -= _speed;

            // Wobble based on animation type
            double offset = 0;
            switch (_animType)
            {
                case 0:
                    offset = Math.Sin(_timeAlive * 2) * 25;
                    _angle = (_angle + 0.5) % 360;
                    break;
                case 1:
                    offset = Math.Sin(_timeAlive * 2.5) * 30;
                    _angle = (_angle + 0.2) % 360;
                    break;
                case 2:
                    offset = Math.Cos(_timeAlive * 1.8) * 25;
                    _angle = (_angle - 1.0) % 360;
                    break;
                case 3:
                    offset = Math.Sin(_timeAlive) * 30 + Math.Cos(_timeAlive * 2) * 15;
                    _angle = (_angle + 0.8) % 360;
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

        // Update visuals
        try
        {
            // Update scale wobble
            var wobble = 0.06 * Math.Sin(_timeAlive * 2.5 + _wobbleOffset);
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
    }

    private void Destroy()
    {
        if (!_isAlive) return;
        _isAlive = false;
        _animTimer.Stop();

        try { _window.Close(); } catch { }
    }

    #region Win32

    private double GetDpiScale()
    {
        try
        {
            using var g = System.Drawing.Graphics.FromHwnd(IntPtr.Zero);
            return g.DpiX / 96.0;
        }
        catch
        {
            return 1.0;
        }
    }

    #endregion
}