using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Controls.Fx;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk;
using ConditioningControlPanel.Fx;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.FirstShow;
using ConditioningControlPanel.Services.Fyp;
using ConditioningControlPanel.Services.Fyp.Online;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows.WelcomeShow
{
    /// <summary>
    /// PORTED from ConditioningControlPanel/Services/FirstShow/FirstShowDesktopWindow.cs (WPF 7.1.5): EMI
    /// and the show's effects, directly over the player's desktop.
    ///
    /// <para><b>The stage is native, transparent, click-through and never takes focus.</b> A layered
    /// window's painted pixels take the mouse whatever the visual tree says, so the stage is made
    /// input-transparent at the window level through <see cref="X11Overlay"/>: on Windows
    /// WS_EX_TRANSPARENT | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW (Win32Overlay, guarded by
    /// OperatingSystem.IsWindows), on X11 an empty XFixes input shape on an override-redirect window. It is
    /// shown without activation and owns nothing, so it never lifts the app over what the player is using.
    /// While bubbles are up, only their discs take a click (an input region of their rects) so "pop one"
    /// works; everything else still falls through. Native Wayland has neither call: there the stage would
    /// swallow clicks, so the show refuses to open unless the overlay calls succeed (XWayland is fine).</para>
    ///
    /// <para>EMI is the real <see cref="EmiDeskWindow"/> in presentation mode, raised above the stage; her
    /// speech card is its own small window owned by her, so its buttons stay ordinary clickable chrome.</para>
    ///
    /// <para>Nothing here writes a setting. The preset the player picks only chooses the demo pictures.</para>
    /// </summary>
    internal sealed partial class FirstShowStageWindow : Window
    {
        private readonly Canvas _stage = new();
        private readonly FxSurface _surface = new() { IsHitTestVisible = false };
        private readonly EmiDeskWindow _emi;
        private readonly MainShellWindow? _main;
        private readonly Border _bubble = new()
        {
            Width = 390, CornerRadius = new CornerRadius(18), Padding = new Thickness(18),
            Background = new SolidColorBrush(Color.FromRgb(24, 24, 50)), BorderBrush = Brushes.HotPink, BorderThickness = new Thickness(1)
        };
        private readonly Window _speechWindow = new()
        {
            Title = "CCP - Emi speech", WindowDecorations = WindowDecorations.None, Background = Brushes.Transparent,
            TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent }, CanResize = false, ShowInTaskbar = false,
            ShowActivated = false, Topmost = true, SizeToContent = SizeToContent.Manual, WindowStartupLocation = WindowStartupLocation.Manual
        };
        private readonly StackPanel _bubbleContent = new();
        private readonly ScrollViewer _speechScroll = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        private readonly TextBlock _speech = new() { Foreground = Brushes.White, FontSize = 17, TextWrapping = TextWrapping.Wrap };
        private readonly StackPanel _loadingPanel = new() { IsVisible = false, Margin = new Thickness(0, 14, 0, 0) };
        private readonly ProgressBar _loadingBar = new() { Height = 9, MinHeight = 9, Minimum = 0, Maximum = 6, Foreground = Brushes.HotPink, Background = new SolidColorBrush(Color.FromRgb(55, 38, 70)) };
        private readonly TextBlock _loadingStatus = new() { Foreground = Brushes.LightGray, FontSize = 13, Margin = new Thickness(0, 7, 0, 0), TextWrapping = TextWrapping.Wrap };
        private readonly WrapPanel _choices = new() { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 14, 0, 0) };
        private readonly Border _logo = new() { Width = 460, CornerRadius = new CornerRadius(22), Padding = new Thickness(5), BorderThickness = new Thickness(3), Opacity = 0, IsHitTestVisible = false };
        private readonly FirstShowAudio _audio = new();
        private readonly FrameClock _clock;
        private readonly Stopwatch _life = Stopwatch.StartNew();
        private FirstShowEffects? _effects;
        private readonly List<string> _files = new();
        private CancellationTokenSource? _load;
        private bool _closed, _greeted, _centered, _playing, _ending, _serious, _opened;
        private double _last, _moveAt, _spokeAt = -10, _showStart = double.NaN, _showFrozen;
        private int _beat = -1;
        private bool _moving, _speechPending;
        private double _settledAt;
        private string _pendingSpeech = "";
        private bool _bundled;
        private double _x, _y;
        private readonly string _panic;
        private PixelRect _screenPx;
        private double _scale = 1;
        private bool _inputShaped;
        private PixelRect[] _inputRects = new PixelRect[4];

        /// <summary>Seconds since the stage was built. Tests step it.</summary>
        internal Func<double> Clock;

        /// <summary>Test seams: the feed, the download and its release, and the bundled pictures.</summary>
        internal Func<string, int, CancellationToken, Task<IReadOnlyList<FypAssetManifest.Entry>>> FetchFeed = DefaultFetch;
        internal Func<string, CancellationToken, Task<string?>> Download = (url, ct) => Games.PbpTempCache.MaterializeAsync(url, ct);
        internal Action<string> Release = path => Games.PbpTempCache.Release(path);
        internal Func<IReadOnlyList<string>> BundledPictures = () => Enumerable.Range(0, 4)
            .Select(i => ContentLocator.Resolve(Path.Combine("Resources", "web", "backroom", "stations", "slot", "fallback", "gif" + i + ".webp")) ?? "")
            .Where(p => p.Length > 0).ToArray();

        internal static string Text(string key) => Loc.Get("first_show_" + key);
        private static MotionLevel Motion => global::ConditioningControlPanel.Avalonia.Controls.AmbientFxCanvas.Env.Level;

        // What a test reads.
        internal string SpeechText => _speech.Text ?? string.Join("", _speech.Inlines?.OfType<Run>().Select(r => r.Text) ?? Array.Empty<string>());
        internal IReadOnlyList<Button> ChoiceButtons => _choices.Children.OfType<Button>().ToArray();
        internal bool Playing => _playing;
        internal bool Ending => _ending;
        internal bool Guiding => _guiding;
        internal bool LoadingShown => _loadingPanel.IsVisible;
        internal string LoadingText => _loadingStatus.Text ?? "";
        internal double LogoOpacity => _logo.Opacity;
        internal FirstShowEffects? Effects => _effects;
        internal Window SpeechWindow => _speechWindow;
        internal bool ClickThrough { get; private set; }

        public FirstShowStageWindow(EmiDeskWindow emi, MainShellWindow? main)
        {
            _emi = emi; _main = main;
            Clock = () => _life.Elapsed.TotalSeconds;
            _panic = CoreSettings.Current?.PanicKey ?? "Escape";
            Title = "CCP - Emi show";
            WindowDecorations = WindowDecorations.None; Background = Brushes.Transparent;
            TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
            CanResize = false; ShowInTaskbar = false; ShowActivated = false; Topmost = true; Focusable = false;
            WindowStartupLocation = WindowStartupLocation.Manual;
            // The primary screen, never the virtual desktop or a changed monitor preference.
            var primary = Screens?.Primary ?? Screens?.All.FirstOrDefault();
            _screenPx = primary?.Bounds ?? new PixelRect(0, 0, 1920, 1080);
            _scale = primary?.Scaling > 0 ? primary!.Scaling : 1;
            Position = _screenPx.Position; Width = _screenPx.Width / _scale; Height = _screenPx.Height / _scale;
            Content = _stage;
            _surface.Width = Width; _surface.Height = Height; _stage.Children.Add(_surface);
            _surface.PaintSurface += (_, e) =>
            {
                e.Canvas.Clear(SkiaSharp.SKColors.Transparent);
                _effects?.Render(e.Canvas, e.Info.Width, e.Info.Height);
            };
            _logo.BorderBrush = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, .7, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(Colors.White, .0), new GradientStop(Colors.SlateGray, .24), new GradientStop(Colors.White, .45),
                    new GradientStop(Colors.DimGray, .7), new GradientStop(Colors.Silver, 1)
                }
            };
            try
            {
                using var art = global::Avalonia.Platform.AssetLoader.Open(new Uri("avares://CCP.Avalonia/Resources/features/ccp_banner.png"));
                _logo.Child = new Image { Source = new Bitmap(art), Stretch = Stretch.Uniform };
            }
            catch (Exception ex) { Log.Debug("First show logo: {Error}", ex.Message); }
            _logo.RenderTransformOrigin = new RelativePoint(.5, .5, RelativeUnit.Relative);
            _stage.Children.Add(_logo);
            _loadingPanel.Children.Add(_loadingBar); _loadingPanel.Children.Add(_loadingStatus);
            _bubbleContent.Children.Add(_speech); _bubbleContent.Children.Add(_loadingPanel); _bubbleContent.Children.Add(_choices);
            _bubble.Child = _bubbleContent; _speechWindow.Content = _bubble;
            _x = Width - 205; _y = Height - 150;
            _clock = new FrameClock(_stage) { Interval = TimeSpan.FromMilliseconds(33) };
            _clock.Tick += (_, _) => Frame();
            Opened += (_, _) => { _opened = true; _clock.Start(); };
            Closed += (_, _) => Cleanup();
            KeyDown += OnStopKey;
            _speechWindow.KeyDown += OnStopKey;
            _stage.Background = null;
            _stage.PointerPressed += (_, e) =>
            {
                if (!_playing || ShowTime >= FirstShowScript.GatherAt) return;
                var p = e.GetPosition(_surface);
                _effects?.Pop(p.X, p.Y);
            };
        }

        /// <summary>Make the stage input-transparent and passive, then show it. False when the platform refuses.</summary>
        internal bool Launch()
        {
            bool ok = false;
            try { ok = X11Overlay.SetClickThrough(this, true) & X11Overlay.SetOverrideRedirect(this, _screenPx, passive: true); }
            catch (Exception ex) { Log.Debug(ex, "First show stage: overlay calls failed"); }
            Show();
            // A fresh native window answers only once it exists: apply again now that it does.
            try
            {
                ok = X11Overlay.SetClickThrough(this, true);
                X11Overlay.SetOverrideRedirect(this, _screenPx, passive: true);
            }
            catch (Exception ex) { Log.Debug(ex, "First show stage: overlay calls failed after show"); }
            ClickThrough = ok;
            if (OperatingSystem.IsWindows()) Win32PanicKey.KeyDown += OnGlobalKey;
            return ok;
        }

        // Windows: the low-level hook reports every key, so Esc and the panic key stop the show even with
        // the panic key switched off in Settings (WPF ran its own GlobalKeyboardHook for exactly this).
        private void OnGlobalKey(int vk, string? name)
        {
            if (vk == 0x1B || string.Equals(name, _panic, StringComparison.OrdinalIgnoreCase))
                Dispatcher.UIThread.Post(() => { if (!_closed) Close(); });
        }

        private void OnStopKey(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape || string.Equals(e.Key.ToString(), _panic, StringComparison.OrdinalIgnoreCase)) { e.Handled = true; Close(); }
        }

        private double ShowTime => double.IsNaN(_showStart) ? _showFrozen : Clock() - _showStart;
        private Point ToScreen(double x, double y) => new(Position.X + x * _scale, Position.Y + y * _scale);

        internal void BeginEntrance()
        {
            _emi.PresentAt(ToScreen(_x, _y), 0, 0, "idle", "^_^");
            _emi.RunPresentationEntrance();
        }

        private void Say(string text, bool serious = false)
        {
            _loadingPanel.IsVisible = false;
            _serious = serious; _speech.Inlines?.Clear(); _speech.Text = text;
            _pendingSpeech = text; _speechPending = true;
            if (_speechWindow.IsVisible) _speechWindow.Hide();
        }

        private void AddButton(string label, Action action)
        {
            var button = new Button
            {
                Content = label, Margin = new Thickness(4), Padding = new Thickness(13, 8, 13, 8), FontSize = 14,
                Foreground = Brushes.White, Background = new SolidColorBrush(Color.FromRgb(70, 35, 79)), BorderBrush = Brushes.HotPink,
                BorderThickness = new Thickness(1), Cursor = new Cursor(StandardCursorType.Hand)
            };
            button.Click += (_, _) => { _audio.Cue("click"); action(); };
            _choices.Children.Add(button);
        }

        private void Greeting()
        {
            Say(Text("greeting")); _choices.Children.Clear();
            AddButton(Text("yes"), Choose); AddButton(Text("later"), Close);
        }

        private void Choose()
        {
            _centered = true; _moveAt = Clock();
            Say(Text("pickQuestion") + "\n\n" + Text("pickHint")); _choices.Children.Clear();
            foreach (var preset in FirstShowPresets.All)
            {
                var id = preset;
                AddButton(id == "censored" ? Text("censored") : id, () => _ = Load(id));
            }
            AddButton(Text("later"), Close);
        }

        private static async Task<IReadOnlyList<FypAssetManifest.Entry>> DefaultFetch(string preset, int attempt, CancellationToken token)
        {
            // Native image effects use the provider's still/animated-image feed, never a clip stand-in.
            var source = new ScrolllerSource(preset == "cosplay", pageLimit: 8, preferTop: true);
            var sources = FirstShowPresets.Sources(preset);
            var channel = new FeedChannelState { Name = sources[attempt % sources.Length] };
            // A second picture feed can be dry too; GIF posts provide ordinary still posters.
            var kind = attempt == 0 ? FeedMediaKind.Image : FeedMediaKind.GifStill;
            return (await source.FetchPageAsync(channel, kind, token))?.Entries ?? new List<FypAssetManifest.Entry>();
        }

        internal async Task Load(string preset)
        {
            _load?.Cancel(); _load?.Dispose();
            var request = _load = new CancellationTokenSource();
            _bundled = false;
            _choices.Children.Clear(); Say(Text("loading"));
            AddButton(Text("bundled"), () => _ = Bundled()); AddButton(Text("later"), Close);
            var paths = new List<string>(); FirstShowEffects? candidate = null;
            try
            {
                int attempt = 0;
                paths = await FirstShowMediaLoader.LoadAsync(token => FetchFeed(preset, attempt++, token), Download, Release, request.Token,
                    progress => Dispatcher.UIThread.Post(() => { if (!_closed && _load == request) ShowLoading(progress); }));
                if (paths.Count == 0) throw new IOException("No demo images available");
                ShowPreparing();
                candidate = new FirstShowEffects(_audio.Cue, Width, Height);
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
                    Log.Warning("First show preset {Preset} unavailable: {Reason}", preset, ex.Message);
                if (!_closed && _load == request)
                {
                    Say(Text("error")); _choices.Children.Clear();
                    AddButton(Text("pickQuestion"), Choose); AddButton(Text("bundled"), () => _ = Bundled()); AddButton(Text("later"), Close);
                }
            }
            finally { candidate?.Dispose(); foreach (var path in paths) Release(path); }
        }

        private void ShowLoading(FirstShowMediaProgress progress)
        {
            _loadingPanel.IsVisible = true;
            _loadingBar.IsIndeterminate = progress.Finding;
            _loadingBar.Maximum = Math.Max(1, progress.Total); _loadingBar.Value = progress.Ready;
            _loadingStatus.Text = progress.Finding ? Text(progress.Attempt == 1 ? "loadFinding" : "loadRetry") :
                Text("loadDownload").Replace("{ready}", progress.Ready.ToString()).Replace("{total}", progress.Total.ToString());
        }

        private void ShowPreparing()
        {
            _loadingPanel.IsVisible = true; _loadingBar.IsIndeterminate = true;
            _loadingStatus.Text = Text("loadPreparing");
        }

        internal async Task Bundled()
        {
            _load?.Cancel(); _load?.Dispose(); _load = null;
            _bundled = true;
            _choices.Children.Clear(); Say(Text("loading")); ShowPreparing(); AddButton(Text("later"), Close);
            var effects = new FirstShowEffects(_audio.Cue, Width, Height);
            try
            {
                await effects.LoadAsync(BundledPictures());
                if (_closed) { effects.Dispose(); return; }
                _effects?.Dispose(); _effects = effects; Confirm();
            }
            catch (Exception ex) { effects.Dispose(); Log.Debug(ex, "First show fallback unavailable"); if (!_closed) Choose(); }
        }

        private void Confirm()
        {
            Say(Text("warning"), true); _choices.Children.Clear();
            var label = FirstShowScript.PanicLabel(_panic);
            SetInlines(new Run(Text("warning")), new LineBreak(), new LineBreak(),
                new Run(label) { Foreground = Brushes.Red, FontWeight = FontWeight.Bold }, new Run(" " + Text("panicHint")));
            _pendingSpeech = Text("warning") + label + " " + Text("panicHint");
            AddButton(Text("ready"), Start); AddButton(Text("later"), Close);
        }

        private void SetInlines(params Inline[] inlines)
        {
            _speech.Text = null;
            _speech.Inlines ??= new InlineCollection();
            _speech.Inlines.Clear();
            _speech.Inlines.AddRange(inlines);
        }

        internal void Start()
        {
            _choices.Children.Clear(); _serious = false; _playing = true; _ending = false; _beat = -1;
            _showStart = Clock();
            AddButton(Text("stop"), Close);
        }

        internal void Frame()
        {
            if (_closed) return;
            // The show never runs over a session or Lockdown: one that starts mid-show ends it at once.
            if (!FirstShowService.MayRun()) { Close(); return; }
            var now = Clock(); var dt = Math.Min(.05, now - _last); _last = now;
            if (!_greeted && now > .9 && !_emi.PresentationArriving) { _greeted = true; Greeting(); }
            var time = ShowTime;
            if (_playing)
            {
                _effects?.Tick(time, dt); _surface.Redraw(); _surface.InvalidateVisual();
                ShapeInput();
                while (_beat + 1 < FirstShowScript.Beats.Length && time >= FirstShowScript.Beats[_beat + 1])
                {
                    _beat++; var label = FirstShowScript.PanicLabel(_panic);
                    Say(Loc.Get(FirstShowScript.LineKey(_beat)).Replace("{panicKey}", label)); _audio.Cue(FirstShowScript.Cues[_beat]);
                    if (_beat == 0)
                    {
                        var template = Text("beat0").Split("{panicKey}");
                        var runs = new List<Inline> { new Run(template[0]), new Run(label) { Foreground = Brushes.Red, FontWeight = FontWeight.Bold } };
                        if (template.Length > 1) runs.Add(new Run(template[1]));
                        SetInlines(runs.ToArray());
                    }
                }
                if (time >= FirstShowScript.Length)
                {
                    _playing = false; _ending = true; _showFrozen = time; _showStart = double.NaN;
                    ShapeInput();
                    AskVerdict();
                }
            }
            var m = Motion == MotionLevel.Off ? 0 : Motion == MotionLevel.Reduced ? .4 : 1;
            var move = _centered ? Math.Clamp((now - _moveAt) / .85, 0, 1) : 0; move = 1 - Math.Pow(1 - move, 3);
            if (m == 0 && _centered) move = 1;
            var previous = new Point(_x, _y);
            if (!_guiding)
            {
                var body = BodySize();
                _x = (Width - Math.Max(205, body.Width / 2 + 24)) * (1 - move) + Width * .5 * move;
                _y = (Height - Math.Max(150, body.Height / 2 + 24)) * (1 - move) + Height * .56 * move;
            }
            else GuideFrame(dt);
            if (!_guiding && (_playing || _ending))
            {
                // Emi conducts from just below center, then steps aside for the logo.
                _x = Width * .5; _y = Height * .56;
                if (time >= 31.6)
                {
                    var beside = FirstShowLayout.GuideBody(SafeBounds(), BodySize(), SpeechSize(), LogoBounds());
                    double step = m == 0 ? 1 : Math.Clamp((time - 31.6) / .7, 0, 1);
                    step = step * step * (3 - 2 * step);
                    _x += (beside.Left + beside.Width / 2 - _x) * step;
                    _y += (beside.Top + beside.Height / 2 - _y) * step;
                }
            }
            double moved = Math.Sqrt(Math.Pow(_x - previous.X, 2) + Math.Pow(_y - previous.Y, 2));
            _moving = _emi.PresentationArriving || moved > .3 ||
                (_centered && move < 1) || (!_guiding && (_playing || _ending) && time >= 31.6 && time < 32.3);
            if (_moving) _settledAt = now;
            var speech = Math.Max(0, 1 - (now - _spokeAt) / 1.4);
            var bob = (Math.Sin(now * 2.5) * 3 + Math.Sin(now * 1.1) * 2 + speech * Math.Sin(now * 16) * 2) * m;
            var turn = (Math.Sin(now * 1.5) * 1.6 + speech * Math.Sin(now * 9)) * m;
            if (_opened) _emi.PresentAt(ToScreen(_x, _y), bob, turn,
                FirstShowScript.Pose(_playing, _ending, time, m), FirstShowScript.Face(_serious, m, now));
            if (!_guiding && time >= FirstShowScript.RevealAt && (_playing || _ending))
            {
                // The logo's IN; its OUT is BeginWelcome (or the close).
                var reveal = Math.Clamp((time - FirstShowScript.RevealAt) / .85, 0, 1);
                _logo.Opacity = Math.Clamp(reveal * (m == 0 ? 1 : .95 + .05 * Math.Sin(now * 1.7)), 0, 1);
                _logo.RenderTransform = new ScaleTransform(.82 + .18 * reveal, .82 + .18 * reveal);
            }
            Place();
        }

        // Only live bubbles take a click; the rest of the stage stays input-transparent.
        private void ShapeInput()
        {
            if (!ClickThrough) return;
            try
            {
                int n = 0;
                if (_playing && _effects != null)
                    foreach (var (x, y, size) in _effects.Bubbles())
                    {
                        if (n == _inputRects.Length) break;
                        double r = size * .52;
                        _inputRects[n++] = new PixelRect((int)((x - r) * _scale), (int)((y - r) * _scale), (int)(r * 2 * _scale), (int)(r * 2 * _scale));
                    }
                if (n > 0) { X11Overlay.SetInputRects(this, _inputRects, n); _inputShaped = true; }
                else if (_inputShaped) { X11Overlay.SetClickThrough(this, true); _inputShaped = false; }
            }
            catch (Exception ex) { Log.Debug(ex, "First show stage: input shape failed"); }
        }

        private ShowSize BodySize()
        {
            var body = _emi.BodyScreenRect;
            return new ShowSize(body.Width / _scale, body.Height / _scale);
        }

        private RectD SafeBounds()
        {
            global::Avalonia.Platform.Screen? screen = null;
            try
            {
                screen = _guiding && _main != null ? Screens?.ScreenFromWindow(_main) : Screens?.Primary;
            }
            catch (Exception ex) { Log.Debug(ex, "First show stage: no screen"); }
            if (screen == null || !_opened) return new RectD(16, 16, Math.Max(1, Width - 32), Math.Max(1, Height - 32));
            var area = screen.WorkingArea;
            double ax = (area.X - Position.X) / _scale, ay = (area.Y - Position.Y) / _scale;
            double bx = (area.Right - Position.X) / _scale, by = (area.Bottom - Position.Y) / _scale;
            return new RectD(ax + 16, ay + 16, Math.Max(1, bx - ax - 32), Math.Max(1, by - ay - 32));
        }

        private ShowSize SpeechSize()
        {
            var bounds = SafeBounds();
            _bubble.Width = Math.Min(390, bounds.Width);
            _bubble.MaxHeight = bounds.Height;
            // Normal lines need no scrolling chrome. Add it only when the content exceeds the monitor.
            _bubbleContent.Measure(new Size(Math.Max(1, _bubble.Width - 38), double.PositiveInfinity));
            bool overflow = _bubbleContent.DesiredSize.Height + 38 > _bubble.MaxHeight;
            if (overflow && _bubble.Child != _speechScroll)
            {
                _bubble.Child = null; _speechScroll.Content = _bubbleContent; _bubble.Child = _speechScroll;
            }
            else if (!overflow && _bubble.Child == _speechScroll)
            {
                _speechScroll.Content = null; _bubble.Child = _bubbleContent;
            }
            _bubble.Measure(new Size(_bubble.Width, _bubble.MaxHeight));
            return new ShowSize(_bubble.Width, Math.Max(1, _bubble.DesiredSize.Height));
        }

        private RectD LogoBounds()
        {
            _logo.Width = Math.Min(460, Math.Max(160, Width - 2 * (BodySize().Width + 48)));
            _logo.Measure(new Size(_logo.Width, double.PositiveInfinity));
            return new RectD(Width / 2 - _logo.Width / 2, Height * .43 - _logo.DesiredSize.Height / 2, _logo.Width, _logo.DesiredSize.Height);
        }

        private void Place()
        {
            var logo = LogoBounds();
            Canvas.SetLeft(_logo, logo.Left); Canvas.SetTop(_logo, logo.Top);
            if (!_opened || !_greeted) return;
            if (_moving || Clock() - _settledAt < .16)
            {
                if (_speechWindow.IsVisible) _speechWindow.Hide();
                return;
            }
            var size = BodySize(); var speech = SpeechSize(); var bounds = SafeBounds();
            var body = new RectD(_x - size.Width / 2, _y - size.Height / 2, size.Width, size.Height);
            var avoid = _guiding ? TargetRect : _logo.Opacity > 0 ? logo : FirstShowLayout.Empty;
            var rect = FirstShowLayout.Speech(bounds, speech, body, avoid, new Vec2(body.Right + 18, body.Top));
            if (_speechPending)
            {
                _speechPending = false; _spokeAt = Clock();
                _audio.Speak(_pendingSpeech, _serious ? "idle" : "celebration");
            }
            // Own the speech above Emi, not under her or the application's native child surfaces.
            var pixel = ToScreen(rect.Left, rect.Top);
            _speechWindow.Position = new PixelPoint((int)Math.Round(pixel.X), (int)Math.Round(pixel.Y));
            _speechWindow.Width = rect.Width; _speechWindow.Height = rect.Height;
            if (!_speechWindow.IsVisible)
            {
                try { if (_emi.IsVisible) _speechWindow.Show(_emi); else _speechWindow.Show(); }
                catch (Exception ex) { Log.Debug(ex, "First show speech window failed to show"); }
            }
        }

        private void ReleaseFiles() { foreach (var path in _files) Release(path); _files.Clear(); }

        private void Cleanup()
        {
            if (_closed) return;
            ClearGuideTarget();
            _closed = true;
            if (OperatingSystem.IsWindows()) Win32PanicKey.KeyDown -= OnGlobalKey;
            try { _speechWindow.Close(); } catch (Exception ex) { Log.Debug(ex, "First show speech window close failed"); }
            _clock.Stop();
            _load?.Cancel(); _load?.Dispose(); _load = null;
            _effects?.Dispose(); _effects = null; _audio.Dispose(); ReleaseFiles();
        }
    }
}
