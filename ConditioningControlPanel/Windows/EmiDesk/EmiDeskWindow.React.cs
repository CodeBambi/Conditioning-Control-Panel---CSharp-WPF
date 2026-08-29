using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using ConditioningControlPanel.Services.EmiDesk;
using Serilog;

// NAMESPACE TRAP: the FLAT ConditioningControlPanel namespace, same as every other file under
// Windows\. See the header of EmiDeskWindow.xaml.cs before "tidying" this.
namespace ConditioningControlPanel;

/// <summary>
/// SHE FEELS THE POINTER. The physical half of her reactions: the squash on a click, the pet on a
/// head click, and the drag wobble.
///
/// <para>Why this file exists at all. On the first live run she was, in the owner's words, dead: a
/// click on her body toggled the ring and nothing about HER moved, and dragging her across the
/// desktop slid a sticker around. Every reaction she had was a chain - a face, a bubble, a line -
/// and chains are the wrong tool for this: they are cancellable, they are rate-limited, they hold
/// the surface, and a line engine that says "not now" would eat the one bit of feedback a click has
/// to give. Touch feedback must be UNCONDITIONAL and instant.</para>
///
/// <para>So: raw storyboards on dedicated transform slots (<c>SquashScale</c>, <c>WobbleRotate</c>
/// in the XAML's transform group), which cannot collide with <c>CrtScale</c>'s power-on or with
/// <c>MoveShift</c>'s nod / droop / shiver. The one thing here that IS routed through
/// <see cref="EmiChains"/> is the pet, because a pet is a performance and she already has one.</para>
/// </summary>
public partial class EmiDeskWindow
{
    // ---------------------------------------------------------------- the squash

    /// <summary>Squash down to this on Y, and the matching stretch on X. emi.css's own pop values.</summary>
    private const double SquashY = 0.92;

    /// <inheritdoc cref="SquashY"/>
    private const double SquashX = 1.06;

    /// <summary>How long the squash takes to reach its deepest point.</summary>
    private const int SquashDownMs = 90;

    /// <summary>...and how long the spring back takes. The two together are one animation.</summary>
    private const int SquashUpMs = 260;

