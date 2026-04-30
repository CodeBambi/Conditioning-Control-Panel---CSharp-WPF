using System.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models.Deeper;
using Microsoft.Win32;

namespace ConditioningControlPanel.Views.Deeper
{
    public partial class NewEnhancementDialog : Window
    {
        public string SelectedMediaType { get; private set; } = MediaTypes.Video;
        public string SelectedSource { get; private set; } = "";

        public NewEnhancementDialog()
        {
            InitializeComponent();
        }

        private void BtnBrowse_Click(object sender, RoutedEventArgs e)
        {
            var isVideo = RbVideo.IsChecked == true;
            var dialog = new OpenFileDialog
            {
                Title = Loc.Get(isVideo ? "deeper_dialog_pick_video" : "deeper_dialog_pick_audio"),
                Filter = isVideo
                    ? "Video files|*.mp4;*.webm;*.mkv;*.mov;*.avi;*.m4v|All files|*.*"
                    : "Audio files|*.mp3;*.wav;*.m4a;*.aac;*.flac;*.ogg|All files|*.*",
                CheckFileExists = true
            };
            var lastDir = App.EnhancementLibrary?.LastDirectory;
            if (!string.IsNullOrEmpty(lastDir)) dialog.InitialDirectory = lastDir;

            if (dialog.ShowDialog() == true)
            {
                TxtSource.Text = dialog.FileName;
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void BtnCreate_Click(object sender, RoutedEventArgs e)
        {
            var source = TxtSource.Text?.Trim() ?? "";
            if (string.IsNullOrEmpty(source))
            {
                MessageBox.Show(this, Loc.Get("deeper_dialog_source_required"), Loc.Get("deeper_dialog_new_title"),
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            SelectedMediaType = RbVideo.IsChecked == true ? MediaTypes.Video : MediaTypes.Audio;
            SelectedSource = source;
            DialogResult = true;
            Close();
        }
    }
}
