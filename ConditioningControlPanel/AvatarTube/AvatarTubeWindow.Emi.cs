using System;
using System.Windows.Media.Imaging;
using ConditioningControlPanel.Services.Companion;
namespace ConditioningControlPanel;

public partial class AvatarTubeWindow
{
    private void RefreshEmiDeskVisibility()
        => App.EmiDesk?.SetTubeEmiVisible(EmiTubePreview.SuppressesDesk(
            EmiTubePreview.Available, _currentAvatarSet, IsVisible, _isAttached));

    private static BitmapImage[] LoadEmiTubePoses()
    {
        var poses = new BitmapImage[8];
        for (var i = 0; i < poses.Length; i++)
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.UriSource = new Uri($"pack://application:,,,/ConditioningControlPanel;component/Resources/emi/tube/pose{i + 1}.png");
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.EndInit();
            image.Freeze();
            poses[i] = image;
        }
        return poses;
    }
}
