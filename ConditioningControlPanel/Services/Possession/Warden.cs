using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace ConditioningControlPanel.Services.Possession;

// =====================================================================================================
//  THE WARDEN - the companion's half of Possession. Read Services/Possession/POSSESSION.md first.
//
//  Four verbs, all driven by the director: knock (R3 - stand beside a card, a beat, the card falls),
//  stare (tripwire repeat 3+ / R4 - come to the middle of the window and say one line), leave (R4 -
//  the tube goes off-frame) and return (reassembly).
//
//  WHY it is built on the bubble-egg movement API rather than WPF Left/Top: the tube is a layered
//  window living on its OWN thread, and Left/Top is DIP space that goes non-linear across monitors of
//  mixed DPI. The egg already solved that by moving in PHYSICAL pixels through SetWindowPos, which is
//  the same space FrameworkElement.PointToScreen hands back - so target rects need no conversion.
//
//  EVERY method here is a no-op when there is no avatar window, never throws, honours the caller's
//  cancellation, and gives up after 15 s so a wedged glide can never strand the director.
// =====================================================================================================

/// <summary>The companion as Lockdown's warden. See <see cref="IPossessionWarden"/>.</summary>
public sealed class Warden : IPossessionWarden
{
    // The tube is a shared, user-visible actor: the bark system, the bubble egg and the chat window all
    // move it. These cooldowns keep the haunt from feeling like the companion is having a seizure.
    private static readonly TimeSpan AppearanceCooldown = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan StareCooldown = TimeSpan.FromSeconds(60);

    /// <summary>Hard ceiling on any single verb. A glide that never returns (tube thread wedged, handle
    /// died mid-flight) must not hold the director's effect slot for the rest of the lockdown.</summary>
    private static readonly TimeSpan VerbTimeout = TimeSpan.FromSeconds(15);

    private DateTime _lastAppearance = DateTime.MinValue;
    private DateTime _lastStare = DateTime.MinValue;

    // 0 = idle, 1 = a verb is mid-flight. Interlocked because the director may tick from the UI thread
    // while a previous verb's fire-and-forget homeward leg is still resolving on a pool thread.
    private int _busy;

    /// <summary>True once <see cref="LeaveAsync"/> actually took the tube off-frame, so the reassembly
    /// only announces a "return" when there was a leaving to undo.</summary>
    private bool _hasLeft;

    // The leave is the one verb the director fires and forgets while the lockdown may end underneath
    // it: a glide takes about half a second, and ReturnAsync running in the middle of one used to
    // clear the tube's captured home BEFORE the leave finished, which parked a detached tube at the
    // bottom of the screen with the note up, after the lockdown was over. So the leave is tracked.
    private readonly object _leaveGate = new();
    private Task? _leaveTask;
    private CancellationTokenSource? _leaveCts;

    /// <summary>Bumped by every <see cref="ReturnAsync"/> / <see cref="Reset"/>. A leave captures it
    /// when it starts and refuses to finish (no note, no bark, no _hasLeft) if it changed underneath.</summary>
    private int _returnEpoch;

    private bool _hooked;

    private static AvatarTubeWindow? Tube => App.AvatarWindow;

    public Warden()
    {
        EnsureHooked();
    }

    /// <summary>True while a warden verb owns the tube. Read by other Possession effects that move the
    /// tube themselves (StealCardEffect) so a theft and a knock can never fight over it.</summary>
    public bool IsBusy => Volatile.Read(ref _busy) != 0;

    /// <summary>Take the tube for a NON-warden mover (the steal card). False when a verb owns it. The
    /// caller must <see cref="ReleaseTube"/> when it is done - including after its own ReturnHomeAsync,
    /// because that call clears the capture a warden verb would otherwise need to get home.</summary>
    public bool TryTakeTube() => Interlocked.Exchange(ref _busy, 1) == 0;

    /// <summary>Give the tube back after <see cref="TryTakeTube"/>.</summary>
    public void ReleaseTube() => Interlocked.Exchange(ref _busy, 0);

