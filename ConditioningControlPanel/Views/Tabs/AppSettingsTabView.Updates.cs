namespace ConditioningControlPanel.Views.Tabs
{
    /// <summary>
    /// Settings · UPDATES compat surface. No MainWindow partial writes these today (the check
    /// button's handler is self-contained, as it was in the AppInfo popup); they are exposed so the
    /// Ctrl+K palette and the render tests can reach them by name, same as every other section.
    /// </summary>
    public partial class AppSettingsTabView
    {
        internal System.Windows.Controls.Button BtnCheckUpdates => SectionUpdates.BtnCheckUpdates;
        internal System.Windows.Controls.Button BtnViewPatchNotes => SectionUpdates.BtnViewPatchNotes;
        internal System.Windows.Controls.TextBlock TxtUpdatesVersion => SectionUpdates.TxtUpdatesVersion;
        internal System.Windows.Controls.TextBlock TxtPatchNotes => SectionUpdates.TxtPatchNotes;
    }
}
