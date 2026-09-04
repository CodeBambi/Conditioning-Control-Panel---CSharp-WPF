// PORTED from ConditioningControlPanel/MainWindow/MainWindow.Roadmap.cs (RefreshRoadmapUI,
// GenerateRoadmapNodes, CreateRoadmapNode, RoadmapNode_Click, ShowPhotoConfirmation,
// RefreshRoadmapStats — lines 78-431).
//
// WHY IT LIVES HERE AND NOT ON THE SHELL. On WPF the roadmap markup is inline in MainWindow.xaml,
// so the painter hung off MainWindow and reached the controls through QuestsTab.*. On this head
// the markup is this view's own, and a UserControl's x:Name fields are private to it — the shell
// cannot reach RoadmapNodesPanel at all. MainShellWindow.Roadmap.cs says the same thing about the
// three chrome handlers, and this is the rest of that sentence.
//
// THE SERVICE IS REAL. RoadmapService is CCP.Core/Services/RoadmapService.cs and this head's one
// instance is MainShellWindow.Roadmap, so every number, photo and gate below is the user's own —
// nothing here is placeholder data. That is what makes RoadmapStartDialog, RoadmapConfirmDialog
// and RoadmapDiaryDialog reachable: before this file, all three were fully ported windows that
// nothing in CCP.Avalonia ever constructed.
//
// Deviations from the WPF original:
//  - MouseLeftButtonUp -> PointerReleased, filtered on InitialPressMouseButton == Left so a
//    right-click drag off a node cannot open a step.
//  - Microsoft.Win32.OpenFileDialog -> StorageProvider.OpenFilePickerAsync, which is async. The
//    whole click path is therefore async, and every dialog result is AWAITED BEFORE the thing it
//    gated runs: StartStep only after the start dialog answers true, SubmitPhoto only after the
//    confirm dialog answers true AND Confirmed. Firing either beside its answer would turn the
//    confirmation into decoration.
//  - MessageBox.Show -> MessageDialog.ShowAsync, this head's replacement.
//  - ShowDialog needs a VISIBLE owner on Avalonia (a shell minimised to tray is loaded but not
//    visible and Avalonia throws), so every open goes through OwnerWindow().
//  - The active node's pulse is an Avalonia Animation on Opacity. Opacity is not a transform, so
//    the TransformAnimator trap in CLAUDE.md does not apply here.
//  - Colours that WPF read off Application.Current.Resources are read off this control's
//    resource chain instead, with the same keys and the same literal fallbacks.
//
// ponytail: the WPF painter's App.Achievements celebration on a completed step is not here —
// that is OnRoadmapStepCompleted (MainWindow.Roadmap.cs:452), a subscription this file does not
// take, and it needs App.Achievements plus the sound service, neither of which is in Core.

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Tabs
{
    public partial class QuestsTabView
    {
        private RoadmapTrack _currentRoadmapTrack = RoadmapTrack.EmptyDoll;

        /// <summary>
        /// Cancels the active step's pulse when the nodes are rebuilt. WPF needs no equivalent:
        /// its TimeManager holds animation clocks weakly, so a Storyboard on a detached Ellipse
        /// stops on its own. Avalonia's animator subscribes to the global clock and HOLDS its
        /// target, so an infinite RunAsync on a node that has been cleared out of
        /// RoadmapNodesPanel keeps ticking on a dead subtree forever — one leaked node per track
        /// click, sub-tab click and photo submit.
        /// </summary>
        private CancellationTokenSource? _pulseCts;

        private static RoadmapService Roadmap => MainShellWindow.Roadmap;

        /// <summary>The window this view sits in, or null when it is not on screen. Null is the
        /// answer a tray-minimised shell gives, and every caller below simply does nothing —
        /// which is correct: none of these paths can run without a user clicking a node.</summary>
        private Window? OwnerWindow() =>
            TopLevel.GetTopLevel(this) is Window { IsVisible: true } w ? w : null;

        private IBrush Brush(string key, Color fallback) =>
            this.TryFindResource(key, out var v) && v is IBrush b ? b : new SolidColorBrush(fallback);

        // ---- the panel ------------------------------------------------------------

        internal void RefreshRoadmapUI()
        {
            var trackDef = RoadmapTrackDefinition.GetByTrack(_currentRoadmapTrack);
            if (trackDef == null) return;

            TxtRoadmapTrackName.Text = trackDef.Name;
            TxtRoadmapTrackSubtitle.Text = trackDef.Subtitle;

            var (completed, total) = Roadmap.GetTrackProgress(_currentRoadmapTrack);
            TxtRoadmapTrackProgress.Text = $"{completed} / {total} steps completed";

            bool isUnlocked = Roadmap.IsTrackUnlocked(_currentRoadmapTrack);
            TrackLockedOverlay.IsVisible = !isUnlocked;
            RoadmapScrollContainer.IsVisible = isUnlocked;

            if (!isUnlocked)
            {
                TxtLockReason.Text = _currentRoadmapTrack switch
                {
                    RoadmapTrack.ObedientPuppet => "Complete Track 1 Boss to unlock",
                    RoadmapTrack.SluttyBlowdoll => "Complete Track 2 Boss to unlock",
                    _ => "Track locked",
                };
            }

            BadgeIndicator.IsVisible = _currentRoadmapTrack == RoadmapTrack.SluttyBlowdoll
                                       && Roadmap.Progress.HasCertifiedBlowdollBadge;

            GenerateRoadmapNodes();
            RefreshRoadmapStats();
        }

        private void GenerateRoadmapNodes()
        {
            _pulseCts?.Cancel();
            _pulseCts = new CancellationTokenSource();

            RoadmapNodesPanel.Children.Clear();

            var trackDef = RoadmapTrackDefinition.GetByTrack(_currentRoadmapTrack);
            foreach (var step in RoadmapStepDefinition.GetStepsForTrack(_currentRoadmapTrack))
                RoadmapNodesPanel.Children.Add(CreateRoadmapNode(step, trackDef));
        }

        private Border CreateRoadmapNode(RoadmapStepDefinition step, RoadmapTrackDefinition? trackDef)
        {
            bool isCompleted = Roadmap.IsStepCompleted(step.Id);
            bool isActive = Roadmap.IsStepActive(step.Id);
            bool isLocked = !isCompleted && !isActive;
            var progress = Roadmap.GetStepProgress(step.Id);
            bool isBoss = step.StepType == RoadmapStepType.Boss;

            var accent = new SolidColorBrush(
                Color.Parse(string.IsNullOrEmpty(trackDef?.AccentColor) ? "#FF69B4" : trackDef!.AccentColor));
            var panelAccent = Brush("PanelAccentBrush", Color.FromRgb(0x3D, 0x3D, 0x60));
            var gold = new SolidColorBrush(Colors.Gold);

            var container = new Border
            {
                Width = 150,
                Height = 240,
                Margin = new Thickness(10, 0, 10, 0),
                CornerRadius = new CornerRadius(15),
                Background = Brush("PanelBgBrush", Color.FromRgb(0x25, 0x25, 0x42)),
                BorderThickness = new Thickness(isBoss ? 3 : 2),
                BorderBrush = isBoss ? gold : (isActive ? accent : panelAccent),
                Cursor = new Cursor(StandardCursorType.Hand),
                Tag = step.Id,
            };

            var stack = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
            };

            // ---- photo circle
            var circle = new Grid { Width = 80, Height = 80 };
            var bgEllipse = new Ellipse
            {
                Fill = Brush("DarkerBgBrush", Color.FromRgb(0x1A, 0x1A, 0x2E)),
                Stroke = isActive ? accent : panelAccent,
                StrokeThickness = isActive ? 3 : 2,
            };
            circle.Children.Add(bgEllipse);

            if (isCompleted)
            {
                if (!string.IsNullOrEmpty(progress?.PhotoPath))
                {
                    try
                    {
                        var fullPath = Roadmap.GetFullPhotoPath(progress!.PhotoPath!);
                        if (!string.IsNullOrEmpty(fullPath) && File.Exists(fullPath))
                        {
                            // DecodePixelWidth = 100 in the original; Avalonia's equivalent is
                            // DecodeToWidth on the load, which does the same downscale on decode.
                            using var fs = File.OpenRead(fullPath);
                            var bitmap = Bitmap.DecodeToWidth(fs, 100);
                            circle.Children.Add(new Ellipse
                            {
                                Width = 74,
                                Height = 74,
                                HorizontalAlignment = HorizontalAlignment.Center,
                                VerticalAlignment = VerticalAlignment.Center,
                                Fill = new ImageBrush(bitmap) { Stretch = Stretch.UniformToFill },
                            });
                        }
                    }
                    catch (Exception ex) { Log.Debug("Roadmap thumbnail failed: {Error}", ex.Message); }
                }

                circle.Children.Add(new TextBlock
                {
                    Text = "✓",
                    FontSize = 20,
                    FontWeight = FontWeight.Bold,
                    Foreground = new SolidColorBrush(Colors.LimeGreen),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Bottom,
                    Margin = new Thickness(0, 0, 5, 5),
                });
            }
            else if (isLocked)
            {
                circle.Children.Add(new TextBlock
                {
                    Text = "🔒",
                    FontSize = 28,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                });
            }
            else
            {
                circle.Children.Add(new TextBlock
                {
                    Text = "📷",
                    FontSize = 28,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                });

                _ = new Animation
                {
                    Duration = TimeSpan.FromSeconds(0.8),
                    Easing = new LinearEasing(),
                    IterationCount = IterationCount.Infinite,
                    PlaybackDirection = PlaybackDirection.Alternate,
                    Children =
                    {
                        new KeyFrame { Cue = new Cue(0d), Setters = { new Setter(OpacityProperty, 0.5) } },
                        new KeyFrame { Cue = new Cue(1d), Setters = { new Setter(OpacityProperty, 1.0) } },
                    },
                }.RunAsync(bgEllipse, _pulseCts?.Token ?? CancellationToken.None);
            }

            // ---- objective box, above the circle
            var requirement = step.PhotoRequirement ?? "";
            if (requirement.StartsWith("Photo: ", StringComparison.Ordinal)) requirement = requirement.Substring(7);
            if (requirement.Length > 50) requirement = requirement.Substring(0, 47) + "...";

            stack.Children.Add(new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0xCC, 0x1A, 0x1A, 0x2E)),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 3, 6, 3),
                Margin = new Thickness(0, 0, 0, 8),
                MaxWidth = 140,
                Child = new TextBlock
                {
                    Text = requirement,
                    Foreground = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)),
                    FontSize = 9,
                    TextWrapping = TextWrapping.Wrap,
                    TextAlignment = TextAlignment.Center,
                },
            });

            stack.Children.Add(circle);

            stack.Children.Add(new TextBlock
            {
                Text = isBoss ? "BOSS" : $"Step {step.StepNumber}",
                Foreground = isBoss ? gold : new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)),
                FontSize = 11,
                FontWeight = isBoss ? FontWeight.Bold : FontWeight.Normal,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 10, 0, 2),
            });

            stack.Children.Add(new TextBlock
            {
                Text = step.Title,
                Foreground = new SolidColorBrush(Colors.White),
                FontSize = 12,
                FontWeight = FontWeight.SemiBold,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 120,
                HorizontalAlignment = HorizontalAlignment.Center,
            });

            if (isCompleted && !string.IsNullOrEmpty(progress?.UserNote))
            {
                var note = progress!.UserNote!;
                if (note.Length > 35) note = note.Substring(0, 32) + "...";

                stack.Children.Add(new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(0x80, 0x25, 0x25, 0x42)),
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(6, 3, 6, 3),
                    Margin = new Thickness(0, 8, 0, 0),
                    MaxWidth = 140,
                    Child = new TextBlock
                    {
                        Text = $"\"{note}\"",
                        Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)),
                        FontSize = 9,
                        FontStyle = FontStyle.Italic,
                        TextWrapping = TextWrapping.Wrap,
                        TextAlignment = TextAlignment.Center,
                    },
                });
            }

            container.Child = stack;
            container.PointerReleased += RoadmapNode_Click;
            return container;
        }

        // ---- the click ------------------------------------------------------------

        private async void RoadmapNode_Click(object? sender, PointerReleasedEventArgs e)
        {
            if (e.InitialPressMouseButton != MouseButton.Left) return;

            try
            {
                if ((sender as Border)?.Tag is not string stepId || stepId.Length == 0) return;

                var stepDef = RoadmapStepDefinition.GetById(stepId);
                if (stepDef == null) return;

                var owner = OwnerWindow();
                if (owner == null) return;

                var progress = Roadmap.GetStepProgress(stepId);

                // Completed: the diary, read-only.
                if (progress?.IsCompleted == true)
                {
                    await new RoadmapDiaryDialog(stepId, stepDef, progress).ShowDialog(owner);
                    return;
                }

                // THE GATE. A step that is neither completed nor active is locked, and the WPF
                // path stops here with a message rather than opening the start dialog. It comes
                // over with the call: a start dialog reachable on a locked step would let a user
                // stamp step 5 before step 1.
                if (!Roadmap.IsStepActive(stepId))
                {
                    await MessageDialog.ShowAsync(owner, "Step Locked",
                        Loc.Get("msg_complete_the_previous_steps_first"));
                    return;
                }

                if (await new RoadmapStartDialog(stepDef).ShowDialog<bool?>(owner) != true) return;

                // Records the start time. Only after the dialog said yes.
                Roadmap.StartStep(stepId);

                var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = $"Select Photo for: {stepDef.Title}",
                    AllowMultiple = false,
                    FileTypeFilter = new[]
                    {
                        new FilePickerFileType("Image files")
                        {
                            Patterns = new[] { "*.jpg", "*.jpeg", "*.png", "*.gif", "*.bmp" },
                        },
                        new FilePickerFileType("All files") { Patterns = new[] { "*" } },
                    },
                });
                if (files.Count != 1 || files[0].TryGetLocalPath() is not { } path) return;

                await ShowPhotoConfirmation(stepId, stepDef, path, owner);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Roadmap node click failed");
            }
        }

        private async Task ShowPhotoConfirmation(string stepId, RoadmapStepDefinition stepDef,
            string photoPath, Window owner)
        {
            var confirm = new RoadmapConfirmDialog(stepDef.Title, stepDef.PhotoRequirement);
            if (await confirm.ShowDialog<bool?>(owner) != true || !confirm.Confirmed) return;

            string? note = null;
            var noteDialog = new InputDialog(Loc.Get("title_add_note"), Loc.Get("msg_add_note_prompt"), "");
            if (await noteDialog.ShowDialog<bool?>(owner) == true && !string.IsNullOrEmpty(noteDialog.ResultText))
                note = noteDialog.ResultText;

            Roadmap.SubmitPhoto(stepId, photoPath, note);
            RefreshRoadmapUI();
        }

        // ---- the stats strip ------------------------------------------------------

        private void RefreshRoadmapStats()
        {
            var progress = Roadmap.Progress;

            TxtRoadmapTotalSteps.Text = $"{progress.TotalStepsCompleted} / 21";
            TxtRoadmapPhotos.Text = progress.TotalPhotosSubmitted.ToString();
            TxtRoadmapJourneyDays.Text = progress.JourneyStartedAt.HasValue
                ? ((int)(DateTime.Now - progress.JourneyStartedAt.Value).TotalDays).ToString()
                : "--";
        }
    }
}