    /// <summary>A new lockdown starts with a clean warden: cooldown stamps cleared (otherwise the first
    /// verb of this lockdown is blocked by the last one's 90 s appearance stamp), no leave in flight,
    /// and <see cref="_hasLeft"/> false so reassembly cannot announce a "return" for a leaving that
    /// happened in a previous lockdown.
    ///
    /// <para>The director has no warden-lifecycle seam - it never calls into the warden on activation -
    /// so the warden hooks LockdownActivated itself, the same shape StealCardEffect uses for its own
    /// per-lockdown state. If a call site ever appears in PossessionDirector.OnLockdownActivated,
    /// calling Reset() there instead is equivalent (it is idempotent).</para></summary>
    public void Reset()
    {
        try
        {
            Interlocked.Increment(ref _returnEpoch);
            lock (_leaveGate)
            {
                try { _leaveCts?.Cancel(); } catch { }
                _leaveCts = null;
                _leaveTask = null;
            }
            _lastAppearance = DateTime.MinValue;
            _lastStare = DateTime.MinValue;
            _hasLeft = false;
            ClearNote();
        }
        catch (Exception ex) { App.Logger?.Debug("Possession warden reset failed: {Error}", ex.Message); }
    }

    /// <summary>Subscribe to LockdownActivated once. Lazy as well as constructor-driven: App.Lockdown is
    /// assigned a line before the warden is built, but a test (or a future re-order) must not silently
    /// lose the per-lockdown reset.</summary>
    private void EnsureHooked()
    {
        if (_hooked) return;
        try
        {
            var lockdown = App.Lockdown;
            if (lockdown == null) return;
            _hooked = true;
            lockdown.LockdownActivated += Reset;
        }
        catch { /* no lockdown service yet; retried from IsAvailable */ }
    }

    public bool IsAvailable
    {
        get
        {
            try
            {
                EnsureHooked();
                if (App.Settings?.Current?.LockdownWardenEnabled != true) return false;
                var tube = Tube;
                if (tube == null || !tube.CanPerformPossessionMove) return false;
                // A fullscreen video owns the screen; a tube gliding over it is both invisible and a
                // known render-thread hazard (see the bubble egg's #628 notes).
                if (App.Video?.IsPlaying == true) return false;
                if (Volatile.Read(ref _busy) != 0) return false;
                return DateTime.UtcNow - _lastAppearance >= AppearanceCooldown;
            }
            catch { return false; }
        }
    }

    // =================================================================================================
    //  KNOCK - stand beside the target, a beat, two little shoves, then the caller drops it.
    // =================================================================================================
    public async Task KnockAsync(PossessionTarget target, CancellationToken ct)
    {
        var tube = Tube;
        if (tube == null || target?.Element == null) return;
        if (Interlocked.Exchange(ref _busy, 1) != 0) return;

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(VerbTimeout);
        var token = cts.Token;
        try
        {
            var rect = await GetElementScreenRectAsync(target.Element).ConfigureAwait(true);
            if (rect.IsEmpty || rect.Width <= 0 || rect.Height <= 0) return;

            _lastAppearance = DateTime.UtcNow;
            await tube.GlideToScreenRectAsync(rect, token).ConfigureAwait(true);
            if (token.IsCancellationRequested) return;

            // The beat: the pause is the whole point. Something arrives, considers the card, and only
            // THEN does the card go. Without it the fall reads as a glitch instead of an act.
            await Task.Delay(500, token).ConfigureAwait(true);

            // The knock itself: two 12 px shoves toward the target and back. Small on purpose - the
            // card falling is the payoff, the tube only has to look like the cause.
            if (tube.TryGetTubeScreenRect(out var here))
            {
                double dx = rect.X + rect.Width / 2.0 > here.X + here.Width / 2.0 ? 12 : -12;
                for (int i = 0; i < 2 && !token.IsCancellationRequested; i++)
                {
                    await tube.GlideToScreenPointAsync(new Point(here.X + dx, here.Y), token).ConfigureAwait(true);
                    await tube.GlideToScreenPointAsync(new Point(here.X, here.Y), token).ConfigureAwait(true);
                }
            }

            NameIt("knock");
        }
        catch (OperationCanceledException) { /* lockdown ended / timed out mid-knock */ }
        catch (Exception ex) { App.Logger?.Warning("Possession warden knock failed: {Error}", ex.Message); }
        finally
        {
            Interlocked.Exchange(ref _busy, 0);
            // Do NOT go home inside the knock: the caller drops the card the instant we return, and the
            // tube standing there while it falls is the attribution. Home follows on its own two seconds
            // later unless the director calls ReturnAsync first (ReturnAsync is safe to double-fire).
            ScheduleGoHome(ct);
        }
    }

