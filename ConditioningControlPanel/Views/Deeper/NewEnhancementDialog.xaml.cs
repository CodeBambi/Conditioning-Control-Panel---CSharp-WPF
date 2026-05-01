using System.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models.Deeper;
using ConditioningControlPanel.Services;
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

        // Stub: launches the (forthcoming) interactive Local Video tutorial. Until
        // the step list is built, this just nudges the user toward Browse.
        private void BtnLocalVideoTutorial_Click(object sender, RoutedEventArgs e)
        {
            RbVideo.IsChecked = true;
            MessageBox.Show(this,
                Loc.Get("deeper_tutorial_coming_soon_local_video"),
                Loc.Get("deeper_dialog_new_title"),
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        // Stub: launches the (forthcoming) interactive Local Audio tutorial.
        private void BtnLocalAudioTutorial_Click(object sender, RoutedEventArgs e)
        {
            RbAudio.IsChecked = true;
            MessageBox.Show(this,
                Loc.Get("deeper_tutorial_coming_soon_local_audio"),
                Loc.Get("deeper_dialog_new_title"),
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        // Auto-fills the dialog with a known TikTok HT URL, then kicks off the
        // on-rails interactive tutorial. The user clicks the spotlighted Create
        // button to advance into the editor; the tutorial follows them in.
        private void BtnTryHypnoTubeTutorial_Click(object sender, RoutedEventArgs e)
        {
            string url = "https://hypnotube.com/video/bambis-naughty-tiktok-collection-117314.html";
            try
            {
                foreach (var kvp in AvatarTubeWindow.KnownVideoLinks)
                {
                    if (kvp.Key.IndexOf("tiktok", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        url = kvp.Value;
                        break;
                    }
                }
            }
            catch { /* fall back to default */ }

            RbVideo.IsChecked = true;
            TxtSource.Text = url;

            // Mark settings flag so any future "first-time hint" doesn't double up.
            try
            {
                if (App.Settings?.Current is { } s)
                {
                    s.HasSeenDeeperHTInteractiveTutorial = true;
                    App.Settings?.Save();
                }
            }
            catch { }

            // Two-part tutorial. Part 1 is a single-step overlay that lives
            // inside this dialog and ends when the user clicks Create. Setting
            // the flag here tells DeeperEditorWindow.Loaded to spin up Part 2
            // with a fresh overlay scoped to the editor — sidesteps the cross-
            // window race entirely.
            TutorialEventBus.StartHTPart2OnEditorLoad = true;
            try
            {
                App.Tutorial?.Start(TutorialType.DeeperEditorInteractiveHT);
                if (App.Tutorial != null)
                {
                    var overlay = new ConditioningControlPanel.TutorialOverlay(this, App.Tutorial);
                    overlay.Show();
                }
            }
            catch (System.Exception ex)
            {
                App.Logger?.Warning(ex, "Failed to start HT interactive tutorial");
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
