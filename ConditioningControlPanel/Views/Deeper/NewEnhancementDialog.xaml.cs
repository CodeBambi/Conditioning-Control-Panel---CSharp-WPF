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

        // What tutorial flow launched this dialog (if any). On a successful
        // BtnCreate (validated source, dialog closing with DialogResult=true),
        // we hand this off to TutorialEventBus.PendingPart2Tutorial so the
        // editor's Loaded handler picks up Part 2. Stays null until the user
        // clicks one of the three "walk me through" buttons below.
        private TutorialType? _pendingPart2Tutorial;

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

            // The TutorialOverlay window is Topmost=true to stay over CCP
            // siblings, but that also makes its dim+card render on top of
            // the OS file picker - obscuring the file list and Open/Cancel
            // buttons even with the picker properly owned by us. Toggling
            // Topmost alone wasn't enough (the WPF Topmost change can race
            // with ShowDialog's pump), so we collapse the overlay window
            // entirely for the duration and restore on return. The tutorial
            // service's state is unaffected; only the visual overlay blinks.
            var overlaysToRestore = new System.Collections.Generic.List<Window>();
            try
            {
                if (Application.Current != null)
                {
                    foreach (Window w in Application.Current.Windows)
                    {
                        if (w is ConditioningControlPanel.TutorialOverlay && w.IsVisible)
                        {
                            w.Visibility = Visibility.Hidden;
                            overlaysToRestore.Add(w);
                        }
                    }
                }
            }
            catch { }

            try
            {
                // Owner = this dialog so the OS picker is anchored to
                // NewEnhancementDialog (not the foreground window).
                if (dialog.ShowDialog(this) == true)
                {
                    TxtSource.Text = dialog.FileName;
                }
            }
            finally
            {
                foreach (var w in overlaysToRestore)
                {
                    try { w.Visibility = Visibility.Visible; } catch { }
                }
            }
        }

        // Launches the interactive Local Video tutorial. The user picks any
        // local video file via Browse; Part 1 ends when they click Create with
        // a valid source, then Part 2 walks them through the editor.
        private void BtnLocalVideoTutorial_Click(object sender, RoutedEventArgs e)
        {
            RbVideo.IsChecked = true;
            StartInteractiveTutorial(
                TutorialType.DeeperEditorInteractiveLocalVideo,
                TutorialType.DeeperEditorInteractiveLocalVideoPart2);
        }

        // Launches the interactive Local Audio tutorial. Identical shape to
        // the Local Video flow but anchored on RbAudio + the audio-mode editor
        // (waveform preview, audio-only triggers, etc).
        private void BtnLocalAudioTutorial_Click(object sender, RoutedEventArgs e)
        {
            RbAudio.IsChecked = true;
            StartInteractiveTutorial(
                TutorialType.DeeperEditorInteractiveLocalAudio,
                TutorialType.DeeperEditorInteractiveLocalAudioPart2);
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

            StartInteractiveTutorial(
                TutorialType.DeeperEditorInteractiveHT,
                TutorialType.DeeperEditorInteractiveHTPart2);
        }

        // Common Part 1 launcher. Records which Part 2 to queue (set on
        // BtnCreate_Click only after validation succeeds), then starts the
        // Part 1 overlay scoped to this dialog. Splitting the tutorial into
        // two overlays sidesteps the cross-window state machine entirely.
        private void StartInteractiveTutorial(TutorialType part1, TutorialType part2)
        {
            _pendingPart2Tutorial = part2;
            try
            {
                if (App.Tutorial == null) return;
                if (App.Tutorial.IsActive) App.Tutorial.Skip();
                App.Tutorial.Start(part1);
                var overlay = new ConditioningControlPanel.TutorialOverlay(this, App.Tutorial);
                overlay.Show();
            }
            catch (System.Exception ex)
            {
                App.Logger?.Warning(ex, "Failed to start interactive tutorial Part 1");
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

            // Validation passed - only NOW hand off Part 2 to the editor. If we
            // queued the flag earlier and the user fumbled their first click
            // (empty source), the flag would survive forever and ambush a later
            // unrelated editor-open. Setting it here guarantees one-shot, on-
            // success delivery.
            if (_pendingPart2Tutorial.HasValue)
            {
                TutorialEventBus.PendingPart2Tutorial = _pendingPart2Tutorial.Value;
            }

            DialogResult = true;
            Close();
        }
    }
}
