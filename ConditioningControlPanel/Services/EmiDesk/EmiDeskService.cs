using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using ConditioningControlPanel.Localization;
using Serilog;

namespace ConditioningControlPanel.Services.EmiDesk;

/// <summary>
/// A moment: the app telling EMI something happened. Chunk B3 turns these into lines; chunk B1
/// only fires them, so the wiring exists before there is anything to say.
/// </summary>
/// <param name="Id">The moment id, e.g. <c>sessionEnd</c>. Ids are the vocabulary the line pools key off.</param>
/// <param name="Context">Optional payload (an XP number, a level, a target id). May be null.</param>
public sealed record EmiMoment(string Id, object? Context);

/// <summary>
/// EMI Desk's facade: <c>App.EmiDesk</c>. Owns the widget window's lifetime, the summon hotkey,
/// the moment bus and the avatar-mute arbitration.
///
/// She is SUMMONED, not always on. Everything here is built so that "she is not out" is the cheap
/// path: no window until the first summon, no timers while she is away, and every gate the rest of
/// the app asks (<see cref="AvatarMuted"/>) short-circuits on <see cref="IsOut"/>.
/// </summary>
public sealed class EmiDeskService : IDisposable
{
    /// <summary>The default summon chord. A chord is required; bare keys are refused.</summary>
    public const string DefaultHotkey = "Ctrl+Alt+E";

    private EmiDeskWindow? _window;
    private bool _muteAccepted;
    private bool _mutePromptShownThisSession;
    private bool _hotkeyArmed;
    private bool _disposed;

    /// <summary>
    /// Bumped by every summon and every dismiss. It exists because <see cref="Summon"/> can BLOCK
    /// half way through, inside <c>EmiMutePromptWindow.ShowDialog</c>, and a modal runs a NESTED
    /// message pump: the chord, the dock chip and the tray all still fire while it is up. A summon
    /// that comes back from that dialog holding a stale generation has been overtaken and must not
    /// finish. See the trap note in <see cref="Summon"/>.
    /// </summary>
    private long _summonGen;

    // ---------------------------------------------------------------- state

    /// <summary>True while she is on screen (including her intro and outro).</summary>
    public bool IsOut { get; private set; }

    /// <summary>The widget window, or null before her first summon. Chunk B2 / B3 reach her through this.</summary>
    public EmiDeskWindow? Window => _window;

    /// <summary>Raised whenever <see cref="IsOut"/> changes. The dock chip listens.</summary>
    public event EventHandler<bool>? OutChanged;

    /// <summary>
    /// Raised for every <see cref="Fire"/>. Chunk B3 subscribes and decides whether the moment is
    /// worth a line. B1 raises and forgets: no queue, no backlog, nothing to drain.
    /// </summary>
    public event EventHandler<EmiMoment>? MomentFired;

    /// <summary>Raised for every <see cref="NoteOpen"/>. Chunk B2's suggester listens.</summary>
    public event EventHandler<string>? TargetOpened;

    /// <summary>
    /// Raised when the avatar tube was about to speak. Fires whether or not the mute swallowed it,
    /// so chunk B3 can keep EMI from talking over her.
    /// </summary>
    public event EventHandler? AvatarSpeaking;

    // ---------------------------------------------------------------- mute arbitration

    /// <summary>
    /// THE gate the avatar's speech paths ask. True only while EMI is actually out, the user has
    /// the setting on, AND the user agreed (or said "do not ask", which counts as agreeing from
    /// then on). Two voices at once is the failure mode this whole feature exists to avoid, and a
    /// mute the user never chose is the second one, so both halves are required.
    /// </summary>
    public bool AvatarMuted
    {
        get
        {
            try
            {
                if (!IsOut) return false;
                var s = App.Settings?.Current;
                if (s == null || !s.EmiDeskMuteAvatar) return false;
                return _muteAccepted;
            }
            catch { return false; }
        }
    }

    /// <summary>True while the avatar tube has a bubble on screen. Chunk B3 waits rather than overlapping.</summary>
    public bool TubeBubbleLive
    {
        get
        {
            try { return App.AvatarWindow?.HasBubbleUp == true; }
            catch { return false; }
        }
    }

    /// <summary>
    /// True while the companion voice channel is actually playing a clip. Separate from
    /// <see cref="TubeBubbleLive"/> because a voiced line can outlast its bubble, or have none at
    /// all, and a hold that ends mid-sentence is worse than no hold.
    /// </summary>
    public bool TubeSpeakingAudio
    {
        get
        {
            try { return App.AvatarWindow?.IsSpeakingAudio == true; }
            catch { return false; }
        }
    }

    /// <summary>
    /// Called from the tube's speech chokepoints the instant before a bubble goes up, muted or not.
    /// Keep it cheap: this runs on every giggle.
    /// </summary>
    public void NoteAvatarSpeaking()
    {
        try { AvatarSpeaking?.Invoke(this, EventArgs.Empty); }
        catch (Exception ex) { Log.Debug(ex, "[EmiDesk] AvatarSpeaking handler threw"); }

        // THE AVATAR OWNS VOICE (BRIEF 4, MOMENTS 3.5). The tube is about to talk, so EMI holds to
        // blink-only faces until its bubble comes down, plus the moment table's 20 s tail. The hold
        // is armed even when she is muted: the mute is about the tube's audio, not about who is
        // allowed to interrupt whom.
        try { ArmVoiceHold("avatarSpeaking"); }
        catch (Exception ex) { Log.Debug(ex, "[EmiDesk] avatar hold arm failed"); }
    }

    /// <summary>
    /// The awareness arbiter has decided the tube speaks about the active window (MOMENTS 1.x).
    /// Same law as <see cref="NoteAvatarSpeaking"/> and the same release, under its own moment id
    /// so the 20 s tail is the awareness moment's own.
    /// </summary>
    public void NoteAwarenessReaction()
    {
        try { ArmVoiceHold("awarenessReaction"); }
        catch (Exception ex) { Log.Debug(ex, "[EmiDesk] awareness hold arm failed"); }
    }

    private System.Windows.Threading.DispatcherTimer? _avatarHoldTimer;

    /// <summary>Which voice holds this service has armed and still owes a release.</summary>
    private readonly HashSet<string> _voiceHolds = new(StringComparer.Ordinal);

    /// <summary>
    /// Hold her while the tube has a bubble up, and release it the moment the bubble goes away.
    /// A one-second poll, not an event: the tube exposes a state (<see cref="TubeBubbleLive"/>) and
    /// no "stopped speaking" signal, and a poll that only runs while a hold is live costs nothing.
    /// </summary>
    private void ArmVoiceHold(string momentId)
    {
        var disp = Application.Current?.Dispatcher;
        if (disp == null || disp.HasShutdownStarted) return;
        if (!disp.CheckAccess()) { disp.BeginInvoke(new Action(() => ArmVoiceHold(momentId))); return; }

        if (_voiceHolds.Add(momentId))
        {
            Fire(momentId);
        }

        if (_avatarHoldTimer != null) return;
        _avatarHoldTimer = new System.Windows.Threading.DispatcherTimer(
            System.Windows.Threading.DispatcherPriority.Background, disp)
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _avatarHoldTimer.Tick += OnAvatarHoldTick;
        _avatarHoldTimer.Start();
    }

