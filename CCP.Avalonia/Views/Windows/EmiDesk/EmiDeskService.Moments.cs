using System;
using System.Collections.Generic;
using Avalonia.Threading;
using ConditioningControlPanel.Services.EmiDesk;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk
{
    /// <summary>
    /// The moment bus: WPF <c>EmiDeskService.cs</c> Fire :661, ReleaseHold :715, Speak :727,
    /// NoteEmiSpoke :782 and the summon greeting :847-950, plus the three summon beats (backSoon,
    /// weekend, bedtimeBroken) and the <c>dismissed</c> fire. The engine (Core EmiLineEngine) decides
    /// WHETHER she speaks; the window decides how it looks. A line is text in her bubble with her
    /// Blipese blips only: there is no spoken word here, so there is nothing to synthesise.
    ///
    /// <para>Offers: the engine deals an ask through <see cref="EmiLineEngine.AskSituationProbe"/> and
    /// the window puts the chips up (EmiDeskService.Offers.cs, EmiDeskWindow.Ask.cs). The goodbye window (FarewellForArcademy) has no caller on this head.</para>
    /// </summary>
    internal sealed partial class EmiDeskService
    {
        /// <summary>Raised for every fire, whether or not she is out (WPF MomentFired).</summary>
        public event EventHandler<EmiMoment>? MomentFired;

        /// <summary>WPF NeverSpeaks: fire, count and stamp, but never a bubble (owner locks).</summary>
        private static readonly HashSet<string> NeverSpeaks =
            new(StringComparer.Ordinal) { "dismissed", "appClosing" };

        /// <summary>The CRT power-on plus the wake chain, plus a beat of air (WPF SummonGreetDelayMs).</summary>
        internal const int SummonGreetDelayMs = 2600;

        private DispatcherTimer? _summonTimer;
        private string? _summonMoment;
        private string _summonVia = "rail";
        private bool _summonTouring;
        private DateTime _outSinceUtc = DateTime.MinValue;
        private DateTime _lastDismissUtc = DateTime.MinValue;
        private int _bedtimeSkips;

        private EmiDeskService()
        {
            // The one sink for every Core and head Fire. Wired on first touch of the service, which
            // App startup does (ApplyHotkey) before any feature can raise a moment.
            EmiDeskBus.Sink = Fire;
            EmiDeskBus.ReleaseSink = ReleaseHold;
            EmiDeskBus.VideoTitleProbe = () => null;   // no LastVideoTitle on this head: the line drops its {target}
            ConditioningControlPanel.Services.HapticService.EmiDeskFire = Fire;
            EmiNames.TargetLabelProbe = id => EmiTargets.Find(id) is { } t ? ConditioningControlPanel.Localization.Loc.Get(t.LabelKey) : null;
            SeedKnockProbes();
            SeedOffers();
        }

        /// <summary>
        /// Tell EMI something happened. Cheap and safe from anywhere: a no-op while she is away
        /// (holds excepted), never throws, never draws on the caller's thread.
        /// </summary>
        public void Fire(string momentId, object? ctx = null)
        {
            if (string.IsNullOrWhiteSpace(momentId)) return;
            try
            {
                Log.Debug("[EmiDesk] moment {Moment} (out={Out})", momentId, IsOut);
                MomentFired?.Invoke(this, new EmiMoment(momentId, ctx));
            }
            catch (Exception ex) { Log.Debug(ex, "[EmiDesk] Fire({Moment}) handler threw", momentId); }

            // HOLDS ARE SAFETY, NOT DECORATION (WPF :673): a panic pressed or a lockdown counting
            // down while she is away must still arm the silence, so a summon landing in the middle
            // of one does not come with chatter.
            if (!IsOut)
            {
                try
                {
                    if (!EmiLineEngine.Instance.IsHoldMoment(momentId)) return;
                    EmiLineEngine.Instance.Draw(momentId, EmiLineEngine.ToCtx(ctx));
                }
                catch (Exception ex) { Log.Debug(ex, "[EmiDesk] hold arm for {Moment} while away failed", momentId); }
                return;
            }

            try
            {
                if (!Dispatcher.UIThread.CheckAccess())
                {
                    Dispatcher.UIThread.Post(() => Speak(momentId, ctx));
                    return;
                }
                if (_window?.PresentationActive != true) Speak(momentId, ctx);
            }
            catch (Exception ex) { Log.Debug(ex, "[EmiDesk] Fire({Moment}) failed", momentId); }
        }

        /// <summary>Let a holdUntilReleased hold go. Releasing a hold nobody holds is a no-op.</summary>
        public void ReleaseHold(string momentId)
        {
            if (string.IsNullOrWhiteSpace(momentId)) return;
            try { EmiLineEngine.Instance.ReleaseHold(momentId); }
            catch (Exception ex) { Log.Debug(ex, "[EmiDesk] ReleaseHold({Moment}) failed", momentId); }
        }

        /// <summary>The speaking half of <see cref="Fire"/>, always on the UI thread (WPF :727).</summary>
        private void Speak(string momentId, object? ctx)
        {
            try
            {
                if (!IsOut) return;
                var win = _window;
                if (win == null || win.PresentationActive || !win.IsVisible) return;

                var dict = EmiLineEngine.ToCtx(ctx);
                var line = EmiLineEngine.Instance.Draw(momentId, dict);
                var ask = EmiLineEngine.Instance.DrawAsk(momentId, dict);
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
            catch (Exception ex) { Log.Debug(ex, "[EmiDesk] Speak({Moment}) failed", momentId); }
        }

        /// <summary>Count an EMI line against the avatar's own min-gap, unless she mutes the avatar (WPF :782).</summary>
        public void NoteEmiSpoke()
        {
            try
            {
                if (AvatarMuted) return;
                Platform.BarkHead.Engine?.NotifyExternalLineSpoken();
            }
            catch (Exception ex) { Log.Debug(ex, "[EmiDesk] bark min-gap stamp failed"); }
        }

        // ---------------------------------------------------------------- the summon greeting

        private int MinutesOut()
        {
            if (_outSinceUtc == DateTime.MinValue) return 0;
            return Math.Max(0, (int)(DateTime.UtcNow - _outSinceUtc).TotalMinutes);
        }

        /// <summary>WPF ChooseGreetMoment :847: the first-ever summon is the introduction, the next
        /// is desktopFirstBoot, everything after is the ordinary summoned.</summary>
        internal static string ChooseGreetMoment(EmiLineEngine? engine = null)
        {
            try
            {
                engine ??= EmiLineEngine.Instance;
                if (!engine.EverSpent("firstContact")) return "firstContact";
                if (!engine.EverSpent("desktopFirstBoot")) return "desktopFirstBoot";
            }
            catch (Exception ex) { Log.Debug(ex, "[EmiDesk] greeting choice fell back to summoned"); }
            return "summoned";
        }

        /// <summary>The tail of WPF Summon :332-395: pick the greeting, schedule it, fire the summon beats.</summary>
        private void OnSummoned(string? why, int summons)
        {
            try
            {
                StartNudges(summons);
                MaybeOfferBookOnSummon(summons);
                _summonMoment = ChooseGreetMoment();
                if (TakeKnockContact() is { } contact) _summonMoment = contact;   // WPF :340: the knock picks by population
                _summonVia = string.Equals(why, "hotkey", StringComparison.OrdinalIgnoreCase) ? "hotkey" : "rail";
                _summonTouring = string.Equals(why, "tour", StringComparison.OrdinalIgnoreCase);
                ScheduleSummonMoment();

                if (_lastDismissUtc != DateTime.MinValue)
                {
                    var gone = DateTime.UtcNow - _lastDismissUtc;
                    if (gone.TotalMinutes <= 5)
                        Fire("backSoon", new { minutes = Math.Max(0, (int)gone.TotalMinutes) });
                }

                var today = DateTime.Now.DayOfWeek;
                if (today == DayOfWeek.Saturday || today == DayOfWeek.Sunday) Fire("weekend", null);

                if (EmiLineEngine.BedtimeSet)
                {
                    _bedtimeSkips++;
                    Fire("bedtimeBroken", new { n = _bedtimeSkips });
                }
                else _bedtimeSkips = 0;
            }
            catch (Exception ex) { Log.Debug(ex, "[EmiDesk] summon beats failed"); }
        }

        /// <summary>The head of WPF Dismiss :455: cancel the greeting, fire the silent goodbye, stamp the clock.</summary>
        private void OnDismissing()
        {
            try
            {
                CancelOfferBeats();
                CancelSummonMoment();
                Fire("dismissed", new { minutes = MinutesOut() });
                _lastDismissUtc = DateTime.UtcNow;
                StopNudges();
            }
            catch (Exception ex) { Log.Debug(ex, "[EmiDesk] dismiss beats failed"); }
        }

        private void ScheduleSummonMoment()
        {
            try
            {
                _outSinceUtc = DateTime.UtcNow;
                CancelSummonMoment();
                _summonTimer = new DispatcherTimer(DispatcherPriority.Background)
                {
                    Interval = TimeSpan.FromMilliseconds(SummonGreetDelayMs)
                };
                _summonTimer.Tick += OnSummonGreetTick;
                _summonTimer.Start();
            }
            catch (Exception ex) { Log.Debug(ex, "[EmiDesk] summon greeting schedule failed"); }
        }

        private void OnSummonGreetTick(object? sender, EventArgs e)
        {
            try
            {
                CancelSummonMoment();
                if (!IsOut) return;
                var moment = _summonMoment;
                _summonMoment = null;
                bool touring = _summonTouring;
                _summonTouring = false;
                ScheduleEmptyLibraryBeat();   // WPF :930: a few seconds behind the hello, never inside it
                if (string.IsNullOrEmpty(moment)) return;
                Fire(moment!, new { via = _summonVia, minutes = MinutesOut(), touring });
            }
            catch (Exception ex) { Log.Debug(ex, "[EmiDesk] summon greeting failed"); }
        }

        private void CancelSummonMoment()
        {
            var t = _summonTimer;
            _summonTimer = null;
            if (t == null) return;
            try { t.Stop(); t.Tick -= OnSummonGreetTick; }
            catch (Exception ex) { Log.Debug(ex, "[EmiDesk] summon greeting cancel failed"); }
        }
    }
}
