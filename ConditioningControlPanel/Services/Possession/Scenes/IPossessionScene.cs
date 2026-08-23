using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ConditioningControlPanel.Services.Possession.Effects;

namespace ConditioningControlPanel.Services.Possession.Scenes;

// =====================================================================================================
//  SCENES - a haunt with a SENTENCE in it. Read Services/Possession/POSSESSION.md first.
//
//  An effect is a word: one control, one verb, undo. That is the right unit for the ladder's lower
//  rungs, where the whole point is deniability. It is the wrong unit from Melt upwards, where the room
//  is supposed to feel authored: the owner's first live run read as "occasional random glitches"
//  precisely because nothing ever happened in a sequence.
//
//  A scene is 3-5 BEATS across several controls over 4-8 s, each beat speaking the same attribution
//  grammar as an effect (charge -> possess -> move -> undo). The warden names the scene once, at the
//  top, so the whole choreography is attributed as ONE act rather than three unexplained twitches.
//
//  Cancellation: RunAsync must return promptly when ct fires (UndoAll / lockdown exit) and must leave
//  nothing behind. The base class below owns that: every beat registers its restore, and UndoAsync
//  runs them in reverse whether the scene finished or was pulled apart mid-sentence.
// =====================================================================================================

/// <summary>One choreography. Instances are shared (the director keeps one of each), so all mutable
/// state belongs to the run, not the object - see <see cref="PossessionSceneBase"/>.</summary>
public interface IPossessionScene
{
    string Id { get; }

    /// <summary>Scenes are a Melt-and-up device by design; below that the room is still pretending
    /// nothing is wrong, and a three-beat sequence gives that away.</summary>
    PossessionRung MinRung { get; }

    /// <summary>Run the choreography. <paramref name="pick"/> hands out victims of a role, already
    /// filtered for cooldown / already-possessed and BOOKED by the director - a scene never has to do
    /// the bookkeeping, and never gets the same control twice. It returns null when nothing suitable
    /// is free, which every scene must survive (a beat is skipped, not faked).</summary>
    Task RunAsync(PossessionContext ctx, IPossessionHost host, Func<PossessionRole, PossessionTarget?> pick, CancellationToken ct);

    /// <summary>Undo everything this run touched, restoring EXACTLY. Safe before RunAsync, twice, and
    /// after the window died. <paramref name="duration"/> Zero is the synchronous crash path.</summary>
    Task UndoAsync(TimeSpan duration);

    /// <summary>Roughly how many live ghosts this scene is worth while it runs. The director charges it
    /// against the concurrency cap so a scene plus a full deck cannot stack into a seizure.</summary>
    int Beats { get; }
}

/// <summary>
/// Shared spine: the beat helper (charge, name, possess, act) and a restore stack that unwinds in
/// reverse. Scenes are stateful only for the duration of one run; the director never starts a second
/// run of the same scene while one is live.
/// </summary>
public abstract class PossessionSceneBase : IPossessionScene
{
    public abstract string Id { get; }
    public virtual PossessionRung MinRung => PossessionRung.Melt;
    public virtual int Beats => 3;

    /// <summary>Guard against a second run landing on top of a live one.</summary>
    public bool IsRunning { get; private set; }

    private readonly List<IDisposable> _handles = new();
    private readonly List<TransformLease> _leases = new();
    private readonly List<Action> _restores = new();
    private bool _named;
    private bool _undone = true;

    protected PossessionContext? Ctx;

    public async Task RunAsync(PossessionContext ctx, IPossessionHost host, Func<PossessionRole, PossessionTarget?> pick, CancellationToken ct)
    {
        if (IsRunning) return;
        IsRunning = true;
        _undone = false;
        _named = false;
        Ctx = ctx;
        try
        {
            await RunCoreAsync(ctx, host, pick ?? (_ => null), ct).ConfigureAwait(true);
        }
        catch (OperationCanceledException) { /* the exit pulled us; UndoAsync does the rest */ }
        catch (Exception ex)
        {
            App.Logger?.Warning("Possession scene {Id} failed: {Error}", Id, ex.Message);
            try { await UndoAsync(TimeSpan.Zero).ConfigureAwait(true); } catch { }
        }
        finally { IsRunning = false; }
    }

    protected abstract Task RunCoreAsync(PossessionContext ctx, IPossessionHost host, Func<PossessionRole, PossessionTarget?> pick, CancellationToken ct);

