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
        
        App.Logger?.Debug("Creating AchievementPopup for: {Name}", achievement.Name);
        
        // Set content
        TxtName.Text = achievement.Name;
        TxtFlavor.Text = achievement.FlavorText;
        
        // Load achievement image
        LoadAchievementImage(achievement.ImageName);
        
        // Position in bottom-right corner of primary screen
        PositionWindow();
        
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
            App.Logger?.Debug("AchievementPopup loaded, starting fade-in animation");
            var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300));
            BeginAnimation(OpacityProperty, fadeIn);
        };
    }
    
    /// <summary>
    /// Position the window in the bottom-right corner of the primary screen
    /// </summary>
    private void PositionWindow()
    {
        try
        {
            // Get the working area of the primary screen (excludes taskbar)
            var workArea = SystemParameters.WorkArea;
            
            // Position in bottom-right corner with 20px margin
            Left = workArea.Right - Width - 20;
            Top = workArea.Bottom - Height - 20;
            
            App.Logger?.Debug("Positioned popup at Left={Left}, Top={Top}", Left, Top);
        }
        catch (Exception ex)
        {
            App.Logger?.Error(ex, "Failed to position achievement popup, using defaults");
            // Fallback: center on screen
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }
    }
    
    private void LoadAchievementImage(string imageName)
    {
        try
        {
            App.Logger?.Debug("Loading achievement image: {Name}", imageName);
            
            // Try to load from Resources/achievements folder (file on disk)
            var imagePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "achievements", imageName);
            
            if (File.Exists(imagePath))
            {
                App.Logger?.Debug("Found image file at: {Path}", imagePath);
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
                    App.Logger?.Debug("Trying pack URI for: {Name}", imageName);
                    var packUri = new Uri($"pack://application:,,,/Resources/achievements/{imageName}", UriKind.Absolute);
                    var bitmap = new BitmapImage(packUri);
                    AchievementImage.Source = bitmap;
                    App.Logger?.Debug("Loaded image from pack URI");
                }
                catch (Exception packEx)
                {
                    App.Logger?.Warning(packEx, "Achievement image not found: {Name}", imageName);
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
        try
        {
            var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(300));
            fadeOut.Completed += (s, e) => 
            {
                try
                {
                    Close();
                }
                catch { /* Ignore close errors */ }
            };
            BeginAnimation(OpacityProperty, fadeOut);
        }
        catch (Exception ex)
        {
            App.Logger?.Error(ex, "Error during fade out, closing directly");
            try { Close(); } catch { }
        }
    }
    
    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        _autoCloseTimer.Stop();
        FadeOutAndClose();
    }
    
    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Allow dragging the window
        try
        {
            DragMove();
        }
        catch { /* Ignore drag errors */ }
    }
    
    protected override void OnClosed(EventArgs e)
    {
        _autoCloseTimer.Stop();
        base.OnClosed(e);
        App.Logger?.Debug("AchievementPopup closed");
    }
}
