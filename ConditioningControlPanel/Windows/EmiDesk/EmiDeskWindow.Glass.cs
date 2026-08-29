using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using ConditioningControlPanel.Services.EmiDesk;
using Serilog;

// NAMESPACE TRAP: see the note in EmiDeskWindow.Fx.cs. Flat ConditioningControlPanel, always.
namespace ConditioningControlPanel;

/// <summary>
/// THE GLASS: what she puts on her own little screen when nobody has touched her for a while, and
/// what a tap on it does.
///
/// The whole feature is one offer with no words in it. After 90 s of real idle her glass glitches
/// over to a channel for 10 s and then glitches back. Tap it and the thing on the screen happens.
/// Do not, and she never mentions it: the decline line is wordless by design (BRIEF 6), because the
/// failure mode here is an ad break, and an ad break that also nags is unforgivable.
///
/// LOCAL ASSETS ONLY. Every channel reads the user's own assets folder. Nothing in this file, or in
/// <see cref="EmiChannels"/>, fetches anything; the app-wide remote-media consent is not consulted
/// because there is nothing here that would need it, and the day something does, it must be gated
/// on <c>AppSettings.HasRemoteMediaConsent</c> being ALREADY true, never on asking for it.
///
/// The face keeps painting underneath the whole time. Killing a channel is hiding one node.
/// </summary>
public partial class EmiDeskWindow
{
    private const int IdlePollMs = 1000;
    private const int FrameMs = 33;          // ~30 fps, which is what the pitch demo ran at
    private const int GlitchFrameMs = 55;    // four torn frames inside EmiChannels.GlitchMs

    private DispatcherTimer? _idleWatch;
    private DispatcherTimer? _frames;
    private DispatcherTimer? _glitch;
    private DispatcherTimer? _channelLife;

    private Canvas? _glassLayer;
    private Canvas? _glitchLayer;
    private EmiChannelPainter? _painter;
    private string? _channel;

    private DateTime _lastActivityUtc = DateTime.UtcNow;
    private DateTime _channelUpUtc;
    private bool _glassLive;
    private bool _channelTapped;
    private bool _emiOwnsTheVideo;
    private bool _glassHooked;

    // ---------------------------------------------------------------- seams

    /// <summary>Start the idle watch as soon as the widget exists. One timer, always running.</summary>
    partial void OnReadyCore()
    {
        try
        {
            HookGlass();
            StartIdleWatch();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[EmiDesk] glass start failed");
        }
    }

    /// <summary>True while a channel (or its glitch) is on the glass.</summary>
    partial void OnGlassLiveQuery(ref bool live)
    {
        if (_glassLive) live = true;
    }

    /// <summary>
    /// A tap that landed inside the glass rect while a channel was up. This is the entire offer:
    /// the thing she is showing you is the thing that happens.
    /// </summary>
    partial void OnGlassClickedCore(ref bool handled)
    {
        try
        {
            if (!_glassLive || _channel == null || _painter == null) return;
            handled = true;
            _channelTapped = true;

            string id = _channel;
            string? payload = _painter.Payload;
            Log.Information("[EmiDesk] glass tapped: {Channel}", id);

            CloseChannel(declined: false);

            // effectFired (with the channel) is raised by EmiChannels/EmiOffers, so there is one
            // place a fired effect is announced no matter which door it came through.
            EmiChannels.Fire(id, payload);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[EmiDesk] glass tap failed");
        }
    }

