using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Services.EmiDesk;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk
{
    /// <summary>
    /// Face, pose, chains, the five canon body moves and the idle beats (blink + sway). Port of WPF
    /// Windows/EmiDesk/EmiDeskWindow.xaml.cs :811-1268 and Alive.cs PlayIdleBlink (rows E1-E3).
    /// </summary>
    public partial class EmiDeskWindow
    {
        private static readonly Random Rng = new();

        private readonly EmiChains.Player _player = new(Dispatcher.UIThread);

        // Keyed by POSE: the sheet she is wearing only (WPF :213 / :221). SetOutfit drops both.
        private readonly Dictionary<string, IImage> _bodyCache = new(StringComparer.Ordinal);
        private readonly Dictionary<string, IImage> _overCache = new(StringComparer.Ordinal);
        private readonly Dictionary<string, bool> _overArmed = new(StringComparer.Ordinal);
        private string? _bodySkin;
        private string? _overPose;

        private DispatcherTimer? _idleTimer, _swayTimer;
        private int _swayAt;
        private DispatcherTimer? _moveScaleTween, _moveShiftTween;

        // ---------------------------------------------------------------- face + pose

        /// <summary>Paint one face frame. The chain player's draw hook; safe to call directly.</summary>
        public void DrawFace(string? text, bool small = false, bool flat = false)
        {
            try
            {
                var face = string.IsNullOrEmpty(text) ? RestFace : text;
                bool changed = _faceView.Face != face;
                _faceView.Draw(face, small, flat);
                if (changed) FaceChanged?.Invoke(this, face);
            }
            catch (Exception ex) { Log.Debug(ex, "[EmiDesk] DrawFace failed"); }
        }

        /// <summary>The face string on the glass right now (test seam).</summary>
        internal string? FaceText => _faceView.Face;

        /// <summary>The face on the glass right now. The dock chip's mini face mirrors it (EmiDock).</summary>
        public string Face => _faceView.Face ?? RestFace;

        /// <summary>Raised with the new face string on every frame that changes it (WPF binds
        /// EmiDock.MiniFace onto EmiFace.Face; Avalonia has no such property, so this is the feed).</summary>
        public event EventHandler<string>? FaceChanged;

        /// <summary>The pose key that is up (test seam).</summary>
        internal string PoseKey => _pose;

        /// <summary>Swap the body pose PNG. A no-op when that pose is already up (this runs per sway step).</summary>
        public void SetPose(string? frame)
        {
            try
            {
                var key = EmiChains.FrameKey(frame) ?? "idle";
                if (key == _pose && _bodyImage.Source != null
                    && string.Equals(_bodySkin, _outfit, StringComparison.Ordinal)) return;
                _pose = key;
                if (!_bodyCache.TryGetValue(key, out var img))
                {
                    var path = EmiChains.BodyPath(key, _outfit);
                    if (path == null)
                    {
                        // Art may arrive after the code: keep whatever is up rather than blanking her.
                        Log.Debug("[EmiDesk] body art missing for pose {Pose}", key);
                        _bodyPlaceholder.IsVisible = _bodyImage.Source is null;
                        return;
                    }
                    img = new Bitmap(path);
                    _bodyCache[key] = img;
                }
                _bodyImage.Source = img;
                _bodySkin = _outfit;
                _bodyPlaceholder.IsVisible = false;
                PaintOutfitOver();
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "[EmiDesk] SetPose failed for {Frame}", frame);
            }
        }

        /// <summary>
        /// Dress her in a wardrobe sheet, or take it off with null. THE SKIN LAW's one seam: moves
        /// BOTH halves of a garment (the re-drawn body and the overlay above the glass).
        /// </summary>
        public void SetOutfit(string? outfit)
        {
            try
            {
                var want = EmiChains.OutfitName(outfit);
                if (string.Equals(want, _outfit, StringComparison.Ordinal)) return;
                _outfit = want;
                _bodyCache.Clear();
                _overCache.Clear();
                var pose = _pose;
                _pose = string.Empty;
                SetPose(pose);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "[EmiDesk] SetOutfit failed for {Outfit}", outfit);
            }
        }

        private bool ArmOutfitOver(string outfit)
        {
            if (_overArmed.TryGetValue(outfit, out var armed)) return armed;
            armed = EmiChains.OverPath(outfit, "idle") != null;
            _overArmed[outfit] = armed;
            return armed;
        }

        /// <summary>Lay the overlay for the pose that is up, or take it off (WPF :938).</summary>
        private void PaintOutfitOver()
        {
            try
            {
                var outfit = _outfit;
                if (outfit == null || !ArmOutfitOver(outfit))
                {
                    _outfitOverImage.IsVisible = false;
                    _outfitOverImage.Source = null;
                    _overPose = null;
                    return;
                }
                var key = outfit + "/" + _pose;
                if (key == _overPose && _outfitOverImage.Source != null) return;
                if (!_overCache.TryGetValue(key, out var img))
                {
                    var path = EmiChains.OverPath(outfit, _pose);
                    if (path == null)
                    {
                        // A HALF-PRESENT SHEET IS NO SHEET.
                        _overArmed[outfit] = false;
                        _outfitOverImage.IsVisible = false;
                        _outfitOverImage.Source = null;
                        _overPose = null;
                        return;
                    }
                    img = new Bitmap(path);
                    _overCache[key] = img;
                }
                _outfitOverImage.Source = img;
                _outfitOverImage.IsVisible = true;
                _overPose = key;
            }
            catch (Exception ex) { Log.Debug(ex, "[EmiDesk] outfit overlay paint failed"); }
        }

        // ---------------------------------------------------------------- chains

        private EmiChainHooks BuildHooks(Action? done = null) => new()
        {
            Draw = (t, small, flat) => DrawFace(t, small, flat),
            Bubble = text =>
            {
                try { OnBubbleTextCore(text); }
                catch (Exception ex) { Log.Debug(ex, "[EmiDesk] bubble seam threw"); }
            },
            BodyFrame = SetPose,
            Fx = kind =>
            {
                try { OnChainFxCore(kind); }
                catch (Exception ex) { Log.Debug(ex, "[EmiDesk] fx seam threw"); }
            },
            Move = RunBodyMove,
            Done = () =>
            {
                try
                {
                    // chains.js settles every finished chain back to the resting face and pose.
                    SetPose("idle");
                    DrawFace(EmiChains.RestFace);
                    RestartIdleBeats();
                    done?.Invoke();
                }
                catch (Exception ex) { Log.Debug(ex, "[EmiDesk] chain done failed"); }
            }
        };

        /// <summary>Play a canon chain by id (see <see cref="EmiChains.Chains"/>). Unknown ids are ignored.</summary>
        public void PlayChain(string chainId, Action? done = null, string? bodyFrameOverride = null)
            => PlayChain(EmiChains.Get(chainId), done, bodyFrameOverride);

        /// <summary>Play a chain. Cancels whatever was running; stops the idle beats for its duration.</summary>
        public void PlayChain(EmiChain? chain, Action? done = null, string? bodyFrameOverride = null)
        {
            if (chain == null) return;
            try
            {
                var voice = EmiChains.FrameKey(bodyFrameOverride) ?? EmiChains.FrameKey(chain.BodyFrame);
                if (voice != null) _voxMood = voice;
                StopIdleBeats();
                _player.Play(chain, BuildHooks(done), bodyFrameOverride);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[EmiDesk] PlayChain failed for {Chain}", chain.Id);
            }
        }

        /// <summary>Say a line on the locked . / .. / ... cadence.</summary>
        public void Say(string? line, string reactionFace = "^_^", Action? done = null)
        {
            _voxMood = EmiChains.FrameForFace(reactionFace);
            PlayChain(EmiChains.MakeSay(line, reactionFace, EmiChains.SayHoldMs(line)), done);
        }

        /// <summary>Kill the running chain without firing its done hook.</summary>
        public void CancelChain()
        {
            try
            {
                _player.Cancel();
                OnBubbleTextCore(null);
            }
            catch (Exception ex) { Log.Debug(ex, "[EmiDesk] CancelChain failed"); }
        }

        /// <summary>The mood her voice speaks in (WPF Bubble.cs _voxMood): the pose key of the line.</summary>
        private string _voxMood = "idle";

        // ---------------------------------------------------------------- body moves

        /// <summary>The five canon moves from emi.css (WPF :1094), as tweens on the body transforms.</summary>
        private void RunBodyMove(string move)
        {
            bool handled = false;
            try { OnBodyMoveCore(move, ref handled); }
            catch (Exception ex) { Log.Debug(ex, "[EmiDesk] body-move seam threw"); }
            if (handled) return;

            try
            {
                switch (move)
                {
                    case "bounce": AnimateScalePulse(0.34, 1.08, 0.94); break;
                    case "thud": AnimateScalePulse(0.28, 1.10, 0.88); break;
                    case "nod": AnimateOffset(0, 4, 0.9, 2); break;
                    case "droop": AnimateOffset(0, 6, 0.6, 1); break;
                    case "shiver": AnimateOffset(2, 0, 0.32, 4); break;
                }
            }
            catch (Exception ex) { Log.Debug(ex, "[EmiDesk] body move {Move} failed", move); }
        }

        private void AnimateScalePulse(double seconds, double sx, double sy)
        {
            _moveScaleTween?.Stop();
            _moveScaleTween = TransformTween.Run(_crtScale, TimeSpan.FromSeconds(seconds), new (double, AvaloniaProperty, double)[]
            {
                (0.0, ScaleTransform.ScaleXProperty, 1.0), (0.4, ScaleTransform.ScaleXProperty, sx), (1.0, ScaleTransform.ScaleXProperty, 1.0),
                (0.0, ScaleTransform.ScaleYProperty, 1.0), (0.4, ScaleTransform.ScaleYProperty, sy), (1.0, ScaleTransform.ScaleYProperty, 1.0),
            });
        }

        /// <summary>
        /// WPF AnimateOffset: X runs -dx..dx auto-reversed for <paramref name="repeats"/> cycles of
        /// 2 x seconds; Y runs 0..dy..0 for cycles of <paramref name="seconds"/>. FillBehavior Stop:
        /// both land back on 0.
        /// </summary>
        private void AnimateOffset(double dx, double dy, double seconds, int repeats)
        {
            _moveShiftTween?.Stop();
            var keys = new List<(double, AvaloniaProperty, double)>();
            double total;
            if (dx != 0)
            {
                total = seconds * 2 * repeats;
                int legs = repeats * 2;
                for (int i = 0; i <= legs; i++)
                    keys.Add(((double)i / legs, TranslateTransform.XProperty, i % 2 == 0 ? -dx : dx));
            }
            else
            {
                total = seconds * repeats;
                int legs = repeats * 2;
                for (int i = 0; i <= legs; i++)
                    keys.Add(((double)i / legs, TranslateTransform.YProperty, i % 2 == 0 ? 0 : dy));
            }
            var shift = _moveShift;
            _moveShiftTween = TransformTween.Run(shift, TimeSpan.FromSeconds(total), keys);
            After((int)(total * 1000) + 20, () => { shift.X = 0; shift.Y = 0; });
        }

        // ---------------------------------------------------------------- idle beats

        /// <summary>True when something is on screen that an idle beat must not interrupt.</summary>
        private bool Busy()
        {
            if (_transiting || _player.IsLive || InputLocked) return true;
            bool glass = false;
            try { OnGlassLiveQuery(ref glass); }
            catch (Exception ex) { Log.Debug(ex, "[EmiDesk] glass-live seam threw"); }
            return glass;
        }

        /// <summary>Start (or restart) the idle blink cycle and the idle sway.</summary>
        public void RestartIdleBeats()
        {
            StopIdleBeats();
            if (_closingForGood || !IsVisible) return;
            try
            {
                _idleTimer = new DispatcherTimer(DispatcherPriority.Background)
                {
                    Interval = TimeSpan.FromMilliseconds(EmiAlive.BlinkDelayMs(Rng))
                };
                _idleTimer.Tick += OnIdleTick;
                _idleTimer.Start();

                _swayAt = 0;
                _swayTimer = new DispatcherTimer(DispatcherPriority.Background)
                {
                    Interval = TimeSpan.FromMilliseconds(SwayHold("idle"))
                };
                _swayTimer.Tick += OnSwayTick;
                _swayTimer.Start();
            }
            catch (Exception ex) { Log.Debug(ex, "[EmiDesk] RestartIdleBeats failed"); }
        }

        /// <summary>Stop the idle blink cycle and the sway.</summary>
        public void StopIdleBeats()
        {
            try
            {
                if (_idleTimer != null) { _idleTimer.Stop(); _idleTimer.Tick -= OnIdleTick; _idleTimer = null; }
                if (_swayTimer != null) { _swayTimer.Stop(); _swayTimer.Tick -= OnSwayTick; _swayTimer = null; }
            }
            catch (Exception ex) { Log.Debug(ex, "[EmiDesk] StopIdleBeats failed"); }
        }

        /// <summary>True while the idle beats are on the clock (test seam).</summary>
        internal bool IdleBeatsRunning => _idleTimer != null && _swayTimer != null;

        private void OnIdleTick(object? sender, EventArgs e)
        {
            try
            {
                if (_closingForGood) return;
                // THE CLOCK WANDERS, THE BLINK DOES NOT SKIP: jitter re-rolled every tick.
                if (_idleTimer != null) _idleTimer.Interval = TimeSpan.FromMilliseconds(EmiAlive.BlinkDelayMs(Rng));
                if (Busy()) return;
                PlayIdleBlink();
            }
            catch (Exception ex) { Log.Debug(ex, "[EmiDesk] idle tick failed"); }
        }

        private static int SwayHold(string key)
        {
            if (key != "idle") return EmiChains.SwayStepMs;
            int lo = EmiChains.SwayCentreMinMs;
            int hi = Math.Max(lo, EmiChains.SwayCentreMaxMs);
            return lo + Rng.Next(hi - lo + 1);
        }

        private void OnSwayTick(object? sender, EventArgs e)
        {
            try
            {
                if (_closingForGood || _swayTimer == null) return;
                if (Busy() || _dragging || _resizing)
                {
                    _swayTimer.Interval = TimeSpan.FromMilliseconds(SwayHold("idle"));
                    return;
                }
                var key = EmiChains.SwayCycle[_swayAt % EmiChains.SwayCycle.Count];
                _swayAt++;
                SetPose(key);
                _swayTimer.Interval = TimeSpan.FromMilliseconds(SwayHold(key));
            }
            catch (Exception ex) { Log.Debug(ex, "[EmiDesk] sway tick failed"); }
        }

        /// <summary>WPF Alive.cs:700 - one blink, sometimes two.</summary>
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
            catch (Exception ex) { Log.Debug(ex, "[EmiDesk] blink failed"); }
        }

        private bool BlinkStillOurs()
            => !_closingForGood && IsVisible && !Busy() && !_dragging && !_resizing;

        /// <summary>The chain player goes with the window (WPF :1954 / :1981).</summary>
        private void DisposeChains()
        {
            try
            {
                StopIdleBeats();
                _player.Dispose();
                _moveScaleTween?.Stop();
                _moveShiftTween?.Stop();
            }
            catch (Exception ex) { Log.Debug(ex, "[EmiDesk] chain teardown failed"); }
        }
    }
}