    /// <summary>
    /// The click bump: she compresses about 8 % and springs back. Plays on EVERY click on her body,
    /// alongside whatever that click actually did (the ring toggle, the glass tap, the pet), and it
    /// is never cancelled by anything - it owns its own transform, so it cannot fight a chain.
    /// </summary>
    public void PlayClickSquash()
    {
        try
        {
            if (_closingForGood) return;

            double total = SquashDownMs + SquashUpMs;
            var dur = TimeSpan.FromMilliseconds(total);
            double downAt = SquashDownMs / total;

            var into = new CubicEase { EasingMode = EasingMode.EaseOut };
            // The way back is elastic, not eased: one small overshoot is the difference between
            // "she reacted" and "the widget resized". Oscillations 1 keeps it to that one.
            var back = new ElasticEase { EasingMode = EasingMode.EaseOut, Oscillations = 1, Springiness = 4 };

            var ax = new DoubleAnimationUsingKeyFrames { Duration = dur, FillBehavior = FillBehavior.Stop };
            ax.KeyFrames.Add(new EasingDoubleKeyFrame(SquashX, KeyTime.FromPercent(downAt), into));
            ax.KeyFrames.Add(new EasingDoubleKeyFrame(1.0, KeyTime.FromPercent(1.0), back));

            var ay = new DoubleAnimationUsingKeyFrames { Duration = dur, FillBehavior = FillBehavior.Stop };
            ay.KeyFrames.Add(new EasingDoubleKeyFrame(SquashY, KeyTime.FromPercent(downAt), into));
            ay.KeyFrames.Add(new EasingDoubleKeyFrame(1.0, KeyTime.FromPercent(1.0), back));

            SquashScale.BeginAnimation(ScaleTransform.ScaleXProperty, ax);
            SquashScale.BeginAnimation(ScaleTransform.ScaleYProperty, ay);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] click squash failed");
        }
    }

    // ---------------------------------------------------------------- the pat

    /// <summary>Is this body-local point on her head? The region the HOVER pet arms on.</summary>
    private bool IsOnHead(Point p)
    {
        double bh = _bodyWidth * BodyAspect;
        return p.Y >= 0 && p.Y <= bh * HeadBottomFrac && p.X >= 0 && p.X <= _bodyWidth;
    }

    /// <summary>
    /// The half-second acknowledgement she gives a pat that lands inside the cooldown: one wink and
    /// back to rest, with the pet pose and a bounce, and no line.
    ///
    /// <para>Not the <c>wink</c> chain, which runs 1.28 s and reads as a whole beat of its own. The
    /// point of this one is that it is over before you can pat again, so mashing her reads as
    /// "she keeps noticing" rather than as a queue of animations you have to sit through.</para>
    /// </summary>
    private static readonly EmiChain PetFlickChain = new(
        "petFlick", "PAT (cooling down)",
        new[] { new EmiFrame("^_~", 320), new EmiFrame(EmiChains.RestFace, 180) },
        Move: "bounce", BodyFrame: "pet");

    /// <summary>
    /// LEFT CLICK ANYWHERE ON HER BODY IS A PAT (owner, 2026-08-29). Wave 2 put the pat on her head
    /// only and left the rest of her opening the ring, which meant the obvious gesture - click the
    /// mascot - did the one thing that is not affection, and the report came back as "emi is not
    /// reacting to the pats". Her whole silhouette is the pat now; the ring moved to the right
    /// button and to the cards glyph.
    ///
    /// <para>Returns true whenever the click was consumed, which on this path is ALWAYS. The
    /// caller has already played the squash, so every one of the early exits below still leaves a
    /// visible reaction on screen: a click is never silently swallowed, which was the other half of
    /// the same complaint.</para>
    ///
    /// <para>Inside <see cref="PetCooldownMs"/> she flicks instead of speaking. That is also what
    /// makes a double click harmless: the second click of the pair lands about 200 ms into a 6 s
    /// cooldown, so it can never draw a second line.</para>
    /// </summary>
    private bool PetFromClick()
    {
        try
        {
            if (_transiting || InputLocked) return true;

            // LAW 3: a line in flight is never cut for a pat. Consumed anyway - a click that opened
            // the ring because she happened to be mid-sentence would be the least predictable
            // thing on the desktop.
            if (_player.IsLive) return true;

            // The hover pet and the click pat are one gesture with two triggers. Disarming here
            // stops a pointer that is resting on her head from firing a second pat 1.2 s later.
            DisarmPet();
            _petArmed = true;
            RaiseActivity();

            if (DateTime.UtcNow < _petCooldownUntil)
            {
                PlayChain(PetFlickChain);
                return true;
            }

            _petCooldownUntil = DateTime.UtcNow.AddMilliseconds(PetCooldownMs);
            PlayChain("pet");
            CountPat();
            App.EmiDesk?.Fire("petted");
            return true;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] pat failed");
            return true;
        }
    }

    /// <summary>
    /// Book one pat. Shared by the click pat and the 1.2 s hover pet so the two cannot drift: this
    /// count is what the pet nudge watches to decide the user has the gist, and a pat that did not
    /// count would keep her teaching a lesson that was already learned.
    ///
    /// <para>Only a pat that got PAST the cooldown counts. The winks inside it are acknowledgement,
    /// not affection she registered, and letting a mashed pointer reach the gist in one second
    /// would defeat the point of teaching the gesture at all.</para>
    /// </summary>
    private void CountPat()
    {
        try { EmiState.NotePet(); }
        catch (Exception ex) { Log.Debug(ex, "[EmiDesk] pat bookkeeping failed"); }
    }

    // ---------------------------------------------------------------- the drag wobble

    /// <summary>Hardest she ever leans while being dragged, in degrees.</summary>
    private const double WobbleMaxDeg = 9.0;

    /// <summary>Degrees of lean per DIP/second of horizontal drag speed.</summary>
    private const double WobbleDegPerVel = 0.010;

    /// <summary>How much of the old velocity survives one frame. The low-pass; higher is smoother.</summary>
    private const double WobbleVelKeep = 0.78;

    /// <summary>How far the drawn angle closes on the target angle each frame. The second smoother.</summary>
    private const double WobbleFollow = 0.35;

    /// <summary>Above this drag speed (DIP/s) she pulls a face; above the second one, a worse one.</summary>
    private const double WobbleFaceVel = 420.0;

    /// <inheritdoc cref="WobbleFaceVel"/>
    private const double WobbleDizzyVel = 1150.0;

    /// <summary>The release pendulum's length. Two and a bit swings, each smaller than the last.</summary>
    private const int WobbleSettleMs = 720;

    private bool _wobbleLive;
    private double _wobbleLastX;
    private double _wobbleVx;
    private double _wobbleAngle;
    private DateTime _wobbleLastTick;
    private string? _wobbleFace;

    /// <summary>
    /// Start hanging. Called from the mouse-down, not from the first move, so the very first frame
    /// of a drag already has a velocity baseline to measure against.
    /// </summary>
    private void BeginWobble()
    {
        try
        {
            if (_wobbleLive || _closingForGood) return;
            _wobbleLive = true;
            _wobbleLastX = Left;
            _wobbleVx = 0;
            _wobbleAngle = WobbleRotate.Angle;
            _wobbleLastTick = DateTime.UtcNow;

            // Hand the angle back to us: a settle animation still running from the last drop holds
            // the property, and an animated property ignores writes.
            WobbleRotate.BeginAnimation(RotateTransform.AngleProperty, null);
            WobbleRotate.Angle = _wobbleAngle;

            CompositionTarget.Rendering += OnWobbleFrame;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] wobble start failed");
            _wobbleLive = false;
        }
    }

    /// <summary>
    /// One frame of hanging. Velocity is sampled off her actual window position on the RENDER tick
    /// rather than off the mouse-move, because a mouse that stops moving stops raising events: a
    /// move-driven wobble freezes mid-lean the instant you hold still, which is the one thing that
    /// would make her look broken instead of heavy.
    /// </summary>
    private void OnWobbleFrame(object? sender, EventArgs e)
    {
        try
        {
            if (!_wobbleLive) return;

            var now = DateTime.UtcNow;
            double dt = (now - _wobbleLastTick).TotalSeconds;
            _wobbleLastTick = now;
            if (dt < 0.004) dt = 0.004;
            if (dt > 0.064) dt = 0.064;      // a stalled frame must not read as a huge velocity

            double x = Left;
            double raw = (x - _wobbleLastX) / dt;
            _wobbleLastX = x;

            _wobbleVx = _wobbleVx * WobbleVelKeep + raw * (1.0 - WobbleVelKeep);

            // She TRAILS the hand: drag her right and her feet swing left, which about a head-high
            // pivot is a positive (clockwise) angle in WPF's y-down frame.
            double target = Math.Max(-WobbleMaxDeg, Math.Min(WobbleMaxDeg, _wobbleVx * WobbleDegPerVel));
            _wobbleAngle += (target - _wobbleAngle) * WobbleFollow;
            WobbleRotate.Angle = _wobbleAngle;

            UpdateWobbleFace(Math.Abs(_wobbleVx));
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] wobble frame failed");
        }
    }

    /// <summary>The face she pulls while she is being flung about. Never over a live chain.</summary>
    private void UpdateWobbleFace(double speed)
    {
        try
        {
            if (_player.IsLive) return;

            string? want = speed >= WobbleDizzyVel ? "@_@"
                         : speed >= WobbleFaceVel ? ">_<"
                         : null;

            if (want == _wobbleFace) return;
            _wobbleFace = want;
            DrawFace(want ?? EmiChains.RestFace);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] wobble face failed");
        }
    }

    /// <summary>
    /// Let go. She keeps the lean she had and swings it off in two and a bit diminishing arcs -
    /// the pendulum is what sells the mass, and stopping dead on the drop undoes the whole effect.
    /// </summary>
    private void EndWobble()
    {
        try
        {
            if (!_wobbleLive) return;
            _wobbleLive = false;
            CompositionTarget.Rendering -= OnWobbleFrame;

            if (_wobbleFace != null)
            {
                _wobbleFace = null;
                if (!_player.IsLive) DrawFace(EmiChains.RestFace);
            }

            double a = _wobbleAngle;

            // Under half a degree there is nothing to swing off; snap and stop, so a click that
            // just cleared the drag threshold does not end in a visible wobble.
            if (Math.Abs(a) < 0.5)
            {
                WobbleRotate.BeginAnimation(RotateTransform.AngleProperty, null);
                WobbleRotate.Angle = 0;
                _wobbleAngle = 0;
                return;
            }

            var ease = new SineEase { EasingMode = EasingMode.EaseInOut };
            var anim = new DoubleAnimationUsingKeyFrames
            {
                Duration = TimeSpan.FromMilliseconds(WobbleSettleMs),
                FillBehavior = FillBehavior.Stop
            };
            anim.KeyFrames.Add(new EasingDoubleKeyFrame(-a * 0.55, KeyTime.FromPercent(0.30), ease));
            anim.KeyFrames.Add(new EasingDoubleKeyFrame(a * 0.28, KeyTime.FromPercent(0.58), ease));
            anim.KeyFrames.Add(new EasingDoubleKeyFrame(-a * 0.12, KeyTime.FromPercent(0.82), ease));
            anim.KeyFrames.Add(new EasingDoubleKeyFrame(0.0, KeyTime.FromPercent(1.0), ease));
            anim.Completed += (_, _) =>
            {
                try
                {
                    WobbleRotate.BeginAnimation(RotateTransform.AngleProperty, null);
                    WobbleRotate.Angle = 0;
                }
                catch { /* she is gone */ }
            };

            _wobbleAngle = 0;
            WobbleRotate.BeginAnimation(RotateTransform.AngleProperty, anim);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] wobble settle failed");
            _wobbleLive = false;
        }
    }

    /// <summary>
    /// Everything this file owns, off. Called from the tear-down and the close handler: a
    /// <c>CompositionTarget.Rendering</c> subscription that outlives the window is a per-frame
    /// callback into a dead surface for the rest of the process.
    /// </summary>
    private void TearDownReactions()
    {
        try
        {
            if (_wobbleLive)
            {
                _wobbleLive = false;
                CompositionTarget.Rendering -= OnWobbleFrame;
            }
            _wobbleFace = null;
            WobbleRotate.BeginAnimation(RotateTransform.AngleProperty, null);
            SquashScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            SquashScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            WobbleRotate.Angle = 0;
            SquashScale.ScaleX = 1;
            SquashScale.ScaleY = 1;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] reaction tear-down failed");
        }
    }
}
