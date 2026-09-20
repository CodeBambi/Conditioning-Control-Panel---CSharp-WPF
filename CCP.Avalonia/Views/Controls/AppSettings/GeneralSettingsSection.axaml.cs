using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Controls.AppSettings
{
    /// <summary>
    /// SETTINGS ▸ GENERAL, ported from the WPF head. Language, startup, window/tray, Deeper switch.
    ///
    /// The language combo is populated for real from <see cref="LocalizationManager.AvailableLanguages"/>
    /// (Core). The settings logic is restored against <see cref="CoreSettings"/>: the live editors
    /// compare before writing, as on WPF, because the section is seeded from outside and an echo
    /// must not save. What still needs the head is named at each handler: the Windows startup
    /// shortcut, the start-hidden warning dialog, the shell's Deeper door. The startup-video
    /// picker is wired to Avalonia's native <c>StorageProvider</c>; choosing a file only stores
    /// the path, playback of it stays with the video engine.
    /// <c>IAppSettingsSection</c> lives in the WPF head's AppSettingsTabView; <see cref="OnSectionShown"/>
    /// keeps the shape so the host can pick it up when it is ported.
    /// </summary>
    public partial class GeneralSettingsSection : UserControl
    {
        public GeneralSettingsSection()
        {
            InitializeComponent();

            var current = LocalizationManager.Instance.CurrentLanguage;
            for (int i = 0; i < LocalizationManager.AvailableLanguages.Length; i++)
            {
                var (code, displayName, _) = LocalizationManager.AvailableLanguages[i];
                var item = new ComboBoxItem { Content = displayName, Tag = code };
                ToolTip.SetTip(item, displayName);
                CmbLanguageSetting.Items.Add(item);
                if (code == current) CmbLanguageSetting.SelectedIndex = i;
            }
            if (CmbLanguageSetting.SelectedIndex < 0) CmbLanguageSetting.SelectedIndex = 0; // WPF PopulateLanguageCombo falls back to the first entry

            CmbLanguageSetting.SelectionChanged += CmbLanguageSetting_SelectionChanged;
            ChkWinStart.Click += ChkWinStart_Click;
            ChkStartHidden.Click += ChkStartHidden_Click;
            ChkAutoRun.IsCheckedChanged += ChkAutoRun_Changed;
            ChkVidLaunch.IsCheckedChanged += ChkVidLaunch_Changed;
            ChkEnableDeeper.IsCheckedChanged += ChkEnableDeeper_Changed;
            BtnSelectStartupVideo.Click += BtnSelectStartupVideo_Click;
            BtnClearStartupVideo.Click += BtnClearStartupVideo_Click;

            OnSectionShown();   // seed from settings; every handler above compares before writing
        }

        /// <summary>The General language surface, owned by the shell's shared language path.</summary>
        internal ComboBox LanguageSelector => CmbLanguageSetting;

        /// <summary>Re-reads the OS startup registration and the startup-video filename.</summary>
        public void OnSectionShown()
        {
            try
            {
                var s = CoreSettings.Current;
                // ponytail: WPF reconciles RunOnStartup against the Windows startup shortcut here
                // (StartupManager). No equivalent on this head; the box shows the stored value.
                Set(ChkWinStart, s.RunOnStartup);
                // Assign only on a real difference: these raise IsCheckedChanged, and their
                // handlers are live editors.
                Set(ChkStartHidden, s.StartMinimized);
                Set(ChkAutoRun, s.AutoStartEngine);
                Set(ChkVidLaunch, s.ForceVideoOnLaunch);
                Set(ChkEnableDeeper, s.EnableDeeper);
                TxtStartupVideo.Text = string.IsNullOrEmpty(s.StartupVideoPath)
                    ? Loc.Get("label_random")
                    : System.IO.Path.GetFileName(s.StartupVideoPath);
            }
            catch (Exception ex)
            {
                Log.Debug("GeneralSettingsSection.OnSectionShown: {E}", ex.Message);
            }

            static void Set(CheckBox box, bool value)
            {
                if ((box.IsChecked ?? false) != value) box.IsChecked = value;
            }
        }

        private void CmbLanguageSetting_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (CmbLanguageSetting.SelectedItem is not ComboBoxItem selected) return;
            var code = string.IsNullOrWhiteSpace(selected.Tag as string) ? "en" : (string)selected.Tag!;

            // The shell owns the shared writer and re-selects the chrome pill. Keep the direct
            // path for a standalone/headless section, where no shell exists to receive the event.
            var shell = TopLevel.GetTopLevel(this) as MainShellWindow
                ?? (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)
                    ?.MainWindow as MainShellWindow;
            if (shell is not null)
            {
                shell.ApplyLanguageSelection(code);
                return;
            }

            var s = CoreSettings.Current;
            if (s.Language == code) return;
            s.Language = code;
            LocalizationManager.Instance.SetLanguage(code);
            CoreSettings.Save();
        }

        private void ChkWinStart_Click(object? sender, RoutedEventArgs e)
        {
            // ponytail: needs StartupManager (a Windows Startup-folder shortcut); no equivalent on this head yet
        }

        private void ChkStartHidden_Click(object? sender, RoutedEventArgs e)
        {
            // ponytail: WPF first warns (a Yes/No dialog) when hidden is enabled while startup is
            // on, and may revert the box; no dialog on this head yet, so the write is direct.
            var s = CoreSettings.Current;
            var want = ChkStartHidden.IsChecked ?? false;
            if (s.StartMinimized == want) return;
            s.StartMinimized = want;
            CoreSettings.Save();
            Log.Information("Start minimized set to {Enabled} (Settings/General)", want);
        }

        /// <summary>Folder the picker opens in — <c>&lt;effective assets&gt;/videos</c>, as on WPF.</summary>
        internal static string StartupVideoFolder() => Path.Combine(CorePaths.EffectiveAssets, "videos");

        /// <summary>WPF's OpenFileDialog title and filter, as Avalonia picker options.</summary>
        internal static FilePickerOpenOptions BuildStartupVideoPickerOptions() => new()
        {
            Title = Loc.Get("title_select_startup_video"),
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Video Files")
                {
                    Patterns = new[] { "*.mp4", "*.mov", "*.avi", "*.wmv", "*.mkv", "*.webm" },
                },
                new FilePickerFileType("All Files") { Patterns = new[] { "*" } },
            },
        };

        /// <summary>
        /// Commits a picked file. <paramref name="localPath"/> is null when the user cancelled or
        /// when the pick has no local path (a non-local storage provider - this head can only store
        /// a filesystem path), and then settings and the label are left exactly as they were.
        /// </summary>
        internal void ApplyPickedStartupVideo(string? localPath)
        {
            if (string.IsNullOrWhiteSpace(localPath)) return;
            CoreSettings.Current.StartupVideoPath = localPath;
            TxtStartupVideo.Text = Path.GetFileName(localPath);
            CoreSettings.Save();
            // Filename only: the full path is the user's private library layout.
            Log.Information("Startup video set to {File} (Settings/General)", Path.GetFileName(localPath));
        }

        // Tests replace this only while driving the real button handler; production uses the
        // existing MessageDialog below. Keeping the seam here avoids inventing a second picker API.
        internal static Func<Window, string, string, Task>? PickerFeedbackOverride { get; set; }

        private async void BtnSelectStartupVideo_Click(object? sender, RoutedEventArgs e)
        {
            var top = TopLevel.GetTopLevel(this);
            await SelectStartupVideoAsync(top?.StorageProvider, top as Window);
        }

        /// <summary>Runs the native picker flow used by the button handler.</summary>
        internal async Task SelectStartupVideoAsync(IStorageProvider? provider, Window? owner)
        {
            if (provider is not { CanOpen: true })
            {
                Log.Warning("Startup video picker unavailable: this window has no file-opening storage provider");
                if (owner is not null)
                    await ShowPickerFeedbackAsync(owner, "msg_startup_video_picker_unavailable");
                return;
            }

            IReadOnlyList<IStorageFile> files;
            try
            {
                var options = BuildStartupVideoPickerOptions();
                options.SuggestedStartLocation = await TryGetStartFolderAsync(provider);
                files = await provider.OpenFilePickerAsync(options);
            }
            catch (Exception ex)
            {
                // A failed picker must leave the stored startup video alone, but the user needs to
                // know why the button did not change it.
                Log.Warning("Startup video picker failed: {E}", ex.Message);
                if (owner is not null)
                    await ShowPickerFeedbackAsync(owner, "msg_startup_video_picker_failed");
                return;
            }

            if (files.Count != 1) return;   // genuine cancellation

            string? local;
            try
            {
                local = files[0].TryGetLocalPath();
            }
            catch (Exception ex)
            {
                Log.Warning("Startup video pick path could not be read: {E}", ex.Message);
                if (owner is not null)
                    await ShowPickerFeedbackAsync(owner, "msg_startup_video_picker_failed");
                return;
            }

            if (string.IsNullOrWhiteSpace(local))
            {
                Log.Warning("Startup video pick has no local path; keeping the previous selection");
                if (owner is not null)
                    await ShowPickerFeedbackAsync(owner, "msg_startup_video_requires_local_file");
                return;
            }

            ApplyPickedStartupVideo(local);
        }

        private static Task ShowPickerFeedbackAsync(Window owner, string messageKey)
        {
            var title = Loc.Get("title_select_startup_video");
            var message = Loc.Get(messageKey);
            return PickerFeedbackOverride?.Invoke(owner, title, message)
                ?? Dialogs.MessageDialog.ShowAsync(owner, title, message);
        }

        private static async Task<IStorageFolder?> TryGetStartFolderAsync(IStorageProvider provider)
        {
            try
            {
                var folder = StartupVideoFolder();
                if (!Directory.Exists(folder)) return null;
                return await provider.TryGetFolderFromPathAsync(folder);
            }
            catch (Exception ex)
            {
                Log.Debug("Startup video start folder unavailable: {E}", ex.Message);
                return null;
            }
        }

        private void BtnClearStartupVideo_Click(object? sender, RoutedEventArgs e)
        {
            CoreSettings.Current.StartupVideoPath = null;
            TxtStartupVideo.Text = Loc.Get("label_random");
            CoreSettings.Save();
            Log.Information("Startup video cleared - will use random");
        }

        private void ChkAutoRun_Changed(object? sender, RoutedEventArgs e)
        {
            var s = CoreSettings.Current;
            var want = ChkAutoRun.IsChecked ?? false;
            if (s.AutoStartEngine == want) return;   // seeding echo, not a user edit
            s.AutoStartEngine = want;
            CoreSettings.Save();
            Log.Information("Auto-start engine set to {Enabled} (Settings/General)", want);
        }

        private void ChkVidLaunch_Changed(object? sender, RoutedEventArgs e)
        {
            var s = CoreSettings.Current;
            var want = ChkVidLaunch.IsChecked ?? false;
            if (s.ForceVideoOnLaunch == want) return;
            s.ForceVideoOnLaunch = want;
            CoreSettings.Save();
            Log.Information("Force video on launch set to {Enabled} (Settings/General)", want);
        }

        private void ChkEnableDeeper_Changed(object? sender, RoutedEventArgs e)
        {
            var s = CoreSettings.Current;
            var enabled = ChkEnableDeeper.IsChecked ?? true;
            if (s.EnableDeeper == enabled) return;
            s.EnableDeeper = enabled;
            CoreSettings.Save();
            // ponytail: WPF also hides the shell's Deeper door and falls back to Settings if Deeper
            // is the active tab (MainWindow.DeeperTab.cs); that is the shell's, not this section's.
        }
    }
}
