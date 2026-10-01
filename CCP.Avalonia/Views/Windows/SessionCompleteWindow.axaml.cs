using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    /// <summary>
    /// The end-of-session recap: a random card image, the headline, three stats and the list of
    /// media the session played.
    ///
    /// PORTED from ConditioningControlPanel/Windows/SessionCompleteWindow.xaml.cs. Deviations:
    ///  - Takes Core's <see cref="SessionLog"/>; the WPF legacy raw-field overload is dropped.
    ///  - <c>DialogResult = true</c> becomes <c>Close(true)</c>, as in TextEditorDialog; the
    ///    "shown non-modally" guard the WPF comment describes is not needed - Avalonia's Close
    ///    works either way.
    ///  - <c>PreviewKeyDown</c> -> <c>KeyDown</c>. Escape still closes.
    ///  - The per-row Click is one handler on the ItemsControl; the row Button carries the path in
    ///    Tag exactly as before.
    /// </summary>
    public partial class SessionCompleteWindow : Window
    {
        // Resource-relative paths - the mod compatibility surface. A mod's
        // resources/Cards/<name>.png wins; never rename these strings.
        private static readonly string[] CardImages = new[]
        {
            "Cards/fireworks.png",
            "Cards/hearth.png",
            "Cards/spotlight.png"
        };

        /// <summary>Render constructor: sample data, so --render-all can discover the window.</summary>
        internal SessionCompleteWindow() : this(SampleLog()) { }

        public SessionCompleteWindow(SessionLog log, bool playSound = true)
        {
            AvaloniaXamlLoader.Load(this);

            LoadRandomCard();
            ApplyLog(log);

            if (playSound && log.Completed)
            {
                PlayCompletionSound();
            }

            this.FindControl<Button>("BtnClose")!.Click += (_, _) => CloseRecap();
            this.FindControl<ItemsControl>("MediaList")!.AddHandler(Button.ClickEvent, MediaRow_Click);

            // SystemDecorations=None + ShowInTaskbar=False means there is no X button at all, and
            // the non-modal (buried-video) path has no owner to fall back on. Escape is the
            // keyboard escape hatch for both.
            KeyDown += (_, e) =>
            {
                if (e.Key == Key.Escape)
                {
                    e.Handled = true;
                    CloseRecap();
                }
            };
        }

        private void ApplyLog(SessionLog log)
        {
            var txtMainMessage = this.FindControl<TextBlock>("TxtMainMessage")!;
            var txtSubMessage = this.FindControl<TextBlock>("TxtSubMessage")!;
            var txtXP = this.FindControl<TextBlock>("TxtXP")!;

            // Header. The headline is BOUND to its key rather than assigned: assigning .Text would
            // survive only until the next language change, because Avalonia keeps the XAML binding
            // alive under a local value (CLAUDE.md, "setting text from code"). The sub-message is a
            // composite string, so it stays a plain assignment - as in WPF.
            if (log.Completed)
            {
                BindLoc(txtMainMessage, log.SessionId == "gamer_girl"
                    ? "label_gg_good_girl"
                    : "label_good_girl_3");
                txtSubMessage.Text = $"{log.SessionIcon} {log.SessionName} {Loc.Get("label_completed")}".Trim();
            }
            else
            {
                BindLoc(txtMainMessage, "label_session_ended_early");
                txtSubMessage.Text = $"{log.SessionIcon} {log.SessionName}".Trim();
                // No XP for aborted sessions - hide that column.
                this.FindControl<StackPanel>("XpPanel")!.IsVisible = false;
            }

            // Stats
            this.FindControl<TextBlock>("TxtSessionName")!.Text = log.SessionName;
            this.FindControl<TextBlock>("TxtDuration")!.Text = $"{log.Duration.Minutes:D2}:{log.Duration.Seconds:D2}";
            txtXP.Text = $"+{log.XPEarned}";
            txtXP.Foreground = log.SessionDifficulty switch
            {
                SessionDifficulty.Easy => new SolidColorBrush(Color.FromRgb(144, 238, 144)),
                SessionDifficulty.Medium => new SolidColorBrush(Color.FromRgb(255, 215, 0)),
                SessionDifficulty.Hard => new SolidColorBrush(Color.FromRgb(255, 165, 0)),
                SessionDifficulty.Extreme => new SolidColorBrush(Color.FromRgb(255, 99, 71)),
                _ => new SolidColorBrush(Color.FromRgb(144, 238, 144))
            };

            // Media list - newest entries last (chronological order matches the session timeline).
            var rows = (log.Media ?? new List<MediaLogEntry>()).Select(m => new MediaRow(m)).ToList();

            var noMedia = this.FindControl<TextBlock>("TxtNoMedia")!;
            var mediaList = this.FindControl<ItemsControl>("MediaList")!;
            var mediaCount = this.FindControl<TextBlock>("TxtMediaCount")!;

            if (rows.Count == 0)
            {
                noMedia.IsVisible = true;
                mediaList.IsVisible = false;
                mediaCount.Text = "";
            }
            else
            {
                noMedia.IsVisible = false;
                mediaList.IsVisible = true;
                mediaList.ItemsSource = rows;
                int videoCount = rows.Count(r => r.Type == MediaType.Video);
                int imageCount = rows.Count - videoCount;
                mediaCount.Text = Loc.GetF("label_media_count_videos_images", videoCount, imageCount);
            }
        }

        /// <summary>What {loc:Str} does, for a key only known at runtime.</summary>
        private static void BindLoc(TextBlock target, string key) =>
            target[!TextBlock.TextProperty] = new Binding($"[{key}]") { Source = LocalizationManager.Instance };

        private void LoadRandomCard()
        {
            // ponytail: only the MOD half resolves. Helpers.ModArt asks CoreModArt for an override
            // and then falls back to avares://, and Cards/*.png is NOT linked into this head - the
            // csproj links Assets/features, /nav, /quests and the loose Assets/*.png, not Cards.
            // So a .ccpmod that ships resources/Cards/hearth.png paints; a stock install collapses
            // the Border, which is the WPF null path rather than a blank plate. Linking the stock
            // cards is a .csproj change, which this layer does not own.
            var card = Helpers.ModArt.TryLoad(CardImages[Random.Shared.Next(CardImages.Length)]);
            if (card != null) this.FindControl<Image>("ImgCard")!.Source = card;
            this.FindControl<Border>("CardBorder")!.IsVisible = card != null;
        }

        /// <summary>
        /// The WPF body verbatim, against the seams: <c>App.Settings.Current</c> is
        /// <see cref="CoreSettings.Current"/> and <c>App.Audio</c> is <see cref="CoreAudio"/>.
        /// lvup.mp3 ships at Resources/sounds on both heads (SharedSoundsPackagingTests).
        /// </summary>
        private void PlayCompletionSound()
        {
            try
            {
                var soundPaths = new[]
                {
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "sounds", "lvup.mp3"),
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "lvlup.mp3"),
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", "sounds", "lvlup.mp3"),
                };

                var soundPath = soundPaths.FirstOrDefault(File.Exists);
                if (soundPath != null)
                {
                    var masterVolume = CoreSettings.Current.MasterVolume / 100f;
                    var curvedVolume = (float)Math.Pow(masterVolume, 1.5) * 0.35f;
                    CoreAudio.PlayOneShot(soundPath, Math.Max(0.01f, curvedVolume), "session-complete");
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to play completion sound");
            }
        }

        private async void MediaRow_Click(object? sender, RoutedEventArgs e)
        {
            if (e.Source is not Control c) return;
            if (c.Tag as string is not { Length: > 0 } path) return;

            try
            {
                // WPF's Helpers/ExplorerLauncher.cs SELECTS the file in Explorer (a Win32 shell
                // call, head-only). The portable half of that is opening the containing folder,
                // which is what every other opener on this head does.
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                {
                    Platform.ExternalOpener.Open(dir);
                    return;
                }

                // Neither the file nor its folder survived (#998). WPF said so in a MessageBox;
                // MessageDialog is this head's equivalent, and the handler is async void for it -
                // safe here because nothing is gated on the answer and the await is the last thing
                // the method does.
                Log.Information("SessionCompleteWindow: media file and its folder are both gone: {Path}", path);
                await MessageDialog.ShowAsync(this, Loc.Get("title_error"),
                    Loc.GetF("msg_file_not_found_with_path", path));
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "SessionCompleteWindow: failed to open file location {Path}", path);
            }
        }

        /// <summary>The one close path, kept from the WPF original.</summary>
        private void CloseRecap() => Close(true);

        /// <summary>Render seat sample.</summary>
        private static SessionLog SampleLog() => new()
        {
            SessionId = "sample",
            SessionName = "Deep Focus",
            SessionIcon = "🌀",
            SessionDifficulty = SessionDifficulty.Medium,
            Duration = TimeSpan.FromSeconds(23 * 60 + 41),
            XPEarned = 180,
            Completed = true,
            Media = new List<MediaLogEntry>
            {
                new() { Type = MediaType.Video, FilePath = "/media/loops/spiral-intro.mp4", SessionTimeSeconds = 12 },
                new() { Type = MediaType.Image, FilePath = "/media/stills/mantra-01.png", SessionTimeSeconds = 248 },
                new() { Type = MediaType.Video, FilePath = "/media/loops/deep-drop.mp4", SessionTimeSeconds = 697 },
                new() { Type = MediaType.Image, FilePath = "/media/stills/mantra-07.png", SessionTimeSeconds = 1142 },
            }
        };

        /// <summary>View model for a single row in the media list. Formatting copied verbatim from
        /// the WPF nested MediaRow.</summary>
        public sealed class MediaRow
        {
            public string DisplayName { get; }
            public string FilePath { get; }
            public string TimeOffsetText { get; }
            public string TypeLabel { get; }
            public IBrush TypeBrush { get; }
            public MediaType Type { get; }

            public MediaRow(MediaLogEntry entry)
            {
                var type = entry.Type;
                var displayName = entry.DisplayName;
                Type = type;
                FilePath = entry.FilePath ?? "";
                DisplayName = !string.IsNullOrEmpty(displayName)
                    ? displayName!
                    : (string.IsNullOrEmpty(FilePath) ? "" : Path.GetFileName(FilePath));

                var t = entry.SessionTime;
                TimeOffsetText = t.TotalHours >= 1
                    ? $"{(int)t.TotalHours}:{t.Minutes:D2}:{t.Seconds:D2}"
                    : $"{t.Minutes:D2}:{t.Seconds:D2}";

                if (type == MediaType.Video)
                {
                    TypeLabel = Loc.Get("label_video");
                    TypeBrush = new SolidColorBrush(Color.FromRgb(255, 105, 180)); // pink
                }
                else
                {
                    TypeLabel = Loc.Get("label_image");
                    TypeBrush = new SolidColorBrush(Color.FromRgb(135, 206, 250)); // light blue
                }
            }
        }
    }
}
