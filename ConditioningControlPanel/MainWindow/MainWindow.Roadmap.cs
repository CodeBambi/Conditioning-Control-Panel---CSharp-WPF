using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Rectangle = System.Windows.Shapes.Rectangle;
using NAudio.Wave;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Helpers;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel
{
    // Roadmap tab: transformation tracks, node generation, photo submission flow.
    public partial class MainWindow
    {
        #region Roadmap Tab

        private Models.RoadmapTrack _currentRoadmapTrack = Models.RoadmapTrack.EmptyDoll;

        internal void BtnQuestSubDaily_Click(object sender, RoutedEventArgs e)
        {
            // Show Daily/Weekly panel, hide Roadmap
            QuestsTab.DailyWeeklyPanel.Visibility = Visibility.Visible;
            QuestsTab.RoadmapPanel.Visibility = Visibility.Collapsed;

            // Update sub-tab button styles
            QuestsTab.BtnQuestSubDaily.Style = (Style)FindResource("TabButtonActive");
            QuestsTab.BtnQuestSubRoadmap.Style = (Style)FindResource("TabButton");
        }

        internal void BtnQuestSubRoadmap_Click(object sender, RoutedEventArgs e)
        {
            // Show Roadmap panel, hide Daily/Weekly
            QuestsTab.DailyWeeklyPanel.Visibility = Visibility.Collapsed;
            QuestsTab.RoadmapPanel.Visibility = Visibility.Visible;

            // Update sub-tab button styles
            QuestsTab.BtnQuestSubDaily.Style = (Style)FindResource("TabButton");
            QuestsTab.BtnQuestSubRoadmap.Style = (Style)FindResource("TabButtonActive");

            // Refresh roadmap UI
            RefreshRoadmapUI();
        }

        internal void BtnTrack_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            if (btn?.Tag is string trackStr && Enum.TryParse<Models.RoadmapTrack>(trackStr, out var track))
            {
                _currentRoadmapTrack = track;

                // Update track button styles
                QuestsTab.BtnTrack1.Style = (Style)FindResource(track == Models.RoadmapTrack.EmptyDoll ? "TabButtonActive" : "TabButton");
                QuestsTab.BtnTrack2.Style = (Style)FindResource(track == Models.RoadmapTrack.ObedientPuppet ? "TabButtonActive" : "TabButton");
                QuestsTab.BtnTrack3.Style = (Style)FindResource(track == Models.RoadmapTrack.SluttyBlowdoll ? "TabButtonActive" : "TabButton");

                RefreshRoadmapUI();
            }
        }

        private void RefreshRoadmapUI()
        {
            if (App.Roadmap == null) return;

            var trackDef = Models.RoadmapTrackDefinition.GetByTrack(_currentRoadmapTrack);
            if (trackDef == null) return;

            // Update track header
            QuestsTab.TxtRoadmapTrackName.Text = trackDef.Name;
            QuestsTab.TxtRoadmapTrackSubtitle.Text = trackDef.Subtitle;

            var (completed, total) = App.Roadmap.GetTrackProgress(_currentRoadmapTrack);
            QuestsTab.TxtRoadmapTrackProgress.Text = $"{completed} / {total} steps completed";

            // Show/hide locked overlay
            bool isUnlocked = App.Roadmap.IsTrackUnlocked(_currentRoadmapTrack);
            QuestsTab.TrackLockedOverlay.Visibility = isUnlocked ? Visibility.Collapsed : Visibility.Visible;
            QuestsTab.RoadmapScrollContainer.Visibility = isUnlocked ? Visibility.Visible : Visibility.Collapsed;

            // Set lock reason
            if (!isUnlocked)
            {
                QuestsTab.TxtLockReason.Text = _currentRoadmapTrack switch
                {
                    Models.RoadmapTrack.ObedientPuppet => "Complete Track 1 Boss to unlock",
                    Models.RoadmapTrack.SluttyBlowdoll => "Complete Track 2 Boss to unlock",
                    _ => "Track locked"
                };
            }

            // Show badge indicator for Track 3 if badge earned
            QuestsTab.BadgeIndicator.Visibility = (_currentRoadmapTrack == Models.RoadmapTrack.SluttyBlowdoll &&
                                         App.Roadmap.Progress.HasCertifiedBlowdollBadge)
                ? Visibility.Visible
                : Visibility.Collapsed;

            // Generate roadmap nodes
            GenerateRoadmapNodes();

            // Update statistics
            RefreshRoadmapStats();
        }

        private void GenerateRoadmapNodes()
        {
            QuestsTab.RoadmapNodesPanel.Children.Clear();

            var steps = Models.RoadmapStepDefinition.GetStepsForTrack(_currentRoadmapTrack);
            var trackDef = Models.RoadmapTrackDefinition.GetByTrack(_currentRoadmapTrack);

            foreach (var step in steps)
            {
                var node = CreateRoadmapNode(step, trackDef);
                QuestsTab.RoadmapNodesPanel.Children.Add(node);
            }
        }

        private Border CreateRoadmapNode(Models.RoadmapStepDefinition step, Models.RoadmapTrackDefinition? trackDef)
        {
            bool isCompleted = App.Roadmap?.IsStepCompleted(step.Id) == true;
            bool isActive = App.Roadmap?.IsStepActive(step.Id) == true;
            bool isLocked = !isCompleted && !isActive;
            var progress = App.Roadmap?.GetStepProgress(step.Id);

            var accentColor = trackDef?.AccentColor ?? "#FF69B4";
            var accentBrush = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(accentColor));

            // Main container - taller to fit info boxes
            var container = new Border
            {
                Width = 150,
                Height = 240,
                Margin = new Thickness(10, 0, 10, 0),
                CornerRadius = new CornerRadius(15),
                Background = (SolidColorBrush)Application.Current.Resources["PanelBgBrush"],
                BorderThickness = new Thickness(step.StepType == Models.RoadmapStepType.Boss ? 3 : 2),
                BorderBrush = step.StepType == Models.RoadmapStepType.Boss
                    ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Gold)
                    : (isActive ? accentBrush : (SolidColorBrush)Application.Current.Resources["PanelAccentBrush"]),
                Cursor = System.Windows.Input.Cursors.Hand,
                Tag = step.Id
            };

            var stackPanel = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };

            // Photo circle container
            var circleGrid = new Grid { Width = 80, Height = 80 };

            // Background ellipse
            var bgEllipse = new System.Windows.Shapes.Ellipse
            {
                Fill = (SolidColorBrush)Application.Current.Resources["DarkerBgBrush"],
                Stroke = isActive ? accentBrush : (SolidColorBrush)Application.Current.Resources["PanelAccentBrush"],
                StrokeThickness = isActive ? 3 : 2
            };
            circleGrid.Children.Add(bgEllipse);

            if (isCompleted)
            {
                // Show photo thumbnail
                if (!string.IsNullOrEmpty(progress?.PhotoPath))
                {
                    try
                    {
                        var fullPath = App.Roadmap?.GetFullPhotoPath(progress.PhotoPath);
                        if (!string.IsNullOrEmpty(fullPath) && System.IO.File.Exists(fullPath))
                        {
                            var photoEllipse = new System.Windows.Shapes.Ellipse
                            {
                                Width = 74,
                                Height = 74,
                                HorizontalAlignment = HorizontalAlignment.Center,
                                VerticalAlignment = VerticalAlignment.Center
                            };
                            var bitmap = new System.Windows.Media.Imaging.BitmapImage();
                            bitmap.BeginInit();
                            bitmap.UriSource = new Uri(fullPath);
                            bitmap.DecodePixelWidth = 100;
                            bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                            bitmap.EndInit();
                            bitmap.Freeze();
                            photoEllipse.Fill = new System.Windows.Media.ImageBrush(bitmap)
                            {
                                Stretch = System.Windows.Media.Stretch.UniformToFill
                            };
                            circleGrid.Children.Add(photoEllipse);
                        }
                    }
                    catch { /* Failed to load photo */ }
                }

                // Checkmark overlay
                var checkmark = new TextBlock
                {
                    Text = "✓",
                    FontSize = 20,
                    FontWeight = FontWeights.Bold,
                    Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.LimeGreen),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Bottom,
                    Margin = new Thickness(0, 0, 5, 5)
                };
                circleGrid.Children.Add(checkmark);
            }
            else if (isLocked)
            {
                // Lock icon
                var lockIcon = new ConditioningControlPanel.Helpers.EmojiTextBlock
                {
                    Text = "🔒",
                    FontSize = 28,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                circleGrid.Children.Add(lockIcon);
            }
            else // Active
            {
                // Camera icon
                var cameraIcon = new ConditioningControlPanel.Helpers.EmojiTextBlock
                {
                    Text = "📷",
                    FontSize = 28,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                circleGrid.Children.Add(cameraIcon);

                // Pulsing effect (simple opacity animation on border)
                var pulseAnimation = new System.Windows.Media.Animation.DoubleAnimation
                {
                    From = 0.5,
                    To = 1.0,
                    Duration = TimeSpan.FromSeconds(0.8),
                    AutoReverse = true,
                    RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever
                };
                bgEllipse.BeginAnimation(System.Windows.Shapes.Ellipse.OpacityProperty, pulseAnimation);
            }

            // Objective requirement box (above circle)
            var requirementText = step.PhotoRequirement;
            // Remove "Photo: " prefix if present
            if (requirementText.StartsWith("Photo: "))
                requirementText = requirementText.Substring(7);
            // Truncate if too long
            if (requirementText.Length > 50)
                requirementText = requirementText.Substring(0, 47) + "...";

            var objectiveBox = new Border
            {
                Background = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromArgb(0xCC, 0x1A, 0x1A, 0x2E)),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 3, 6, 3),
                Margin = new Thickness(0, 0, 0, 8),
                MaxWidth = 140
            };
            var objectiveText = new TextBlock
            {
                Text = requirementText,
                Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0xCC, 0xCC, 0xCC)),
                FontSize = 9,
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center
            };
            objectiveBox.Child = objectiveText;
            stackPanel.Children.Add(objectiveBox);

            stackPanel.Children.Add(circleGrid);

            // Step number
            var stepNum = new TextBlock
            {
                Text = step.StepType == Models.RoadmapStepType.Boss ? "BOSS" : $"Step {step.StepNumber}",
                Foreground = step.StepType == Models.RoadmapStepType.Boss
                    ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Gold)
                    : new System.Windows.Media.SolidColorBrush(
                        (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#888888")),
                FontSize = 11,
                FontWeight = step.StepType == Models.RoadmapStepType.Boss ? FontWeights.Bold : FontWeights.Normal,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 10, 0, 2)
            };
            stackPanel.Children.Add(stepNum);

            // Step title
            var title = new TextBlock
            {
                Text = step.Title,
                Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.White),
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 120,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            stackPanel.Children.Add(title);

            // User note box (below title, only if completed with a note)
            if (isCompleted && !string.IsNullOrEmpty(progress?.UserNote))
            {
                var noteText = progress.UserNote;
                if (noteText.Length > 35)
                    noteText = noteText.Substring(0, 32) + "...";

                var noteBox = new Border
                {
                    Background = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromArgb(0x80, 0x25, 0x25, 0x42)),
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(6, 3, 6, 3),
                    Margin = new Thickness(0, 8, 0, 0),
                    MaxWidth = 140
                };
                var noteTextBlock = new TextBlock
                {
                    Text = $"\"{noteText}\"",
                    Foreground = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(0x88, 0x88, 0x88)),
                    FontSize = 9,
                    FontStyle = FontStyles.Italic,
                    TextWrapping = TextWrapping.Wrap,
                    TextAlignment = TextAlignment.Center
                };
                noteBox.Child = noteTextBlock;
                stackPanel.Children.Add(noteBox);
            }

            container.Child = stackPanel;

            // Click handler
            container.MouseLeftButtonUp += RoadmapNode_Click;

            return container;
        }

        private void RoadmapNode_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            var container = sender as Border;
            var stepId = container?.Tag as string;
            if (string.IsNullOrEmpty(stepId)) return;

            var stepDef = Models.RoadmapStepDefinition.GetById(stepId);
            if (stepDef == null) return;

            var progress = App.Roadmap?.GetStepProgress(stepId);

            // If completed, show diary
            if (progress?.IsCompleted == true)
            {
                var dialog = new RoadmapDiaryDialog(stepId, stepDef, progress);
                dialog.Owner = this;
                dialog.ShowDialog();
                return;
            }

            // If not active (locked), show message
            if (App.Roadmap?.IsStepActive(stepId) != true)
            {
                MessageBox.Show(Loc.Get("msg_complete_the_previous_steps_first"), "Step Locked",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Active step - show themed dialog for photo upload
            var startDialog = new RoadmapStartDialog(stepDef);
            startDialog.Owner = this;
            if (startDialog.ShowDialog() != true) return;

            // Start the step (records start time)
            App.Roadmap?.StartStep(stepId);

            // Open file picker
            var openFileDialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Image files|*.jpg;*.jpeg;*.png;*.gif;*.bmp|All files|*.*",
                Title = $"Select Photo for: {stepDef.Title}"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                ShowPhotoConfirmation(stepId, stepDef, openFileDialog.FileName);
            }
        }

        private void ShowPhotoConfirmation(string stepId, Models.RoadmapStepDefinition stepDef, string photoPath)
        {
            // Show themed confirmation dialog
            var confirmDialog = new RoadmapConfirmDialog(stepDef.Title, stepDef.PhotoRequirement);
            confirmDialog.Owner = this;
            if (confirmDialog.ShowDialog() != true || !confirmDialog.Confirmed) return;

            // Prompt for optional note
            string? note = null;
            var noteDialog = new InputDialog(Loc.Get("title_add_note"),
                Loc.Get("msg_add_note_prompt"), "");
            if (noteDialog.ShowDialog() == true && !string.IsNullOrEmpty(noteDialog.ResultText))
            {
                note = noteDialog.ResultText;
            }

            // Submit the photo
            App.Roadmap?.SubmitPhoto(stepId, photoPath, note);
            RefreshRoadmapUI();
        }

        private void RefreshRoadmapStats()
        {
            if (App.Roadmap == null) return;

            var progress = App.Roadmap.Progress;

            QuestsTab.TxtRoadmapTotalSteps.Text = $"{progress.TotalStepsCompleted} / 21";
            QuestsTab.TxtRoadmapPhotos.Text = progress.TotalPhotosSubmitted.ToString();

            if (progress.JourneyStartedAt.HasValue)
            {
                var days = (int)(DateTime.Now - progress.JourneyStartedAt.Value).TotalDays;
                QuestsTab.TxtRoadmapJourneyDays.Text = days.ToString();
            }
            else
            {
                QuestsTab.TxtRoadmapJourneyDays.Text = "--";
            }
        }

        private void OnRoadmapStepCompleted(object? sender, Services.RoadmapStepCompletedEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                // Show achievement-style popup
                var popup = new RoadmapStepPopup(e.StepDefinition, e.StepProgress);
                popup.Show();

                // Play celebration sound
                System.Media.SystemSounds.Exclamation.Play();

                // Refresh UI
                RefreshRoadmapUI();

                // Show special messages for track unlocks and badge (milestone events)
                if (e.UnlockedNewTrack)
                {
                    MessageBox.Show(
                        "Congratulations! You've unlocked a new track!\n\n" +
                        "Check the track tabs to continue your transformation.",
                        "Track Unlocked!",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }

                if (e.EarnedBadge)
                {
                    MessageBox.Show(
                        "🏆 CONGRATULATIONS! 🏆\n\n" +
                        "You have completed the entire Transformation Roadmap!\n\n" +
                        "You have earned the \"Certified Blowdoll\" badge!",
                        "Badge Earned!",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
            });
        }

        private void OnRoadmapTrackUnlocked(object? sender, Models.RoadmapTrack track)
        {
            Dispatcher.Invoke(() =>
            {
                RefreshRoadmapUI();
            });
        }

        #endregion
    }
}
