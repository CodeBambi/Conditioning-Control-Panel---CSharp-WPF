using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NAudio.Wave;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel
{
    public partial class SessionCompleteWindow : Window
    {
        private static readonly Random _random = new();

        // Available card images
        private static readonly string[] CardImages = new[]
        {
            "pack://application:,,,/Resources/Cards/fireworks.png",
            "pack://application:,,,/Resources/Cards/hearth.png",
            "pack://application:,,,/Resources/Cards/spotlight.png"
        };

        public SessionCompleteWindow(Session session, TimeSpan duration, int xpEarned)
        {
            InitializeComponent();

            // Load random card image
            LoadRandomCard();

            // Set custom message based on session
            if (session.Id == "gamer_girl")
            {
                TxtMainMessage.Text = "GG, Good Girl!";
            }
            else
            {
                TxtMainMessage.Text = "Good Girl!";
            }

            TxtSubMessage.Text = $"{session.Icon} {session.Name} Complete";
            TxtSessionName.Text = session.Name;
            TxtDuration.Text = $"{duration.Minutes:D2}:{duration.Seconds:D2}";
            TxtXP.Text = $"+{xpEarned}";

            // Color XP based on difficulty
            TxtXP.Foreground = session.Difficulty switch
            {
                SessionDifficulty.Easy => new SolidColorBrush(Color.FromRgb(144, 238, 144)), // Light green
                SessionDifficulty.Medium => new SolidColorBrush(Color.FromRgb(255, 215, 0)), // Gold
                SessionDifficulty.Hard => new SolidColorBrush(Color.FromRgb(255, 165, 0)), // Orange
                SessionDifficulty.Extreme => new SolidColorBrush(Color.FromRgb(255, 99, 71)), // Tomato
                _ => new SolidColorBrush(Color.FromRgb(144, 238, 144))
            };

            // Play level up sound
            PlayCompletionSound();
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
                // Hide the image border if loading fails
                ImgCard.Visibility = Visibility.Collapsed;
            }
        }
        
        private void PlayCompletionSound()
        {
            try
            {
                var soundPaths = new[]
                {
                    System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "sounds", "lvup.mp3"),
                    System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "lvlup.mp3"),
                    System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", "sounds", "lvlup.mp3"),
                };

                var soundPath = soundPaths.FirstOrDefault(System.IO.File.Exists);
                if (soundPath != null)
                {
                    System.Threading.Tasks.Task.Run(() =>
                    {
                        try
                        {
                            using var audioFile = new AudioFileReader(soundPath);
                            using var outputDevice = new WaveOutEvent();

                            var masterVolume = App.Settings.Current.MasterVolume / 100f;
                            var curvedVolume = (float)Math.Pow(masterVolume, 1.5) * 0.5f;
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
        
        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }
    }
}
