using System;
using Avalonia.Threading;
using ConditioningControlPanel.Services.EmiDesk;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk
{
    /// <summary>
    /// The two beats that follow a summon or a tour: WPF <c>EmiDeskService.cs</c>
    /// ScheduleEmptyLibraryBeat :955 (<c>noMediaYet</c>, 7 s behind the hello) and
    /// MaybeOfferBookOnSummon :871 with <c>EmiCodex.MaybeOfferSoon</c> / <c>MaybeOffer</c>
    /// (EmiCodex.cs :809 / :847, <c>bookOffer</c>, 20 s behind whatever just spoke), plus the
    /// tour-ended route (EmiTourNarrator.cs :195). The engine and the lines file own every limit;
    /// this only decides when to ask.
    /// </summary>
    internal sealed partial class EmiDeskService
    {
        internal const int EmptyLibraryDelayMs = 7000;
        internal const int BookOfferDelayMs = 20_000;

        private DispatcherTimer? _emptyLibraryTimer;
        private DispatcherTimer? _bookOfferTimer;
        private bool _tourHooked;

        private void ScheduleEmptyLibraryBeat()
        {
            try
            {
                Cancel(ref _emptyLibraryTimer);
                if (!EmiOffers.LibraryIsEmpty()) return;
                _emptyLibraryTimer = After(EmptyLibraryDelayMs, () =>
                {
                    _emptyLibraryTimer = null;
                    if (IsOut) EmiOffers.AnnounceEmptyLibrary();
                });
            }
            catch (Exception ex) { Log.Debug(ex, "[EmiDesk] empty-library beat schedule failed"); }
        }

        /// <summary>WPF MaybeOfferBookOnSummon: from the second summon on, to someone who has never opened it.</summary>
        private void MaybeOfferBookOnSummon(int summons)
        {
            try
            {
                HookTourEnd();
                if (summons < 2) return;
                if (EmiState.Current.CodexOpens > 0) return;
                if (EmiLineEngine.Instance.EverSpent("bookOffer")) return;
                MaybeOfferBookSoon("newuser");
            }
            catch (Exception ex) { Log.Debug(ex, "[EmiDesk] book route failed"); }
        }

        /// <summary>WPF EmiCodex.MaybeOfferSoon: one pending offer at a time, checked again at the tick.</summary>
        internal void MaybeOfferBookSoon(string why, int delayMs = BookOfferDelayMs)
        {
            try
            {
                if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Post(() => MaybeOfferBookSoon(why, delayMs)); return; }
                if (_bookOfferTimer != null) return;
                _bookOfferTimer = After(delayMs, () =>
                {
                    _bookOfferTimer = null;
                    MaybeOfferBook(why);
                });
            }
            catch (Exception ex) { Log.Debug(ex, "[EmiDesk] could not schedule the book offer"); }
        }

        /// <summary>WPF EmiCodex.MaybeOffer: not while the book is open, not to someone who has read it.</summary>
        internal void MaybeOfferBook(string? why = null)
        {
            try
            {
                if (_window?.Book is { IsVisible: true }) return;
                if (EmiState.Current.CodexOpens > 0) return;
                Fire("bookOffer", new { why = why ?? "idle" });
            }
            catch (Exception ex) { Log.Debug(ex, "[EmiDesk] book offer failed"); }
        }

        /// <summary>A tour ended, walked or skipped: both are the right beat for the book.</summary>
        private void HookTourEnd()
        {
            if (_tourHooked) return;
            _tourHooked = true;
            CoreTutorial.Finished += (_, completed) => MaybeOfferBookSoon(completed ? "tourFinished" : "tourSkipped");
        }

        private void CancelOfferBeats()
        {
            Cancel(ref _emptyLibraryTimer);
            Cancel(ref _bookOfferTimer);
        }

        private static DispatcherTimer After(int ms, Action step)
        {
            var t = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(Math.Max(1, ms)) };
            t.Tick += (_, _) =>
            {
                t.Stop();
                try { step(); }
                catch (Exception ex) { Log.Debug(ex, "[EmiDesk] beat failed"); }
            };
            t.Start();
            return t;
        }

        private static void Cancel(ref DispatcherTimer? timer)
        {
            var t = timer;
            timer = null;
            try { t?.Stop(); } catch { /* already dead */ }
        }
    }
}