    /// <summary>
    /// She is going away. Take the glass and anything she was saying with her.
    ///
    /// <para>This is NOT the <c>OnTearDownCore</c> seam any more. Two chunks landed a body for that
    /// one partial method (the ring folds its sibling window, the glass kills its channel), and a
    /// partial method takes exactly one implementer. The ring's file owns the seam and calls this,
    /// ring first, because a fan left hanging after she has poofed reads as a crash. Idempotent by
    /// contract: it can be called with nothing open.</para>
    /// </summary>
    internal void TearDownGlass()
    {
        try
        {
            StopIdleWatch();
            CloseChannel(declined: false, silent: true);
            TearDownBubble();
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] tear-down failed");
        }
    }

    // ---------------------------------------------------------------- the idle watch

    private void HookGlass()
    {
        if (_glassHooked) return;
        _glassHooked = true;

        PointerActivity += (_, __) => NoteGlassActivity();
        Moved += (_, __) => NoteGlassActivity();

        try
        {
            var svc = App.EmiDesk;
            if (svc != null) svc.MomentFired += OnMomentForGlass;
        }
        catch (Exception ex) { Log.Debug(ex, "[EmiDesk] glass moment hook failed"); }

        try
        {
            var video = App.Video;
            if (video != null) video.VideoEnded += OnAppVideoEnded;
        }
        catch (Exception ex) { Log.Debug(ex, "[EmiDesk] video-ended hook failed"); }
    }

    /// <summary>
    /// Anything the user did resets the clock and kills a live channel. A channel is only ever
    /// allowed on an untouched desk: the moment they come back, the glass is her face again.
    /// </summary>
    private void NoteGlassActivity()
    {
        _lastActivityUtc = DateTime.UtcNow;
        if (_glassLive) CloseChannel(declined: true);
    }

    private void StartIdleWatch()
    {
        StopIdleWatch();
        _idleWatch = new DispatcherTimer(DispatcherPriority.Background, Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(IdlePollMs)
        };
        _idleWatch.Tick += OnIdleWatchTick;
        _idleWatch.Start();
    }

    private void StopIdleWatch()
    {
        if (_idleWatch == null) return;
        try { _idleWatch.Stop(); _idleWatch.Tick -= OnIdleWatchTick; } catch { /* already dead */ }
        _idleWatch = null;
    }

    private void OnIdleWatchTick(object? sender, EventArgs e)
    {
        try
        {
            if (Application.Current?.Dispatcher == null) return;
            if (Application.Current.Dispatcher.HasShutdownStarted) return;
            if (_glassLive) return;
            if (!GlassMayFlip()) return;
            if ((DateTime.UtcNow - _lastActivityUtc) < EmiChannels.IdleBeforeFlip) return;
            BeginFlip();
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] idle watch tick failed");
        }
    }

    /// <summary>
    /// Every reason she must NOT wander off to a channel. All of them are "something is already
    /// happening": the glass is the quietest thing she does and it never goes first.
    /// </summary>
    private bool GlassMayFlip()
    {
        try
        {
            if (_closingForGood || Visibility != Visibility.Visible) return false;
            if (InputLocked || Transiting || ChainLive) return false;
            if (AskLive) return false;
            if (_bubble != null && _bubble.Visibility == Visibility.Visible) return false;

            // The user's own switch, and their own bedtime.
            if (App.Settings?.Current?.EmiDeskGlass != true) return false;
            if (EmiLineEngine.BedtimeSet) return false;

            // A hold is the silence law running (the avatar has the voice, panic was pressed). She
            // does not get to put on a show in the middle of one.
            if (EmiLineEngine.Instance.HoldActive) return false;

            // The ring is the loudest "mid-thought" signal there is. Chunk B2 answers; with no ring
            // wired the seam leaves it false, which is the right default.
            bool ringOpen = false;
            try { OnRingOpenQuery(ref ringOpen); }
            catch (Exception ex) { Log.Debug(ex, "[EmiDesk] ring-open seam threw"); }
            if (ringOpen) return false;

            // Something is already on the screen.
            if (App.Video?.IsPlaying == true) return false;

            return EmiChannels.Pick() != null;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] glass gate probe failed");
            return false;
        }
    }

    // ---------------------------------------------------------------- the flip

    private void BeginFlip()
    {
        string? id = EmiChannels.Pick();
        if (id == null) return;

        var rect = GlassRect;
        var painter = EmiChannels.Build(id, rect.Width, rect.Height);
        if (painter == null)
        {
            Log.Debug("[EmiDesk] channel {Channel} had nothing to draw, skipped", id);
            return;
        }

        _channel = id;
        _painter = painter;
        _channelTapped = false;
        _glassLive = true;
        StopIdleBeats();

        EnsureGlassLayers();
        if (_glitchLayer == null || _glassLayer == null) { CloseChannel(false, silent: true); return; }

        _glitchLayer.Children.Clear();
        _glitchLayer.Visibility = Visibility.Visible;
        _glassLayer.Visibility = Visibility.Collapsed;

        PaintGlitchFrame();
        int frames = 0;
        _glitch = new DispatcherTimer(DispatcherPriority.Background, Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(GlitchFrameMs)
        };
        _glitch.Tick += (_, __) =>
        {
            try
            {
                frames++;
                if (frames * GlitchFrameMs < EmiChannels.GlitchMs) { PaintGlitchFrame(); return; }
                StopTimer(ref _glitch);
                LandChannel();
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "[EmiDesk] glitch frame failed");
                CloseChannel(false, silent: true);
            }
        };
        _glitch.Start();
    }

    /// <summary>
    /// One torn frame: a handful of navy and pink bands at random heights, the whole stack shoved
    /// sideways. Four of these in 220 ms reads as a channel change on a dying CRT, which is what
    /// the pitch demo did with a canvas and two lines of putImageData.
    /// </summary>
    private void PaintGlitchFrame()
    {
        if (_glitchLayer == null) return;
        var rect = GlassRect;
        _glitchLayer.Children.Clear();

        _glitchLayer.Children.Add(new Rectangle
        {
            Width = rect.Width,
            Height = rect.Height,
            Fill = EmiChannels.ScreenBrush,
            IsHitTestVisible = false
        });

        int bands = 3 + Rng.Next(3);
        for (int i = 0; i < bands; i++)
        {
            double h = Math.Max(1, rect.Height * (0.03 + Rng.NextDouble() * 0.10));
            double y = Rng.NextDouble() * Math.Max(1, rect.Height - h);
            double dx = (Rng.NextDouble() - 0.5) * rect.Width * 0.5;
            var band = new Rectangle
            {
                Width = rect.Width,
                Height = h,
                Fill = EmiChannels.PinkBrush,
                Opacity = 0.18 + Rng.NextDouble() * 0.5,
                IsHitTestVisible = false
            };
            Canvas.SetLeft(band, dx);
            Canvas.SetTop(band, y);
            _glitchLayer.Children.Add(band);
        }
    }

    /// <summary>The tear settles and the channel is on the glass. This is the offer.</summary>
    private void LandChannel()
    {
        if (_painter == null || _glassLayer == null || _glitchLayer == null)
        {
            CloseChannel(false, silent: true);
            return;
        }

        _glitchLayer.Visibility = Visibility.Collapsed;
        _glitchLayer.Children.Clear();

        _glassLayer.Children.Clear();
        _painter.Attach(_glassLayer);
        _glassLayer.Visibility = Visibility.Visible;

        _channelUpUtc = DateTime.UtcNow;
        _painter.Tick(0);

        _frames = new DispatcherTimer(DispatcherPriority.Background, Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(FrameMs)
        };
        _frames.Tick += OnChannelFrame;
        _frames.Start();

        _channelLife = NewTimer((int)EmiChannels.ChannelLife.TotalMilliseconds,
            () => CloseChannel(declined: true));

        Log.Information("[EmiDesk] glass channel up: {Channel}", _channel);
        App.EmiDesk?.Fire("glassOffer", new { channel = _channel });
    }

    private void OnChannelFrame(object? sender, EventArgs e)
    {
        try
        {
            if (Application.Current?.Dispatcher == null) return;
            if (Application.Current.Dispatcher.HasShutdownStarted) return;
            _painter?.Tick((DateTime.UtcNow - _channelUpUtc).TotalMilliseconds);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] channel frame failed");
            CloseChannel(false, silent: true);
        }
    }

    /// <summary>
    /// Take the channel down. <paramref name="declined"/> means it was shown and not taken, which
    /// is the one thing she is allowed to react to, and only with a face.
    /// </summary>
    private void CloseChannel(bool declined, bool silent = false)
    {
        bool wasLive = _glassLive;
        string? id = _channel;

        _glassLive = false;
        _channel = null;
        _painter = null;

        StopTimer(ref _frames, OnChannelFrame);
        StopTimer(ref _glitch);
        StopTimer(ref _channelLife);

        try
        {
            if (_glassLayer != null) { _glassLayer.Children.Clear(); _glassLayer.Visibility = Visibility.Collapsed; }
            if (_glitchLayer != null) { _glitchLayer.Children.Clear(); _glitchLayer.Visibility = Visibility.Collapsed; }
        }
        catch (Exception ex) { Log.Debug(ex, "[EmiDesk] glass clear failed"); }

        _lastActivityUtc = DateTime.UtcNow;

        if (!wasLive || silent) return;
        if (!_closingForGood && Visibility == Visibility.Visible && !ChainLive && !AskLive)
        {
            DrawFace(EmiChains.RestFace);
            RestartIdleBeats();
        }

        if (declined && !_channelTapped)
        {
            // Wordless on purpose. effectDeclined is a HOLD row in the moments table, so the engine
            // hands back a face and no bubble, and she never mentions the thing you ignored.
            App.EmiDesk?.Fire("effectDeclined", new { channel = id });
        }
    }

    private void EnsureGlassLayers()
    {
        var rect = GlassRect;
        if (_glassLayer == null)
        {
            _glassLayer = new Canvas { ClipToBounds = true, IsHitTestVisible = false, Visibility = Visibility.Collapsed };
            _glitchLayer = new Canvas { ClipToBounds = true, IsHitTestVisible = false, Visibility = Visibility.Collapsed };
            GlassHost.Children.Add(_glassLayer);
            GlassHost.Children.Add(_glitchLayer);
        }
        foreach (var c in new[] { _glassLayer, _glitchLayer })
        {
            if (c == null) continue;
            c.Width = rect.Width;
            c.Height = rect.Height;
        }
    }

    // ---------------------------------------------------------------- video bookkeeping

    /// <summary>
    /// She only gets to talk about a video SHE started. The flag is armed off the videoRunning
    /// moment rather than off the tap, so it covers both doors into a video (the glass channel and
    /// an offer's chip) without either of them knowing about this file.
    /// </summary>
    private void OnMomentForGlass(object? sender, EmiMoment m)
    {
        try
        {
            if (m == null) return;
            if (string.Equals(m.Id, "videoRunning", StringComparison.Ordinal)) _emiOwnsTheVideo = true;
            else if (string.Equals(m.Id, "videoEnded", StringComparison.Ordinal)) _emiOwnsTheVideo = false;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] glass moment handler failed");
        }
    }

    private void OnAppVideoEnded(object? sender, EventArgs e)
    {
        try
        {
            if (!_emiOwnsTheVideo) return;
            _emiOwnsTheVideo = false;
            var disp = Application.Current?.Dispatcher;
            if (disp == null || disp.HasShutdownStarted) return;
            if (!disp.CheckAccess()) { disp.BeginInvoke(new Action(() => App.EmiDesk?.Fire("videoEnded"))); return; }
            App.EmiDesk?.Fire("videoEnded");
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] videoEnded relay failed");
        }
    }

    // ---------------------------------------------------------------- plumbing

    private static void StopTimer(ref DispatcherTimer? t)
    {
        if (t == null) return;
        try { t.Stop(); } catch { /* already dead */ }
        t = null;
    }

    private static void StopTimer(ref DispatcherTimer? t, EventHandler handler)
    {
        if (t == null) return;
        try { t.Stop(); t.Tick -= handler; } catch { /* already dead */ }
        t = null;
    }
}
