using System;
using System.Windows;
using System.Windows.Media;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel
{
    public partial class SessionCompleteWindow : Window
    {
        public SessionCompleteWindow(Session session, TimeSpan duration, int xpEarned)
        {
            InitializeComponent();
            
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
        
        private void PlayCompletionSound()
        {
            try
            {
                var soundPath = System.IO.Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory, 
                    "assets", "sounds", "lvlup.mp3");
                    
                if (System.IO.File.Exists(soundPath))
                {
                    var player = new System.Windows.Media.MediaPlayer();
                    player.Open(new Uri(soundPath));
                    player.Volume = 0.5;
                    player.Play();
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
