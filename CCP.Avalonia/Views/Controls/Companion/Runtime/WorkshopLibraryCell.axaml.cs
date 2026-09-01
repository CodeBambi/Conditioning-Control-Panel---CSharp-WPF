using System;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel.Avalonia.Views.Controls.Companion.Runtime
{
    /// <summary>Z8 · HER LIBRARY. See the XAML header. Pure forwarding.</summary>
    public partial class WorkshopLibraryCell : UserControl
    {
        /// <summary>
        /// Port of the WPF code-behind's <c>Window.GetWindow(this) is MainWindow mw</c> forward:
        /// the host owns the link pool, the cell only reports the click. The Avalonia shell is not
        /// this view's business, so that coupling becomes an event rather than a window cast.
        /// </summary>
        public event EventHandler? AddVideoLinkRequested;

        public WorkshopLibraryCell()
        {
            AvaloniaXamlLoader.Load(this);
            DataContext = new WorkshopLibraryCellViewModel();

            // CompanionWheelRelay is NOT ported: it patches WPF's ScrollViewer marking every wheel
            // notch handled even when it has nothing left to scroll. Avalonia chains the notch on
            // to the parent itself (ScrollViewer.IsScrollChainingEnabled, default true), so the
            // bug the relay exists for does not occur on this head.
            this.FindControl<Button>("BtnAddVideoLink")!.Click +=
                (_, _) => AddVideoLinkRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Strings for the view. See AchievementsTabViewModel for why this class exists.</summary>
    public sealed class WorkshopLibraryCellViewModel
    {
        public string LocLabelCurrentMode => Loc.Get("label_current_mode");
        // Placeholder only, exactly as in WPF: the host overwrites TxtHypnotubeModeLabel.Text with
        // the active content mode (MainWindow.xaml.cs:3029).
        public string LocLabelBambiSleep => Loc.Get("label_bambi_sleep");
        public string LocLabelVideoLinksPoolDesc => Loc.Get("label_video_links_pool_desc");
        public string LocLabelNoVideoLinksYet => Loc.Get("label_no_video_links_yet");
        public string LocBtnAddLinkToPool => Loc.Get("btn_add_link_to_pool");
    }
}
