using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace ConditioningControlPanel.Avalonia.Views.Features
{
    /// <summary>
    /// Scheduler panel, ported from the WPF head. On WPF every handler round-trips
    /// App.Settings.Current.Scheduler* and the SettingsHook/ISettingsRebindable pair keeps the
    /// panel following a cloud-restored settings instance. AppSettings still lives in the WPF
    /// head, so this port renders the markup defaults and persists nothing.
    /// </summary>
    public partial class SchedulerFeatureControl : UserControl
    {
        public SchedulerFeatureControl()
        {
            AvaloniaXamlLoader.Load(this);
            // ponytail: needs App.Settings (Scheduler* fields) + SettingsHook, wired when they move to Core.
            // The ChkEnabled / TxtStart / TxtEnd / Day* handlers are pure settings writes; nothing
            // view-side is left for them to do, so they are not stubbed one by one.
        }
    }
}
