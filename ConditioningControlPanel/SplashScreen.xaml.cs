using System;
using System.Windows;
using System.Windows.Media.Animation;

namespace ConditioningControlPanel
{
    /// <summary>
    /// Splash screen shown during application startup while services initialize.
    /// </summary>
    public partial class SplashScreen : Window
    {
        public SplashScreen()
        {
            InitializeComponent();

            // Set version from UpdateService
            TxtVersion.Text = $"v{Services.UpdateService.AppVersion}";
        }

        /// <summary>
        /// Update the progress bar and status text.
        /// </summary>
        /// <param name="progress">Progress value from 0.0 to 1.0</param>
        /// <param name="status">Status message to display</param>
        public void SetProgress(double progress, string status)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => SetProgress(progress, status));
                return;
            }

            TxtStatus.Text = status;

            // Animate the progress bar
            var animation = new DoubleAnimation
            {
                To = Math.Min(1.0, Math.Max(0.0, progress)),
                Duration = TimeSpan.FromMilliseconds(150),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };

            ProgressScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, animation);
        }

        /// <summary>
        /// Close the splash screen with a fade-out animation.
        /// </summary>
        public void FadeOutAndClose()
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(FadeOutAndClose);
                return;
            }

            var fadeOut = new DoubleAnimation
            {
                From = 1.0,
                To = 0.0,
                Duration = TimeSpan.FromMilliseconds(200)
            };

            fadeOut.Completed += (s, e) => Close();
            BeginAnimation(OpacityProperty, fadeOut);
        }
    }
}
