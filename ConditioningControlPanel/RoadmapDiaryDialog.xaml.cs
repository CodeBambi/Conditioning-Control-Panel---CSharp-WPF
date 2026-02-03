using System;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel
{
    public partial class RoadmapDiaryDialog : Window
    {
        private readonly string _stepId;
        private readonly RoadmapStepProgress _progress;

        public RoadmapDiaryDialog(string stepId, RoadmapStepDefinition stepDef, RoadmapStepProgress progress)
        {
            InitializeComponent();
            _stepId = stepId;
            _progress = progress;

            // Set step info
            TxtStepNumber.Text = stepDef.StepType == RoadmapStepType.Boss
                ? $"BOSS - Step {stepDef.StepNumber}"
                : $"Step {stepDef.StepNumber}";
            TxtStepTitle.Text = stepDef.Title;
            TxtObjective.Text = stepDef.Objective;

            // Load photo
            LoadPhoto();

            // Populate stats
            if (progress.CompletedAt.HasValue)
            {
                TxtCompletedDate.Text = progress.CompletedAt.Value.ToString("MMM d, yyyy");
                TxtCompletedTime.Text = progress.CompletedAt.Value.ToString("h:mm tt");
            }
            else
            {
                TxtCompletedDate.Text = "N/A";
                TxtCompletedTime.Text = "";
            }

            TxtTimeTaken.Text = progress.TimeToCompleteMinutes > 0
                ? $"{progress.TimeToCompleteMinutes} min"
                : "N/A";

            TxtUserNote.Text = progress.UserNote ?? "";
        }

        private void LoadPhoto()
        {
            try
            {
                if (!string.IsNullOrEmpty(_progress.PhotoPath))
                {
                    var fullPath = App.Roadmap?.GetFullPhotoPath(_progress.PhotoPath);
                    if (!string.IsNullOrEmpty(fullPath) && File.Exists(fullPath))
                    {
                        var bitmap = new BitmapImage();
                        bitmap.BeginInit();
                        bitmap.UriSource = new Uri(fullPath);
                        bitmap.CacheOption = BitmapCacheOption.OnLoad;
                        bitmap.EndInit();
                        bitmap.Freeze();

                        ImgFullPhoto.Source = bitmap;
                        TxtNoPhoto.Visibility = Visibility.Collapsed;
                        return;
                    }
                }

                // No photo available
                ImgFullPhoto.Source = null;
                TxtNoPhoto.Visibility = Visibility.Visible;
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "Failed to load diary photo");
                ImgFullPhoto.Source = null;
                TxtNoPhoto.Visibility = Visibility.Visible;
            }
        }

        private void BtnSaveNote_Click(object sender, RoutedEventArgs e)
        {
            var newNote = TxtUserNote.Text?.Trim();
            App.Roadmap?.UpdateStepNote(_stepId, newNote);

            // Visual feedback
            var btn = sender as System.Windows.Controls.Button;
            if (btn != null)
            {
                var originalContent = btn.Content;
                btn.Content = "Saved!";
                btn.IsEnabled = false;

                var timer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(1)
                };
                timer.Tick += (s, args) =>
                {
                    timer.Stop();
                    btn.Content = originalContent;
                    btn.IsEnabled = true;
                };
                timer.Start();
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
