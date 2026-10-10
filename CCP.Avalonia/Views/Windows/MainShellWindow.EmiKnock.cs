using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk;
using ConditioningControlPanel.Services.EmiDesk;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    /// <summary>
    /// EMI knocking and her first-launch hold: WPF MainWindow.xaml.cs :563 / :590-601 (the
    /// <c>firstLaunchEver</c> hold around the wizard) and :4206-4245 (QueueEmiKnock). The knock
    /// waits until nothing owns the screen (the wizard, a startup dialog, a tour), then asks the
    /// machine once. The version it is judged on is the one seen BEFORE this launch stamped
    /// <c>LastSeenVersion</c>, so a fresh install still reads as fresh.
    /// </summary>
    public partial class MainShellWindow
    {
        private string? _emiSeenVersion;
        private bool _emiFirstRunUp;

        /// <summary>Test seam: the poll step and the give-up count (WPF 500 ms x 600 = five minutes).</summary>
        internal static TimeSpan EmiKnockPoll = TimeSpan.FromMilliseconds(500);
        internal static int EmiKnockTries = 600;

        /// <summary>Test seam: the knock itself.</summary>
        internal static Func<string?, bool> EmiKnock = seen => EmiDeskService.Instance.TryKnock(seen);

        /// <summary>Snapshot the seen version and arm the knock for when the shell is on screen.</summary>
        private void HookEmiKnock()
        {
            try
            {
                if (CoreSettings.Service == null) return;   // a render or a headless head: no knock
                _emiSeenVersion = CoreSettings.Current.LastSeenVersion ?? "";
                EmiKnockWorld.WizardUpProbe = () => _emiFirstRunUp;
                Opened += OnEmiKnockShellOpened;
            }
            catch (Exception ex) { Log.Debug(ex, "[EmiDesk] could not arm the knock"); }
        }

        /// <summary>The wizard owns the screen: a HOLD, never a line (MOMENTS 4.B).</summary>
        private void EmiFirstRunBegan()
        {
            _emiFirstRunUp = true;
            EmiDeskBus.Fire("firstLaunchEver");
        }

        /// <summary>The far side of the wizard, however it ended: the hold comes off.</summary>
        private void EmiFirstRunEnded()
        {
            _emiFirstRunUp = false;
            EmiDeskBus.ReleaseHold("firstLaunchEver");
        }

        private void OnEmiKnockShellOpened(object? sender, EventArgs e)
        {
            Opened -= OnEmiKnockShellOpened;
            Dispatcher.UIThread.Post(() => _ = QueueEmiKnockAsync(_emiSeenVersion ?? ""), DispatcherPriority.Normal);
        }

        /// <summary>True while a startup surface, a dialog or a tour owns the screen.</summary>
        internal bool EmiKnockScreenBusy()
        {
            try
            {
                if (_emiFirstRunUp || CoreTutorial.IsActive) return true;
                if (Platform.StartupLadder.PassiveWindowUp()) return true;
                return OwnedWindows.Any(w => w.IsVisible && w is not EmiDeskWindow
                    && (w is FirstRunWizard || (w.GetType().Namespace ?? "").EndsWith(".Dialogs", StringComparison.Ordinal)));
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "[EmiDesk] knock screen probe failed");
                return true;   // unsure is busy: a knock over a dialog is the failure mode
            }
        }

        internal async Task QueueEmiKnockAsync(string seenVersion)
        {
            try
            {
                // SETTLED, not merely "after": past the give-up we simply do not knock. The offer
                // has not been spent, so the next launch can still make it.
                bool settled = false;
                for (int i = 0; i < EmiKnockTries; i++)
                {
                    if (!EmiKnockScreenBusy() && i > 0) { settled = true; break; }
                    await Task.Delay(EmiKnockPoll);
                }
                if (!settled || !IsVisible) return;
                EmiKnock(seenVersion);
            }
            catch (Exception ex) { Log.Debug(ex, "[EmiDesk] the knock could not be queued"); }
        }
    }
}
