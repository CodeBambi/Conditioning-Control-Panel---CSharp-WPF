using System;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
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
        private Window _targetWindow;

        // Active subscription state for the current step's auto-advance trigger.
        private TutorialStep? _subscribedStep;
        private ButtonBase? _subButton;
        private TextBox? _subTextBox;
        private Selector? _subSelector;
        private Slider? _subSlider;
        private RoutedEventHandler? _subButtonHandler;
        private MouseButtonEventHandler? _subButtonMouseHandler;
        private Window? _subParentWindow;
        private System.ComponentModel.CancelEventHandler? _subParentWindowClosing;
        private bool _advanceFiredThisStep;
        // Set when this overlay's tutorial completes. Deferred lambdas (e.g.
        // OnTargetClosed's Background skip-check) check this so they don't
        // affect a successor tutorial running on the same _tutorialService.
        private bool _thisOverlayCompleted;

        // Retry timer used by UpdateSpotlight when the target's bounds aren't
        // measured yet. Held so consecutive UpdateSpotlight calls can cancel
        // the prior tick (otherwise they pile up and fire stale layouts) and
        // so DetachAllSubscriptions stops it on close.
        private System.Windows.Threading.DispatcherTimer? _spotlightDelayTimer;

        public TutorialOverlay(Window targetWindow, TutorialService tutorialService)
        {
            InitializeComponent();

            _targetWindow = targetWindow;
            _tutorialService = tutorialService;

            // Owner = MainWindow scopes the overlay to CCP's Z-order. With Topmost
            // removed, the overlay no longer paints over Discord etc. when the user
            // alt-tabs away. Within CCP, the Deactivated handler below brings us
            // back to the front whenever a sibling CCP window grabs focus, so the
            // spotlight stays visible while the user clicks the highlighted control.
            try
            {
                Owner = Application.Current?.MainWindow ?? targetWindow;
            }
            catch { }

            _tutorialService.StepChanged += OnStepChanged;
            _tutorialService.TutorialCompleted += OnTutorialCompleted;
            TutorialEventBus.Event += OnBusEvent;

            // App-shutdown cleanup so closing CCP via Exit while the tutorial is
            // up doesn't leave the overlay window lingering. Owner=MainWindow
            // *should* auto-close us, but the user reported the overlay stayed
            // after clicking Exit, so we belt-and-suspenders this with explicit
            // MainWindow.Closed + Application.Exit handlers.
            try
            {
                if (Application.Current != null)
                {
                    Application.Current.Exit += OnAppExit;
                    Application.Current.SessionEnding += OnAppSessionEnding;
                    var mainW = Application.Current.MainWindow;
                    if (mainW != null)
                    {
                        mainW.Closed += OnMainWindowClosed;
                    }
                }
            }
            catch { }

            AttachToTarget(_targetWindow);

            KeyDown += OnKeyDown;
            Focusable = true;

            // Deactivated += OnOverlayDeactivated;
            // Removed — with ShowActivated="False" the overlay never takes focus
            // in the first place, so we don't need to fight for re-activation.
            // The earlier re-activate logic stole focus back from the dialog,
            // which broke the dialog's mouse capture for Click handling and
            // prevented BtnCreate from closing the dialog.

            Opacity = 0;
            Loaded += (s, e) =>
            {
                // Compute position/size NOW that the window has an HWND (so
                // WindowInteropHelper.Handle resolves and Screen.FromHandle works).
                // Without this the overlay stays at WPF's default 300x200 because
                // UpdateOverlayPosition only runs again on target window move/resize.
                UpdateOverlayPosition();

                var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300));
                BeginAnimation(OpacityProperty, fadeIn);
                if (_tutorialService.CurrentStep != null)
                {
                    UpdateStep(_tutorialService.CurrentStep);
                }
            };
        }

        private void OnOverlayDeactivated(object? sender, EventArgs e)
        {
            // If a CCP sibling window (e.g. DeeperEditorWindow after the spotlight
            // click activated it) just took focus from us, re-activate the overlay
            // so it stays in front of CCP. If focus left CCP entirely (Discord,
            // Explorer, etc.), don't fight — we want to fall behind.
            try
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (!_tutorialService.IsActive) return;
                    if (Application.Current == null) return;

                    Window? activeCcpWindow = null;
                    foreach (Window w in Application.Current.Windows)
                    {
                        if (w == this) continue;
                        if (w.IsActive) { activeCcpWindow = w; break; }
                    }
                    if (activeCcpWindow != null)
                    {
                        try { Activate(); } catch { }
                    }
                }), System.Windows.Threading.DispatcherPriority.Background);
            }
            catch { }
        }

        private void OnAppExit(object? sender, ExitEventArgs e) => ForceShutdown();
        private void OnAppSessionEnding(object? sender, SessionEndingCancelEventArgs e) => ForceShutdown();
        private void OnMainWindowClosed(object? sender, EventArgs e) => ForceShutdown();

        // Tear the overlay down WITHOUT the 200ms fade-out animation that
        // OnTutorialCompleted normally runs. That animation kept the WPF
        // dispatcher busy during app-shutdown paths, which (combined with
        // dangling event subscriptions) left the process running after
        // MainWindow had closed — visible only as a zombie in Task Manager.
        private void ForceShutdown()
        {
            DetachAllSubscriptions();
            // Mark service inactive (it may already be).
            try { if (_tutorialService.IsActive) _tutorialService.Skip(); } catch { }
            // Stop any running animations on this window so Close() doesn't
            // leave the dispatcher pumping a fade that's already moot.
            try { BeginAnimation(OpacityProperty, null); } catch { }
            try { Close(); } catch { }
        }

        // Idempotent teardown: removes every event subscription this overlay
        // owns. Called from ForceShutdown, OnTutorialCompleted, AND OnClosed
        // so the static TutorialEventBus.Event subscription can never outlive
        // the window — closing the overlay via the host window's X button
        // (which doesn't run TutorialCompleted) used to leave the handler
        // pinned, holding the closed window forever.
        private void DetachAllSubscriptions()
        {
            _thisOverlayCompleted = true;
            try { _spotlightDelayTimer?.Stop(); } catch { }
            _spotlightDelayTimer = null;
            UnsubscribeAdvanceTrigger();
            try { TutorialEventBus.Event -= OnBusEvent; } catch { }
            try { _tutorialService.StepChanged -= OnStepChanged; } catch { }
            try { _tutorialService.TutorialCompleted -= OnTutorialCompleted; } catch { }
            try { DetachFromTarget(_targetWindow); } catch { }
            try
            {
                if (Application.Current != null)
                {
                    Application.Current.Exit -= OnAppExit;
                    Application.Current.SessionEnding -= OnAppSessionEnding;
                    if (Application.Current.MainWindow != null)
                        Application.Current.MainWindow.Closed -= OnMainWindowClosed;
                }
            }
            catch { }
        }

        protected override void OnClosed(EventArgs e)
        {
            try { DetachAllSubscriptions(); } catch { }
            base.OnClosed(e);
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                _tutorialService.Skip();
                e.Handled = true;
            }
        }

        public void RetargetToWindow(Window newWindow)
        {
            if (newWindow == null || newWindow == _targetWindow) return;
            DetachFromTarget(_targetWindow);
            _targetWindow = newWindow;
            AttachToTarget(newWindow);
            UpdateOverlayPosition();
            if (_tutorialService.CurrentStep != null)
            {
                UpdateStep(_tutorialService.CurrentStep);
            }
        }

        private void AttachToTarget(Window w)
        {
            try
            {
                w.LocationChanged += OnTargetMoved;
                w.SizeChanged += OnTargetResized;
                w.Closed += OnTargetClosed;
            }
            catch { }
        }

        private void DetachFromTarget(Window w)
        {
            try
            {
                w.LocationChanged -= OnTargetMoved;
                w.SizeChanged -= OnTargetResized;
                w.Closed -= OnTargetClosed;
            }
            catch { }
        }

        private void OnTargetClosed(object? sender, EventArgs e)
        {
            // Closing the target window is a normal part of the script when EITHER
            // the current step OR the next step targets a different window. We
            // can't rely on "Next() will have pumped before this skip-check"
            // because the click signal that triggers Next() can drop (mouse
            // capture races, ShowActivated quirks, etc). Checking the *next*
            // step makes the cross-window jump structural — closing during a
            // scripted window swap never aborts.
            try
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    // If THIS overlay's tutorial already completed, the
                    // _tutorialService may now be running a different tutorial
                    // (e.g. HT Part 2 in a sibling overlay). Don't reach over
                    // and skip someone else's tutorial.
                    if (_thisOverlayCompleted) return;
                    if (!_tutorialService.IsActive) return;

                    var current = _tutorialService.CurrentStep;
                    if (current != null && !string.IsNullOrEmpty(current.TargetWindowTypeName))
                    {
                        // Already advanced — retarget either happened or is queued.
                        return;
                    }

                    var steps = _tutorialService.CurrentSteps;
                    int idx = _tutorialService.CurrentStepIndex;
                    if (steps != null && idx + 1 < steps.Count)
                    {
                        var next = steps[idx + 1];
                        if (!string.IsNullOrEmpty(next?.TargetWindowTypeName))
                        {
                            // Scripted cross-window jump. Don't skip — wait for
                            // either AdvanceSync's Next() or WindowLoaded:* to
                            // retarget us.
                            return;
                        }
                    }

                    try { _tutorialService.Skip(); } catch { }
                }), System.Windows.Threading.DispatcherPriority.Background);
            }
            catch { }
        }

        private void OnTargetMoved(object? s, EventArgs e) => UpdateOverlayPosition();
        private void OnTargetResized(object? s, SizeChangedEventArgs e) => UpdateOverlayPosition();

        private void OnBusEvent(object? sender, string eventName)
        {
            try
            {
                Dispatcher.BeginInvoke(new Action(() => HandleBusEventOnUi(eventName)));
            }
            catch { }
        }

        private void HandleBusEventOnUi(string eventName)
        {
            // Window-loaded events: retarget if the current step expects this window.
            if (eventName.StartsWith("WindowLoaded:"))
            {
                var typeName = eventName.Substring("WindowLoaded:".Length);
                var step = _tutorialService.CurrentStep;
                if (step != null && step.TargetWindowTypeName == typeName)
                {
                    var w = FindWindowByTypeName(typeName);
                    if (w != null) RetargetToWindow(w);
                    return;
                }

                // Fallback: if the user's click on the previous step somehow failed
                // to trigger AdvanceSync (mouse capture race, focus theft, dropped
                // routing) but the NEXT step explicitly targets the window that
                // just loaded, treat the load as the advance signal so the
                // tutorial doesn't strand on the wrong window.
                var steps = _tutorialService.CurrentSteps;
                int idx = _tutorialService.CurrentStepIndex;
                if (steps != null && idx + 1 < steps.Count)
                {
                    var nextStep = steps[idx + 1];
                    if (nextStep != null &&
                        !string.IsNullOrEmpty(nextStep.TargetWindowTypeName) &&
                        nextStep.TargetWindowTypeName == typeName)
                    {
                        // Advance() will queue Next() at Normal priority. UpdateStep
                        // will then run with the new step, find the loaded window
                        // (IsLoaded=true now), and retarget inline.
                        Advance();
                        return;
                    }
                }
                return;
            }

            // OnEvent advance trigger.
            if (_subscribedStep is { } cs &&
                cs.AdvanceTrigger == TutorialAdvanceTrigger.OnEvent &&
                cs.AdvanceEventName == eventName)
            {
                Advance();
            }
        }

        private static Window? FindWindowByTypeName(string typeName)
        {
            if (Application.Current == null) return null;
            foreach (Window w in Application.Current.Windows)
            {
                if (w.GetType().Name == typeName && w.IsLoaded) return w;
            }
            return null;
        }

        private void UpdateOverlayPosition()
        {
            // Cover the entire monitor that MainWindow is currently on. Reading
            // Window.Left / ActualWidth is unreliable when the MainWindow is
            // maximized with WindowStyle="None" — Left often reports the restore
            // position, which left the overlay shifted and uncovering ~40% of
            // the screen (Update banner / Premium / chrome buttons remained
            // clickable). Using Screen.FromHandle + the WPF DPI scale gives the
            // real on-screen bounds in DIPs, regardless of window state.
            //
            // Z-order is still tied to CCP via Owner=MainWindow, so even though
            // we paint over the whole monitor, other apps (Discord etc.) come
            // above us when they're focused — which is what the user asked for.
            try
            {
                Rect bounds = GetMainWindowMonitorBoundsDip();
                if (bounds.IsEmpty)
                {
                    // Fallback to the old union approach if Win32 lookup fails.
                    bounds = GetWindowBounds(_targetWindow);
                    var mainW = Application.Current?.MainWindow;
                    if (mainW != null && mainW != _targetWindow)
                    {
                        var mainBounds = GetWindowBounds(mainW);
                        if (!mainBounds.IsEmpty)
                        {
                            bounds = bounds.IsEmpty ? mainBounds : Rect.Union(bounds, mainBounds);
                        }
                    }
                }
                if (!bounds.IsEmpty)
                {
                    Left = bounds.Left;
                    Top = bounds.Top;
                    Width = bounds.Width;
                    Height = bounds.Height;
                }
            }
            catch { }

            if (_tutorialService.CurrentStep != null && IsLoaded)
            {
                UpdateSpotlight(_tutorialService.CurrentStep);
            }
        }

        private static Rect GetMainWindowMonitorBoundsDip()
        {
            try
            {
                var mainW = Application.Current?.MainWindow;
                if (mainW == null) return Rect.Empty;
                var hwnd = new System.Windows.Interop.WindowInteropHelper(mainW).Handle;
                if (hwnd == IntPtr.Zero) return Rect.Empty;

                var screen = System.Windows.Forms.Screen.FromHandle(hwnd);
                if (screen == null) return Rect.Empty;

                var dpi = VisualTreeHelper.GetDpi(mainW);
                var sx = dpi.DpiScaleX <= 0 ? 1.0 : dpi.DpiScaleX;
                var sy = dpi.DpiScaleY <= 0 ? 1.0 : dpi.DpiScaleY;

                return new Rect(
                    screen.Bounds.X / sx,
                    screen.Bounds.Y / sy,
                    screen.Bounds.Width / sx,
                    screen.Bounds.Height / sy);
            }
            catch { return Rect.Empty; }
        }

        private static Rect GetWindowBounds(Window? w)
        {
            if (w == null) return Rect.Empty;
            try
            {
                if (double.IsNaN(w.Left) || double.IsNaN(w.Top)) return Rect.Empty;
                var width = w.ActualWidth > 0 ? w.ActualWidth : w.Width;
                var height = w.ActualHeight > 0 ? w.ActualHeight : w.Height;
                if (double.IsNaN(width) || double.IsNaN(height) || width <= 0 || height <= 0)
                    return Rect.Empty;
                return new Rect(w.Left, w.Top, width, height);
            }
            catch { return Rect.Empty; }
        }

        private void OnStepChanged(object? sender, TutorialStep step)
        {
            UpdateStep(step);
        }

        private void UpdateStep(TutorialStep step)
        {
            UnsubscribeAdvanceTrigger();

            // If this step expects a different window, attempt to retarget now.
            if (step.TargetWindowTypeName != null &&
                _targetWindow != null &&
                _targetWindow.GetType().Name != step.TargetWindowTypeName)
            {
                var w = FindWindowByTypeName(step.TargetWindowTypeName);
                if (w != null)
                {
                    RetargetToWindow(w);
                    return; // RetargetToWindow re-calls UpdateStep
                }
                // Window not open yet — render a clean centered "next up" card and
                // bail. Don't try to paint a spotlight against the (potentially
                // closed) old _targetWindow's visual tree — element lookups would
                // miss and we'd flicker a stale dim. HandleBusEventOnUi will
                // retarget us when WindowLoaded:* arrives.
                TxtStepCounter.Text = $"Step {_tutorialService.CurrentStepIndex + 1} of {_tutorialService.TotalSteps}";
                TxtIcon.Text = step.Icon;
                TxtTitle.Text = step.Title;
                TxtDescription.Text = step.Description;
                BtnSupport.Visibility = Visibility.Collapsed;
                BtnSkip.Visibility = step.IsFollowUpCard ? Visibility.Collapsed : Visibility.Visible;
                BtnSkipStep.Visibility = Visibility.Collapsed;
                BtnNext.Visibility = Visibility.Collapsed;
                BtnPrevious.Visibility = Visibility.Collapsed;
                FollowUpPanel.Visibility = Visibility.Collapsed;
                SpotlightCanvas.Children.Clear();
                DrawFullOverlay(step.BlockBackgroundClicks);
                CenterTextPanel();
                _subscribedStep = step;     // so OnEvent triggers still fire while waiting
                _advanceFiredThisStep = false;
                return;
            }

            TxtStepCounter.Text = $"Step {_tutorialService.CurrentStepIndex + 1} of {_tutorialService.TotalSteps}";
            TxtIcon.Text = step.Icon;
            TxtTitle.Text = step.Title;
            TxtDescription.Text = step.Description;

            BtnSupport.Visibility = step.Id == "support" ? Visibility.Visible : Visibility.Collapsed;
            BtnSkip.Visibility = step.IsFollowUpCard ? Visibility.Collapsed : Visibility.Visible;
            BtnSkipStep.Visibility = (step.AllowManualSkip && !step.IsFollowUpCard) ? Visibility.Visible : Visibility.Collapsed;

            // Manual advance trigger -> show Next; otherwise auto-advance handles it.
            bool isManual = step.AdvanceTrigger == TutorialAdvanceTrigger.Manual;
            BtnNext.Visibility = (isManual && !step.IsFollowUpCard) ? Visibility.Visible : Visibility.Collapsed;
            BtnNext.Content = _tutorialService.IsLastStep ? "Finish" : "Next";

            // Hide Previous in rails mode (going back is messy with state) and on follow-up cards.
            BtnPrevious.Visibility = (isManual && !_tutorialService.IsFirstStep && !step.IsFollowUpCard)
                ? Visibility.Visible : Visibility.Collapsed;

            // Follow-up card mode renders a stacked button list inside the panel.
            if (step.IsFollowUpCard)
            {
                FollowUpPanel.Visibility = Visibility.Visible;
                ConfigureFollowUpButton(BtnFollowUp1, step.FollowUpButton1Text, step.FollowUpAction1);
                ConfigureFollowUpButton(BtnFollowUp2, step.FollowUpButton2Text, step.FollowUpAction2);
                ConfigureFollowUpButton(BtnFollowUp3, step.FollowUpButton3Text, step.FollowUpAction3);
            }
            else
            {
                FollowUpPanel.Visibility = Visibility.Collapsed;
            }

            // Run spotlight + subscription SYNCHRONOUSLY rather than via a
            // BeginInvoke at Loaded priority. Reason: ApplyWindowSpotlightRegion
            // (the SetWindowRgn that lets clicks fall through the spotlight hole)
            // MUST be in place before the user clicks. If it's deferred, the
            // user's MouseDown lands on the overlay (no hole yet) and the
            // button below never sets IsPressed — so ButtonBase skips Click on
            // MouseUp and the dialog never closes. UpdateSpotlight has its own
            // timer-based retry for the rare "target not laid out yet" case.
            try { UpdateSpotlight(step); } catch { }
            try { SubscribeAdvanceTrigger(step); } catch { }

            // Defer Focus(). UpdateStep can be called synchronously from inside
            // a PreviewMouseLeftButtonUp handler (rails advance). Calling Focus()
            // there steals keyboard focus from the dialog mid-click, which broke
            // the button's Click sequence so DialogResult/Close never ran.
            Dispatcher.BeginInvoke(new Action(() => { try { Focus(); } catch { } }),
                System.Windows.Threading.DispatcherPriority.Background);
        }

        private static void ConfigureFollowUpButton(Button btn, string? text, Action<TutorialStep>? handler)
        {
            if (string.IsNullOrEmpty(text) || handler == null)
            {
                btn.Visibility = Visibility.Collapsed;
                return;
            }
            btn.Content = text;
            btn.Visibility = Visibility.Visible;
        }

        private void UpdateSpotlight(TutorialStep step)
        {
            // Cancel any retry from a previous step — otherwise rapid
            // StepChanged events queue up overlapping ticks that paint stale
            // bounds over the new step's layout.
            try { _spotlightDelayTimer?.Stop(); } catch { }
            _spotlightDelayTimer = null;

            SpotlightCanvas.Children.Clear();

            // Click-through hole only when the step is gated on a specific element interaction.
            // Manual steps and follow-up cards block all clicks (full opaque overlay).
            bool clickThroughHole = step.AdvanceTrigger != TutorialAdvanceTrigger.Manual &&
                                    !step.IsFollowUpCard;

            if (step.IsFollowUpCard ||
                step.TargetElementName == null ||
                step.TextPosition == TutorialStepPosition.Center)
            {
                DrawFullOverlay(step.BlockBackgroundClicks);
                CenterTextPanel();
                return;
            }

            var targetElement = FindElementByName(_targetWindow, step.TargetElementName);
            if (targetElement == null)
            {
                DrawFullOverlay(step.BlockBackgroundClicks);
                CenterTextPanel();
                return;
            }

            try { targetElement.BringIntoView(); } catch { }
            try { _targetWindow.UpdateLayout(); } catch { }

            var bounds = GetElementBounds(targetElement);

            if (bounds.X == 0 && bounds.Y == 0 && bounds.Width <= 100)
            {
                var currentStep = step;
                _spotlightDelayTimer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(120)
                };
                _spotlightDelayTimer.Tick += (s, e) =>
                {
                    var t = _spotlightDelayTimer;
                    try { t?.Stop(); } catch { }
                    if (t == _spotlightDelayTimer) _spotlightDelayTimer = null;
                    if (_thisOverlayCompleted) return;
                    if (_tutorialService.CurrentStep == currentStep)
                    {
                        var retryBounds = GetElementBounds(targetElement);
                        SpotlightCanvas.Children.Clear();
                        DrawSpotlightOverlay(retryBounds, clickThroughHole);
                        PositionTextPanel(retryBounds, currentStep.TextPosition);
                    }
                };
                _spotlightDelayTimer.Start();
                DrawFullOverlay(step.BlockBackgroundClicks);
                CenterTextPanel();
            }
            else
            {
                DrawSpotlightOverlay(bounds, clickThroughHole);
                PositionTextPanel(bounds, step.TextPosition);
            }
        }

        private void DrawFullOverlay(bool blockClicks = true)
        {
            // When blockClicks=false the user needs to interact with something
            // ON TOP of our overlay (e.g. an OS save dialog). With
            // AllowsTransparency=True, semi-transparent pixels still receive
            // clicks at the OS layered-window level — only fully transparent
            // (alpha=0) pixels pass clicks through. So drop the dim entirely
            // for click-through mode; the card alone signals the wait state.
            byte alpha = blockClicks
                ? (_targetWindow is MainWindow ? (byte)0xA0 : (byte)0x70)
                : (byte)0x00;
            var overlay = new Rectangle
            {
                Width = ActualWidth,
                Height = ActualHeight,
                Fill = new SolidColorBrush(Color.FromArgb(alpha, 0x00, 0x00, 0x00)),
                IsHitTestVisible = blockClicks
            };
            Canvas.SetLeft(overlay, 0);
            Canvas.SetTop(overlay, 0);
            SpotlightCanvas.Children.Add(overlay);

            // Clear any prior OS-level region. The card (TextPanel) lives
            // outside SpotlightCanvas and keeps its own hit-testing — Skip
            // Step / Skip Tutorial work even in click-through mode because
            // the card paints opaque pixels that the OS still routes to us.
            ClearWindowSpotlightRegion();
        }

        private void DrawSpotlightOverlay(Rect highlightBounds, bool clickThroughHole)
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

            // When clickThroughHole=true the dark fill is the full rect MINUS the spotlight rect,
            // so the hole has no geometry and clicks fall through to the underlying control.
            // Otherwise a full opaque rect blocks everything (standard manual-mode overlay).
            Geometry darkGeometry = clickThroughHole
                ? new CombinedGeometry(GeometryCombineMode.Exclude, fullRect, spotlightRect)
                : fullRect;

            // Match DrawFullOverlay's dim levels — see comment there.
            byte spotlightAlpha = _targetWindow is MainWindow ? (byte)0xA0 : (byte)0x70;
            var darkPath = new Path
            {
                Data = darkGeometry,
                Fill = new SolidColorBrush(Color.FromArgb(spotlightAlpha, 0x00, 0x00, 0x00)),
                IsHitTestVisible = true
            };
            SpotlightCanvas.Children.Add(darkPath);

            var glowBorder = new Border
            {
                Width = glowBounds.Width,
                Height = glowBounds.Height,
                CornerRadius = new CornerRadius(8),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0x69, 0xB4)),
                BorderThickness = new Thickness(2),
                Background = Brushes.Transparent,
                IsHitTestVisible = false
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

            // CRITICAL: physically punch a hole in the OS-level window region
            // around the spotlight bounds. WPF's AllowsTransparency=True windows
            // catch clicks across the entire client area regardless of pixel
            // alpha — so the visual hole alone doesn't yield click-through. By
            // SetWindowRgn'ing the window to a region that excludes the spotlight
            // rect, the hole is no longer part of the window at all and clicks
            // there fall through naturally to whatever's beneath.
            if (clickThroughHole)
            {
                ApplyWindowSpotlightRegion(highlightBounds);
            }
            else
            {
                ClearWindowSpotlightRegion();
            }
        }

        // -- Win32 region for OS-level click-through ---------------------------

        [DllImport("user32.dll")]
        private static extern int SetWindowRgn(IntPtr hWnd, IntPtr hRgn, bool bRedraw);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateRectRgn(int x1, int y1, int x2, int y2);

        [DllImport("gdi32.dll")]
        private static extern int CombineRgn(IntPtr hrgnDest, IntPtr hrgnSrc1, IntPtr hrgnSrc2, int fnCombineMode);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);

        private const int RGN_DIFF = 4; // dest = src1 - src2

        private void ApplyWindowSpotlightRegion(Rect spotlightBoundsDip)
        {
            try
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                if (hwnd == IntPtr.Zero) return;

                // Force a layout pass so ActualWidth/Height reflect the size we
                // just set in UpdateOverlayPosition. Without this, on the very
                // first Loaded call ActualWidth can still be the WPF-default
                // ~300 even though Width was set to the monitor width — the
                // SetWindowRgn region would then clip the window to a tiny rect
                // and the spotlight area outside that rect wouldn't get
                // hit-tested at all (Down/Up not delivered to anything).
                try { UpdateLayout(); } catch { }

                var dpi = VisualTreeHelper.GetDpi(this);
                var sx = dpi.DpiScaleX <= 0 ? 1.0 : dpi.DpiScaleX;
                var sy = dpi.DpiScaleY <= 0 ? 1.0 : dpi.DpiScaleY;

                // Use the bigger of ActualWidth and Width — Width is what we set,
                // ActualWidth is what's been measured. Either way, take the max
                // so the region always covers the window's visible extent.
                double w = Math.Max(ActualWidth, Width);
                double h = Math.Max(ActualHeight, Height);
                if (double.IsNaN(w) || w <= 0) return;
                if (double.IsNaN(h) || h <= 0) return;

                int fullW = (int)Math.Round(w * sx);
                int fullH = (int)Math.Round(h * sy);
                int holeL = (int)Math.Round(spotlightBoundsDip.X * sx);
                int holeT = (int)Math.Round(spotlightBoundsDip.Y * sy);
                int holeR = (int)Math.Round((spotlightBoundsDip.X + spotlightBoundsDip.Width) * sx);
                int holeB = (int)Math.Round((spotlightBoundsDip.Y + spotlightBoundsDip.Height) * sy);

                IntPtr full = CreateRectRgn(0, 0, fullW, fullH);
                IntPtr hole = CreateRectRgn(holeL, holeT, holeR, holeB);
                IntPtr result = CreateRectRgn(0, 0, 0, 0);
                CombineRgn(result, full, hole, RGN_DIFF);

                // SetWindowRgn takes ownership of the region on success; don't
                // delete `result`. Always free the temporaries.
                SetWindowRgn(hwnd, result, true);
                DeleteObject(full);
                DeleteObject(hole);

                App.Logger?.Debug("TutorialOverlay region: full={F}x{Fh} hole=({L},{T},{R},{B}) dpi={D}",
                    fullW, fullH, holeL, holeT, holeR, holeB, sx);
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("ApplyWindowSpotlightRegion failed: {Err}", ex.Message);
            }
        }

        private void ClearWindowSpotlightRegion()
        {
            try
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                if (hwnd == IntPtr.Zero) return;
                // SetWindowRgn(hwnd, IntPtr.Zero, true) clears any custom region.
                SetWindowRgn(hwnd, IntPtr.Zero, true);
            }
            catch { }
        }

        private void PositionTextPanel(Rect targetBounds, TutorialStepPosition position)
        {
            TextPanel.HorizontalAlignment = HorizontalAlignment.Left;
            TextPanel.VerticalAlignment = VerticalAlignment.Top;

            TextPanel.UpdateLayout();
            var panelWidth = TextPanel.ActualWidth > 0 ? TextPanel.ActualWidth : 460;
            var panelHeight = TextPanel.ActualHeight > 0 ? TextPanel.ActualHeight : 220;

            const double margin = 20;
            double left = 0, top = 0;

            // Center the panel on the target along the perpendicular axis. With
            // small targets like buttons (width 120) and a much wider card (~460),
            // edge-aligning made the card look "offset" — its center floated way
            // off the target's center. Centering keeps the card visually pointing
            // at the target.
            (left, top) = ComputePanelPosition(position, targetBounds, panelWidth, panelHeight, margin);

            // Clamp to overlay extent.
            double clampedLeft = Math.Max(margin, Math.Min(left, ActualWidth - panelWidth - margin));
            double clampedTop = Math.Max(margin, Math.Min(top, ActualHeight - panelHeight - margin));

            // If clamping moved us into / over the target (small overlay or target
            // near an edge), flip to the opposite side so the card never sits on
            // top of the spotlighted control.
            var panelRect = new Rect(clampedLeft, clampedTop, panelWidth, panelHeight);
            if (panelRect.IntersectsWith(targetBounds))
            {
                var flipped = FlipPosition(position);
                var (altLeft, altTop) = ComputePanelPosition(flipped, targetBounds, panelWidth, panelHeight, margin);
                double altClampedLeft = Math.Max(margin, Math.Min(altLeft, ActualWidth - panelWidth - margin));
                double altClampedTop = Math.Max(margin, Math.Min(altTop, ActualHeight - panelHeight - margin));
                var altRect = new Rect(altClampedLeft, altClampedTop, panelWidth, panelHeight);
                if (!altRect.IntersectsWith(targetBounds))
                {
                    clampedLeft = altClampedLeft;
                    clampedTop = altClampedTop;
                }
            }

            TextPanel.Margin = new Thickness(clampedLeft, clampedTop, 0, 0);
        }

        private static (double left, double top) ComputePanelPosition(
            TutorialStepPosition position, Rect targetBounds,
            double panelWidth, double panelHeight, double margin)
        {
            double left = 0, top = 0;
            switch (position)
            {
                case TutorialStepPosition.Bottom:
                    left = targetBounds.X + (targetBounds.Width - panelWidth) / 2;
                    top = targetBounds.Bottom + margin;
                    break;
                case TutorialStepPosition.Top:
                    left = targetBounds.X + (targetBounds.Width - panelWidth) / 2;
                    top = targetBounds.Top - panelHeight - margin;
                    break;
                case TutorialStepPosition.Left:
                    left = targetBounds.Left - panelWidth - margin;
                    top = targetBounds.Y + (targetBounds.Height - panelHeight) / 2;
                    break;
                case TutorialStepPosition.Right:
                    left = targetBounds.Right + margin;
                    top = targetBounds.Y + (targetBounds.Height - panelHeight) / 2;
                    break;
            }
            return (left, top);
        }

        private static TutorialStepPosition FlipPosition(TutorialStepPosition p) => p switch
        {
            TutorialStepPosition.Top => TutorialStepPosition.Bottom,
            TutorialStepPosition.Bottom => TutorialStepPosition.Top,
            TutorialStepPosition.Left => TutorialStepPosition.Right,
            TutorialStepPosition.Right => TutorialStepPosition.Left,
            _ => TutorialStepPosition.Bottom
        };

        private void CenterTextPanel()
        {
            TextPanel.HorizontalAlignment = HorizontalAlignment.Center;
            if (_targetWindow is MainWindow)
            {
                TextPanel.VerticalAlignment = VerticalAlignment.Center;
                TextPanel.Margin = new Thickness(0);
            }
            else
            {
                TextPanel.VerticalAlignment = VerticalAlignment.Bottom;
                TextPanel.Margin = new Thickness(0, 0, 0, 30);
            }
        }

        private FrameworkElement? FindElementByName(DependencyObject? parent, string name)
        {
            if (parent == null) return null;

            if (parent is FrameworkElement fe)
            {
                var found = fe.FindName(name) as FrameworkElement;
                if (found != null) return found;
            }

            try
            {
                int count = VisualTreeHelper.GetChildrenCount(parent);
                for (int i = 0; i < count; i++)
                {
                    var child = VisualTreeHelper.GetChild(parent, i);
                    if (child is FrameworkElement element && element.Name == name)
                        return element;

                    var result = FindElementByName(child, name);
                    if (result != null) return result;
                }
            }
            catch { }
            return null;
        }

        private Rect GetElementBounds(FrameworkElement element)
        {
            try
            {
                var screenTopLeft = element.PointToScreen(new Point(0, 0));
                var overlayLocal = PointFromScreen(screenTopLeft);
                return new Rect(overlayLocal, new Size(element.ActualWidth, element.ActualHeight));
            }
            catch
            {
                return new Rect(0, 0, 100, 40);
            }
        }

        // -- Auto-advance subscriptions ---------------------------------------

        private void SubscribeAdvanceTrigger(TutorialStep step)
        {
            UnsubscribeAdvanceTrigger();
            _subscribedStep = step;
            _advanceFiredThisStep = false;

            if (step.AdvanceTrigger == TutorialAdvanceTrigger.Manual ||
                step.AdvanceTrigger == TutorialAdvanceTrigger.OnEvent)
            {
                // OnEvent is handled via OnBusEvent (always subscribed).
                return;
            }

            if (string.IsNullOrEmpty(step.TargetElementName)) return;
            var target = FindElementByName(_targetWindow, step.TargetElementName);
            if (target == null) return;

            switch (step.AdvanceTrigger)
            {
                case TutorialAdvanceTrigger.OnButtonClick:
                    if (target is ButtonBase btn)
                    {
                        _subButton = btn;
                        // Subscribe to THREE signals — only one needs to win:
                        //   - PreviewMouseLeftButtonUp tunnels first; lets us
                        //     advance BEFORE the button's own click handler
                        //     closes its parent.
                        //   - Click is the canonical event but can drop our
                        //     handler if the prior handler (BtnCreate_Click)
                        //     closes the dialog mid-routing.
                        //   - Parent window's Closing event (with
                        //     DialogResult==true) is the most reliable signal
                        //     for buttons that close their parent window.
                        // The once-per-step _advanceFiredThisStep guard makes
                        // multi-fire a no-op.
                        _subButtonMouseHandler = (_, __) => AdvanceSync();
                        btn.AddHandler(UIElement.PreviewMouseLeftButtonUpEvent,
                            _subButtonMouseHandler, true);
                        _subButtonHandler = (_, __) => AdvanceSync();
                        btn.AddHandler(ButtonBase.ClickEvent, _subButtonHandler, true);

                        // Hook the parent window's Closing event as a backup —
                        // if the click triggers a positive close (DialogResult=true),
                        // we'll advance from there even if Preview/Click routing
                        // got dropped.
                        var parentWindow = Window.GetWindow(btn);
                        if (parentWindow != null && parentWindow != this)
                        {
                            _subParentWindow = parentWindow;
                            _subParentWindowClosing = (sender, ce) =>
                            {
                                try
                                {
                                    if (sender is Window w && w.DialogResult == true)
                                    {
                                        AdvanceSync();
                                    }
                                }
                                catch { }
                            };
                            parentWindow.Closing += _subParentWindowClosing;
                        }
                    }
                    break;
                case TutorialAdvanceTrigger.OnTextEquals:
                    if (target is TextBox tb)
                    {
                        _subTextBox = tb;
                        tb.TextChanged += OnSubTextChanged;
                        // Don't auto-advance on subscribe even if the value already matches —
                        // require the user to actually edit it (rails: every step is a click).
                    }
                    break;
                case TutorialAdvanceTrigger.OnSelectionEquals:
                    if (target is Selector sel)
                    {
                        _subSelector = sel;
                        sel.SelectionChanged += OnSubSelectionChanged;
                        // Don't auto-advance on subscribe — the user must change the selection.
                    }
                    break;
                case TutorialAdvanceTrigger.OnSliderAtLeast:
                    if (target is Slider sl)
                    {
                        _subSlider = sl;
                        // Advance on mouse release rather than on every ValueChanged.
                        // Otherwise the very first click on the slider (which lands
                        // somewhere along the track) can already satisfy the range
                        // and skip the step before the user has actually chosen a
                        // value. PreviewMouseLeftButtonUp tunnels first so we see
                        // the release reliably; we also handle keyboard release
                        // via LostMouseCapture as a safety net for arrow-key edits.
                        sl.AddHandler(UIElement.PreviewMouseLeftButtonUpEvent,
                            (MouseButtonEventHandler)OnSubSliderMouseUp, true);
                    }
                    break;
            }
        }

        private void UnsubscribeAdvanceTrigger()
        {
            if (_subButton != null)
            {
                if (_subButtonHandler != null)
                {
                    try { _subButton.RemoveHandler(ButtonBase.ClickEvent, _subButtonHandler); } catch { }
                }
                if (_subButtonMouseHandler != null)
                {
                    try { _subButton.RemoveHandler(UIElement.PreviewMouseLeftButtonUpEvent, _subButtonMouseHandler); } catch { }
                }
                _subButton = null;
                _subButtonHandler = null;
                _subButtonMouseHandler = null;
            }
            if (_subParentWindow != null && _subParentWindowClosing != null)
            {
                try { _subParentWindow.Closing -= _subParentWindowClosing; } catch { }
                _subParentWindow = null;
                _subParentWindowClosing = null;
            }
            if (_subTextBox != null)
            {
                try { _subTextBox.TextChanged -= OnSubTextChanged; } catch { }
                _subTextBox = null;
            }
            if (_subSelector != null)
            {
                try { _subSelector.SelectionChanged -= OnSubSelectionChanged; } catch { }
                _subSelector = null;
            }
            if (_subSlider != null)
            {
                try
                {
                    _subSlider.RemoveHandler(UIElement.PreviewMouseLeftButtonUpEvent,
                        (MouseButtonEventHandler)OnSubSliderMouseUp);
                }
                catch { }
                _subSlider = null;
            }
            _subscribedStep = null;
        }

        private void OnSubTextChanged(object sender, TextChangedEventArgs e)
        {
            if (_subscribedStep is { } step && _subTextBox != null)
            {
                if (TextMatches(_subTextBox.Text ?? "", step.AdvanceValue ?? ""))
                {
                    Advance();
                }
            }
        }

        private void OnSubSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_subscribedStep is { } step && _subSelector != null)
            {
                // Empty AdvanceValue means "advance on any user selection change".
                // Useful when the step just wants the user to engage with the
                // dropdown — picking any option counts as progress.
                if (string.IsNullOrEmpty(step.AdvanceValue))
                {
                    Advance();
                    return;
                }
                var actual = GetSelectorValue(_subSelector, step.MatchByTag);
                if (!string.IsNullOrEmpty(actual) &&
                    string.Equals(actual, step.AdvanceValue, StringComparison.OrdinalIgnoreCase))
                {
                    Advance();
                }
            }
        }

        private void OnSubSliderMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (_subscribedStep is { } step && _subSlider != null)
            {
                var v = _subSlider.Value;
                if (v < step.AdvanceMinValue) return;
                if (!double.IsNaN(step.AdvanceMaxValue) && v > step.AdvanceMaxValue) return;
                Advance();
            }
        }

        private static bool TextMatches(string actual, string expected)
        {
            actual = (actual ?? "").Trim();
            expected = (expected ?? "").Trim();
            if (expected.Length == 0) return false;

            if (string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase)) return true;

            if (double.TryParse(actual, NumberStyles.Any, CultureInfo.InvariantCulture, out var av) &&
                double.TryParse(expected, NumberStyles.Any, CultureInfo.InvariantCulture, out var ev))
            {
                return Math.Abs(av - ev) < 0.5;
            }

            // Substring fallback for non-numeric (e.g. "Pulse" matches "Pulse curve").
            return actual.Length >= expected.Length &&
                   actual.IndexOf(expected, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string? GetSelectorValue(Selector sel, bool matchByTag)
        {
            var selected = sel.SelectedItem;
            if (selected is ComboBoxItem cbi)
            {
                return matchByTag ? cbi.Tag?.ToString() : cbi.Content?.ToString();
            }
            return selected?.ToString();
        }

        // Used for OnButtonClick where we hook PreviewMouseLeftButtonUp. Sets the
        // once-per-step flag and unsubscribes immediately so the same step can't
        // re-fire, then DEFERS Next() to the next dispatcher cycle. We can't run
        // Next() synchronously inside Preview tunneling — firing StepChanged →
        // UpdateStep mid-tunnel disrupts the button's click sequence (the
        // bubbling MouseLeftButtonUp / Click never reach BtnCreate_Click and the
        // dialog stays open).
        //
        // The deferral is fine because:
        //   1) the flag + unsub still happen synchronously, so a second
        //      PreviewMouseUp on the same button does nothing;
        //   2) OnTargetClosed defers its skip-check at Background priority, and
        //      our Next() runs at Normal priority, so by the time the skip-check
        //      runs, step has already advanced and skip is suppressed.
        private void AdvanceSync()
        {
            if (_advanceFiredThisStep) return;
            _advanceFiredThisStep = true;
            UnsubscribeAdvanceTrigger();
            try
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (_tutorialService.IsActive) _tutorialService.Next();
                }));
            }
            catch { }
        }

        private void Advance()
        {
            if (_advanceFiredThisStep) return;
            _advanceFiredThisStep = true;
            UnsubscribeAdvanceTrigger();
            try
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (_tutorialService.IsActive) _tutorialService.Next();
                }));
            }
            catch { }
        }

        // -- Button click handlers --------------------------------------------

        private void BtnNext_Click(object sender, RoutedEventArgs e) => _tutorialService.Next();
        private void BtnPrevious_Click(object sender, RoutedEventArgs e) => _tutorialService.Previous();
        private void BtnSkip_Click(object sender, RoutedEventArgs e) => _tutorialService.Skip();
        private void BtnSkipStep_Click(object sender, RoutedEventArgs e) => Advance();

        private void BtnFollowUp1_Click(object sender, RoutedEventArgs e)
        {
            if (_tutorialService.CurrentStep is { } step) step.FollowUpAction1?.Invoke(step);
        }
        private void BtnFollowUp2_Click(object sender, RoutedEventArgs e)
        {
            if (_tutorialService.CurrentStep is { } step) step.FollowUpAction2?.Invoke(step);
        }
        private void BtnFollowUp3_Click(object sender, RoutedEventArgs e)
        {
            if (_tutorialService.CurrentStep is { } step) step.FollowUpAction3?.Invoke(step);
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

        private void OnTutorialCompleted(object? sender, EventArgs e)
        {
            DetachAllSubscriptions();
            try { Deactivated -= OnOverlayDeactivated; } catch { }

            var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(200));
            fadeOut.Completed += (s, args) =>
            {
                try { Close(); } catch { }
            };
            BeginAnimation(OpacityProperty, fadeOut);
        }
    }
}
