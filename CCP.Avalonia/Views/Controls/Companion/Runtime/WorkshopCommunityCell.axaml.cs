using System;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel.Avalonia.Views.Controls.Companion.Runtime
{
    /// <summary>Z8 · COMMUNITY. See the XAML header. Pure forwarding.</summary>
    public partial class WorkshopCommunityCell : UserControl
    {
        // The WPF cell forwards each click to MainWindow (Window.GetWindow(this) is MainWindow).
        // This head has no MainWindow API surface yet and the cell must not grow App. coupling, so
        // the four actions leave the cell as events and the host wires them - the same contract,
        // one indirection later.
        public event EventHandler? BrowsePromptsRequested;
        public event EventHandler? ImportPromptRequested;
        public event EventHandler? ExportPromptRequested;
        public event EventHandler? RefreshPromptsRequested;

        public WorkshopCommunityCell()
        {
            AvaloniaXamlLoader.Load(this);
            DataContext = new WorkshopCommunityCellViewModel();

            this.FindControl<Button>("BtnBrowsePrompts")!.Click += (_, _) => BrowsePromptsRequested?.Invoke(this, EventArgs.Empty);
            this.FindControl<Button>("BtnImportPrompt")!.Click += (_, _) => ImportPromptRequested?.Invoke(this, EventArgs.Empty);
            this.FindControl<Button>("BtnExportPrompt")!.Click += (_, _) => ExportPromptRequested?.Invoke(this, EventArgs.Empty);
            this.FindControl<Button>("BtnRefreshPrompts")!.Click += (_, _) => RefreshPromptsRequested?.Invoke(this, EventArgs.Empty);

            // CompanionWheelRelay.Attach(InstalledPromptsScroll) is NOT ported: it works around
            // WPF's ScrollViewer marking every wheel notch handled even when it cannot scroll.
            // Avalonia's ScrollViewer.IsScrollChainingEnabled (default true) already passes an
            // unusable notch to the parent scroll, which is exactly what the relay re-implements.
        }
    }

    /// <summary>Strings from CCP.Core's Loc. See the porting notes in the repo-root CLAUDE.md
    /// for why {loc:Str} becomes a binding.</summary>
    public sealed class WorkshopCommunityCellViewModel
    {
        public string LocBeta => Loc.Get("label_beta");
        public string LocBrowse => Loc.Get("btn_browse");
        public string LocImport => Loc.Get("btn_import");
        public string LocExport => Loc.Get("btn_export");
        public string LocInstalledPrompts => Loc.Get("label_installed_prompts");
        public string LocRefresh => Loc.Get("btn_refresh");
        public string LocNoPromptsInstalled => Loc.Get("label_no_prompts_installed_yet");
    }
}
