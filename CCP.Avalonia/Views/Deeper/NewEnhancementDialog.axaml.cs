using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models.Deeper;

namespace ConditioningControlPanel.Avalonia.Views.Deeper
{
    /// <summary>
    /// PORTED from ConditioningControlPanel/Views/Deeper/NewEnhancementDialog.xaml.cs. Deviations:
    ///  - DialogResult becomes Close(bool); callers use ShowDialog&lt;bool&gt;.
    ///  - OpenFileDialog becomes the StorageProvider file picker.
    ///  - The three interactive tutorials and the last-directory memory are stubs: TutorialService,
    ///    TutorialOverlay, EnhancementLibrary and AppSettings all still live in the WPF head.
    /// </summary>
    public partial class NewEnhancementDialog : Window
    {
        public string SelectedMediaType { get; private set; } = MediaTypes.Video;
        public string SelectedSource { get; private set; } = "";

        private readonly RadioButton _rbVideo;
        private readonly RadioButton _rbAudio;
        private readonly TextBox _txtSource;
        private readonly TextBlock _txtError;

        public NewEnhancementDialog()
        {
            AvaloniaXamlLoader.Load(this);

            _rbVideo = this.FindControl<RadioButton>("RbVideo")!;
            _rbAudio = this.FindControl<RadioButton>("RbAudio")!;
            _txtSource = this.FindControl<TextBox>("TxtSource")!;
            _txtError = this.FindControl<TextBlock>("TxtError")!;

            this.FindControl<Button>("BtnBrowse")!.Click += async (_, _) => await BrowseAsync();
            this.FindControl<Button>("BtnLocalVideoTutorial")!.Click += (_, _) => { _rbVideo.IsChecked = true; StartInteractiveTutorial(); };
            this.FindControl<Button>("BtnLocalAudioTutorial")!.Click += (_, _) => { _rbAudio.IsChecked = true; StartInteractiveTutorial(); };
            this.FindControl<Button>("BtnTryHypnoTubeTutorial")!.Click += (_, _) => BtnTryHypnoTubeTutorial_Click();
            this.FindControl<Button>("BtnCancel")!.Click += (_, _) => Close(false);
            this.FindControl<Button>("BtnCreate")!.Click += (_, _) => BtnCreate_Click();
        }

        private async System.Threading.Tasks.Task BrowseAsync()
        {
            var isVideo = _rbVideo.IsChecked == true;
            // ponytail: needs EnhancementLibrary.LastDirectory for SuggestedStartLocation, wired when it moves to Core
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = Loc.Get(isVideo ? "deeper_dialog_pick_video" : "deeper_dialog_pick_audio"),
                AllowMultiple = false,
                FileTypeFilter = new List<FilePickerFileType>
                {
                    isVideo
                        ? new FilePickerFileType("Video files") { Patterns = new[] { "*.mp4", "*.webm", "*.mkv", "*.mov", "*.avi", "*.m4v" } }
                        : new FilePickerFileType("Audio files") { Patterns = new[] { "*.mp3", "*.wav", "*.m4a", "*.aac", "*.flac", "*.ogg" } },
                    FilePickerFileTypes.All,
                },
            });
            if (files.Count > 0)
                _txtSource.Text = files[0].TryGetLocalPath() ?? files[0].Path.ToString();
        }

        private void BtnTryHypnoTubeTutorial_Click()
        {
            // ponytail: needs AvatarTubeWindow.KnownVideoLinks and AppSettings.HasSeenDeeperHTInteractiveTutorial, wired when they move to Core
            _rbVideo.IsChecked = true;
            _txtSource.Text = "https://hypnotube.com/video/bambis-naughty-tiktok-collection-117314.html";
            StartInteractiveTutorial();
        }

        private void StartInteractiveTutorial()
        {
            // ponytail: needs TutorialService + TutorialOverlay + TutorialEventBus.PendingPart2Tutorial, wired when they move to Core
        }

        private void BtnCreate_Click()
        {
            var source = _txtSource.Text?.Trim() ?? "";
            if (string.IsNullOrEmpty(source))
            {
                _txtError.IsVisible = true;
                return;
            }
            SelectedMediaType = _rbVideo.IsChecked == true ? MediaTypes.Video : MediaTypes.Audio;
            SelectedSource = source;
            Close(true);
        }
    }
}