    public async Task UndoAsync(TimeSpan duration)
    {
        if (_undone) return;
        _undone = true;

        // Reverse order: the last thing that moved is the first thing that comes back, which is what
        // makes the reassembly read as the scene running backwards rather than everything snapping.
        for (int i = _restores.Count - 1; i >= 0; i--)
        {
            try { _restores[i](); } catch (Exception ex) { App.Logger?.Warning("Possession scene {Id} restore failed: {Error}", Id, ex.Message); }
        }
        _restores.Clear();

        for (int i = _leases.Count - 1; i >= 0; i--)
        {
            try
            {
                if (duration <= TimeSpan.Zero) _leases[i].ReleaseImmediate();
                else await _leases[i].ReleaseAsync(TimeSpan.FromMilliseconds(Math.Clamp(duration.TotalMilliseconds, 150, 600))).ConfigureAwait(true);
            }
            catch { }
        }
        _leases.Clear();

        foreach (var h in _handles) { try { h.Dispose(); } catch { } }
        _handles.Clear();
        Ctx = null;
    }

    // ---- beat helpers ---------------------------------------------------------------------------

    /// <summary>The grammar, per beat: ember charge over the control, then the possessed outline, then
    /// the caller moves it. Returns false when the beat should be skipped (no element, cancelled).</summary>
    protected async Task<bool> ChargeAsync(FrameworkElement? el, CancellationToken ct, int chargeMs = 260)
    {
        if (el == null || ct.IsCancellationRequested || Ctx == null) return false;
        try { await Ctx.Attribution.ChargeAsync(el, ct, chargeMs).ConfigureAwait(true); }
        catch (OperationCanceledException) { return false; }
        catch (Exception ex) { App.Logger?.Warning("Possession scene {Id} charge failed: {Error}", Id, ex.Message); }
        if (ct.IsCancellationRequested) return false;
        Possess(el);
        return true;
    }

    protected void Possess(FrameworkElement? el)
    {
        if (el == null || Ctx == null) return;
        try
        {
            var h = Ctx.Attribution.Possess(el);
            if (h != null) _handles.Add(h);
        }
        catch (Exception ex) { App.Logger?.Warning("Possession scene {Id} possess failed: {Error}", Id, ex.Message); }
    }

    /// <summary>Take a transform lease that the base will release for us. Never leaks a lease on a
    /// cancelled beat, which is the failure mode that leaves a control stuck 8 px out of true.</summary>
    protected TransformLease? Lease(FrameworkElement? el)
    {
        var lease = TransformLease.Take(el);
        if (lease != null) _leases.Add(lease);
        return lease;
    }

    /// <summary>Register an exact restore (text, opacity, anything the lease does not cover).</summary>
    protected void OnUndo(Action restore)
    {
        if (restore != null) _restores.Add(restore);
    }

    /// <summary>The warden names the SCENE, once, at the top. A bark per beat would turn a choreography
    /// into a running commentary.</summary>
    protected void NameOnce(string? targetName)
    {
        if (_named || Ctx == null) return;
        _named = true;
        try { Ctx.Name(Id, targetName); }
        catch (Exception ex) { App.Logger?.Warning("Possession scene {Id} name failed: {Error}", Id, ex.Message); }
    }

    protected void EdgePulse(double strength)
    {
        try { Ctx?.Attribution.EdgePulse(strength); } catch { }
    }

    protected Random Rng => Ctx?.Rng ?? Random.Shared;
    protected bool Photosafe => Ctx?.Photosafe == true;

    /// <summary>Photosafe halves every amplitude, exactly as the effects do.</summary>
    protected double Amp(double v) => Photosafe ? v * 0.5 : v;

    protected Task<bool> Beat(double ms, CancellationToken ct) => PossAnim.DelayAsync(ms, ct);

    /// <summary>Swap one glyph in a label for a look-alike and register the exact restore. Returns false
    /// when the text is bound or too short to touch (never fight a binding - it snaps back and reads as
    /// a bug rather than a haunt).</summary>
    protected bool TypoInto(FrameworkElement? el)
    {
        try
        {
            var tb = PossessionVisual.FindTextBlock(el);
            if (!PossessionVisual.IsRewritable(tb, 3)) return false;

            var original = tb!.Text;
            var chars = original.ToCharArray();
            var options = new List<int>();
            for (int i = 1; i < chars.Length; i++)
                if (char.IsLetter(chars[i])) options.Add(i);
            if (options.Count == 0) return false;

            var idx = options[Rng.Next(options.Count)];
            chars[idx] = chars[idx] switch
            {
                'o' or 'O' => '0',
                'l' or 'I' => '1',
                'e' or 'E' => '3',
                'a' or 'A' => '4',
                's' or 'S' => '5',
                't' => '7',
                _ => char.IsUpper(chars[idx]) ? 'X' : 'x',
            };
            tb.Text = new string(chars);
            OnUndo(() => { try { tb.Text = original; } catch { } });
            return true;
        }
        catch { return false; }
    }

    /// <summary>Centre of an element in ghost-layer coordinates, for left-to-right ordering.</summary>
    protected static double CentreX(IPossessionHost host, FrameworkElement el)
    {
        try
        {
            var r = PossessionVisual.BoundsOf(host, el);
            return r.IsEmpty ? 0 : r.X + r.Width / 2;
        }
        catch { return 0; }
    }
}
