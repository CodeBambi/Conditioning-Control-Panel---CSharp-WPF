using System;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel;

/// <summary>
/// Popup window shown when an achievement is unlocked
/// </summary>
public partial class AchievementPopup : Window
{
    private readonly DispatcherTimer _autoCloseTimer;
    
    public AchievementPopup(Achievement achievement)
    {
        InitializeComponent();
        
        // Set content
        TxtName.Text = achievement.Name;
        TxtFlavor.Text = achievement.FlavorText;
        
        // Load achievement image
        LoadAchievementImage(achievement.ImageName);
        
        // Position in bottom-right corner of primary screen
        var screen = System.Windows.Forms.Screen.PrimaryScreen;
        if (screen != null)
        {
            Left = screen.WorkingArea.Right - Width - 20;
            Top = screen.WorkingArea.Bottom - Height - 20;
        }
        
        // Auto-close after 6 seconds
        _autoCloseTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
        _autoCloseTimer.Tick += (s, e) =>
        {
            _autoCloseTimer.Stop();
            FadeOutAndClose();
        };
        _autoCloseTimer.Start();
        
        // Fade in animation
        Opacity = 0;
        Loaded += (s, e) =>
        {
            var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300));
            BeginAnimation(OpacityProperty, fadeIn);
        };
    }
    
    private void LoadAchievementImage(string imageName)
    {
        try
        {
            // Try to load from Resources/achievements folder
            var imagePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "achievements", imageName);
            
            if (File.Exists(imagePath))
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(imagePath, UriKind.Absolute);
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                AchievementImage.Source = bitmap;
            }
            else
            {
                // Try pack URI (embedded resource)
                try
                {
                    var packUri = new Uri($"pack://application:,,,/Resources/achievements/{imageName}", UriKind.Absolute);
                    var bitmap = new BitmapImage(packUri);
                    AchievementImage.Source = bitmap;
                }
                catch
                {
                    App.Logger?.Warning("Achievement image not found: {Name}", imageName);
                }
            }
        }
        catch (Exception ex)
        {
            App.Logger?.Error(ex, "Failed to load achievement image: {Name}", imageName);
        }
    }
    
    private void FadeOutAndClose()
    {
        var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(300));
        fadeOut.Completed += (s, e) => Close();
        BeginAnimation(OpacityProperty, fadeOut);
    }
    
    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        _autoCloseTimer.Stop();
        FadeOutAndClose();
    }
    
    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Allow dragging the window
        DragMove();
    }
    
    protected override void OnClosed(EventArgs e)
    {
        _autoCloseTimer.Stop();
        base.OnClosed(e);
    }
}
