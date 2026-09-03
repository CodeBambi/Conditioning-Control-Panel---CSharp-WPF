using System;
using System.IO;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Avalonia.Views.Dialogs
{
    /// <summary>
    /// PORTED from ConditioningControlPanel/Dialogs/RoadmapDiaryDialog.xaml.cs. Deviations:
    ///  - BitmapImage -> Avalonia.Media.Imaging.Bitmap(path).
    ///  - The "Saved!" flash swaps the button's TextBlock text (the button holds a TextBlock, not
    ///    Content) and uses DispatcherTimer.RunOnce instead of a hand-rolled timer.
    ///  - App.Roadmap (photo path resolution, note persistence) is a head service: see the
    ///    ponytail stubs. A PhotoPath that is already absolute still loads.
    /// </summary>
    public partial class RoadmapDiaryDialog : Window
    {
        private readonly string _stepId;
        private readonly RoadmapStepProgress _progress;
        private readonly Image _imgFullPhoto;
        private readonly TextBlock _txtNoPhoto;
        private readonly TextBox _txtUserNote;

        /// <summary>Render/design constructor: sample data so --render-view can draw the dialog.</summary>
        public RoadmapDiaryDialog() : this("t1_step1",
            new RoadmapStepDefinition("t1_step1", RoadmapTrack.EmptyDoll, 1, "The Blank Slate",
                "Sit still for five minutes and let every thought drain away.", "A photo of your empty desk"),
            new RoadmapStepProgress("t1_step1")
            {
                IsCompleted = true,
                CompletedAt = new DateTime(2024, 1, 15, 15, 45, 0),
                TimeToCompleteMinutes = 45,
                UserNote = "It was easier than I expected."
            })
        { }

        public RoadmapDiaryDialog(string stepId, RoadmapStepDefinition stepDef, RoadmapStepProgress progress)
        {
            AvaloniaXamlLoader.Load(this);
            _stepId = stepId;
            _progress = progress;

            _imgFullPhoto = this.FindControl<Image>("ImgFullPhoto")!;
            _txtNoPhoto = this.FindControl<TextBlock>("TxtNoPhoto")!;
            _txtUserNote = this.FindControl<TextBox>("TxtUserNote")!;

            // Set step info
            this.FindControl<TextBlock>("TxtStepNumber")!.Text = stepDef.StepType == RoadmapStepType.Boss
                ? $"BOSS - Step {stepDef.StepNumber}"
                : $"Step {stepDef.StepNumber}";
            this.FindControl<TextBlock>("TxtStepTitle")!.Text = stepDef.Title;
            this.FindControl<TextBlock>("TxtObjective")!.Text = stepDef.Objective;

            // Load photo
            LoadPhoto();

            // Populate stats
            var completedDate = this.FindControl<TextBlock>("TxtCompletedDate")!;
            var completedTime = this.FindControl<TextBlock>("TxtCompletedTime")!;
            if (progress.CompletedAt.HasValue)
            {
                completedDate.Text = progress.CompletedAt.Value.ToString("MMM d, yyyy");
                completedTime.Text = progress.CompletedAt.Value.ToString("h:mm tt");
            }
            else
            {
                completedDate.Text = "N/A";
                completedTime.Text = "";
            }

            this.FindControl<TextBlock>("TxtTimeTaken")!.Text = progress.TimeToCompleteMinutes > 0
                ? $"{progress.TimeToCompleteMinutes} min"
                : "N/A";

            _txtUserNote.Text = progress.UserNote ?? "";

            this.FindControl<Button>("BtnSaveNote")!.Click += (_, _) => BtnSaveNote_Click();
            this.FindControl<Button>("BtnClose")!.Click += (_, _) => Close();
            this.FindControl<Button>("BtnCloseX")!.Click += (_, _) => Close();
        }

        private void LoadPhoto()
        {
            try
            {
                // ponytail: needs RoadmapService.GetFullPhotoPath for a diary-relative path, wired when it moves to Core
                var fullPath = _progress.PhotoPath;
                if (!string.IsNullOrEmpty(fullPath) && Path.IsPathRooted(fullPath) && File.Exists(fullPath))
                {
                    _imgFullPhoto.Source = new Bitmap(fullPath);
                    _txtNoPhoto.IsVisible = false;
                    return;
                }

                // No photo available
                _imgFullPhoto.Source = null;
                _txtNoPhoto.IsVisible = true;
            }
            catch (Exception ex)
            {
                Serilog.Log.Error(ex, "Failed to load diary photo");
                _imgFullPhoto.Source = null;
                _txtNoPhoto.IsVisible = true;
            }
        }

        private void BtnSaveNote_Click()
        {
            var newNote = _txtUserNote.Text?.Trim();
            // ponytail: needs RoadmapService.UpdateStepNote(_stepId, newNote), wired when it moves to Core
            _progress.UserNote = newNote;

            // Visual feedback
            var btn = this.FindControl<Button>("BtnSaveNote")!;
            var label = this.FindControl<TextBlock>("TxtSaveNote")!;
            var originalText = label.Text;
            label.Text = Loc.Get("btn_saved");
            btn.IsEnabled = false;

            DispatcherTimer.RunOnce(() =>
            {
                label.Text = originalText;
                btn.IsEnabled = true;
            }, TimeSpan.FromSeconds(1));
        }
    }
}