    private void OnAvatarHoldTick(object? sender, EventArgs e)
    {
        try
        {
            if (Application.Current?.Dispatcher == null) return;
            if (Application.Current.Dispatcher.HasShutdownStarted) return;
            if (TubeBubbleLive || TubeSpeakingAudio) return;

            if (_avatarHoldTimer != null)
            {
                _avatarHoldTimer.Stop();
                _avatarHoldTimer.Tick -= OnAvatarHoldTick;
                _avatarHoldTimer = null;
            }
            if (_voiceHolds.Count == 0) return;
            foreach (var id in _voiceHolds) EmiLineEngine.Instance.ReleaseHold(id);
            _voiceHolds.Clear();
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] avatar hold tick failed");
        }
    }

    /// <summary>
    /// The situational half of the offer gates (LINES-SCHEMA 5.6): the ones only the app can see.
    /// The engine owns the cadence (10 minutes, the third summon, the ignore streak, bedtime) and
    /// asks this for the rest, so no chunk has to know both halves.
    /// </summary>
    internal bool AskSituationOk()
    {
        try
        {
            if (!IsOut) return false;
            var win = _window;
            if (win == null || win.Visibility != Visibility.Visible) return false;
            if (win.InputLocked || win.Transiting) return false;
            if (win.AskLive) return false;

            if (App.Video?.IsPlaying == true) return false;
            if (SessionEngine.Active?.IsRunning == true) return false;
            if (TubeBubbleLive) return false;

            var main = Application.Current?.MainWindow;
            if (main != null && main.WindowState == WindowState.Minimized) return false;

            return true;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] ask situation probe failed");
            return false;
        }
    }

    // ---------------------------------------------------------------- summon / dismiss

    /// <summary>Summon her if she is away, send her away if she is out.</summary>
    public void Toggle()
    {
        // A widget that is ON SCREEN always wins over the flag. If the two ever disagree, the chord
        // has to be the way OUT of that state and never a second summon stacked on top of her -
        // which is exactly what the mute-prompt re-entry used to produce (QA 2026-08-29).
        if (IsOut || WindowOnScreen) Dismiss();
        else Summon();
    }

    /// <summary>
    /// Her window is realised and visible, whatever <see cref="IsOut"/> currently claims. The two
    /// should never disagree; this is the check that makes a disagreement recoverable rather than
    /// permanent.
    /// </summary>
    private bool WindowOnScreen
    {
        get
        {
            try { return _window != null && _window.Visibility == Visibility.Visible; }
            catch { return false; }
        }
    }

    /// <summary>
    /// Bring her out. Safe to call when she is already out (no-op) and when the feature is off
    /// (logged no-op). Builds the window on first use.
    /// </summary>
    public void Summon(string? why = null)
    {
        try
        {
            if (_disposed) return;
            var s = App.Settings?.Current;
            if (s != null && !s.EmiDeskEnabled)
            {
                Log.Debug("[EmiDesk] summon ignored: EmiDeskEnabled is off");
                return;
            }
            var disp = Application.Current?.Dispatcher;
            if (disp == null || disp.HasShutdownStarted) return;
            if (!disp.CheckAccess())
            {
                disp.BeginInvoke(new Action(() => Summon(why)));
                return;
            }
            if (IsOut) return;

            var win = EnsureWindow();
            if (win == null) return;

            IsOut = true;
            long gen = ++_summonGen;

            // THE MODAL RE-ENTRY TRAP (found on the first live run, QA 2026-08-29).
            // MaybeAskAboutMuting can block in EmiMutePromptWindow.ShowDialog, and a modal runs a
            // NESTED message pump: the summon chord, the dock chip and the tray menu all keep
            // working while the prompt is up. A dismiss inside that window set IsOut=false and hid
            // her, then this method carried straight on and Show()ed her again - she came back on
            // screen with IsOut=false. That STRANDS her: Dismiss(), the hover x and AvatarMuted all
            // guard on IsOut, so the x did nothing, the chord re-summoned instead of dismissing,
            // and the avatar talked over her for the rest of the session. If anything overtook us
            // while that pump was running, this summon is stale and stops here.
            MaybeAskAboutMuting();
            if (_disposed) return;
            if (_summonGen != gen || !IsOut)
            {
                Log.Information("[EmiDesk] summon abandoned: she was sent away while the mute prompt was up");
                return;
            }

            win.RestorePlacement();
            win.Show();
            win.RunSummon();

            var st = EmiState.Current;
            bool first = !st.FirstBootSeen;
            int summons = EmiState.NoteSummon();

            RaiseOutChanged();
            Log.Information("[EmiDesk] summoned ({Why}), firstBoot={First}, summon #{N}",
                why ?? "user", first, summons);

            // THE NUDGE MACHINE (wave 3). Armed per summon, and it is the summon instant that
            // every "how long since" inside it is measured from.
            StartNudges(summons);

            // Her greeting rides AFTER the wake chain, not on top of it: RunSummon plays the CRT
            // power-on and then `wake`, and a bubble fired here would land while she is still a
            // flat line. The delay is the summon FX budget (BRIEF 3, ~1 s) plus the wake chain.
            _summonMoment = first ? "desktopFirstBoot" : "summoned";
            _summonVia = string.Equals(why, "hotkey", StringComparison.OrdinalIgnoreCase) ? "hotkey" : "rail";
            ScheduleSummonMoment();

            // She was sent away and called straight back (MOMENTS 1.x). Fired here rather than
            // folded into the greeting: it is a different beat and the engine's floor decides which
            // of the two actually lands.
            try
            {
                if (_lastDismissUtc != DateTime.MinValue)
                {
                    var gone = DateTime.UtcNow - _lastDismissUtc;
                    if (gone.TotalMinutes <= 5)
                        Fire("backSoon", new { minutes = Math.Max(0, (int)gone.TotalMinutes) });
                }
            }
            catch (Exception ex) { Log.Debug(ex, "[EmiDesk] backSoon probe failed"); }

            // A bedtime is a promise she made about tonight, and being summoned through it is the
            // user breaking it, not her. {n} is how many times, so the line can escalate honestly.
            try
            {
                if (EmiLineEngine.BedtimeSet)
                {
                    _bedtimeSkips++;
                    Fire("bedtimeBroken", new { n = _bedtimeSkips });
                }
                else
                {
                    _bedtimeSkips = 0;
                }
            }
            catch (Exception ex) { Log.Debug(ex, "[EmiDesk] bedtimeBroken probe failed"); }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[EmiDesk] Summon failed");
        }
    }

    /// <summary>Send her away. Safe to call when she is not out.</summary>
    public void Dismiss()
    {
        try
        {
            if (_disposed) return;
            var disp = Application.Current?.Dispatcher;
            if (disp == null || disp.HasShutdownStarted) return;
            if (!disp.CheckAccess())
            {
                disp.BeginInvoke(new Action(Dismiss));
                return;
            }
            // Invalidate any summon parked in a nested message pump before anything else: without
            // this, the mute prompt returns and puts her straight back on screen behind us.
            _summonGen++;

            if (_window == null) return;
            if (!IsOut && !WindowOnScreen) return;

            if (!IsOut)
            {
                // She is on screen with the flag down, so nothing else in the app believes she is
                // out. Put the two back in agreement and take her off through the normal outro,
                // rather than leaving a widget that nobody can dismiss.
                Log.Warning("[EmiDesk] dismissing a desynced widget: visible while IsOut was false");
                IsOut = true;
            }

            CancelSummonMoment();
            // `dismissed` is a LOCKED silent moment: the wink chain and the CRT power-off are the
            // whole goodbye. It still fires so the hooks and the counters see it, and Fire() drops
            // it before it can reach a pool (see NeverSpeaks).
            Fire("dismissed", new { minutes = MinutesOut() });
            _lastDismissUtc = DateTime.UtcNow;
            StopNudges();
            _window.RunDismiss(() =>
            {
                IsOut = false;
                RaiseOutChanged();
                Log.Information("[EmiDesk] dismissed");
            });
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[EmiDesk] Dismiss failed");
            IsOut = false;
            RaiseOutChanged();
        }
    }

    // ---------------------------------------------------------------- the arcademy farewell

    /// <summary>
    /// The pool id for the goodbye she says when the Arcademy takes the screen.
    ///
    /// <para>It is a CONST and not a literal at the call site on purpose. The typo guard in
    /// <c>EmiMomentIdWiringTests</c> scans every <c>Fire("...")</c> literal in the tree and fails
    /// when the id is not in <c>desk-lines.json</c>; the pool is authored separately from this
    /// code, so a literal here would red the suite for as long as the two halves are out of step.
    /// Drawing through the const keeps the guard honest about everything else while
    /// <c>EmiDeskFarewellTests</c> pins this one id and its fallback instead.</para>
    /// </summary>
    public const string ArcademyByeMoment = "arcademyBye";

    /// <summary>Her line when the pool has not landed yet. Owner text, BRIEF wave 2.</summary>
    public const string ArcademyByeFallbackText = "bye bye. see you at school.";

    /// <summary>Her face on the fallback line.</summary>
    public const string ArcademyByeFallbackFace = "^_~";

    /// <summary>
    /// How long after the farewell she leaves. <c>MakeSay</c>'s dot cadence is LOCKED at
    /// 420 + 420 + 520 ms, so the words themselves do not land until ~1360 ms; the owner's "about
    /// 1.6 s to read it" is 1.6 s AFTER that, not after the fire.
    /// </summary>
    public const int ArcademyByeDismissMs = 1360 + 1600;

    /// <summary>A little air past the outro, so a late moment cannot chase her off screen.</summary>
    public const int ArcademyByeSuppressMs = ArcademyByeDismissMs + 250;

    /// <summary>Nothing but the locked-silent moments may speak until this instant.</summary>
    private DateTime _farewellUntilUtc = DateTime.MinValue;

    private System.Windows.Threading.DispatcherTimer? _farewellTimer;

    /// <summary>
    /// The Arcademy is opening and she is out: say goodbye, then wink off by herself.
    ///
    /// <para>Called from every Arcademy launch path (they all funnel through
    /// <c>ArcademyHostService.Launch</c>), so the rail button, the ring card and the dashboard all
    /// get the same beat. A no-op when she is away, and safe to call twice: a second call inside
    /// the same farewell is dropped rather than restarting the timer.</para>
    /// </summary>
    public void FarewellForArcademy()
    {
        try
        {
            if (_disposed || !IsOut) return;
            var disp = Application.Current?.Dispatcher;
            if (disp == null || disp.HasShutdownStarted) return;
            if (!disp.CheckAccess())
            {
                disp.BeginInvoke(new Action(FarewellForArcademy));
                return;
            }
            if (DateTime.UtcNow < _farewellUntilUtc) return;

            var win = _window;
            if (win == null || win.Visibility != Visibility.Visible) return;

            // Claim the silence BEFORE the line, so `arcademyOpened` cannot slip in between.
            _farewellUntilUtc = DateTime.UtcNow.AddMilliseconds(ArcademyByeSuppressMs);
            try { MomentFired?.Invoke(this, new EmiMoment(ArcademyByeMoment, null)); }
            catch (Exception ex) { Log.Debug(ex, "[EmiDesk] arcademyBye handler threw"); }

            var line = DrawArcademyBye();
            win.SpeakLine(line);

            CancelFarewell();
            _farewellTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(ArcademyByeDismissMs)
            };
            _farewellTimer.Tick += (_, _) =>
            {
                CancelFarewell();
                try { Dismiss(); }
                catch (Exception ex) { Log.Debug(ex, "[EmiDesk] farewell dismiss failed"); }
            };
            _farewellTimer.Start();

            Log.Information("[EmiDesk] arcademy farewell: {Line}", line.Id);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[EmiDesk] FarewellForArcademy failed");
        }
    }

    /// <summary>
    /// Her goodbye, from the pool when it exists and from the hardcoded line when it does not. The
    /// engine may also refuse the draw for its own reasons (a limit, a cooldown), and a farewell
    /// that silently says nothing and then yanks her off screen would read as a crash, so ANY
    /// empty result falls through to the fallback.
    /// </summary>
    public static LineDraw DrawArcademyBye()
    {
        try
        {
            var drawn = EmiLineEngine.Instance.Draw(ArcademyByeMoment);
            if (drawn != null && !drawn.Hold && !string.IsNullOrWhiteSpace(drawn.Text)) return drawn;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] arcademyBye draw failed, using the fallback line");
        }

        return new LineDraw(
            ArcademyByeMoment + ".fallback", ArcademyByeMoment,
            ArcademyByeFallbackText, ArcademyByeFallbackFace,
            null, 3, false, 0);
    }

    private void CancelFarewell()
    {
        try
        {
            if (_farewellTimer == null) return;
            _farewellTimer.Stop();
            _farewellTimer = null;
        }
        catch (Exception ex) { Log.Debug(ex, "[EmiDesk] CancelFarewell failed"); }
    }

    private EmiDeskWindow? EnsureWindow()
    {
        try
        {
            if (_window != null) return _window;
            _window = new EmiDeskWindow();
            // Realise the HWND once, off screen and unlit, so SourceInitialized can apply
            // WS_EX_TOOLWINDOW before she is ever visible: applying it later makes her flash into
            // the taskbar for a frame.
            _window.Opacity = 0;
            _window.Show();
            _window.Hide();
            _window.Opacity = 1;
            _window.Closed += (_, _) =>
            {
                _window = null;
                IsOut = false;
                RaiseOutChanged();
            };
            return _window;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[EmiDesk] could not build the widget window");
            _window = null;
            return null;
        }
    }

    private void RaiseOutChanged()
    {
        try { OutChanged?.Invoke(this, IsOut); }
        catch (Exception ex) { Log.Debug(ex, "[EmiDesk] OutChanged handler threw"); }
    }

    // ---------------------------------------------------------------- moments

    /// <summary>
    /// Moments that fire, count and stamp their clocks but must NEVER produce a bubble. Owner
    /// locks, not tuning: <c>dismissed</c> is the wink and the power-off (BRIEF 3), and
    /// <c>appClosing</c> has no pool and never will (MOMENTS 3.8).
    /// </summary>
    private static readonly HashSet<string> NeverSpeaks =
        new(StringComparer.Ordinal) { "dismissed", "appClosing" };

    /// <summary>
    /// Tell EMI something happened. Cheap and safe from anywhere: a no-op while she is away, it
    /// never throws, and it never does the drawing on the caller's thread.
    ///
    /// The pipeline is LINES-SCHEMA 5 and it runs in ONE place so a fire cannot half-happen: the
    /// event goes out first (the ring's suggester and the dock listen whether or not she is out),
    /// then, only when she is actually on screen, the engine is asked for a line OR an offer and
    /// the winner is handed to the window.
    /// </summary>
    public void Fire(string momentId, object? ctx = null)
    {
        if (string.IsNullOrWhiteSpace(momentId)) return;
        try
        {
            Log.Debug("[EmiDesk] moment {Moment} (out={Out})", momentId, IsOut);
            MomentFired?.Invoke(this, new EmiMoment(momentId, ctx));
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] Fire({Moment}) handler threw", momentId);
        }

        // HOLDS ARE SAFETY, NOT DECORATION. A panic pressed, an attention check shown or a
        // lockdown counting down while she is away must still arm the silence, so that a summon
        // landing in the middle of one does not come with chatter. Everything else is skipped here:
        // Fire has to stay cheap enough to sit in the bark funnel.
        if (!IsOut)
        {
            try
            {
                if (!EmiLineEngine.Instance.IsHoldMoment(momentId)) return;
                EmiLineEngine.Instance.Draw(momentId, EmiLineEngine.ToCtx(ctx));
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "[EmiDesk] hold arm for {Moment} while away failed", momentId);
            }
            return;
        }

        try
        {
            var disp = Application.Current?.Dispatcher;
            if (disp == null || disp.HasShutdownStarted) return;
            if (!disp.CheckAccess())
            {
                disp.BeginInvoke(new Action(() => Speak(momentId, ctx)));
                return;
            }
            Speak(momentId, ctx);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] Fire({Moment}) failed", momentId);
        }
    }

    /// <summary>
    /// Let a <c>holdUntilReleased</c> hold go: the attention check resolved, intake closed, the
    /// emergency exit shut, the lockdown ended. The host services call this rather than reaching
    /// into the engine, and releasing a hold nobody is holding is a no-op, so an error path may
    /// call it freely.
    /// </summary>
    public void ReleaseHold(string momentId)
    {
        if (string.IsNullOrWhiteSpace(momentId)) return;
        try { EmiLineEngine.Instance.ReleaseHold(momentId); }
        catch (Exception ex) { Log.Debug(ex, "[EmiDesk] ReleaseHold({Moment}) failed", momentId); }
    }

    /// <summary>
    /// The speaking half of <see cref="Fire"/>, always on the dispatcher. Draws once, hands the
    /// result to the window, and lets the window acknowledge it only when it actually reached the
    /// screen: the engine must not burn a cooldown on a line nobody saw.
    /// </summary>
    private void Speak(string momentId, object? ctx)
    {
        try
        {
            if (!IsOut) return;
            var win = _window;
            if (win == null || win.Visibility != Visibility.Visible) return;

            // THE GOODBYE WINS. She has said her arcademy farewell and the outro is already
            // scheduled; anything landing inside that window would talk over her last line and
            // then be cut off mid-word by the power-off. `arcademyFromRing` is the one that would
            // do it every single time, because the ring fires it AFTER the target's Open() has
            // already reached ArcademyHostService and started the farewell.
            if (DateTime.UtcNow < _farewellUntilUtc && !NeverSpeaks.Contains(momentId))
            {
                Log.Debug("[EmiDesk] {Moment} dropped: she is saying goodbye", momentId);
                return;
            }

            var engine = EmiLineEngine.Instance;
            var dict = EmiLineEngine.ToCtx(ctx);

            var line = engine.Draw(momentId, dict);
            var ask = engine.DrawAsk(momentId, dict);

            if (ask != null)
            {
                if (NeverSpeaks.Contains(momentId)) return;
                win.ShowAsk(ask);
                return;
            }
            if (line == null) return;

            // A hold is a face, never a bubble, so it plays even on a locked-silent moment.
            if (line.Hold) { win.HoldFace(line); return; }

            if (NeverSpeaks.Contains(momentId))
            {
                Log.Debug("[EmiDesk] {Moment} drew {Line} but is locked silent, dropped", momentId, line.Id);
                return;
            }

            win.SpeakLine(line);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] Speak({Moment}) failed", momentId);
        }
    }

    /// <summary>
    /// Count an EMI line against the avatar's own min-gap. Only when she is NOT muting the avatar:
    /// while the avatar is muted there is no second voice to stagger against and stamping the gap
    /// would quietly shorten the avatar's next window for no reason (BRIEF 4).
    /// </summary>
    public void NoteEmiSpoke()
    {
        try
        {
            if (AvatarMuted) return;
            App.Bark?.NotifyExternalLineSpoken();
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] bark min-gap stamp failed");
        }
    }

    // ---------------------------------------------------------------- the summon greeting

    private System.Windows.Threading.DispatcherTimer? _summonTimer;
    private string? _summonMoment;
    private string _summonVia = "rail";
    private DateTime _outSinceUtc = DateTime.MinValue;

    /// <summary>When she was last sent away, for the "back already?" beat. MinValue = never.</summary>
    private DateTime _lastDismissUtc = DateTime.MinValue;

    /// <summary>How many times a set bedtime has been walked through tonight. Reset when there is
    /// no bedtime, so the count is always about the promise currently standing.</summary>
    private int _bedtimeSkips;

    /// <summary>The mute prompt's verdict, waiting for the greeting to go first.</summary>
    private string? _pendingMuteMoment;

    /// <summary>The CRT power-on plus the wake chain, plus a beat of air after it.</summary>
    private const int SummonGreetDelayMs = 2600;

    /// <summary>How long she has been out this time, in whole minutes. Never negative.</summary>
    private int MinutesOut()
    {
        if (_outSinceUtc == DateTime.MinValue) return 0;
        return Math.Max(0, (int)(DateTime.UtcNow - _outSinceUtc).TotalMinutes);
    }

    private void ScheduleSummonMoment()
    {
        try
        {
            _outSinceUtc = DateTime.UtcNow;
            CancelSummonMoment();
            var disp = Application.Current?.Dispatcher;
            if (disp == null || disp.HasShutdownStarted) return;

            _summonTimer = new System.Windows.Threading.DispatcherTimer(
                System.Windows.Threading.DispatcherPriority.Background, disp)
            {
                Interval = TimeSpan.FromMilliseconds(SummonGreetDelayMs)
            };
            _summonTimer.Tick += OnSummonGreetTick;
            _summonTimer.Start();
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] summon greeting schedule failed");
        }
    }

    private void OnSummonGreetTick(object? sender, EventArgs e)
    {
        try
        {
            CancelSummonMoment();
            if (Application.Current?.Dispatcher == null) return;
            if (Application.Current.Dispatcher.HasShutdownStarted) return;
            if (!IsOut) return;
            var moment = _summonMoment;
            _summonMoment = null;

            // The mute verdict rides here, behind the greeting: the answer to "should the avatar sit
            // out?" is only interesting once she has actually said hello. The engine's floor decides
            // which of the two lands - it will usually be the greeting, which is correct.
            var mute = _pendingMuteMoment;
            _pendingMuteMoment = null;
            if (!string.IsNullOrEmpty(mute)) Fire(mute!, null);

            if (string.IsNullOrEmpty(moment)) return;
            Fire(moment!, new { via = _summonVia, minutes = MinutesOut() });
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] summon greeting failed");
        }
    }

    private void CancelSummonMoment()
    {
        try
        {
            if (_summonTimer == null) return;
            _summonTimer.Stop();
            _summonTimer.Tick -= OnSummonGreetTick;
            _summonTimer = null;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] summon greeting cancel failed");
        }
    }

    // ---------------------------------------------------------------- the nudge machine

    /// <summary>
    /// The onboarding tracker. Pure logic in <see cref="EmiNudgeMachine"/>; this service owns only
    /// the clock that asks it and the path that speaks its answer.
    /// </summary>
    private readonly EmiNudgeMachine _nudges = new();

    private readonly EmiNudgeWorld _nudgeWorld = new();

    private System.Windows.Threading.DispatcherTimer? _nudgeTimer;

    /// <summary>
    /// How often the machine is asked. Five seconds is fine granularity against intervals measured
    /// in minutes, and the poll only exists while she is on screen.
    /// </summary>
    private const int NudgePollMs = 5_000;

    /// <summary>Her line when the pet pool has not landed yet.</summary>
    private const string PetNudgeFallbackText = "pat me. it's allowed.";

    /// <inheritdoc cref="PetNudgeFallbackText"/>
    private const string RingNudgeFallbackText = "my other side has stuff in it. the other button.";

    /// <inheritdoc cref="PetNudgeFallbackText"/>
    private const string PinNudgeFallbackText = "like one? squeeze it. the other button. it stays.";

    private void StartNudges(int summonCount)
    {
        try
        {
            _nudges.NoteSummon(summonCount);

            StopNudgeTimer();
            var disp = Application.Current?.Dispatcher;
            if (disp == null || disp.HasShutdownStarted) return;

            _nudgeTimer = new System.Windows.Threading.DispatcherTimer(
                System.Windows.Threading.DispatcherPriority.Background, disp)
            {
                Interval = TimeSpan.FromMilliseconds(NudgePollMs)
            };
            _nudgeTimer.Tick += OnNudgeTick;
            _nudgeTimer.Start();
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] nudge machine failed to arm");
        }
    }

    private void StopNudges()
    {
        try { _nudges.NoteDismiss(); } catch (Exception ex) { Log.Debug(ex, "[EmiDesk] nudge disarm failed"); }
        StopNudgeTimer();
    }

    private void StopNudgeTimer()
    {
        try
        {
            if (_nudgeTimer == null) return;
            _nudgeTimer.Stop();
            _nudgeTimer.Tick -= OnNudgeTick;
            _nudgeTimer = null;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] nudge timer stop failed");
        }
    }

    private void OnNudgeTick(object? sender, EventArgs e)
    {
        try
        {
            if (Application.Current?.Dispatcher?.HasShutdownStarted != false) return;
            if (!IsOut) { StopNudges(); return; }

            var track = _nudges.Tick(_nudgeWorld);
            if (track == null) return;
            SpeakNudge(track);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] nudge tick failed");
        }
    }

    /// <summary>
    /// The ring just opened: count it, and offer the pin nudge at most once a summon. Called from
    /// the widget's ring partial rather than off the <c>ringOpen</c> moment, because that moment
    /// already goes through the ordinary line path and firing both would put two bubbles in flight
    /// for one event.
    /// </summary>
    internal void NoteRingOpened()
    {
        try
        {
            EmiState.NoteRingOpen();
            var track = _nudges.OnRingOpened(_nudgeWorld);
            if (track == null) return;

            // Let the fan finish dealing before she narrates it: 650 ms is the last card at rest.
            var disp = Application.Current?.Dispatcher;
            if (disp == null || disp.HasShutdownStarted) return;
            var t = new System.Windows.Threading.DispatcherTimer(
                System.Windows.Threading.DispatcherPriority.Background, disp)
            { Interval = TimeSpan.FromMilliseconds(900) };
            t.Tick += (_, _) =>
            {
                try { t.Stop(); SpeakNudge(track); }
                catch (Exception ex) { Log.Debug(ex, "[EmiDesk] pin nudge failed"); }
            };
            t.Start();
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] ring-open bookkeeping failed");
        }
    }

    /// <summary>
    /// Say one nudge, and tell the machine what happened. The attempt is always recorded, whether
    /// or not a line reached the screen: see <see cref="EmiNudgeMachine.Attempted"/>.
    /// </summary>
    private void SpeakNudge(string track)
    {
        bool spoke = false;
        try
        {
            if (!IsOut) return;
            var win = _window;
            if (win == null || win.Visibility != Visibility.Visible) return;

            // The goodbye still wins: she is on her way to the Arcademy and must not be teaching
            // anybody anything on the way out.
            if (DateTime.UtcNow < _farewellUntilUtc) return;

            var line = DrawNudge(track);
            if (line != null)
            {
                win.SpeakLine(line);
                spoke = true;
                Log.Information("[EmiDesk] nudge {Track}: {Line}", track, line.Id);
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] nudge {Track} failed", track);
        }
        finally
        {
            try { _nudges.Attempted(_nudgeWorld, track, spoke); }
            catch (Exception ex) { Log.Debug(ex, "[EmiDesk] nudge bookkeeping failed"); }
        }
    }

    /// <summary>
    /// One nudge line, or null when she should stay quiet.
    ///
    /// <para>Defensive on purpose, and the pool file is the reason. The three pools are authored
    /// on the content side, so this code has to be correct BEFORE they land and after: when the
    /// engine knows the moment, the engine's answer is final (a refusal is a refusal, and faking a
    /// line past a cooldown would make the tutorial the loudest thing she owns); when it does not
    /// know the moment at all, a hardcoded line stands in, behind nothing but the safety hold.</para>
    ///
    /// <para>The track id is a CONST rather than a literal at the call site for the same reason
    /// <see cref="ArcademyByeMoment"/> is: <c>EmiMomentIdWiringTests</c> scans <c>Fire("...")</c>
    /// literals against the shipped pools, and this path does not go through <c>Fire</c> at all.</para>
    /// </summary>
    public static LineDraw? DrawNudge(string track)
    {
        try
        {
            var engine = EmiLineEngine.Instance;

            bool known = false;
            try { known = engine.MomentIds.Contains(track); }
            catch (Exception ex) { Log.Debug(ex, "[EmiDesk] nudge vocabulary probe failed"); }

            if (known)
            {
                var drawn = engine.Draw(track);
                if (drawn != null && !drawn.Hold && !string.IsNullOrWhiteSpace(drawn.Text)) return drawn;
                return null;
            }

            if (engine.HoldActive) return null;

            var (text, face) = track switch
            {
                EmiNudgeMachine.PetTrack => (PetNudgeFallbackText, "^_^"),
                EmiNudgeMachine.RingTrack => (RingNudgeFallbackText, "^_~"),
                EmiNudgeMachine.PinTrack => (PinNudgeFallbackText, "^_^"),
                _ => (null, null)
            };
            if (text == null || face == null) return null;

            return new LineDraw(track + ".fallback", track, text, face, null, 2, false, 0);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] nudge draw failed for {Track}", track);
            return null;
        }
    }

    /// <summary>
    /// QA ONLY: replay onboarding. Wipes the three gist latches and the lifetime fire counts, then
    /// re-arms the machine against the summon she is currently on, so the first pet nudge is due
    /// 25 s later without a restart. Reached from <see cref="EmiDebug"/> and from the hidden
    /// modifier click on her body.
    /// </summary>
    public void ResetOnboarding()
    {
        try
        {
            EmiState.ResetOnboarding();
            if (IsOut) StartNudges(EmiState.Current.SummonCount);
            Log.Information("[EmiDesk] onboarding replayed (QA)");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[EmiDesk] onboarding replay failed");
        }
    }

    // ---------------------------------------------------------------- the ring, from an offer

    /// <summary>
    /// RING SEAM (SEAMS 7.3). <c>EmiOffers.EffectFeasible</c> asks this for every <c>open:&lt;id&gt;</c>
    /// and <c>pinTop:&lt;id&gt;</c> effect BEFORE the offer is drawn, so a chip that could not do what
    /// it says is never put on the screen at all (LINES-SCHEMA 4).
    ///
    /// <para>Available means all three: the catalogue knows the id, the door exists in this build and
    /// on this account (<c>IsAvailable</c>), and the tier gate is not refusing it today
    /// (<c>IsLocked</c>). A locked door is deliberately NOT offerable: she does not get to dangle a
    /// padlock as a favour.</para>
    /// </summary>
    public bool IsTargetAvailable(string? targetId)
    {
        if (string.IsNullOrWhiteSpace(targetId)) return false;
        try
        {
            var t = EmiTargets.Find(targetId);
            if (t == null)
            {
                Log.Debug("[EmiDesk] IsTargetAvailable({Target}): not in the catalogue", targetId);
                return false;
            }

            // Both probes are the wrapped properties, which swallow a throwing lambda and answer
            // "hidden" / "locked" rather than taking the offer draw down with them.
            return t.Available && !t.Locked;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] IsTargetAvailable({Target}) failed", targetId);
            return false;
        }
    }

    /// <summary>
    /// The <c>open:&lt;id&gt;</c> offer effect: walk the user through a door she picked for them.
    ///
    /// <para>It goes through the catalogue entry's own <c>Open</c>, which is <c>EmiTargets.Pick</c>,
    /// so an offer-driven open is bookkept exactly like a ring card: the tier gate owns the refusal,
    /// the usage counter moves, and the <c>ringPick</c> / <c>arcademyFromRing</c> /
    /// <c>lockedCardTapped</c> moment fires from one place. Navigation runs on the dispatcher because
    /// every target ends in window work.</para>
    /// </summary>
    public void OpenTarget(string? targetId)
    {
        if (string.IsNullOrWhiteSpace(targetId)) return;
        try
        {
            var t = EmiTargets.Find(targetId);
            if (t == null)
            {
                Log.Information("[EmiDesk] OpenTarget({Target}) ignored: not in the catalogue", targetId);
                return;
            }

            var disp = Application.Current?.Dispatcher;
            if (disp == null || disp.HasShutdownStarted) return;

            if (!disp.CheckAccess()) { disp.BeginInvoke(new Action(() => OpenTarget(targetId))); return; }

            Log.Information("[EmiDesk] offer opened {Target}", targetId);
            t.Open();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[EmiDesk] OpenTarget({Target}) failed", targetId);
        }
    }

    /// <summary>
    /// The <c>pinTop:&lt;id&gt;</c> offer effect: nail a card to the front row of the ring.
    ///
    /// <para>The target takes slot 0 and every existing pin slides down one; the sixth pin falls off
    /// the end, because <see cref="EmiSuggester.MaxPins"/> slots is the whole fan. If the fan happens
    /// to be open under the pointer it is re-composed in place rather than folded, and then
    /// <c>pinAdded</c> fires so she can react to her own favour exactly once, through the same moment
    /// a right-click pin raises.</para>
    /// </summary>
    public void PinTop(string? targetId)
    {
        if (string.IsNullOrWhiteSpace(targetId)) return;
        try
        {
            if (EmiTargets.Find(targetId) == null)
            {
                Log.Information("[EmiDesk] PinTop({Target}) ignored: not in the catalogue", targetId);
                return;
            }

            EmiSuggester.PinToTop(targetId);

            var disp = Application.Current?.Dispatcher;
            if (disp != null && !disp.HasShutdownStarted)
            {
                if (disp.CheckAccess()) RebuildRingIfOpen();
                else disp.BeginInvoke(new Action(RebuildRingIfOpen));
            }

            Fire("pinAdded", new { target = targetId });
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[EmiDesk] PinTop({Target}) failed", targetId);
        }
    }

    /// <summary>
    /// Re-fan an open ring in place, from anywhere. The settings picker calls it after a pin so a
    /// fan that happens to be up under the user's pointer shows the change immediately rather than
    /// on the next open.
    /// </summary>
    public void RefreshRing()
    {
        try
        {
            var disp = Application.Current?.Dispatcher;
            if (disp == null || disp.HasShutdownStarted) return;
            if (disp.CheckAccess()) RebuildRingIfOpen();
            else disp.BeginInvoke(new Action(RebuildRingIfOpen));
        }
        catch (Exception ex) { Log.Debug(ex, "[EmiDesk] RefreshRing failed"); }
    }

    /// <summary>Re-fan an open ring in place. Nothing at all when it is shut or she is away.</summary>
    private void RebuildRingIfOpen()
    {
        try { _window?.RebuildRing(); }
        catch (Exception ex) { Log.Debug(ex, "[EmiDesk] ring rebuild after pin failed"); }
    }

    /// <summary>
    /// Tell EMI a target was opened, however it was opened (her ring, the nav rail, a hotkey). The
    /// suggester learns from ALL of them, so the ring reflects what you actually use rather than
    /// what you last used her for.
    /// </summary>
    public void NoteOpen(string targetId)
    {
        if (string.IsNullOrWhiteSpace(targetId)) return;
        try
        {
            EmiState.NoteUsage(targetId);
            Log.Debug("[EmiDesk] target opened: {Target}", targetId);
            TargetOpened?.Invoke(this, targetId);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] NoteOpen({Target}) failed", targetId);
        }
    }

    // ---------------------------------------------------------------- the mute prompt

    /// <summary>
    /// Ask, at most once per app session and only when something that actually talks is live,
    /// whether the avatar should sit out while EMI is here. Dismissing the dialog keeps the avatar:
    /// silence is never assumed to be consent to being silenced.
    /// </summary>
    private void MaybeAskAboutMuting()
    {
        try
        {
            var s = App.Settings?.Current;
            if (s == null) return;

            if (!s.EmiDeskMuteAvatar)
            {
                _muteAccepted = false;
                return;
            }
            if (s.EmiDeskMuteDontAsk)
            {
                // "Do not ask again" was chosen ON the mute button, so it means mute from now on.
                _muteAccepted = true;
                return;
            }
            if (_mutePromptShownThisSession) return;
            if (!AnyTalkingFeatureLive())
            {
                // Nothing is going to talk over her, so there is nothing to arbitrate. Do NOT burn
                // the once-per-session prompt on a silent app.
                _muteAccepted = false;
                return;
            }

            _mutePromptShownThisSession = true;
            var choice = EmiMutePromptWindow.Ask();
            switch (choice)
            {
                case EmiMuteChoice.Mute:
                    _muteAccepted = true;
                    break;
                case EmiMuteChoice.DontAsk:
                    _muteAccepted = true;
                    s.EmiDeskMuteDontAsk = true;
                    App.Settings?.Save();
                    break;
                default:
                    _muteAccepted = false;
                    break;
            }
            Log.Information("[EmiDesk] mute prompt answered: {Choice}", choice);

            // MOMENTS 4.D: the verdict is hers to react to, either way. Queued behind the summon
            // greeting by the engine's own floor, not by a delay here.
            _pendingMuteMoment = _muteAccepted ? "avatarMuted" : "avatarKept";
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[EmiDesk] mute prompt failed, keeping the avatar");
            _muteAccepted = false;
        }
    }

    /// <summary>
    /// Is anything that can put words on screen or in your ears switched on? Takeover, the mic
    /// wake word, AI chat, Awareness, or a connected remote controller.
    /// </summary>
    private static bool AnyTalkingFeatureLive()
    {
        try
        {
            if (App.Autonomy?.IsEnabled == true) return true;
            var s = App.Settings?.Current;
            if (s != null)
            {
                if (s.SpeechWakeWordEnabled) return true;
                if (s.AiChatEnabled) return true;
                if (s.AwarenessModeEnabled && s.AwarenessConsentGiven) return true;
            }
            if (App.RemoteControl?.ControllerConnected == true) return true;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] live-feature probe failed");
        }
        return false;
    }

    // ---------------------------------------------------------------- hotkey

    /// <summary>
    /// Arm (or disarm) the system-wide summon chord. Every refusal is a logged no-op: the dock chip
    /// and the settings switch keep working, so a taken chord costs a line in the log and nothing
    /// else. Call it from the main window's Loaded and again whenever the setting changes.
    /// </summary>
    public void ApplyHotkey()
    {
        try
        {
            var s = App.Settings?.Current;

            // THE ARMING TRAP (found on the first live run, QA 2026-08-29). The only caller is
            // MainWindow's Loaded handler, and WPF raises Loaded from INSIDE `mainWindow.Show()`
            // (App.xaml.cs:2466) - while `App.MainWindowRef` is not assigned until the line AFTER
            // Show() returns (App.xaml.cs:2480). So this method saw a null owner on every launch,
            // logged it at Debug (the file sink's minimum is Information, so the line was invisible
            // too) and returned without ever retrying: Ctrl+Alt+E was dead in every build and only
            // the dock chip summoned her. `Application.Current.MainWindow` is set by the Window
            // constructor itself, so it is already there when Loaded runs - the same
            // `MainWindowRef ?? Current.MainWindow` fallback the rest of App.xaml.cs uses.
            Window? owner = App.MainWindowRef ?? Application.Current?.MainWindow;

            if (s == null || !s.EmiDeskEnabled)
            {
                GlobalHotkeyService.Unregister(GlobalHotkeyService.EmiDeskHotkeyId);
                _hotkeyArmed = false;
                Log.Information("[EmiDesk] summon hotkey not armed: EMI Desk is off");
                return;
            }
            if (owner == null)
            {
                // Warning, not Debug: this is exactly the silent-failure shape above. If it ever
                // prints again the chord is unarmed and something has to call ApplyHotkey again.
                Log.Warning("[EmiDesk] summon hotkey NOT armed: no main window yet. " +
                            "The dock chip in the nav rail still summons her.");
                return;
            }

            var chord = string.IsNullOrWhiteSpace(s.EmiDeskHotkey) ? DefaultHotkey : s.EmiDeskHotkey;
            var parsed = ParseChord(chord);
            if (parsed == null)
            {
                GlobalHotkeyService.Unregister(GlobalHotkeyService.EmiDeskHotkeyId);
                _hotkeyArmed = false;
                Log.Warning("[EmiDesk] summon hotkey NOT armed: {Chord} is not a valid chord " +
                            "(a modifier is required, bare keys are refused). Use the dock chip in the nav rail.", chord);
                return;
            }
            var (mods, key) = parsed.Value;

            // Same guard the Quick Recal chord uses. The panic and pause keys ride the modifier-blind
            // WH_KEYBOARD_LL hook and do NOT consume the press, so a summon chord whose BASE key is
            // one of them would summon EMI and tear the session down in the same keystroke. Refuse.
            if (Safety.PanicPolicy.FindHookClash(
                    key.ToString(), Safety.PanicPolicy.HookBoundBaseKeys(s)) is { } clash)
            {
                GlobalHotkeyService.Unregister(GlobalHotkeyService.EmiDeskHotkeyId);
                _hotkeyArmed = false;
                Log.Warning(
                    "[EmiDesk] summon hotkey {Chord} NOT armed: it shares its base key with the {Binding} binding ({BoundKey}), " +
                    "and the global keyboard hook ignores modifiers without consuming the press. Rebind one of them to free {Key}. " +
                    "The dock chip in the nav rail is unaffected.",
                    chord, clash.Name, clash.Key, key);
                return;
            }

            bool ok = GlobalHotkeyService.Register(
                GlobalHotkeyService.EmiDeskHotkeyId, owner, mods, key,
                // Win32 hotkeys arrive on the message-pump thread, so marshal before touching UI.
                () => owner.Dispatcher.BeginInvoke(new Action(Toggle)));

            _hotkeyArmed = ok;
            if (!ok)
            {
                Log.Warning("[EmiDesk] summon hotkey {Chord} could not be registered: another process holds it. " +
                            "The dock chip in the nav rail still summons her.", chord);
                return;
            }
            Log.Information("[EmiDesk] summon hotkey armed: {Chord} (slot=0x{Id:X})",
                chord, GlobalHotkeyService.EmiDeskHotkeyId);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[EmiDesk] ApplyHotkey failed");
        }
    }

    /// <summary>True while the summon chord is registered with the OS.</summary>
    public bool HotkeyArmed => _hotkeyArmed;

    /// <summary>
    /// Parse a stored chord string ("Ctrl+Alt+E"). Returns null when it is unparseable OR carries
    /// no modifier: a bare-key global summon would eat that letter in every other app on the
    /// machine, which is exactly the class of bug the panic-key hook note warns about.
    /// </summary>
    public static (ModifierKeys Mods, Key Key)? ParseChord(string? chord)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(chord)) return null;
            var mods = ModifierKeys.None;
            Key key = Key.None;
            foreach (var raw in chord.Split('+'))
            {
                var part = raw.Trim();
                if (part.Length == 0) continue;
                switch (part.ToLowerInvariant())
                {
                    case "ctrl":
                    case "control": mods |= ModifierKeys.Control; continue;
                    case "alt": mods |= ModifierKeys.Alt; continue;
                    case "shift": mods |= ModifierKeys.Shift; continue;
                    case "win":
                    case "windows": mods |= ModifierKeys.Windows; continue;
                }
                if (!Enum.TryParse<Key>(part, ignoreCase: true, out var k)) return null;
                key = k;
            }
            if (key == Key.None) return null;
            if (mods == ModifierKeys.None) return null;
            return (mods, key);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Render a chord the way it is stored and shown: "Ctrl+Alt+E".</summary>
    public static string FormatChord(ModifierKeys mods, Key key)
    {
        var parts = new List<string>(4);
        if ((mods & ModifierKeys.Control) != 0) parts.Add("Ctrl");
        if ((mods & ModifierKeys.Alt) != 0) parts.Add("Alt");
        if ((mods & ModifierKeys.Shift) != 0) parts.Add("Shift");
        if ((mods & ModifierKeys.Windows) != 0) parts.Add("Win");
        parts.Add(key.ToString());
        return string.Join("+", parts);
    }

    /// <summary>
    /// Why a candidate chord cannot be used, or null when it is fine. Localized, for the capture
    /// UI to show inline. Checks: a modifier is required, the base key must not be on the global
    /// keyboard hook (panic / pause), and it must not be the Quick Recal chord.
    /// </summary>
    public static string? ValidateChord(ModifierKeys mods, Key key)
    {
        try
        {
            if (key == Key.None) return Loc.Get("emi_desk_hotkey_err_empty");
            if (mods == ModifierKeys.None) return Loc.Get("emi_desk_hotkey_err_bare");

            var s = App.Settings?.Current;
            if (Safety.PanicPolicy.FindHookClash(
                    key.ToString(), Safety.PanicPolicy.HookBoundBaseKeys(s)) is { } clash)
            {
                return Loc.GetF("emi_desk_hotkey_err_hook", clash.Name, clash.Key);
            }

            // Ctrl+Alt+G is Quick Recal (MainWindow.QuickRecalHotkey*). Two Win32 slots cannot hold
            // the same combo and the loser would just fail to register, so say so up front.
            if (mods == (ModifierKeys.Control | ModifierKeys.Alt) && key == Key.G)
            {
                return Loc.Get("emi_desk_hotkey_err_quickrecal");
            }
            return null;
        }
        catch
        {
            return null;
        }
    }

    // ---------------------------------------------------------------- app events + her own clock

    /// <summary>How much XP in one award is worth reacting to. Below this she says nothing: a
    /// running total of small awards is not an event, it is Tuesday.</summary>
    private const int XpBigAwardFloor = 250;

    /// <summary>When this process started, for <c>longSitting</c>. Falls back to service
    /// construction if the process cannot be queried, which is close enough for a 2 h beat.</summary>
    private static readonly DateTime _launchedUtc = ResolveLaunchUtc();

    private static DateTime ResolveLaunchUtc()
    {
        try { return System.Diagnostics.Process.GetCurrentProcess().StartTime.ToUniversalTime(); }
        catch { return DateTime.UtcNow; }
    }

    private bool _wired;
    private System.Windows.Threading.DispatcherTimer? _clockTimer;

    // Handlers are held so the unsubscribe in Dispose can be exact. A lambda that is not kept is a
    // subscription that can never be removed.
    private EventHandler<ProgressionService.XpAward>? _onXpAwarded;
    private EventHandler<bool>? _onAutonomyEnabled;
    private EventHandler<bool>? _onListeningChanged;
    private Action? _onDailyFreeChanged;
    private Action<Bark.BarkRule, string>? _onBarkSpoken;

    private DateTime _takeoverSinceUtc = DateTime.MinValue;
    private string? _lateNightNight;
    private string? _smallHoursNight;
    private string? _morningDay;
    private bool _saidLongSitting;
    private bool _saidIdleLong;

    /// <summary>
    /// MOMENTS 4.C: subscribe her to the app events no bark trigger already carries, and start her
    /// own 60 s clock for the time beats. Called once from <c>App.OnStartup</c>, at the first point
    /// where Progression, Autonomy, Speech, DailyFree and Bark all exist.
    ///
    /// <para>Idempotent, and every subscription is armed on its own: one service missing must not
    /// cost the others, and nothing in here may throw back into startup.</para>
    /// </summary>
    public void WireAppEvents()
    {
        if (_wired || _disposed) return;
        _wired = true;

        // ---- a single award big enough to notice -------------------------------------------
        try
        {
            _onXpAwarded = (_, award) =>
            {
                try
                {
                    // A level-up is its own, louder moment (mirrored off the LevelUp bark). She does
                    // not narrate the XP over her own fanfare.
                    if (award.LeveledUp) return;
                    int amount = (int)Math.Round(award.Amount);
                    if (amount < XpBigAwardFloor) return;
                    Fire("xpBigAward", new { n = amount, target = EmiNames.XpSource(award.Source) });
                }
                catch (Exception ex) { Log.Debug(ex, "[EmiDesk] xpBigAward handler failed"); }
            };
            App.Progression.XPAwarded += _onXpAwarded;
        }
        catch (Exception ex) { Log.Debug(ex, "[EmiDesk] XPAwarded subscribe failed"); }

        // ---- takeover let go ------------------------------------------------------------------
        try
        {
            _onAutonomyEnabled = (_, enabled) =>
            {
                try
                {
                    // The start is the bark's BambiTakeoverStarted; only the END needs a duration,
                    // and nothing else in the app is counting it.
                    if (enabled) { _takeoverSinceUtc = DateTime.UtcNow; return; }

                    int minutes = _takeoverSinceUtc == DateTime.MinValue
                        ? 0
                        : Math.Max(0, (int)(DateTime.UtcNow - _takeoverSinceUtc).TotalMinutes);
                    _takeoverSinceUtc = DateTime.MinValue;
                    Fire("takeoverEnded", new { minutes });
                }
                catch (Exception ex) { Log.Debug(ex, "[EmiDesk] takeoverEnded handler failed"); }
            };
            App.Autonomy.EnabledChanged += _onAutonomyEnabled;
        }
        catch (Exception ex) { Log.Debug(ex, "[EmiDesk] EnabledChanged subscribe failed"); }

        // ---- the mic opened -------------------------------------------------------------------
        try
        {
            _onListeningChanged = (_, on) =>
            {
                try { if (on) Fire("sheListeningOn", null); }
                catch (Exception ex) { Log.Debug(ex, "[EmiDesk] sheListeningOn handler failed"); }
            };
            App.Speech.ListeningChanged += _onListeningChanged;
        }
        catch (Exception ex) { Log.Debug(ex, "[EmiDesk] ListeningChanged subscribe failed"); }

        // ---- today's free feature -------------------------------------------------------------
        try
        {
            _onDailyFreeChanged = () =>
            {
                try { Fire("dailyFreeToday", new { target = EmiNames.Feature(App.DailyFree?.TodayKey) }); }
                catch (Exception ex) { Log.Debug(ex, "[EmiDesk] dailyFreeToday handler failed"); }
            };
            App.DailyFree!.TodayChanged += _onDailyFreeChanged;
        }
        catch (Exception ex) { Log.Debug(ex, "[EmiDesk] TodayChanged subscribe failed"); }

        // ---- the tube spoke -------------------------------------------------------------------
        try
        {
            // The belt to ShowGiggle's braces: a bark that reaches the screen through any path at
            // all still owes her the hold. Arming a hold twice is free; missing one is not.
            _onBarkSpoken = (_, __) =>
            {
                try { NoteAvatarSpeaking(); }
                catch (Exception ex) { Log.Debug(ex, "[EmiDesk] BarkSpoken handler failed"); }
            };
            App.Bark!.BarkSpoken += _onBarkSpoken;
        }
        catch (Exception ex) { Log.Debug(ex, "[EmiDesk] BarkSpoken subscribe failed"); }

        // ---- her clock ------------------------------------------------------------------------
        try
        {
            var disp = Application.Current?.Dispatcher;
            if (disp != null && !disp.HasShutdownStarted)
            {
                _clockTimer = new System.Windows.Threading.DispatcherTimer(
                    System.Windows.Threading.DispatcherPriority.Background, disp)
                {
                    Interval = TimeSpan.FromSeconds(60)
                };
                _clockTimer.Tick += OnClockTick;
                _clockTimer.Start();
            }
        }
        catch (Exception ex) { Log.Debug(ex, "[EmiDesk] clock start failed"); }

        // QA: EMI_DESK_RESET_ONBOARDING=1 puts the three nudges back to a fresh install for this
        // launch, so the tutorial can be play-tested without deleting the whole ledger.
        try
        {
            if (EmiDebug.ResetOnboarding) EmiState.ResetOnboarding();
        }
        catch (Exception ex) { Log.Debug(ex, "[EmiDesk] onboarding reset switch failed"); }

        Log.Information("[EmiDesk] app events wired");
    }

    /// <summary>
    /// The time group (MOMENTS 5). One minute is the coarsest tick that can still call an hour
    /// boundary honestly, and every beat below is latched by night, day or launch so the tick is a
    /// check and not a firehose.
    /// </summary>
    private void OnClockTick(object? sender, EventArgs e)
    {
        try
        {
            var disp = Application.Current?.Dispatcher;
            if (disp == null || disp.HasShutdownStarted) return;

            // Everything here is something she SAYS. None of it is a hold, so none of it is worth
            // computing while she is away.
            if (!IsOut) return;

            var now = DateTime.Now;

            // A "night" runs to 06:00, so 00:30 and 03:30 are the same one and she cannot greet the
            // small hours twice on either side of a date change.
            string nightKey = now.AddHours(-6).ToString("yyyy-MM-dd");
            string dayKey = now.ToString("yyyy-MM-dd");

            if (now.Hour < 3 && _lateNightNight != nightKey)
            {
                _lateNightNight = nightKey;
                Fire("lateNight", new { n = now.Hour });
            }
            else if (now.Hour >= 3 && now.Hour < 5 && _smallHoursNight != nightKey)
            {
                _smallHoursNight = nightKey;
                Fire("smallHours", new { n = now.Hour });
            }
            else if (now.Hour >= 5 && now.Hour < 10 && _morningDay != dayKey)
            {
                _morningDay = dayKey;
                Fire("morningFirst", null);
            }

            var open = DateTime.UtcNow - _launchedUtc;
            if (!_saidLongSitting && open.TotalHours >= 2)
            {
                _saidLongSitting = true;
                Fire("longSitting", new { minutes = (int)open.TotalMinutes });
            }

            int idleSeconds = 0;
            try { idleSeconds = ActivityTracker.GetIdleSeconds(); } catch { }

            if (idleSeconds >= 1200)
            {
                // Also mirrored off ActivityTracker's own IdleStateChanged edge, which fires first
                // but cannot say how long. The moment's 1/launch limit keeps the pair to one line.
                if (!_saidIdleLong)
                {
                    _saidIdleLong = true;
                    Fire("appIdleLong", new { minutes = idleSeconds / 60 });
                }
            }
            else if (idleSeconds >= 300)
            {
                // Deliberately unlatched: the ambient heartbeat. Its own 5 min cooldown and 0.06
                // odds are the rate limit, so this is roughly one murmur an hour of sitting still.
                Fire("idleShort", new { minutes = idleSeconds / 60 });
            }
            else
            {
                _saidIdleLong = false;   // input came back, so the long-idle beat may arm again
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] clock tick failed");
        }
    }

    /// <summary>Drop everything <see cref="WireAppEvents"/> took. Safe to call unwired.</summary>
    private void UnwireAppEvents()
    {
        try
        {
            if (_clockTimer != null)
            {
                _clockTimer.Stop();
                _clockTimer.Tick -= OnClockTick;
                _clockTimer = null;
            }
        }
        catch { }

        try { if (_onXpAwarded != null) App.Progression.XPAwarded -= _onXpAwarded; } catch { }
        try { if (_onAutonomyEnabled != null) App.Autonomy.EnabledChanged -= _onAutonomyEnabled; } catch { }
        try { if (_onListeningChanged != null) App.Speech.ListeningChanged -= _onListeningChanged; } catch { }
        try { if (_onDailyFreeChanged != null && App.DailyFree != null) App.DailyFree.TodayChanged -= _onDailyFreeChanged; } catch { }
        try { if (_onBarkSpoken != null && App.Bark != null) App.Bark.BarkSpoken -= _onBarkSpoken; } catch { }

        _onXpAwarded = null;
        _onAutonomyEnabled = null;
        _onListeningChanged = null;
        _onDailyFreeChanged = null;
        _onBarkSpoken = null;
        _wired = false;
    }

    // ---------------------------------------------------------------- lifetime

    /// <summary>Tear her down at app shutdown. Idempotent.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        UnwireAppEvents();
        StopNudges();
        CancelFarewell();
        try
        {
            GlobalHotkeyService.Unregister(GlobalHotkeyService.EmiDeskHotkeyId);
            _hotkeyArmed = false;
            _window?.ShutDown();
            _window = null;
            IsOut = false;
            EmiState.SaveNow();
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] dispose failed");
        }
    }
}
