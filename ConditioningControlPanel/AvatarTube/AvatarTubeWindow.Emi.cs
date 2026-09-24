using System;
using System.Windows;
using System.Windows.Media.Imaging;
using ConditioningControlPanel.Services.Companion;
namespace ConditioningControlPanel;

public partial class AvatarTubeWindow
{
    private void RefreshEmiDeskVisibility()
        => App.EmiDesk?.SetTubeEmiVisible(EmiTubePreview.SuppressesDesk(
            EmiTubePreview.Available, _currentAvatarSet, IsVisible, _isAttached));

    private void ApplyEmiTubeFit()
    {
        bool isEmi = EmiTubePreview.IsEmi(_currentAvatarSet);
        AvatarBorder.Width = isEmi ? 124 : double.NaN;
        AvatarBorder.Height = isEmi ? 232 : double.NaN;
        AvatarBorder.ClipToBounds = isEmi;
        if (!isEmi)
        {
            if (!_portraitMode)
            {
                ImgAvatar.MaxWidth = LegacyAvatarMaxWidth;
                ImgAvatar.MaxHeight = LegacyAvatarMaxHeight;
            }
            return;
        }

        // EMI is nearly square. The tall sprite slot and its per-set zoom spill out of the glass.
        // Keep the entire bounce/glow layer inside the chamber, including after pop-out.
        bool attached = _isAttached || ModOverridesAttachedTubeOnly();
        AvatarBorder.Margin = attached
            ? new Thickness(5, 100, 106, 278)
            : new Thickness(5, 100, 416, 292);
        AvatarBorder.RenderTransform = null;
        ImgAvatar.LayoutTransform = null;
        ImgAvatar.MaxWidth = 112;
        ImgAvatar.MaxHeight = 156;
    }

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
