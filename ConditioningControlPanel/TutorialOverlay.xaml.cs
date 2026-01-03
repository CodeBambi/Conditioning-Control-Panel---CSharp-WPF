using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel
{
    public partial class TutorialOverlay : Window
    {
        private readonly TutorialService _tutorialService;
        private readonly Window _targetWindow;

        public TutorialOverlay(Window targetWindow, TutorialService tutorialService)
        {
            InitializeComponent();

            _targetWindow = targetWindow;
            _tutorialService = tutorialService;

            _tutorialService.StepChanged += OnStepChanged;
            _tutorialService.TutorialCompleted += OnTutorialCompleted;

            UpdateOverlayPosition();
            _targetWindow.LocationChanged += (s, e) => UpdateOverlayPosition();
            _targetWindow.SizeChanged += (s, e) => UpdateOverlayPosition();

            Opacity = 0;
            Loaded += (s, e) =>
            {
                var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300));
                BeginAnimation(OpacityProperty, fadeIn);
                if (_tutorialService.CurrentStep != null)
                {
                    UpdateStep(_tutorialService.CurrentStep);
                }
            };
        }

        private void UpdateOverlayPosition()
        {
            Left = _targetWindow.Left;
            Top = _targetWindow.Top;
            Width = _targetWindow.ActualWidth;
            Height = _targetWindow.ActualHeight;

            if (_tutorialService.CurrentStep != null && IsLoaded)
            {
                UpdateSpotlight(_tutorialService.CurrentStep);
            }
        }

        private void OnStepChanged(object? sender, TutorialStep step)
        {
            UpdateStep(step);
        }

        private void UpdateStep(TutorialStep step)
        {
            TxtStepCounter.Text = $"Step {_tutorialService.CurrentStepIndex + 1} of {_tutorialService.TotalSteps}";
            TxtIcon.Text = step.Icon;
            TxtTitle.Text = step.Title;
            TxtDescription.Text = step.Description;

            BtnPrevious.Visibility = _tutorialService.IsFirstStep ? Visibility.Collapsed : Visibility.Visible;
            BtnNext.Content = _tutorialService.IsLastStep ? "Finish" : "Next";

            // Show support button on the support step
            BtnSupport.Visibility = step.Id == "support" ? Visibility.Visible : Visibility.Collapsed;

            UpdateSpotlight(step);
        }

        private void UpdateSpotlight(TutorialStep step)
        {
            SpotlightCanvas.Children.Clear();

            if (step.TargetElementName == null || step.TextPosition == TutorialStepPosition.Center)
            {
                DrawFullOverlay();
                CenterTextPanel();
            }
            else
            {
                var targetElement = FindElementByName(_targetWindow, step.TargetElementName);
                if (targetElement != null)
                {
                    var bounds = GetElementBounds(targetElement);
                    DrawSpotlightOverlay(bounds);
                    PositionTextPanel(bounds, step.TextPosition);
                }
                else
                {
                    DrawFullOverlay();
                    CenterTextPanel();
                }
            }
        }

        private void DrawFullOverlay()
        {
            var overlay = new Rectangle
            {
                Width = ActualWidth,
                Height = ActualHeight,
                Fill = new SolidColorBrush(Color.FromArgb(0xE0, 0x00, 0x00, 0x00))
            };
            Canvas.SetLeft(overlay, 0);
            Canvas.SetTop(overlay, 0);
            SpotlightCanvas.Children.Add(overlay);
        }

        private void DrawSpotlightOverlay(Rect highlightBounds)
        {
            var padding = 8.0;
            var glowBounds = new Rect(
                highlightBounds.X - padding,
                highlightBounds.Y - padding,
                highlightBounds.Width + padding * 2,
                highlightBounds.Height + padding * 2
            );

            var fullRect = new RectangleGeometry(new Rect(0, 0, ActualWidth, ActualHeight));
            var spotlightRect = new RectangleGeometry(glowBounds, 8, 8);
            var combined = new CombinedGeometry(GeometryCombineMode.Exclude, fullRect, spotlightRect);

            var overlay = new Path
            {
                Data = combined,
                Fill = new SolidColorBrush(Color.FromArgb(0xE0, 0x00, 0x00, 0x00))
            };
            SpotlightCanvas.Children.Add(overlay);

            var glowBorder = new Border
            {
                Width = glowBounds.Width,
                Height = glowBounds.Height,
                CornerRadius = new CornerRadius(8),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0x69, 0xB4)),
                BorderThickness = new Thickness(2),
                Background = Brushes.Transparent
            };
            glowBorder.Effect = new DropShadowEffect
            {
                Color = Color.FromRgb(0xFF, 0x69, 0xB4),
                BlurRadius = 15,
                ShadowDepth = 0,
                Opacity = 0.7
            };
            Canvas.SetLeft(glowBorder, glowBounds.X);
            Canvas.SetTop(glowBorder, glowBounds.Y);
            SpotlightCanvas.Children.Add(glowBorder);
        }

        private void PositionTextPanel(Rect targetBounds, TutorialStepPosition position)
        {
            TextPanel.HorizontalAlignment = HorizontalAlignment.Left;
            TextPanel.VerticalAlignment = VerticalAlignment.Top;

            TextPanel.UpdateLayout();
            var panelWidth = TextPanel.ActualWidth > 0 ? TextPanel.ActualWidth : 400;
            var panelHeight = TextPanel.ActualHeight > 0 ? TextPanel.ActualHeight : 200;

            const double margin = 20;
            double left = 0, top = 0;

            switch (position)
            {
                case TutorialStepPosition.Bottom:
                    left = targetBounds.X;
                    top = targetBounds.Bottom + margin;
                    break;
                case TutorialStepPosition.Top:
                    left = targetBounds.X;
                    top = targetBounds.Top - panelHeight - margin;
                    break;
                case TutorialStepPosition.Left:
                    left = targetBounds.Left - panelWidth - margin;
                    top = targetBounds.Y;
                    break;
                case TutorialStepPosition.Right:
                    left = targetBounds.Right + margin;
                    top = targetBounds.Y;
                    break;
            }

            left = Math.Max(margin, Math.Min(left, ActualWidth - panelWidth - margin));
            top = Math.Max(margin, Math.Min(top, ActualHeight - panelHeight - margin));

            TextPanel.Margin = new Thickness(left, top, 0, 0);
        }

        private void CenterTextPanel()
        {
            TextPanel.HorizontalAlignment = HorizontalAlignment.Center;
            TextPanel.VerticalAlignment = VerticalAlignment.Center;
            TextPanel.Margin = new Thickness(0);
        }

        private FrameworkElement? FindElementByName(DependencyObject parent, string name)
        {
            if (parent is FrameworkElement fe)
            {
                var found = fe.FindName(name) as FrameworkElement;
                if (found != null) return found;
            }

            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is FrameworkElement element && element.Name == name)
                    return element;

                var result = FindElementByName(child, name);
                if (result != null) return result;
            }
            return null;
        }

        private Rect GetElementBounds(FrameworkElement element)
        {
            try
            {
                var transform = element.TransformToAncestor(_targetWindow);
                var topLeft = transform.Transform(new Point(0, 0));
                return new Rect(topLeft, new Size(element.ActualWidth, element.ActualHeight));
            }
            catch
            {
                return new Rect(0, 0, 100, 40);
            }
        }

        private void BtnNext_Click(object sender, RoutedEventArgs e)
        {
            _tutorialService.Next();
        }

        private void BtnPrevious_Click(object sender, RoutedEventArgs e)
        {
            _tutorialService.Previous();
        }

        private void BtnSkip_Click(object sender, RoutedEventArgs e)
        {
            _tutorialService.Skip();
        }

        private void BtnSupport_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "https://linktr.ee/CodeBambi",
                    UseShellExecute = true
                });
            }
            catch { }
        }

        private void RootGrid_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            // Block all clicks from reaching the window below
            e.Handled = true;
        }

        private void OnTutorialCompleted(object? sender, EventArgs e)
        {
            var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(200));
            fadeOut.Completed += (s, args) => Close();
            BeginAnimation(OpacityProperty, fadeOut);
        }
    }
}
