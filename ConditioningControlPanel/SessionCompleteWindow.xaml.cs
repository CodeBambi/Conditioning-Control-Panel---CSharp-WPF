using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NAudio.Wave;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel
{
    public partial class SessionCompleteWindow : Window
    {
        private static readonly Random _random = new();

        private static readonly string[] CardImages = new[]
        {
            "pack://application:,,,/Resources/Cards/fireworks.png",
            "pack://application:,,,/Resources/Cards/hearth.png",
            "pack://application:,,,/Resources/Cards/spotlight.png"
        };

        public SessionCompleteWindow(SessionLog log, bool playSound = true)
        {
            InitializeComponent();

            LoadRandomCard();
            ApplyLog(log);

            if (playSound && log.Completed)
            {
                PlayCompletionSound();
            }
        }

        // Back-compat: callers that don't have a SessionLog (legacy paths) can still
        // build the dialog from raw fields. Renders with an empty media list.
        public SessionCompleteWindow(Session session, TimeSpan duration, int xpEarned)
            : this(BuildLogFromLegacy(session, duration, xpEarned))
        {
        }

        private static SessionLog BuildLogFromLegacy(Session session, TimeSpan duration, int xpEarned)
        {
            return new SessionLog
            {
                SessionId = session?.Id ?? "",
                SessionName = session?.Name ?? "",
                SessionIcon = session?.Icon ?? "",
                SessionDifficulty = session?.Difficulty ?? SessionDifficulty.Easy,
                Duration = duration,
                XPEarned = xpEarned,
                Completed = true,
            };
        }

        private void ApplyLog(SessionLog log)
        {
            // Header
            if (log.Completed)
            {
                TxtMainMessage.Text = log.SessionId == "gamer_girl"
                    ? Loc.Get("label_gg_good_girl")
                    : Loc.Get("label_good_girl_3");
                TxtSubMessage.Text = $"{log.SessionIcon} {log.SessionName} {Loc.Get("label_completed")}".Trim();
            }
            else
            {
                TxtMainMessage.Text = Loc.Get("label_session_ended_early");
                TxtSubMessage.Text = $"{log.SessionIcon} {log.SessionName}".Trim();
                // No XP for aborted sessions - hide that column.
                XpPanel.Visibility = Visibility.Collapsed;
            }

            // Stats
            TxtSessionName.Text = log.SessionName;
            TxtDuration.Text = $"{log.Duration.Minutes:D2}:{log.Duration.Seconds:D2}";
            TxtXP.Text = $"+{log.XPEarned}";
            TxtXP.Foreground = log.SessionDifficulty switch
            {
                SessionDifficulty.Easy => new SolidColorBrush(Color.FromRgb(144, 238, 144)),
                SessionDifficulty.Medium => new SolidColorBrush(Color.FromRgb(255, 215, 0)),
                SessionDifficulty.Hard => new SolidColorBrush(Color.FromRgb(255, 165, 0)),
                SessionDifficulty.Extreme => new SolidColorBrush(Color.FromRgb(255, 99, 71)),
                _ => new SolidColorBrush(Color.FromRgb(144, 238, 144))
            };

            // Media list - newest entries last (chronological order matches the session timeline).
            var rows = (log.Media ?? new List<MediaLogEntry>())
                .Select(m => new MediaRow(m))
                .ToList();

            if (rows.Count == 0)
            {
                TxtNoMedia.Visibility = Visibility.Visible;
                MediaList.Visibility = Visibility.Collapsed;
                TxtMediaCount.Text = "";
            }
            else
            {
                TxtNoMedia.Visibility = Visibility.Collapsed;
                MediaList.Visibility = Visibility.Visible;
                MediaList.ItemsSource = rows;
                int videoCount = rows.Count(r => r.Type == MediaType.Video);
                int imageCount = rows.Count - videoCount;
                TxtMediaCount.Text = Loc.GetF("label_media_count_videos_images", videoCount, imageCount);
            }
        }

        private void LoadRandomCard()
        {
            try
            {
                var cardUri = CardImages[_random.Next(CardImages.Length)];
                var bitmap = new BitmapImage(new Uri(cardUri, UriKind.Absolute));
                ImgCard.Source = bitmap;
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Failed to load session completion card");
                CardBorder.Visibility = Visibility.Collapsed;
            }
        }

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
                    System.Threading.Tasks.Task.Run(() =>
                    {
                        try
                        {
                            using var audioFile = new AudioFileReader(soundPath);
                            using var outputDevice = new WaveOutEvent();
                            App.Audio?.ApplyPreferredDevice(outputDevice);

                            var masterVolume = App.Settings.Current.MasterVolume / 100f;
                            var curvedVolume = (float)Math.Pow(masterVolume, 1.5) * 0.35f;
                            audioFile.Volume = Math.Max(0.01f, curvedVolume);

                            outputDevice.Init(audioFile);
                            outputDevice.Play();

                            while (outputDevice.PlaybackState == PlaybackState.Playing)
                                System.Threading.Thread.Sleep(50);
                        }
                        catch (Exception ex)
                        {
                            App.Logger?.Debug("Failed to play completion sound: {Error}", ex.Message);
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "Failed to play completion sound");
            }
        }

        private void MediaRow_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement fe) return;
            var path = fe.Tag as string;
            if (string.IsNullOrEmpty(path)) return;

            try
            {
                if (File.Exists(path))
                {
                    Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
                    return;
                }

                // File missing: try to open the parent folder if it still exists.
                var parent = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(parent) && Directory.Exists(parent))
                {
                    Process.Start(new ProcessStartInfo("explorer.exe", $"\"{parent}\"") { UseShellExecute = true });
                    App.Logger?.Information("SessionCompleteWindow: file gone, opened parent folder {Parent}", parent);
                    return;
                }

                MessageBox.Show(this,
                    Loc.GetF("msg_file_not_found_with_path", path),
                    Loc.Get("title_error"),
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "SessionCompleteWindow: failed to open file location {Path}", path);
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        // View model for a single row in the media list.
        private class MediaRow
        {
            public string DisplayName { get; }
            public string FilePath { get; }
            public string TimeOffsetText { get; }
            public string TypeLabel { get; }
            public Brush TypeBrush { get; }
            public MediaType Type { get; }

            public MediaRow(MediaLogEntry entry)
            {
                Type = entry.Type;
                FilePath = entry.FilePath ?? "";
                DisplayName = !string.IsNullOrEmpty(entry.DisplayName)
                    ? entry.DisplayName
                    : (string.IsNullOrEmpty(FilePath) ? "" : Path.GetFileName(FilePath));

                var t = entry.SessionTime;
                TimeOffsetText = t.TotalHours >= 1
                    ? $"{(int)t.TotalHours}:{t.Minutes:D2}:{t.Seconds:D2}"
                    : $"{t.Minutes:D2}:{t.Seconds:D2}";

                if (entry.Type == MediaType.Video)
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
