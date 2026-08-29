using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.EmiDesk;
using Serilog;

// NAMESPACE TRAP: the FLAT ConditioningControlPanel namespace, same as every other file under
// Windows\. See the header of EmiDeskWindow.xaml.cs before "tidying" this.
namespace ConditioningControlPanel;

/// <summary>
/// SHE WATCHES YOU. Wave A of <c>docs/emi-desk/ALIVE-PLAN.md</c>: the cursor gaze lean, the
/// approach perk, the hover linger, the micro-fidgets, the rare stretch, the poke ladder, and the
/// blink the idle beats now run on.
///
/// <para>The whole wave rides ONE 100 ms <see cref="DispatcherTimer"/> that runs only while she is
/// on screen. A feature-per-timer version of this file would have five clocks waking the process
/// ten times a second between them, all to answer the same question: where is the pointer. It
/// reads <c>GetCursorPos</c> (there is no document pointermove to borrow on a desktop) and
/// converts to DIPs with HER window's scale, never an assumed 1.0 (THE COORDINATE TRAP, primer
/// section 10.1).</para>
///
/// <para><b>Everything here is the lowest priority thing she owns.</b> A perk, a linger, a fidget
/// or a stretch may only START when nothing else has the face
/// (<see cref="EmiAlive.CanPerk(bool, bool, bool, bool, bool, bool)"/>), and anything that begins
/// afterwards - a pat, a chain, an ask, an engine hold, panic - simply takes the face, because all
/// of those already cancel the running chain. A say is never cut by anything in this file, and
/// nothing in this file ever speaks: wave A is watching, not talking.</para>
/// </summary>
public partial class EmiDeskWindow
{
    // ---------------------------------------------------------------- win32

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out POINT lpPoint);

    // ---------------------------------------------------------------- state

    private DispatcherTimer? _aliveTimer;

    private double _gazeX, _gazeY;               // the lean she is wearing, DIPs
    private double _gazeNudgeX, _gazeNudgeY;     // a fidget's standing look, DIPs
    private DateTime _gazeNudgeUntil = DateTime.MinValue;

    private Point _aliveLastCursor;
    private DateTime _aliveLastTick = DateTime.MinValue;

    private bool _apInside;
    private DateTime _apCoolUntil = DateTime.MinValue;

    private DateTime _lingerFrom = DateTime.MinValue;
    private int _lingerStage;
    private int _lingerPets0;

    private readonly EmiAlive.FidgetScheduler _fidgets = new();
    private DateTime _fidgetDue = DateTime.MaxValue;
    private DateTime _stretchDue = DateTime.MaxValue;

    private readonly EmiAlive.PokeLadder _pokes = new();
    private DateTime _lastPokeAt = DateTime.MinValue;

    // ---------------------------------------------------------------- the chains
    //
    // Authored HERE and not in EmiChains, exactly as PetFlickChain is: EmiChains is a verbatim
    // port of chains.js and the campus has no use for any of these. Every one is wordless.

    /// <summary>The quiet perk: someone walked up to her.</summary>
    private static readonly EmiChain PerkChain = new(
        "perk", "APPROACH (walked up)",
        new[] { new EmiFrame(EmiAlive.PerkFace, EmiAlive.PerkHoldMs) },
        BodyFrame: "idle");

    /// <summary>Two seconds of hovering with no click: expectant.</summary>
    private static readonly EmiChain LingerChain = new(
        "linger", "HOVER LINGER (expectant)",
        new[] { new EmiFrame(EmiAlive.LingerFace, EmiAlive.LingerHoldMs) },
        BodyFrame: "idle");

    /// <summary>Four seconds, still no pat: she looks away, pretending she was not waiting.</summary>
    private static readonly EmiChain LingerAwayChain = new(
        "lingerAway", "HOVER LINGER (look away)",
        new[] { new EmiFrame(EmiAlive.LingerAwayFace, EmiAlive.LingerHoldMs) },
        BodyFrame: "idle");

    /// <summary>The antenna twitch: half a second of nothing much, which is the point.</summary>
    private static readonly EmiChain TwitchChain = new(
        "fidgetTwitch", "FIDGET (antenna twitch)",
        new[] { new EmiFrame(EmiChains.RestFace, 500) },
        BodyFrame: "idle");

    /// <summary>The rare stretch: up four percent, and pleased with herself about it.</summary>
    private static readonly EmiChain StretchChain = new(
        "stretch", "STRETCH (rare)",
        new[]
        {
            new EmiFrame(EmiAlive.StretchFace, EmiAlive.StretchUpMs + EmiAlive.StretchDownMs),
            new EmiFrame(EmiAlive.StretchSettleFace, 700)
        },
        BodyFrame: "idle");

    /// <summary>The second poke inside the window. The pet flick, wearing a look.</summary>
    private static readonly EmiChain PokeAnnoyChain = new(
        "pokeAnnoy", "POKE 2 (annoyed)",
        new[] { new EmiFrame(EmiAlive.PokeAnnoyFace, 700), new EmiFrame(EmiChains.RestFace, 180) },
        BodyFrame: "idle");

    /// <summary>
    /// The third. The canon <c>rage</c> frames and then the glare, held, and WORDLESS: she does not
    /// tell you off, which is funnier and keeps the line engine out of a gesture.
    /// </summary>
    private static readonly EmiChain PokeRageChain = new(
        "pokeRage", "POKE 3 (rage)",
        new[]
        {
            new EmiFrame(">.<", 200), new EmiFrame(">_<", 200), new EmiFrame(">.<", 200),
            new EmiFrame(EmiAlive.PokeRageFace, EmiAlive.PokeRageHoldMs)
        },
        Fx: "storm", Move: "shiver", BodyFrame: "shock");

    // ---------------------------------------------------------------- lifetime

    /// <summary>
    /// True when the motion budget allows the moving half of the wave (the lean, the twitch, the
    /// weight shift, the stretch's scale). The FACES still play at Reduced and Off - a look is not
    /// motion - which is exactly what the campus does under <c>prefers-reduced-motion</c>.
    /// </summary>
    private static bool AliveMotionOk
    {
        get
        {
            try { return MotionFx.Level == MotionLevel.Full; }
            catch { return true; }
        }
    }

    /// <summary>
    /// Start the one wave-A clock. Hung off <see cref="Window.IsVisibleChanged"/> rather than off
    /// the summon, so every road that puts her on screen or takes her off it - the summon, the
    /// dismiss, a bare Hide during teardown - moves the timer with her and none of them has to
    /// remember to.
    /// </summary>
    private void StartAlive()
    {
        try
        {
            if (_closingForGood) return;
            StopAlive();

            _aliveLastTick = DateTime.MinValue;
            _apInside = false;
            _lingerFrom = DateTime.MinValue;
            _lingerStage = 0;
            _pokes.Reset();
            ResetGaze();

            var now = DateTime.UtcNow;
            _fidgetDue = now.AddMilliseconds(_fidgets.NextDelayMs());
            _stretchDue = now.AddMilliseconds(_fidgets.NextStretchDelayMs());

            _aliveTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher)
            {
                Interval = TimeSpan.FromMilliseconds(EmiAlive.PollMs)
            };
            _aliveTimer.Tick += OnAliveTick;
            _aliveTimer.Start();
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] alive poll start failed");
        }
    }

    /// <summary>Stop the wave-A clock and put the lean back to centre.</summary>
    private void StopAlive()
    {
        try
        {
            if (_aliveTimer != null)
            {
                _aliveTimer.Stop();
                _aliveTimer.Tick -= OnAliveTick;
                _aliveTimer = null;
            }
            ResetGaze();
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] alive poll stop failed");
        }
    }

    private void ResetGaze()
    {
        try
        {
            _gazeX = 0;
            _gazeY = 0;
            _gazeNudgeX = 0;
            _gazeNudgeY = 0;
            _gazeNudgeUntil = DateTime.MinValue;
            GazeShift.X = 0;
            GazeShift.Y = 0;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] gaze reset failed");
        }
    }

    // ---------------------------------------------------------------- the tick

    private void OnAliveTick(object? sender, EventArgs e)
    {
        try
        {
            if (Application.Current?.Dispatcher == null) return;
            if (Application.Current.Dispatcher.HasShutdownStarted) return;
            if (_closingForGood || !IsVisible) return;

            var now = DateTime.UtcNow;

            double s = DipScale;
            if (s <= 0) s = 1.0;

            if (!GetCursorPos(out var raw)) return;
            var cursor = new Point(raw.X / s, raw.Y / s);

            var bodyPx = BodyScreenRect;
            var body = new Rect(bodyPx.X / s, bodyPx.Y / s, bodyPx.Width / s, bodyPx.Height / s);
            if (body.Width <= 0 || body.Height <= 0) return;

            // Cursor SPEED in DIPs per millisecond, off the poll itself: the approach test needs to
            // know whether you walked up to her or flew at her.
            double speed = 0;
            if (_aliveLastTick != DateTime.MinValue)
            {
                double ms = (now - _aliveLastTick).TotalMilliseconds;
                if (ms > 0)
                {
                    double dx = cursor.X - _aliveLastCursor.X;
                    double dy = cursor.Y - _aliveLastCursor.Y;
                    speed = Math.Sqrt(dx * dx + dy * dy) / ms;
                }
            }
            _aliveLastCursor = cursor;
            _aliveLastTick = now;

            StepGaze(cursor, body, now);
            StepApproach(cursor, body, now, speed);
            StepLinger(cursor, body, now);
            StepFidgets(now);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] alive tick failed");
        }
    }

    /// <summary>
    /// May a wave-A beat start? The gate, and the whole priority story: anything else that owns her
    /// face outranks every one of these.
    /// </summary>
    private bool CanPerk()
    {
        bool hold = false;
        try { hold = EmiLineEngine.Instance?.HoldActive == true; }
        catch (Exception ex) { Log.Debug(ex, "[EmiDesk] hold probe threw"); }

        return EmiAlive.CanPerk(
            busy: Busy(),
            chainLive: _player.IsLive,
            askLive: AskLive,
            holdActive: hold,
            dragging: _dragging,
            resizing: _resizing);
    }

    // ---------------------------------------------------------------- gaze

    /// <summary>
    /// THE LEAN. The face glyph slides up to three DIPs (scaled by how big she is) toward the
    /// cursor and eases home the moment anything else takes the face or she is picked up.
    ///
    /// <para>It rides a <c>TranslateTransform</c> on the FACE element, under her body's own
    /// transform group, so it composes with the CRT scale, the click squash and the drag wobble for
    /// free instead of fighting them - and it never touches the locked renderer.</para>
    /// </summary>
    private void StepGaze(Point cursor, Rect body, DateTime now)
    {
        try
        {
            bool active = AliveMotionOk && !Busy() && !_dragging && !_resizing;

            double tx = 0, ty = 0;
            if (active)
            {
                if (now < _gazeNudgeUntil)
                {
                    tx = _gazeNudgeX;
                    ty = _gazeNudgeY;
                }
                else
                {
                    (tx, ty) = EmiAlive.GazeTarget(cursor, body, _bodyWidth);
                }
            }

            double k = EmiAlive.GazeEasePerPoll;
            _gazeX = EmiAlive.Ease(_gazeX, tx, k);
            _gazeY = EmiAlive.Ease(_gazeY, ty, k);

            // Settled is settled: snap the last hundredth so the transform stops being rewritten.
            if (Math.Abs(_gazeX - tx) + Math.Abs(_gazeY - ty) < 0.02)
            {
                _gazeX = tx;
                _gazeY = ty;
            }

            GazeShift.X = _gazeX;
            GazeShift.Y = _gazeY;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] gaze step failed");
        }
    }

    /// <summary>A standing look to one side, for the glance fidget. Eased home when it expires.</summary>
    private void NudgeGaze(double dirX, double dirY, int ms)
    {
        try
        {
            _gazeNudgeX = EmiAlive.GazeNudge(dirX, _bodyWidth);
            _gazeNudgeY = EmiAlive.GazeNudge(dirY, _bodyWidth);
            _gazeNudgeUntil = DateTime.UtcNow.AddMilliseconds(Math.Max(1, ms));
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] gaze nudge failed");
        }
    }

    // ---------------------------------------------------------------- approach

    /// <summary>
    /// SHE NOTICES YOU BEFORE YOU TOUCH HER. Crossing into the approach radius earns one beat -
    /// the glance chain if you came at her fast, the quiet perk if you walked - and then nothing at
    /// all for thirty seconds, so she is a mascot and not a bell you can keep ringing.
    /// </summary>
    private void StepApproach(Point cursor, Rect body, DateTime now, double speed)
    {
        try
        {
            bool inside = EmiAlive.WithinApproach(cursor, body);
            if (inside == _apInside) return;
            _apInside = inside;
            if (!inside) return;

            if (now < _apCoolUntil) return;
            _apCoolUntil = now.AddMilliseconds(EmiAlive.ApproachCooldownMs);

            if (!CanPerk()) return;

            if (speed > EmiAlive.GlanceSpeedDipPerMs)
            {
                PlayChain("glance", bodyFrameOverride: "idle");
            }
            else
            {
                PlayChain(PerkChain);
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] approach step failed");
        }
    }

    // ---------------------------------------------------------------- hover linger

    /// <summary>
    /// THE INVITATION. A pointer resting ON her for two seconds with no click is someone deciding
    /// whether to touch her, and she looks like she noticed; two seconds later with still no pat
    /// she looks away. The episode ends the moment the pointer leaves her, and a pat cancels the
    /// look-away outright - the point of it is that the pat never came.
    /// </summary>
    private void StepLinger(Point cursor, Rect body, DateTime now)
    {
        try
        {
            if (!body.Contains(cursor))
            {
                _lingerFrom = DateTime.MinValue;
                _lingerStage = 0;
                return;
            }

            if (_lingerFrom == DateTime.MinValue)
            {
                _lingerFrom = now;
                _lingerStage = 0;
                try { _lingerPets0 = EmiState.Current.PetsTotal; }
                catch (Exception ex) { Log.Debug(ex, "[EmiDesk] linger pet count read failed"); }
                return;
            }

            double held = (now - _lingerFrom).TotalMilliseconds;
            bool touched = _lastPokeAt >= _lingerFrom;
            try { touched = touched || EmiState.Current.PetsTotal > _lingerPets0; }
            catch (Exception ex) { Log.Debug(ex, "[EmiDesk] linger pet count read failed"); }

            if (_lingerStage == 0 && held >= EmiAlive.LingerMs)
            {
                // The stage advances whether or not the face reached the screen: a beat this small
                // is worth exactly one attempt, and retrying it every 100 ms until she happened to
                // be free would make her stare at a parked pointer.
                _lingerStage = 1;
                if (!touched && CanPerk()) PlayChain(LingerChain);
                return;
            }

            if (_lingerStage == 1 && held >= EmiAlive.LingerAwayMs)
            {
                _lingerStage = 2;
                if (!touched && CanPerk()) PlayChain(LingerAwayChain);
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] linger step failed");
        }
    }

    // ---------------------------------------------------------------- fidgets

    /// <summary>
    /// THE THING THAT KEEPS HER FROM BEING A STICKER. The campus gets away without micro-fidgets
    /// because the room behind her moves; a desktop does not, so every 25 to 50 seconds of genuine
    /// idleness she does one small wordless thing, and never the same one twice running.
    /// </summary>
    private void StepFidgets(DateTime now)
    {
        try
        {
            if (now >= _stretchDue)
            {
                if (!CanPerk()) return;               // due, not forced: it waits for a quiet moment
                _stretchDue = now.AddMilliseconds(_fidgets.NextStretchDelayMs());
                _fidgetDue = now.AddMilliseconds(_fidgets.NextDelayMs());
                RunStretch();
                return;
            }

            if (now < _fidgetDue) return;
            if (!CanPerk()) return;
            _fidgetDue = now.AddMilliseconds(_fidgets.NextDelayMs());
            RunFidget(_fidgets.Next());
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] fidget step failed");
        }
    }

    private void RunFidget(EmiFidget kind)
    {
        try
        {
            switch (kind)
            {
                case EmiFidget.Twitch:
                    if (!AliveMotionOk) return;
                    PlayChain(TwitchChain);
                    AnimateOffset(MoveShift, EmiAlive.TwitchDip, 0, 0.22, 1);
                    break;

                case EmiFidget.WeightShift:
                    // No chain and no face: she just leans on the other foot for a moment. It is the
                    // one fidget that can happen without claiming the glass at all.
                    if (!AliveMotionOk) return;
                    RunWeightShift();
                    break;

                case EmiFidget.Glance:
                    // The canon glance, plus a small standing look to the side she glanced at, so
                    // the two halves of "she looked over there" agree.
                    PlayChain("glance", bodyFrameOverride: "idle");
                    NudgeGaze(Rng.Next(2) == 0 ? -1 : 1, 0, 900);
                    break;
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] fidget {Kind} failed", kind);
        }
    }

    /// <summary>
    /// One degree of lean, held two seconds, released. It rides <c>WobbleRotate</c>, which the drag
    /// also uses: <c>BeginWobble</c> hands the property back to itself before it writes, so picking
    /// her up mid-shift takes the rotation over cleanly instead of fighting it.
    /// </summary>
    private void RunWeightShift()
    {
        try
        {
            double deg = Rng.Next(2) == 0 ? -EmiAlive.WeightShiftDeg : EmiAlive.WeightShiftDeg;
            double total = EmiAlive.WeightShiftTravelMs * 2.0 + EmiAlive.WeightShiftHoldMs;

            var ease = new SineEase { EasingMode = EasingMode.EaseInOut };
            var anim = new DoubleAnimationUsingKeyFrames
            {
                Duration = TimeSpan.FromMilliseconds(total),
                FillBehavior = FillBehavior.Stop
            };
            anim.KeyFrames.Add(new EasingDoubleKeyFrame(
                deg, KeyTime.FromPercent(EmiAlive.WeightShiftTravelMs / total), ease));
            anim.KeyFrames.Add(new EasingDoubleKeyFrame(
                deg, KeyTime.FromPercent((EmiAlive.WeightShiftTravelMs + EmiAlive.WeightShiftHoldMs) / total), ease));
            anim.KeyFrames.Add(new EasingDoubleKeyFrame(0.0, KeyTime.FromPercent(1.0), ease));
            anim.Completed += (_, _) =>
            {
                try
                {
                    if (_wobbleLive) return;          // a drag took the property; leave it alone
                    WobbleRotate.BeginAnimation(RotateTransform.AngleProperty, null);
                    WobbleRotate.Angle = 0;
                }
                catch { /* she is gone */ }
            };

            WobbleRotate.BeginAnimation(RotateTransform.AngleProperty, anim);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] weight shift failed");
        }
    }

    /// <summary>
    /// THE STRETCH. Twenty to forty minutes of being out buys one: four percent taller over 400 ms
    /// wearing <c>&gt;_&lt;</c>, then back down and pleased with herself. Rare on purpose - a
    /// surprise on a cooldown of hours stays a surprise.
    /// </summary>
    private void RunStretch()
    {
        try
        {
            PlayChain(StretchChain);
            if (!AliveMotionOk) return;

            double total = EmiAlive.StretchUpMs + EmiAlive.StretchDownMs;
            var up = new CubicEase { EasingMode = EasingMode.EaseOut };
            var down = new CubicEase { EasingMode = EasingMode.EaseInOut };

            var a = new DoubleAnimationUsingKeyFrames
            {
                Duration = TimeSpan.FromMilliseconds(total),
                FillBehavior = FillBehavior.Stop
            };
            a.KeyFrames.Add(new EasingDoubleKeyFrame(
                EmiAlive.StretchScale, KeyTime.FromPercent(EmiAlive.StretchUpMs / total), up));
            a.KeyFrames.Add(new EasingDoubleKeyFrame(1.0, KeyTime.FromPercent(1.0), down));

            var b = a.Clone();
            CrtScale.BeginAnimation(ScaleTransform.ScaleXProperty, a);
            CrtScale.BeginAnimation(ScaleTransform.ScaleYProperty, b);

            Log.Debug("[EmiDesk] stretch");
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] stretch failed");
        }
    }

    // ---------------------------------------------------------------- blink

    /// <summary>
    /// THE BLINK, and it is the single biggest "she is dead" tell there was. The pitch stage rolled
    /// a coin on a 4.2 s tick and, when it won, played the 2.7 s <c>blink</c> CHAIN - so she stood
    /// stone still for anything up to twelve seconds and then blinked twice in a row, and every
    /// blink stopped the idle beats and restarted them.
    ///
    /// <para>This is the campus blink instead (widget.js <c>idle()</c>): a raw lid swap of
    /// <see cref="EmiAlive.BlinkHoldMs"/> ms on a <see cref="EmiAlive.BlinkEveryMs"/> clock, which
    /// claims nothing, cancels nothing and cannot be cancelled - plus 600 ms of jitter so it never
    /// becomes a metronome, and one blink in seven doubled.</para>
    ///
    /// <para>Every step re-checks <c>Busy()</c> and the drag, exactly as the campus's
    /// <c>later()</c> does, because the lid is a bare <see cref="DrawFace"/>: if a chain took the
    /// face while her eyes were shut, the restore must not paint over it.</para>
    /// </summary>
    private void PlayIdleBlink()
    {
        try
        {
            if (_closingForGood || !IsVisible) return;
            if (Busy() || _dragging || _resizing) return;

            bool twice = Rng.Next(EmiAlive.DoubleBlinkOneIn) == 0;

            DrawFace(EmiAlive.BlinkFace);
            After(EmiAlive.BlinkHoldMs, () =>
            {
                if (!BlinkStillOurs()) return;
                DrawFace(EmiChains.RestFace);
                if (!twice) return;

                After(EmiAlive.DoubleBlinkGapMs, () =>
                {
                    if (!BlinkStillOurs()) return;
                    DrawFace(EmiAlive.BlinkFace);
                    After(EmiAlive.BlinkHoldMs, () =>
                    {
                        if (!BlinkStillOurs()) return;
                        DrawFace(EmiChains.RestFace);
                    });
                });
            });
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] blink failed");
        }
    }

    /// <summary>Is the face still ours to paint, one blink step later?</summary>
    private bool BlinkStillOurs()
        => !_closingForGood && IsVisible && !Busy() && !_dragging && !_resizing;

    // ---------------------------------------------------------------- the poke ladder

    /// <summary>
    /// ONE COMPLETED PAT, through the ladder. Called from <c>PetFromClick</c> only: the 1.2 s hover
    /// pet cannot be spammed (the timer only re-arms when the pointer leaves her head and comes
    /// back), so it is a pat by definition and has no rung to climb.
    ///
    /// <para>It does NOT decide what a pat does - <c>PetCooldownMs</c> still does that - it only
    /// says which face the cooldown's flick should wear, which is why the two cannot fight. Three
    /// pats inside four seconds are all inside the six second pet cooldown by construction, so the
    /// ladder only ever re-dresses a flick and can never eat a pat that was going to draw a line.
    /// </para>
    /// </summary>
    private EmiPokeStep NotePoke()
    {
        var now = DateTime.UtcNow;
        _lastPokeAt = now;
        _lingerStage = 2;                    // touched: the look-away has nothing to be about
        try { return _pokes.Note(now); }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] poke ladder failed");
            return EmiPokeStep.Pat;
        }
    }

    /// <summary>The flick a poke earns: the plain wink, the annoyed look, or the glare.</summary>
    private void PlayPokeFlick(EmiPokeStep step)
    {
        switch (step)
        {
            case EmiPokeStep.Rage:
                PlayChain(PokeRageChain);
                break;
            case EmiPokeStep.Annoyed:
                PlayChain(PokeAnnoyChain);
                break;
            default:
                PlayChain(PetFlickChain);
                break;
        }
    }
}