    // =================================================================================================
    //  STARE - come to the middle of the window and hold the look.
    // =================================================================================================
    public async Task StareAsync(string reason, CancellationToken ct)
    {
        var tube = Tube;
        if (tube == null) return;
        if (DateTime.UtcNow - _lastStare < StareCooldown) return;
        if (Interlocked.Exchange(ref _busy, 1) != 0) return;

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(VerbTimeout);
        var token = cts.Token;
        try
        {
            var window = await GetWindowScreenRectAsync().ConfigureAwait(true);
            if (window.IsEmpty || window.Width <= 0) return;
            if (!tube.TryGetTubeScreenRect(out var self)) return;

            _lastAppearance = DateTime.UtcNow;
            _lastStare = DateTime.UtcNow;

            var centre = new Point(
                window.X + window.Width / 2.0 - self.Width / 2.0,
                window.Y + window.Height / 2.0 - self.Height / 2.0);
            await tube.GlideToScreenPointAsync(centre, token).ConfigureAwait(true);
            if (token.IsCancellationRequested) return;

            Say("stare", reason);

            // Linger: the line has to finish and the silence after it is what makes it a stare.
            await Task.Delay(2500, token).ConfigureAwait(true);
        }
        catch (OperationCanceledException) { /* lockdown ended / timed out mid-stare */ }
        catch (Exception ex) { App.Logger?.Warning("Possession warden stare failed: {Error}", ex.Message); }
        finally
        {
            Interlocked.Exchange(ref _busy, 0);
            try { await ReturnAsync(CancellationToken.None).ConfigureAwait(true); } catch { }
        }
    }

    // =================================================================================================
    //  LEAVE - R4: the companion is not in the tube any more.
    // =================================================================================================
    public Task LeaveAsync(CancellationToken ct)
    {
        if (Tube == null || _hasLeft) return Task.CompletedTask;

        // Publish the leave BEFORE it starts: ReturnAsync has to be able to cancel and await it, or
        // the lockdown can end mid-glide and the leave finishes after the tube's home was cleared.
        lock (_leaveGate)
        {
            if (_leaveTask is { IsCompleted: false }) return Task.CompletedTask;
            var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(VerbTimeout);
            _leaveCts = cts;
            return _leaveTask = LeaveCoreAsync(cts, Volatile.Read(ref _returnEpoch));
        }
    }

    private async Task LeaveCoreAsync(CancellationTokenSource cts, int epoch)
    {
        var tube = Tube;
        if (tube == null) { cts.Dispose(); return; }
        if (Interlocked.Exchange(ref _busy, 1) != 0) { cts.Dispose(); return; }

        var token = cts.Token;
        try
        {
            // There is no "empty tube" pose in the avatar art sets (SetPose only takes 1-4, all of them
            // the companion), and hiding the window outright would fight AvatarEnabled / HideAvatarTube
            // and could leave the tube gone after the lockdown. So the companion LEAVES instead: it
            // slides off the bottom of its screen and the frame is simply empty. ReturnAsync brings the
            // exact captured position back, so nothing about the user's layout is lost.
            if (!tube.TryGetTubeScreenRect(out var self)) return;
            var screen = System.Windows.Forms.Screen.FromPoint(
                new System.Drawing.Point((int)self.X, (int)self.Y));

            // Bounds, NOT WorkingArea: the working area EXCLUDES the taskbar, so parking the tube's
            // TOP-LEFT on that line left a taskbar-height slice of her on screen - topmost - through
            // the entire "she's gone" beat. Bounds is the whole monitor, so the frame is really empty.
            var bottom = screen.Bounds.Bottom;

            _lastAppearance = DateTime.UtcNow;
            await tube.GlideToScreenPointAsync(new Point(self.X, bottom), token, clampToWorkArea: false)
                      .ConfigureAwait(true);

            // A return (reassembly, or the knock's delayed homeward leg) that began while we were
            // gliding owns the tube now: it has already cleared the tube's captured home, so finishing
            // here would pin a note in a frame nobody is coming back to and leave _hasLeft true for the
            // NEXT lockdown to announce a phantom return.
            if (token.IsCancellationRequested || Volatile.Read(ref _returnEpoch) != epoch) return;

            _hasLeft = true;

            // The note goes up AFTER the glide, so it appears in a frame that is already empty rather
            // than riding down the screen with her.
            LeaveNote();

            NameIt("leave");
        }
        catch (OperationCanceledException) { /* lockdown ended mid-exit; ReturnAsync still restores */ }
        catch (Exception ex) { App.Logger?.Warning("Possession warden leave failed: {Error}", ex.Message); }
        finally
        {
            Interlocked.Exchange(ref _busy, 0);
            try { cts.Dispose(); } catch { }
        }
    }

