using System;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using ConditioningControlPanel.Services.EmiDesk;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk
{
    /// <summary>
    /// The knock: WPF <c>EmiDeskService.cs</c> :1022-1150. Once, for someone who has never met her,
    /// she arrives on her own, the dock chip is asked to pulse (<see cref="KnockRequested"/>), and
    /// her opener is chosen by population (new, returning, updated) instead of by summon order. The
    /// machine, its once-ever latch and the offer cap are Core <c>EmiKnock.cs</c>.
    ///
    /// <para>Called from the shell once startup settles (MainShellWindow.EmiKnock.cs, WPF
    /// MainWindow.xaml.cs:4215-4238); EmiDock pulses on <see cref="KnockRequested"/>. Her opener deals
    /// its walk offer through the engine like any other ask (EmiDeskWindow.Ask.cs).</para>
    /// </summary>
    internal sealed partial class EmiDeskService
    {
        private readonly EmiKnockMachine _knock = new();
        private IEmiKnockWorld? _knockWorld;
        private bool _knockPending;

        /// <summary>Raised when she knocks: the dock chip pulses (WPF KnockRequested).</summary>
        public event EventHandler? KnockRequested;

        /// <summary>True between a knock and the summon it opens.</summary>
        public bool KnockPending => _knockPending;

        /// <summary>The live world for <see cref="TryKnock"/>. A seam so a test can stand in for the app.</summary>
        internal Func<string?, IEmiKnockWorld> KnockWorldFactory = seen => new EmiKnockWorld(seen);

        private void SeedKnockProbes()
        {
            EmiKnockWorld.AlreadyOutProbe = () => IsOut;
            EmiKnockWorld.WizardUpProbe = () => false;   // the port's first-run wizard holds the startup ladder, which the caller waits on
            EmiKnockWorld.UpdateDialogUpProbe = () => false;
            EmiKnockWorld.WindowUsableProbe = () =>
            {
                var main = (global::Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
                return main != null && main.IsVisible && main.WindowState != WindowState.Minimized;
            };
        }

        /// <summary>
        /// Knock if the machine allows it: once ever per population, never over a wizard, a session
        /// or a hidden window. True when she actually made it out.
        /// </summary>
        public bool TryKnock(string? seenVersionSnapshot)
        {
            try
            {
                if (!Dispatcher.UIThread.CheckAccess())
                {
                    Dispatcher.UIThread.Post(() => TryKnock(seenVersionSnapshot));
                    return false;
                }

                var world = KnockWorldFactory(seenVersionSnapshot);
                if (!_knock.MayKnock(world))
                {
                    Log.Debug("[EmiDesk] no knock: population={Pop}, state={State}, offers={Offers}",
                        _knock.Population(world), world.KnockState, world.KnockOffers);
                    return false;
                }

                // Spend the latch BEFORE the summon: a knock that throws half way must not repeat.
                EmiState.NoteKnocked();
                _knockWorld = world;
                _knockPending = true;
                Log.Information("[EmiDesk] the knock: population={Pop}, offer {N} of {Cap}",
                    _knock.Population(world), EmiState.Current.KnockOffers, EmiKnockMachine.OfferCap);

                try { KnockRequested?.Invoke(this, EventArgs.Empty); }
                catch (Exception ex) { Log.Debug(ex, "[EmiDesk] knock handler threw"); }

                _ = Summon("knock");

                if (!IsOut)
                {
                    _knockPending = false;
                    Log.Information("[EmiDesk] the knock spent its offer but she never made it out");
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[EmiDesk] TryKnock failed");
                return false;
            }
        }

        /// <summary>The knock's opener for this summon, or null when this summon is not a knock (WPF :340).</summary>
        private string? TakeKnockContact()
        {
            try
            {
                if (!_knockPending) return null;
                _knockPending = false;
                var contact = _knockWorld == null ? null : _knock.ContactMoment(_knockWorld);
                if (!string.IsNullOrEmpty(contact)) Log.Information("[EmiDesk] the knock opens with {Moment}", contact);
                return string.IsNullOrEmpty(contact) ? null : contact;
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "[EmiDesk] knock hand-off failed");
                return null;
            }
        }

        /// <summary>QA: un-latch the knock so it can be seen again (WPF ResetKnock).</summary>
        public void ResetKnock()
        {
            try
            {
                EmiState.ResetKnock();
                _knockPending = false;
                _knockWorld = null;
                Log.Information("[EmiDesk] knock replayed (QA)");
            }
            catch (Exception ex) { Log.Debug(ex, "[EmiDesk] ResetKnock failed"); }
        }
    }
}
