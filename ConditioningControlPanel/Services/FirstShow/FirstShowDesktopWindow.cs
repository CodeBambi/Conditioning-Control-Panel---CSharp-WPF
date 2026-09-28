using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.EmiDesk;
using ConditioningControlPanel.Services.Fyp;
using ConditioningControlPanel.Services.Fyp.Online;
using SkiaSharp.Views.WPF;

namespace ConditioningControlPanel.Services.FirstShow;

/// <summary>Emi and the production effect renderers, directly over the player's desktop.</summary>
internal sealed partial class FirstShowDesktopWindow : Window
{
    private readonly Canvas _stage = new() { Background = null };
    private readonly SKElement _surface = new() { IsHitTestVisible = false };
    private readonly EmiDeskWindow _emi;
    private readonly MainWindow? _main;
    private readonly Border _bubble = new() { Width = 390, CornerRadius = new CornerRadius(18), Padding = new Thickness(18), Background = new SolidColorBrush(Color.FromRgb(24,24,50)), BorderBrush = Brushes.HotPink, BorderThickness = new Thickness(1), Visibility = Visibility.Collapsed };
    private readonly Window _speechWindow = new()
    {
        Title = "CCP - Emi speech", WindowStyle = WindowStyle.None, AllowsTransparency = true,
        Background = Brushes.Transparent, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false,
        ShowActivated = false, Topmost = true, SizeToContent = SizeToContent.Manual
    };
    private readonly StackPanel _bubbleContent = new();
    private readonly ScrollViewer _speechScroll = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    private readonly TextBlock _speech = new() { Foreground = Brushes.White, FontSize = 17, TextWrapping = TextWrapping.Wrap };
    private readonly StackPanel _loadingPanel = new() { Visibility = Visibility.Collapsed, Margin = new Thickness(0,14,0,0) };
    private readonly ProgressBar _loadingBar = new() { Height = 9, Minimum = 0, Maximum = 6, Foreground = Brushes.HotPink, Background = new SolidColorBrush(Color.FromRgb(55,38,70)) };
    private readonly TextBlock _loadingStatus = new() { Foreground = Brushes.LightGray, FontSize = 13, Margin = new Thickness(0,7,0,0), TextWrapping = TextWrapping.Wrap };
    private readonly WrapPanel _choices = new() { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0,14,0,0) };
    private readonly Border _logo = new() { Width = 460, CornerRadius = new CornerRadius(22), Padding = new Thickness(5), BorderThickness = new Thickness(3), Opacity = 0, IsHitTestVisible = false };
    private readonly FirstShowAudio _audio;
    private readonly GlobalKeyboardHook _panicHook = new();
    private FirstShowEffects? _effects;
    private readonly Stopwatch _life = Stopwatch.StartNew(), _show = new();
    private readonly List<string> _files = new();
    private CancellationTokenSource? _load;
    private bool _closed, _greeted, _centered, _playing, _ending, _serious;
    private double _last, _moveAt, _spokeAt = -10;
    private int _beat = -1;
    private bool _moving, _speechPending;
    private double _settledAt;
    private string _pendingSpeech = "";
    private string _selected = "";
    private bool _bundled;
    private double _x, _y;
    private readonly string _panic;
    private static readonly double[] Beats = { 0,2,7,11,15,19,24,28,32 };
    private static readonly string[] Cues = { "entrance","flash","pink","drain","words","bubbles","mix","gather","reveal" };
    internal static string Text(string key) => Loc.Get("first_show_" + key);

    public FirstShowDesktopWindow(bool preview, EmiDeskWindow emi, MainWindow? main)
    {
        _emi = emi; _main = main;
        _audio = new FirstShowAudio(preview);
        _panic = App.Settings?.Current?.PanicKey ?? "Escape";
        Title = "CCP - Emi show";
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false; Topmost = true;
        WindowStartupLocation = WindowStartupLocation.Manual;
        // Primary-screen DIPs, never virtual-desktop dimensions or a changed monitor preference.
        Left = 0; Top = 0; Width = SystemParameters.PrimaryScreenWidth; Height = SystemParameters.PrimaryScreenHeight;
        Content = _stage;
        _surface.Width = Width; _surface.Height = Height; _stage.Children.Add(_surface);
        _surface.PaintSurface += (_, e) =>
        {
            e.Surface.Canvas.Clear(SkiaSharp.SKColors.Transparent);
            _effects?.Render(e.Surface.Canvas, e.Info.Width, e.Info.Height);
        };
        _logo.BorderBrush = new LinearGradientBrush(new GradientStopCollection
        {
            new(Colors.White,.0), new(Colors.SlateGray,.24), new(Colors.White,.45), new(Colors.DimGray,.7), new(Colors.Silver,1)
        }, 35);
        _logo.Child = new Image { Source = new BitmapImage(new Uri("pack://application:,,,/Resources/features/ccp_banner.png")), Stretch = Stretch.Uniform };
        _stage.Children.Add(_logo);
        _loadingPanel.Children.Add(_loadingBar); _loadingPanel.Children.Add(_loadingStatus);
        _bubbleContent.Children.Add(_speech); _bubbleContent.Children.Add(_loadingPanel); _bubbleContent.Children.Add(_choices);
        _bubble.Child = _bubbleContent; _speechWindow.Content = _bubble;
        _x = Width - 205; _y = Height - 150;
        Place();
        Activated += (_, _) => { if (_guiding) FirstShowInput.PassThrough(this); };
        DpiChanged += (_, _) => { if (_guiding) FirstShowInput.PassThrough(this); };
        Loaded += (_, _) => { Activate(); _panicHook.Start(); CompositionTarget.Rendering += Frame; };
        _panicHook.KeyPressed += key =>
        {
            if (key == Key.Escape || string.Equals(key.ToString(), _panic, StringComparison.OrdinalIgnoreCase))
                Dispatcher.BeginInvoke(new Action(() => { if (!_closed) Close(); }));
        };
        Closed += (_, _) => Cleanup();
        PreviewKeyDown += (_, e) =>
        {
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key == Key.Escape || string.Equals(key.ToString(), _panic, StringComparison.OrdinalIgnoreCase)) { e.Handled = true; Close(); }
        };
        MouseLeftButtonDown += (_, e) =>
        {
            if (!_playing || _show.Elapsed.TotalSeconds >= 28 || e.OriginalSource is not Canvas) return;
            var p = e.GetPosition(_surface); var dpi = VisualTreeHelper.GetDpi(this);
            _effects?.Pop(p.X*dpi.DpiScaleX,p.Y*dpi.DpiScaleY);
        };
    }
    internal void BeginEntrance()
    {
        _emi.PresentAt(PointToScreen(new Point(_x,_y)),0,0,"idle","^_^");
        _emi.RunPresentationEntrance();
    }
    private void Say(string text, bool serious = false)
    {
        _loadingPanel.Visibility = Visibility.Collapsed;
        _serious = serious; _speech.Text = text; _bubble.Visibility = Visibility.Hidden;
        _pendingSpeech = text; _speechPending = true;
        _speechWindow.Hide();
    }
    private void Button(string label, Action action)
    {
        var button = new Button { Content = label, Margin = new Thickness(4), Padding = new Thickness(13,8,13,8), FontSize = 14,
            Foreground = Brushes.White, Background = new SolidColorBrush(Color.FromRgb(70,35,79)), BorderBrush = Brushes.HotPink };
        button.Click += (_, _) => { _audio.Cue("click"); action(); };
        _choices.Children.Add(button);
    }
    private void Greeting()
    {
        Say(Text("greeting")); _choices.Children.Clear();
        Button(Text("yes"), Choose); Button(Text("later"), Close);
    }
    private void Choose()
    {
        _centered = true; _moveAt = _life.Elapsed.TotalSeconds;
        Say(Text("pickQuestion") + "\n\n" + Text("pickHint")); _choices.Children.Clear();
        foreach (var preset in FirstShowPresets.All)
        {
            var id = preset;
            Button(id == "censored" ? Text("censored") : id, () => _ = Load(id));
        }
        Button(Text("later"), Close);
    }
    private async Task Load(string preset)
    {
        _load?.Cancel(); _load?.Dispose();
        var request = _load = new CancellationTokenSource();
        _bundled = false;
        _selected = preset; _choices.Children.Clear(); Say(Text("loading"));
        Button(Text("bundled"), () => _ = Bundled()); Button(Text("later"), Close);
        var paths = new List<string>(); FirstShowEffects? candidate = null;
        try
        {
            // Native image effects use the provider's still/animated-image feed, never a clip stand-in.
            var source = new ScrolllerSource(preset == "cosplay", pageLimit: 8, preferTop: true);
            var sources = FirstShowPresets.Sources(preset);
            int attempt = 0;
            paths = await FirstShowMediaLoader.LoadAsync(async token =>
            {
                var channel = new FeedChannelState { Name = sources[attempt % sources.Length] };
                // A second picture feed can be dry too; GIF posts provide ordinary still posters.
                var kind = attempt++ == 0 ? FeedMediaKind.Image : FeedMediaKind.GifStill;
                return (await source.FetchPageAsync(channel, kind, token))?.Entries ?? new List<FypAssetManifest.Entry>();
            }, RemoteMediaCache.MaterializeAsync, RemoteMediaCache.ReleaseTempFile, request.Token,
            progress => { if (!_closed && _load == request) ShowLoading(progress); });
            if (paths.Count == 0) throw new IOException("No demo images available");
            ShowPreparing();
            candidate = new FirstShowEffects(_audio.Cue);
            await candidate.LoadAsync(paths);
            request.Token.ThrowIfCancellationRequested();
            if (_closed || _load != request) return;
            _effects?.Dispose(); _effects = candidate; candidate = null;
            ReleaseFiles(); _files.AddRange(paths); paths.Clear();
            Confirm();
        }
        catch (Exception ex)
        {
            if (!request.IsCancellationRequested)
                App.Logger?.Warning("First show preset {Preset} unavailable: {Reason}", preset, ex.Message);
            if (!_closed && _load == request)
            {
                Say(Text("error")); _choices.Children.Clear();
                Button(Text("pickQuestion"), Choose); Button(Text("bundled"), () => _ = Bundled()); Button(Text("later"), Close);
            }
        }
        finally { candidate?.Dispose(); foreach (var path in paths) RemoteMediaCache.ReleaseTempFile(path); }
    }
    private void ShowLoading(FirstShowMediaProgress progress)
    {
        _loadingPanel.Visibility = Visibility.Visible;
        _loadingBar.IsIndeterminate = progress.Finding;
        _loadingBar.Maximum = Math.Max(1,progress.Total); _loadingBar.Value = progress.Ready;
        _loadingStatus.Text = progress.Finding ? Text(progress.Attempt == 1 ? "loadFinding" : "loadRetry") :
            Text("loadDownload").Replace("{ready}",progress.Ready.ToString()).Replace("{total}",progress.Total.ToString());
    }
    private void ShowPreparing()
    {
        _loadingPanel.Visibility = Visibility.Visible; _loadingBar.IsIndeterminate = true;
        _loadingStatus.Text = Text("loadPreparing");
    }
    private async Task Bundled()
    {
        _load?.Cancel(); _load?.Dispose(); _load = null;
        _bundled = true;
        _choices.Children.Clear(); Say(Text("loading")); ShowPreparing(); Button(Text("later"), Close);
        var effects = new FirstShowEffects(_audio.Cue);
        try
        {
            await effects.LoadAsync(Enumerable.Range(0,4).Select(i => Path.Combine(AppContext.BaseDirectory,"Resources","web","backroom","stations","slot","fallback","gif"+i+".webp")).ToArray());
            if (_closed) { effects.Dispose(); return; }
            _effects?.Dispose(); _effects = effects; Confirm();
        }
        catch (Exception ex) { effects.Dispose(); App.Logger?.Debug(ex,"First show fallback unavailable"); if (!_closed) Choose(); }
    }
    private void Confirm()
    {
        Say(Text("warning"),true); _choices.Children.Clear();
        _speech.Inlines.Add(new LineBreak()); _speech.Inlines.Add(new LineBreak());
        _speech.Inlines.Add(new Run(_panic == "Escape" ? "Esc" : _panic) { Foreground = Brushes.Red, FontWeight = FontWeights.Bold });
        _speech.Inlines.Add(new Run(" " + Text("panicHint")));
        _pendingSpeech = _speech.Text;
        Button(Text("ready"), Start); Button(Text("later"), Close);
    }
    private void Start()
    {
        _choices.Children.Clear(); _serious = false; _playing = true; _ending = false; _beat = -1;
        _stage.Background = Brushes.Transparent; _show.Restart();
        Button(Text("stop"), Close);
    }
    private void Frame(object? sender, EventArgs args)
    {
        if (_closed) return;
        var now = _life.Elapsed.TotalSeconds; var dt = Math.Min(.05, now-_last); _last = now;
        if (!_greeted && now > .9 && !_emi.PresentationArriving) { _greeted = true; Greeting(); }
        var time = _show.Elapsed.TotalSeconds;
        if (_playing)
        {
            _effects?.Tick(time,dt); _surface.InvalidateVisual();
            while (_beat+1 < Beats.Length && time >= Beats[_beat+1])
            {
                _beat++; var line = Text("beat"+_beat).Replace("{panicKey}", _panic == "Escape" ? "Esc" : _panic);
                Say(_beat == 8 ? Text("tada") : line); _audio.Cue(Cues[_beat]);
                if (_beat == 0)
                {
                    _speech.Inlines.Clear();
                    var template = Text("beat0").Split("{panicKey}");
                    _speech.Inlines.Add(new Run(template[0]));
                    _speech.Inlines.Add(new Run(_panic == "Escape" ? "Esc" : _panic) { Foreground = Brushes.Red, FontWeight = FontWeights.Bold });
                    if (template.Length > 1) _speech.Inlines.Add(new Run(template[1]));
                }
            }
            if (time >= 35)
            {
                _playing = false; _ending = true; _show.Stop(); _stage.Background = null;
                AskVerdict();
            }
        }
        var m = MotionFx.Level == MotionLevel.Off ? 0 : MotionFx.Level == MotionLevel.Reduced ? .4 : 1;
        var move = _centered ? Math.Clamp((now-_moveAt)/.85,0,1) : 0; move = 1-Math.Pow(1-move,3);
        if (m == 0 && _centered) move = 1;
        var previous = new Point(_x,_y);
        if (!_guiding)
        {
            var scale = VisualTreeHelper.GetDpi(this);
            var body = _emi.BodyScreenRect;
            _x = (Width-Math.Max(205,body.Width/scale.DpiScaleX/2+24))*(1-move)+Width*.5*move;
            _y = (Height-Math.Max(150,body.Height/scale.DpiScaleY/2+24))*(1-move)+Height*.56*move;
        }
        else GuideFrame(dt);
        if (!_guiding && (_playing || _ending))
        {
            var bodySize = BodySize();
            // Emi conducts from just below center, then steps aside for the logo.
            _x = Width*.5; _y = Height*.56;
            if (time >= 31.6)
            {
                var logo = LogoBounds();
                var bounds = SafeBounds();
                var beside = FirstShowLayout.GuideBody(bounds,bodySize,SpeechSize(),logo);
                double step = m == 0 ? 1 : Math.Clamp((time-31.6)/.7,0,1);
                step = step*step*(3-2*step);
                _x += (beside.X+beside.Width/2-_x)*step;
                _y += (beside.Y+beside.Height/2-_y)*step;
            }
        }
        _moving = _emi.PresentationArriving || (new Point(_x,_y)-previous).Length > .3 ||
            (_centered && move < 1) || (!_guiding && (_playing || _ending) && time >= 31.6 && time < 32.3);
        if (_moving) _settledAt = now;
        var speech = Math.Max(0,1-(now-_spokeAt)/1.4);
        var bob = (Math.Sin(now*2.5)*3 + Math.Sin(now*1.1)*2 + speech*Math.Sin(now*16)*2)*m;
        var turn = (Math.Sin(now*1.5)*1.6 + speech*Math.Sin(now*9))*m;
        var pose = _playing && time >= 24 && time < 25 && m > 0 ? "smug" : (_playing && time >= 28 || _ending) ? "celebration" : "idle";
        var expression = _serious ? "o_o" : "^_^";
        if (!_serious && m > 0 && now%12.7 > 10.9 && now%12.7 < 11.18) expression = "^_~";
        if (m > 0 && now%5.3 < .12) expression = "-_-";
        if (IsLoaded) _emi.PresentAt(PointToScreen(new Point(_x,_y)), bob, turn, pose, expression);
        if (!_guiding && time >= 32 && (_playing || _ending))
        {
            var reveal = Math.Clamp((time-32)/.85,0,1);
            _logo.Opacity = reveal*(m == 0 ? 1 : .95+.05*Math.Sin(now*1.7));
            _logo.RenderTransformOrigin = new Point(.5,.5);
            _logo.RenderTransform = new ScaleTransform(.82+.18*reveal,.82+.18*reveal);
        }
        Place();
    }
    private Size BodySize()
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        return new Size(_emi.BodyScreenRect.Width/dpi.DpiScaleX, _emi.BodyScreenRect.Height/dpi.DpiScaleY);
    }
    private Rect SafeBounds()
    {
        var screen = _guiding && _main != null
            ? System.Windows.Forms.Screen.FromHandle(new System.Windows.Interop.WindowInteropHelper(_main).Handle)
            : System.Windows.Forms.Screen.PrimaryScreen;
        if (screen == null || !IsLoaded) return new Rect(16,16,Math.Max(1,Width-32),Math.Max(1,Height-32));
        var area = screen.WorkingArea;
        var a = PointFromScreen(new Point(area.Left,area.Top));
        var b = PointFromScreen(new Point(area.Right,area.Bottom));
        return new Rect(a.X+16,a.Y+16,Math.Max(1,b.X-a.X-32),Math.Max(1,b.Y-a.Y-32));
    }
    private Size SpeechSize()
    {
        var bounds = SafeBounds();
        var stageDpi = VisualTreeHelper.GetDpi(this);
        var speechDpi = VisualTreeHelper.GetDpi(_speechWindow);
        double sx = stageDpi.DpiScaleX/speechDpi.DpiScaleX, sy = stageDpi.DpiScaleY/speechDpi.DpiScaleY;
        _bubble.Width = Math.Min(390,bounds.Width)*sx;
        _bubble.MaxHeight = bounds.Height*sy;
        // Normal lines need no scrolling chrome. Add it only when the content exceeds the monitor.
        _bubbleContent.Measure(new Size(Math.Max(1,_bubble.Width-38),double.PositiveInfinity));
        bool overflow = _bubbleContent.DesiredSize.Height+38 > _bubble.MaxHeight;
        if (overflow && _bubble.Child != _speechScroll)
        {
            _bubble.Child = null; _speechScroll.Content = _bubbleContent; _bubble.Child = _speechScroll;
        }
        else if (!overflow && _bubble.Child == _speechScroll)
        {
            _speechScroll.Content = null; _bubble.Child = _bubbleContent;
        }
        _bubble.Measure(new Size(_bubble.Width,_bubble.MaxHeight));
        return new Size(_bubble.Width/sx,_bubble.DesiredSize.Height/sy);
    }
    private Rect LogoBounds()
    {
        _logo.Width = Math.Min(460,Math.Max(160,Width-2*(BodySize().Width+48)));
        _logo.Measure(new Size(_logo.Width,double.PositiveInfinity));
        return new Rect(Width/2-_logo.Width/2,Height*.43-_logo.DesiredSize.Height/2,_logo.Width,_logo.DesiredSize.Height);
    }
    private void Place()
    {
        var logo = LogoBounds();
        Canvas.SetLeft(_logo,logo.X); Canvas.SetTop(_logo,logo.Y);
        if (!IsLoaded || !_greeted) return;
        if (_moving || _life.Elapsed.TotalSeconds-_settledAt < .16)
        {
            _speechWindow.Hide();
            return;
        }
        _bubble.Visibility = Visibility.Visible;
        var size = BodySize(); var speech = SpeechSize(); var bounds = SafeBounds();
        var body = new Rect(_x-size.Width/2,_y-size.Height/2,size.Width,size.Height);
        var avoid = _guiding ? _highlight.Target : _logo.Opacity > 0 ? logo : Rect.Empty;
        var preferred = new Point(body.Right+18,body.Top);
        var rect = FirstShowLayout.Speech(bounds,speech,body,avoid,preferred);
        if (_speechPending)
        {
            _speechPending = false; _spokeAt = _life.Elapsed.TotalSeconds;
            _audio.Speak(_pendingSpeech, _serious ? "idle" : "celebration");
        }
        // Own the speech above Emi, not under her or the application's native child surfaces.
        var pixel = PointToScreen(rect.TopLeft);
        var dpi = VisualTreeHelper.GetDpi(_speechWindow);
        _speechWindow.Left = pixel.X/dpi.DpiScaleX; _speechWindow.Top = pixel.Y/dpi.DpiScaleY;
        _speechWindow.Width = rect.Width*VisualTreeHelper.GetDpi(this).DpiScaleX/dpi.DpiScaleX;
        _speechWindow.Height = rect.Height*VisualTreeHelper.GetDpi(this).DpiScaleY/dpi.DpiScaleY;
        if (!_speechWindow.IsVisible)
        {
            _speechWindow.Owner = _emi;
            _speechWindow.Show();
        }
    }
    private void ReleaseFiles() { foreach (var path in _files) RemoteMediaCache.ReleaseTempFile(path); _files.Clear(); }
    private void Cleanup()
    {
        if (_closed) return;
        ClearGuideTarget();
        _closed = true; _speechWindow.Close(); CompositionTarget.Rendering -= Frame; _panicHook.Dispose();
        _load?.Cancel(); _load?.Dispose(); _load = null;
        _show.Stop(); _effects?.Dispose(); _effects = null; _audio.Dispose(); ReleaseFiles();
    }
}