    // =================================================================================================
    //  RETURN - reassembly. Must be safe to call twice, and safe when nothing ever moved.
    // =================================================================================================
    public async Task ReturnAsync(CancellationToken ct)
    {
        // FIRST, before anything else looks at the tube: stop and drain a leave that is still in
        // flight. ReturnHomeAsync clears the tube's captured home, so a leave finishing after this
        // point would strand a detached tube off the bottom of the screen with the note up - after
        // the lockdown ended - and leave _hasLeft true for the next one to bark a phantom return.
        await CancelInFlightLeaveAsync().ConfigureAwait(true);

        var tube = Tube;
        if (tube == null) { _hasLeft = false; return; }

        // Unconditionally, and BEFORE the glide: reassembly may be running because the app is closing,
        // and a note left in the tube after the lockdown ended would outlive the whole feature. It is a
        // no-op when there was never a note.
        ClearNote();

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(VerbTimeout);
        try
        {
            // ReturnHomeAsync no-ops when the tube never left home, which is what makes the double
            // call harmless: the second one finds no captured position and does nothing.
            await tube.ReturnHomeAsync(cts.Token).ConfigureAwait(true);
            if (_hasLeft)
            {
                _hasLeft = false;
                NameIt("return");
            }
        }
        catch (OperationCanceledException) { _hasLeft = false; }
        catch (Exception ex)
        {
            _hasLeft = false;
            App.Logger?.Warning("Possession warden return failed: {Error}", ex.Message);
        }
    }

    // =================================================================================================
    //  helpers
    // =================================================================================================

    /// <summary>Cancel a leave that is still gliding and wait for it to unwind, so the caller owns the
    /// tube outright afterwards. Bumping the epoch is what stops the leave placing its note / setting
    /// _hasLeft even if it wins the race to the line after the cancel. No-op when nothing is in flight,
    /// and never throws (a faulted leave already logged itself).</summary>
    private async Task CancelInFlightLeaveAsync()
    {
        Task? task;
        CancellationTokenSource? cts;
        Interlocked.Increment(ref _returnEpoch);
        lock (_leaveGate)
        {
            task = _leaveTask;
            cts = _leaveCts;
            if (task is { IsCompleted: true }) { _leaveTask = null; _leaveCts = null; task = null; cts = null; }
        }
        if (task == null) return;

        try { cts?.Cancel(); } catch { }
        try { await task.ConfigureAwait(true); } catch { }
        lock (_leaveGate)
        {
            if (ReferenceEquals(_leaveTask, task)) { _leaveTask = null; _leaveCts = null; }
        }
    }

