namespace ConditioningControlPanel;
public partial class EmiDeskWindow
{
    internal void SuspendForTube()
    {
        StopIdleBeats();
        CancelChain();
        DisarmPet();
        TearDownReactions();
        OnTearDownCore();
        _vox?.Stop();
        Hide();
    }

    internal void ResumeFromTube()
    {
        Show();
        RestartIdleBeats();
        OnReadyCore();
    }
}
