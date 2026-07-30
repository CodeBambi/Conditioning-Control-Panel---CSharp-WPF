using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ConditioningControlPanel.Views.Tabs
{
    public partial class AssetsTabView : UserControl
    {
        public AssetsTabView()
        {
            InitializeComponent();
            // FX lifecycle (PR-4b). The Assets tab deliberately has NO ambient loop; this hook
            // only fires the Media Log's unseen-entries pulse when the tab is opened.
            IsVisibleChanged += AssetsTabView_IsVisibleChanged;
        }

        private void AssetsTabView_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.OnAssetsTabVisibilityChanged(IsVisible);
        }

        private void PackCard_MouseEnter(object sender, MouseEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.OnPackCardHover(sender as FrameworkElement, true);
        }

        private void PackCard_MouseLeave(object sender, MouseEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.OnPackCardHover(sender as FrameworkElement, false);
        }

        private void AssetTreeRow_MouseEnter(object sender, MouseEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.OnAssetTreeRowHover(sender as FrameworkElement, true);
        }

        private void AssetTreeRow_MouseLeave(object sender, MouseEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.OnAssetTreeRowHover(sender as FrameworkElement, false);
        }

        private void BtnCreatorDiscord_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnCreatorDiscord_Click(sender, e);
        }
        private void BtnDeleteAssetPreset_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnDeleteAssetPreset_Click(sender, e);
        }
        private void BtnDeleteDownloadedPacks_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnDeleteDownloadedPacks_Click(sender, e);
        }
        private void BtnDeselectAllAssets_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnDeselectAllAssets_Click(sender, e);
        }
        private void BtnGetPacks_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnGetPacks_Click(sender, e);
        }
        private void BtnOpenAssetsFolder_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnOpenAssetsFolder_Click(sender, e);
        }
        private void BtnMediaLog_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var win = new ConditioningControlPanel.MediaHistoryWindow();
                var owner = Window.GetWindow(this);
                if (owner != null && owner.IsLoaded) win.Owner = owner;
                win.Show();
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "Failed to open Media Log window");
            }
        }
        private void BtnPackActivate_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnPackActivate_Click(sender, e);
        }
        private void BtnPackDownload_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnPackDownload_Click(sender, e);
        }
        private void BtnRefreshAssets_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnRefreshAssets_Click(sender, e);
        }
        private void BtnRefreshPacks_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnRefreshPacks_Click(sender, e);
        }
        private void BtnSaveAssetPreset_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnSaveAssetPreset_Click(sender, e);
        }
        private void BtnSelectAllAssets_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnSelectAllAssets_Click(sender, e);
        }
        private void BtnUpdateAssetPreset_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnUpdateAssetPreset_Click(sender, e);
        }
        private void CmbAssetPresets_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.CmbAssetPresets_SelectionChanged(sender, e);
        }
        private void FolderCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.FolderCheckBox_Changed(sender, e);
        }
        private void PacksScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.PacksScrollViewer_PreviewMouseWheel(sender, e);
        }
        private void ThumbnailCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.ThumbnailCheckBox_Changed(sender, e);
        }
        private void ThumbnailItem_Click(object sender, MouseButtonEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.ThumbnailItem_Click(sender, e);
        }

        private void AssetTreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.AssetTreeView_SelectedItemChanged(sender, e);
        }
        private void ThumbnailItem_OpenInExplorer_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.ThumbnailItem_OpenInExplorer_Click(sender, e);
        }
        private void ThumbnailItem_Preview_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.ThumbnailItem_Preview_Click(sender, e);
        }
    }
}
