using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    /// <summary>
    /// The end-of-session recap: a random card image, the headline, three stats and the list of
    /// media the session played.
    ///
    /// PORTED from ConditioningControlPanel/Windows/SessionCompleteWindow.xaml.cs. Deviations:
    ///  - <c>SessionLog</c>, <c>MediaLogEntry</c>, <c>Session</c> and their enums are still in the
    ///    WPF head, and this project may not reference it, so <see cref="Recap"/> /
    ///    <see cref="MediaRow"/> are local stand-ins with the same fields <c>ApplyLog</c> reads.
    ///    The two legacy constructors collapse into one: the legacy overload only built a
    ///    <c>SessionLog</c> from raw fields, which is what <see cref="Recap"/> already is.
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
        internal SessionCompleteWindow() : this(Recap.Sample()) { }

        public SessionCompleteWindow(Recap log, bool playSound = true)
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

        private void ApplyLog(Recap log)
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
                Difficulty.Easy => new SolidColorBrush(Color.FromRgb(144, 238, 144)),
                Difficulty.Medium => new SolidColorBrush(Color.FromRgb(255, 215, 0)),
                Difficulty.Hard => new SolidColorBrush(Color.FromRgb(255, 165, 0)),
                Difficulty.Extreme => new SolidColorBrush(Color.FromRgb(255, 99, 71)),
                _ => new SolidColorBrush(Color.FromRgb(144, 238, 144))
            };

            // Media list - newest entries last (chronological order matches the session timeline).
            var rows = log.Media ?? new List<MediaRow>();

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
                int videoCount = rows.Count(r => r.Type == MediaKind.Video);
                int imageCount = rows.Count - videoCount;
                mediaCount.Text = Loc.GetF("label_media_count_videos_images", videoCount, imageCount);
            }
        }

        /// <summary>What {loc:Str} does, for a key only known at runtime.</summary>
        private static void BindLoc(TextBlock target, string key) =>
            target[!TextBlock.TextProperty] = new Binding($"[{key}]") { Source = LocalizationManager.Instance };

        private void LoadRandomCard()
        {
            // ponytail: needs ModResourceResolver.ResolveImage, wired when it moves to Core. Nothing
            // resolves a card yet, so the host Border collapses - which is the WPF null path, not a
            // blank plate. CardImages is kept because the mod path names are the compat surface.
            _ = CardImages;
            this.FindControl<Border>("CardBorder")!.IsVisible = false;
        }

        private void PlayCompletionSound()
        {
            // ponytail: needs App.Audio + App.Settings.Current.MasterVolume, wired when they move to
            // Core. The WPF version probes Resources/sounds/lvup.mp3 and friends and plays a
            // volume-curved one-shot.
        }

        private void MediaRow_Click(object? sender, RoutedEventArgs e)
        {
            if (e.Source is not Control c) return;
            if (c.Tag as string is not { Length: > 0 } path) return;

            // ponytail: needs Helpers.ExplorerLauncher.RevealInExplorer (Win32 shell) plus a
            // MessageBox for the "file is gone" case, wired when a portable file-reveal lands in
            // Core. Until then the row is inert rather than opening the wrong thing.
            _ = path;
        }

        /// <summary>The one close path, kept from the WPF original.</summary>
        private void CloseRecap() => Close(true);

        /// <summary>
        /// Stand-in for the head's SessionLog. Same fields ApplyLog reads, nothing more - this is
        /// deliberately not a second model, it is the argument shape until SessionLog is in Core.
        /// </summary>
        public sealed class Recap
        {
            public string SessionId { get; set; } = "";
            public string SessionName { get; set; } = "";
            public string SessionIcon { get; set; } = "";
            public Difficulty SessionDifficulty { get; set; } = Difficulty.Easy;
            public TimeSpan Duration { get; set; }
            public int XPEarned { get; set; }
            public bool Completed { get; set; }
            public List<MediaRow>? Media { get; set; }

            internal static Recap Sample() => new()
            {
                SessionId = "sample",
                SessionName = "Deep Focus",
                SessionIcon = "🌀",
                SessionDifficulty = Difficulty.Medium,
                Duration = TimeSpan.FromSeconds(23 * 60 + 41),
                XPEarned = 180,
                Completed = true,
                Media = new List<MediaRow>
                {
                    new(MediaKind.Video, "/media/loops/spiral-intro.mp4", "spiral-intro.mp4", TimeSpan.FromSeconds(12)),
                    new(MediaKind.Image, "/media/stills/mantra-01.png", "mantra-01.png", TimeSpan.FromSeconds(4 * 60 + 8)),
                    new(MediaKind.Video, "/media/loops/deep-drop.mp4", "deep-drop.mp4", TimeSpan.FromSeconds(11 * 60 + 37)),
                    new(MediaKind.Image, "/media/stills/mantra-07.png", "mantra-07.png", TimeSpan.FromSeconds(19 * 60 + 2)),
                }
            };
        }

        public enum Difficulty { Easy, Medium, Hard, Extreme }

        public enum MediaKind { Image, Video }

        /// <summary>View model for a single row in the media list. Formatting copied verbatim from
        /// the WPF nested MediaRow; it took a MediaLogEntry, which is not available here.</summary>
        public sealed class MediaRow
        {
            public string DisplayName { get; }
            public string FilePath { get; }
            public string TimeOffsetText { get; }
            public string TypeLabel { get; }
            public IBrush TypeBrush { get; }
            public MediaKind Type { get; }

            public MediaRow(MediaKind type, string? filePath, string? displayName, TimeSpan sessionTime)
            {
                Type = type;
                FilePath = filePath ?? "";
                DisplayName = !string.IsNullOrEmpty(displayName)
                    ? displayName!
                    : (string.IsNullOrEmpty(FilePath) ? "" : Path.GetFileName(FilePath));

                var t = sessionTime;
                TimeOffsetText = t.TotalHours >= 1
                    ? $"{(int)t.TotalHours}:{t.Minutes:D2}:{t.Seconds:D2}"
                    : $"{t.Minutes:D2}:{t.Seconds:D2}";

                if (type == MediaKind.Video)
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
