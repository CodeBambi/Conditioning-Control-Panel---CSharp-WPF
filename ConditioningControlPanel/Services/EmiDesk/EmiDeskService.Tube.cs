using System;
using System.Windows;
using Serilog;
namespace ConditioningControlPanel.Services.EmiDesk;

public sealed partial class EmiDeskService
{
    private readonly TubeDeskVisibility _tubeVisibility = new();

    /// <summary>Temporarily lend EMI to the attached tube without changing the user's desk choice.</summary>
    public void SetTubeEmiVisible(bool visible)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (_disposed || dispatcher == null || dispatcher.HasShutdownStarted) return;
        if (!dispatcher.CheckAccess()) { dispatcher.BeginInvoke(new Action(() => SetTubeEmiVisible(visible))); return; }
        var action = _tubeVisibility.Update(visible, IsOut && WindowOnScreen);
        try
        {
            if (action.Hide)
            {
                CancelSummonMoment();
                CancelEmptyLibraryBeat();
                _nudgeTimer?.Stop();
                _window?.SuspendForTube();
            }
            else if (action.Restore && IsOut && App.Settings?.Current?.EmiDeskEnabled != false)
            {
                _window?.ResumeFromTube();
                _nudgeTimer?.Start();
            }
        }
        catch (InvalidOperationException ex) { Log.Debug(ex, "[EmiDesk] tube visibility transfer ended during window teardown"); }
    }
}
