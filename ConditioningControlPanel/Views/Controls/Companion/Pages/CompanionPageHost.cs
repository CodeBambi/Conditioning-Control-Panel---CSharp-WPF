using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ConditioningControlPanel.Views.Tabs;

namespace ConditioningControlPanel.Views.Controls.Companion.Pages
{
    /// <summary>
    /// The Companion section pages (Personality, Permissions, Links) host the LIVE controls the v2
    /// ConversationPage collapsed away with the old room (commit 7043dfe85): the room's zones and
    /// Workshop cells, never copies, so every x:Name the MainWindow partials write to keeps meaning
    /// the one control on screen. Same borrow AwarenessTabView.HostCompanionPrivacy does.
    /// </summary>
    internal static class CompanionPageHost
    {
        /// <summary>Takes <paramref name="element"/> off whatever holds it and puts it in <paramref name="host"/>.</summary>
        public static void Adopt(FrameworkElement element, ContentControl host)
        {
            if (ReferenceEquals(host.Content, element)) return;
            Detach(element);
            host.Content = element;
        }

        public static void Detach(FrameworkElement element)
        {
            switch (element.Parent)
            {
                case Panel panel: panel.Children.Remove(element); break;
                case ContentControl control: control.Content = null; break;
                case Decorator decorator: decorator.Child = null; break;
            }
            // A cell handed to a templated ContentPresenter has no logical parent, only a visual one.
            if (VisualTreeHelper.GetParent(element) is ContentPresenter presenter)
                presenter.Content = null;
        }

        /// <summary>The companion tab's collapsed room, which still owns the zones until a page adopts them.</summary>
        public static CompanionRoomView? Room(MainWindow? owner) =>
            owner?.CompanionTab?.FindName("Room") as CompanionRoomView;

        public static CompanionTabView? Tab(MainWindow? owner) => owner?.CompanionTab;
    }
}
