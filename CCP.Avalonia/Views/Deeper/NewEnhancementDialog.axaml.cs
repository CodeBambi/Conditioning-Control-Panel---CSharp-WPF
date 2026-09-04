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
    ///  - The three interactive tutorials and the last-directory memory are stubs. AppSettings is
    ///    NOT what blocks them - the HypnoTube flow's one settings write is restored below - and
    ///    nor is the overlay, which is ported
    ///    (CCP.Avalonia/Views/Windows/TutorialOverlay.axaml.cs). See StartInteractiveTutorial and
    ///    BrowseAsync for what each one is actually waiting on.
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
            // ponytail: WPF seeds InitialDirectory from App.EnhancementLibrary.LastDirectory;
            // ConditioningControlPanel/Services/Deeper/EnhancementLibrary.cs is still head-only, so
            // there is no SuggestedStartLocation and the picker opens where the OS last left it.
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
            // ponytail: WPF prefers the first TikTok entry of
            // ConditioningControlPanel/AvatarTube/AvatarTubeWindow.Speech.cs KnownVideoLinks and
            // falls back to this literal; that window is WebView2-hosted and not on this head, so
            // only the fallback is available - which is the value WPF ships when the lookup misses.
            _rbVideo.IsChecked = true;
            _txtSource.Text = "https://hypnotube.com/video/bambis-naughty-tiktok-collection-117314.html";

            // Mark the flag so a future first-time hint does not double up. Same write WPF makes,
            // and it lands here for the same reason: the user has now seen the walkthrough offer.
            try
            {
                CoreSettings.Current.HasSeenDeeperHTInteractiveTutorial = true;
                CoreSettings.Save();
            }
            catch { /* a settings write must never take the dialog down */ }

            StartInteractiveTutorial();
        }

        /// <summary>
        /// ponytail: still a no-op, but NOT for the reason the old note gave. TutorialOverlay is
        /// ported (CCP.Avalonia/Views/Windows/TutorialOverlay.axaml.cs, live ctor
        /// <c>TutorialOverlay(Window)</c>) and App.Tutorial is now the <c>CoreTutorial</c> seam,
        /// so the two things that note named as head-only are both here. What blocks it:
        ///
        /// <list type="number">
        ///   <item>Nothing seeds <c>CoreTutorial</c> on this head (CCP.Avalonia/App.axaml.cs:71) -
        ///     the step lists are sentences about WPF controls and stay in
        ///     ConditioningControlPanel/Services/TutorialService.cs. <c>Start</c> is a silent
        ///     no-op, so showing the overlay would dim this dialog behind an empty card, which is
        ///     worse than the button doing nothing visible.</item>
        ///   <item>Part 2 needs <c>TutorialEventBus.PendingPart2Tutorial</c>
        ///     (ConditioningControlPanel/Services/TutorialEventBus.cs). <c>CoreTutorial</c> carries
        ///     no event bus by design, so that hand-off has no seam at all - it is an addition to
        ///     Core, not a call into it. The WPF original sets it in BtnCreate_Click, after
        ///     validation, precisely so a fumbled first click cannot leave the flag armed forever.</item>
        /// </list>
        ///
        /// <para>Part 1's whole visible effect - pick the media type, pre-fill the source - already
        /// happens in the three callers. Restore as:
        /// <c>if (CoreTutorial.IsActive) CoreTutorial.Skip(); CoreTutorial.Start(part1); if
        /// (!CoreTutorial.IsActive) return; new TutorialOverlay(this).Show();</c> - where
        /// <c>part1</c> is the WPF <c>TutorialType</c> name as a string
        /// ("DeeperEditorInteractiveLocalVideo" / "…LocalAudio" / "…HT"), since the seam takes a
        /// name rather than an enum Core refuses to copy.</para>
        /// </summary>
        private void StartInteractiveTutorial()
        {
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
