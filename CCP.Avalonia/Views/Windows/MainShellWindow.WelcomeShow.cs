// EMI's welcome show doors on the shell (WPF MainWindow.xaml.cs BtnFirstShowReplay_Click :1508 and the
// "first-show" startup modal :581). The show itself lives in Views/Windows/WelcomeShow.

using System;
using Avalonia.Interactivity;
using ConditioningControlPanel.Avalonia.Views.Windows.WelcomeShow;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        /// <summary>Test seam: stands in for <see cref="FirstShowService.Open"/>.</summary>
        internal Func<MainShellWindow, bool> OpenWelcomeShow = shell => FirstShowService.Open(shell);

        /// <summary>Help: "Watch Emi's demo". The service refuses over a session, Lockdown or Strict Lock.</summary>
        private void BtnFirstShowReplay_Click(object? sender, RoutedEventArgs e)
        {
            try { OpenWelcomeShow(this); }
            catch (Exception ex) { Log.Warning(ex, "[FirstShow] replay from Help failed"); }
        }

        /// <summary>The far side of the first-run wizard: the show, only once the age gate was accepted
        /// and the shell is still up (the wizard can end in a quit).</summary>
        internal void OfferWelcomeShowAfterFirstRun()
        {
            try
            {
                if (CoreSettings.Current?.HasAcceptedAgeVerification != true || !IsVisible) return;
                OpenWelcomeShow(this);
            }
            catch (Exception ex) { Log.Warning(ex, "[FirstShow] first-run show failed to open"); }
        }
    }
}