    /// <summary>Send the tube home shortly after a verb that deliberately stayed put. Fire-and-forget,
    /// so it carries its own dispatcher/shutdown guard and can never surface an unobserved exception.</summary>
    private void ScheduleGoHome(CancellationToken ct)
    {
        try
        {
            _ = Task.Delay(2000, ct).ContinueWith(t =>
            {
                if (t.IsCanceled) return;
                if (Application.Current?.Dispatcher == null
                    || Application.Current.Dispatcher.HasShutdownStarted) return;
                try { _ = ReturnAsync(CancellationToken.None); } catch { }
            }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
        }
        catch { /* ct already disposed by a torn-down director */ }
    }

    // =================================================================================================
    //  THE NOTE - what is in the tube while the companion is not (wave 2, item C4).
    //
    //  LeaveAsync used to leave an empty frame, which reads as "the avatar broke" rather than "she went
    //  somewhere". A note fixes the attribution in one line: something left, on purpose, and it expects
    //  to come back. Crimson, not ember - ember is the verb (the room is DOING something right now), and
    //  a note is the opposite of that; it is the Lockdown theme sitting still.
    //
    //  Three variants, localized (loc-additions/companion.json, all 9 languages). They are loc keys and
    //  not bark lines because the note is TEXT ON SCREEN with no audio and no per-mod substitution - the
    //  bark packs own what she SAYS, the language files own what the room WRITES.
    // =================================================================================================

    private static readonly string[] NoteKeys =
    {
        "possession_tube_note_1",
        "possession_tube_note_2",
        "possession_tube_note_3",
    };

    /// <summary>English fallbacks, for the case where the key is missing from a language file.</summary>
    private static readonly string[] NoteFallbacks =
    {
        "back soon. you are not.",
        "stepped out. you did not.",
        "gone a moment. you have longer.",
    };

    private static readonly Random NoteRng = new();

    /// <summary>Put the note in the empty tube. Safe with no tube, and safe to call twice.</summary>
    private static void LeaveNote()
    {
        try
        {
            var tube = Tube;
            if (tube == null) return;

            int i = NoteRng.Next(NoteKeys.Length);
            string text;
            try { text = Localization.Loc.Get(NoteKeys[i]); }
            catch { text = string.Empty; }

            // Loc.Get hands back the KEY when a language file is missing it; a card reading
            // "possession_tube_note_2" would be the single least in-character thing in the feature.
            if (string.IsNullOrWhiteSpace(text) || string.Equals(text, NoteKeys[i], StringComparison.Ordinal))
                text = NoteFallbacks[i];

            tube.ShowPossessionNote(text);
        }
        catch (Exception ex) { App.Logger?.Debug("Possession warden note failed: {Error}", ex.Message); }
    }

    /// <summary>Take the note down. Safe when there never was one, and safe to call twice.</summary>
    private static void ClearNote()
    {
        try { Tube?.HidePossessionNote(); }
        catch (Exception ex) { App.Logger?.Debug("Possession warden note removal failed: {Error}", ex.Message); }
    }

    /// <summary>The element's bounds in PHYSICAL screen pixels - the space the tube moves in.
    /// PointToScreen already returns device pixels, so there is no DPI conversion to do here.</summary>
    private static async Task<Rect> GetElementScreenRectAsync(FrameworkElement element)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null) return Rect.Empty;
        try
        {
            return await dispatcher.InvokeAsync(() =>
            {
                try
                {
                    if (!element.IsVisible || !element.IsLoaded) return Rect.Empty;
                    if (element.ActualWidth <= 0 || element.ActualHeight <= 0) return Rect.Empty;
                    if (PresentationSource.FromVisual(element) == null) return Rect.Empty;
                    var tl = element.PointToScreen(new Point(0, 0));
                    var br = element.PointToScreen(new Point(element.ActualWidth, element.ActualHeight));
                    return new Rect(tl, br);
                }
                catch { return Rect.Empty; }
            });
        }
        catch { return Rect.Empty; }
    }

    /// <summary>The haunted window's bounds in PHYSICAL screen pixels.</summary>
    private static async Task<Rect> GetWindowScreenRectAsync()
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null) return Rect.Empty;
        try
        {
            return await dispatcher.InvokeAsync(() =>
            {
                try
                {
                    Window? w = App.MainWindowRef ?? Application.Current?.MainWindow;
                    if (w == null || !w.IsLoaded || w.ActualWidth <= 0 || w.ActualHeight <= 0) return Rect.Empty;
                    if (PresentationSource.FromVisual(w) == null) return Rect.Empty;
                    var tl = w.PointToScreen(new Point(0, 0));
                    var br = w.PointToScreen(new Point(w.ActualWidth, w.ActualHeight));
                    return new Rect(tl, br);
                }
                catch { return Rect.Empty; }
            });
        }
        catch { return Rect.Empty; }
    }

    /// <summary>Route a verb to the bark system (PossessionWarden trigger, ctx verb). Silent when the
    /// bark service is absent - the movement alone still reads.</summary>
    private static void NameIt(string verb)
    {
        try { App.Bark?.NotifyPossessionWarden(verb); }
        catch (Exception ex) { App.Logger?.Debug("Possession warden bark '{Verb}' failed: {Error}", verb, ex.Message); }
    }

    /// <summary>The stare's line. The packs own the words (PossessionWarden / verb_eq stare); the
    /// hardcoded fallback exists only for the case where there is no bark service at all.</summary>
    private static void Say(string verb, string reason)
    {
        if (App.Bark != null) { NameIt(verb); return; }
        try
        {
            App.AvatarWindow?.GigglePriority(FallbackStareLine(reason), playSound: false,
                aiGenerated: false, mood: "possessive");
        }
        catch { /* the tube may be tearing down */ }
    }

    private static string FallbackStareLine(string reason) => reason switch
    {
        EscapeKinds.Close => "still trying the door? i can see you doing it~",
        EscapeKinds.Minimize => "you can hide the window. you cannot hide from me~",
        EscapeKinds.Stop => "no. we are not stopping. look at me~",
        EscapeKinds.SystemKey => "that key does nothing. i do~",
        _ => "i am watching you. i have been the whole time~",
    };
}
