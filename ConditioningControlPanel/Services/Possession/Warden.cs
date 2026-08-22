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

    private static AvatarTubeWindow? Tube => App.AvatarWindow;

    public bool IsAvailable
    {
        get
        {
            try
            {
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
    public async Task LeaveAsync(CancellationToken ct)
    {
        var tube = Tube;
        if (tube == null || _hasLeft) return;
        if (Interlocked.Exchange(ref _busy, 1) != 0) return;

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(VerbTimeout);
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
            var wa = screen.WorkingArea;

            _lastAppearance = DateTime.UtcNow;
            _hasLeft = true;
            await tube.GlideToScreenPointAsync(new Point(self.X, wa.Bottom), token, clampToWorkArea: false)
                      .ConfigureAwait(true);

            NameIt("leave");
        }
        catch (OperationCanceledException) { /* lockdown ended mid-exit; ReturnAsync still restores */ }
        catch (Exception ex) { App.Logger?.Warning("Possession warden leave failed: {Error}", ex.Message); }
        finally { Interlocked.Exchange(ref _busy, 0); }
    }

    // =================================================================================================
    //  RETURN - reassembly. Must be safe to call twice, and safe when nothing ever moved.
    // =================================================================================================
    public async Task ReturnAsync(CancellationToken ct)
    {
        var tube = Tube;
        if (tube == null) { _hasLeft = false; return; }

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
