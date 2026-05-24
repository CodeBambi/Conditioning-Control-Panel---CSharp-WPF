using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace ConditioningControlPanel.Views.Deeper
{
    // Metadata drawer behavior (Mission 1, commit 3 wires it up — commit 2
    // adds the stub so TutorialStep.PrepareTargetWindowAction can already
    // reference it). The drawer itself lives in the sidebar XAML under
    // x:Name "MetadataDrawer" with a chevron toggle "MetadataDrawerToggle"
    // and a content panel "MetadataDrawerContent" that's Collapsed by default.
    public partial class DeeperEditorWindow
    {
        // Programmatic expand — used by tutorial steps that highlight a field
        // inside the (default-collapsed) drawer. No-op until the drawer XAML
        // lands in commit 3; safe to call before then.
        public void ExpandMetadataDrawer()
        {
            try
            {
                var toggle = FindName("MetadataDrawerToggle") as ToggleButton;
                if (toggle != null && toggle.IsChecked != true)
                    toggle.IsChecked = true;

                var content = FindName("MetadataDrawerContent") as FrameworkElement;
                if (content != null)
                    content.Visibility = Visibility.Visible;

                UpdateLayout();
            }
            catch { }
        }
    }
}
