using System.Windows;
using System.Windows.Controls;
using ConditioningControlPanel.Services.Companion;
using ConditioningControlPanel.Views.Controls.Companion;

namespace ConditioningControlPanel.Views.Tabs;

public partial class AwarenessTabView
{
    private bool _companionPrivacyHosted;
    private void HostCompanionPrivacy()
    {
        if (!CompanionExperience.IsV2Enabled || _companionPrivacyHosted || Window.GetWindow(this) is not MainWindow owner) return;
        if (owner.CompanionTab.FindName("Room") is not CompanionRoomView room || room.FindName("AwarenessZone") is not AwarenessPrivacyView privacy) return;
        var vm = owner.CompanionTab.Vm;
        if (privacy.Parent is Panel previous) previous.Children.Remove(privacy);
        privacy.DataContext = vm.Awareness;
        CompanionPrivacyHost.Content = privacy;
        CompanionTuningHost.Content = vm.Shelf.Awareness;
        CompanionTuningFold.Visibility = Visibility.Visible;
        _companionPrivacyHosted = true;
        vm.AwarenessVm.Sync();
    }
    internal void RevealCompanionTuning()
    {
        HostCompanionPrivacy();
        CompanionTuningFold.IsExpanded = true;
        CompanionTuningFold.BringIntoView();
    }
}
