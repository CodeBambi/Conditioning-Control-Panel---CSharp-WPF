using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ConditioningControlPanel.Avalonia.Platform;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    /// <summary>
    /// The tutorial's full-screen coach mark: a dim sheet over the app with an optional lit hole
    /// around the control the current step is about, plus the card that carries the step's icon,
    /// title, description and its Skip / Skip step / Previous / Next row.
    ///
    /// PORTED from ConditioningControlPanel/Windows/TutorialOverlay.xaml.cs. Everything that only
    /// touches the view is real here — the spotlight geometry, the glow ring, the panel placer
    /// with its clamp-and-flip, the settle timer, the visual-tree element lookup. What is stubbed,
    /// and why:
    ///
    /// <list type="bullet">
    ///   <item><b>TutorialService / TutorialEventBus / TutorialStep.</b> All three live in the WPF
    ///     head (Services/, Models/), which this head may not reference, so the overlay has no
    ///     step source. <see cref="TutorialStep"/> and its two enums are copied as private nested
    ///     types — the same call CornerGifWindow makes for CornerGifOverlaySetting — and they
    ///     delete when the models reach Core. Next / Previous / Skip / Advance, the auto-advance
    ///     subscriptions (OnButtonClick, OnTextEquals, OnSelectionEquals, OnSliderAtLeast,
    ///     OnEvent), the WindowLoaded:* retarget and the app-shutdown teardown are all
    ///     <c>ponytail:</c> stubs: every one of them is a call INTO the service, so porting the
    ///     plumbing now would be writing against an API that does not exist yet.</item>
    ///
    ///   <item><b>SetWindowRgn / CreateRectRgn / CombineRgn — the click-through hole.</b> WPF
    ///     punched the spotlight rect out of the window's OS-level region, because a WPF layered
    ///     window catches clicks across its whole client area whatever the pixel alpha. The X11
    ///     shim on this head exposes <c>SetClickThrough(TopLevel, bool)</c>, which is whole-window
    ///     only, so <see cref="ClearWindowSpotlightRegion"/> maps exactly and
    ///     <see cref="ApplyWindowSpotlightRegion"/> does not — see its own comment. NO DllImport
    ///     was carried across; user32/gdi32 do not exist off Windows.</item>
    ///
    ///   <item><b>The 300ms fade in and 200ms fade out.</b> Dropped rather than re-expressed as a
    ///     transition: the headless render captures two dispatcher passes after Show(), which is
    ///     inside the fade, so a faithful fade-in would have drawn a nearly blank PNG and hidden
    ///     the very thing the render proves. The overlay simply appears.</item>
    /// </list>
    ///
    /// The parameterless constructor is the render constructor (see RenderProof): it seeds one
    /// sample step and a sample target rect so the PNG shows the dim sheet, the glow ring and a
    /// fully populated card rather than an empty window.
    /// </summary>
    public partial class TutorialOverlay : Window
    {
        // ---- Copies of ConditioningControlPanel.Models.TutorialStep ------------------
        // The originals live in the WPF head. Only the members this view reads are kept;
        // PrepareTargetWindowAction is typed on Avalonia's Window instead of WPF's.

        /// <summary>Copy of ConditioningControlPanel.Models.TutorialStepPosition.</summary>
        private enum TutorialStepPosition { Top, Bottom, Left, Right, Center }

        /// <summary>Copy of ConditioningControlPanel.Models.TutorialAdvanceTrigger.</summary>
        private enum TutorialAdvanceTrigger { Manual, OnButtonClick, OnTextEquals, OnSelectionEquals, OnSliderAtLeast, OnEvent }

        /// <summary>Copy of ConditioningControlPanel.Models.TutorialStep.</summary>
        private sealed class TutorialStep
        {
            public string Id { get; set; } = "";
            public string Title { get; set; } = "";
            public string Description { get; set; } = "";
            public string Icon { get; set; } = "";
            public string? TargetElementName { get; set; }
            public string? TargetWindowTypeName { get; set; }
            public TutorialStepPosition TextPosition { get; set; } = TutorialStepPosition.Bottom;
            public TutorialAdvanceTrigger AdvanceTrigger { get; set; } = TutorialAdvanceTrigger.Manual;
            public bool AllowManualSkip { get; set; }
            public bool IsFollowUpCard { get; set; }
            /// <summary>True (default): the dim absorbs clicks outside the hole and the card.</summary>
            public bool BlockBackgroundClicks { get; set; } = true;
            public Action<Window>? PrepareTargetWindowAction { get; set; }
            public Action<TutorialStep>? FollowUpAction1 { get; set; }
            public Action<TutorialStep>? FollowUpAction2 { get; set; }
            public Action<TutorialStep>? FollowUpAction3 { get; set; }
            public string? FollowUpButton1Text { get; set; }
            public string? FollowUpButton2Text { get; set; }
            public string? FollowUpButton3Text { get; set; }
        }

        // ---- Named parts ------------------------------------------------------------

        private readonly Canvas _spotlightCanvas;
        private readonly Border _textPanel;
        private readonly TextBlock _txtStepCounter;
        private readonly TextBlock _txtIcon;
        private readonly TextBlock _txtTitle;
        private readonly TextBlock _txtDescription;
        private readonly Button _btnSupport;
        private readonly StackPanel _followUpPanel;
        private readonly Button _btnFollowUp1;
        private readonly Button _btnFollowUp2;
        private readonly Button _btnFollowUp3;
        private readonly Button _btnSkip;
        private readonly Button _btnSkipStep;
        private readonly Button _btnPrevious;
        private readonly Button _btnNext;

        // ---- State ------------------------------------------------------------------

        /// <summary>The window the spotlight measures against. Null in render/sample mode, which
        /// is also what keeps <see cref="UpdateOverlayPosition"/> from resizing the window under
        /// the headless render.</summary>
        private Window? _targetWindow;

        private TutorialStep? _step;
        private bool _loaded;

        /// <summary>Sample spotlight rect used by the render constructor, so the PNG exercises the
        /// spotlight path (geometry + glow ring + panel placement) and not just the plain dim.</summary>
        private Rect? _sampleTargetBounds;

        // Retry timer used by UpdateSpotlight when the target's bounds aren't measured yet. Held
        // so consecutive UpdateSpotlight calls can cancel the prior tick (otherwise they pile up
        // and fire stale layouts) and so teardown stops it on close.
        private DispatcherTimer? _spotlightDelayTimer;

        /// <summary>Render constructor. Placeholder step data — the real text comes from
        /// TutorialService, which is still head-side.</summary>
        public TutorialOverlay()
        {
            AvaloniaXamlLoader.Load(this);

            _spotlightCanvas = this.FindControl<Canvas>("SpotlightCanvas")!;
            _textPanel = this.FindControl<Border>("TextPanel")!;
            _txtStepCounter = this.FindControl<TextBlock>("TxtStepCounter")!;
            _txtIcon = this.FindControl<TextBlock>("TxtIcon")!;
            _txtTitle = this.FindControl<TextBlock>("TxtTitle")!;
            _txtDescription = this.FindControl<TextBlock>("TxtDescription")!;
            _btnSupport = this.FindControl<Button>("BtnSupport")!;
            _followUpPanel = this.FindControl<StackPanel>("FollowUpPanel")!;
            _btnFollowUp1 = this.FindControl<Button>("BtnFollowUp1")!;
            _btnFollowUp2 = this.FindControl<Button>("BtnFollowUp2")!;
            _btnFollowUp3 = this.FindControl<Button>("BtnFollowUp3")!;
            _btnSkip = this.FindControl<Button>("BtnSkip")!;
            _btnSkipStep = this.FindControl<Button>("BtnSkipStep")!;
            _btnPrevious = this.FindControl<Button>("BtnPrevious")!;
            _btnNext = this.FindControl<Button>("BtnNext")!;

            _btnSupport.Click += BtnSupport_Click;
            _btnFollowUp1.Click += (_, _) => InvokeFollowUp(1);
            _btnFollowUp2.Click += (_, _) => InvokeFollowUp(2);
            _btnFollowUp3.Click += (_, _) => InvokeFollowUp(3);
            _btnSkip.Click += (_, _) => SkipTutorial();
            _btnSkipStep.Click += (_, _) => Advance();
            _btnPrevious.Click += (_, _) => PreviousStep();
            _btnNext.Click += (_, _) => NextStep();

            KeyDown += OnKeyDown;
            Focusable = true;

            // Placeholder: the first step of the full tour, re-pointed at a sample rect so the
            // render draws a spotlight rather than the Center-positioned plain card.
            _step = new TutorialStep
            {
                Id = "welcome",
                Icon = "~",
                Title = "Welcome to Conditioning Control Panel!",
                Description = "Everything lives behind seven doors down the left. This quick tour opens each " +
                              "one so you know where things are.\n\n" +
                              "You can replay it any time from the ? button in the title bar.",
                TextPosition = TutorialStepPosition.Bottom,
                TargetElementName = "SampleTarget",
                AdvanceTrigger = TutorialAdvanceTrigger.Manual,
                AllowManualSkip = true,
            };
            _sampleTargetBounds = new Rect(120, 90, 300, 64);

            Loaded += (_, _) =>
            {
                _loaded = true;
                UpdateOverlayPosition();
                if (_step != null) UpdateStep(_step);
            };
        }

        protected override void OnClosed(EventArgs e)
        {
            try { _spotlightDelayTimer?.Stop(); } catch { /* already stopped */ }
            _spotlightDelayTimer = null;
            base.OnClosed(e);
            // ponytail: needs TutorialService + TutorialEventBus, wired when they move to Core.
            // WPF also unhooked StepChanged / TutorialCompleted / the static event bus / the
            // Application.Exit + MainWindow.Closed shutdown handlers here; the static bus
            // subscription outliving the window is what used to leave a zombie process.
        }

        private void OnKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                SkipTutorial();
                e.Handled = true;
            }
        }

        // ---- Position over the target's monitor -------------------------------------

        /// <summary>
        /// Cover the whole monitor the target is on. WPF read Screen.FromHandle plus the WPF DPI
        /// scale because Window.Left is unreliable for a maximized WindowStyle="None" window;
        /// Avalonia's Screens gives the same answer without a P/Invoke, so
        /// GetDpiForMonitor / MonitorFromPoint / SystemParameters.WorkArea are all gone. The
        /// union-of-window-bounds fallback goes with them: it existed only for a failed Win32
        /// lookup.
        /// </summary>
        private void UpdateOverlayPosition()
        {
            // No target means render/sample mode: leave the window at the size the render proof
            // chose instead of snapping it to the headless screen.
            if (_targetWindow is not null)
            {
                try
                {
                    var screen = Screens.ScreenFromWindow(_targetWindow) ?? Screens.Primary;
                    if (screen is not null)
                    {
                        var scale = screen.Scaling <= 0 ? 1.0 : screen.Scaling;
                        Position = screen.Bounds.Position;
                        Width = screen.Bounds.Width / scale;
                        Height = screen.Bounds.Height / scale;
                    }
                }
                catch { /* no screen info; keep the current bounds */ }
            }

            if (_step != null && _loaded) UpdateSpotlight(_step);
        }

        // ---- Step rendering ---------------------------------------------------------

        private void UpdateStep(TutorialStep step)
        {
            _step = step;

            // WPF retargeted here when step.TargetWindowTypeName named a different window, and
            // drew a clean centered "next up" card while that window was still opening.
            // ponytail: needs TutorialService (CurrentSteps / CurrentStepIndex) and the
            // WindowLoaded:* bus event to know when to retarget; wired when they move to Core.

            // ponytail: needs TutorialService (CurrentStepIndex, TotalSteps). Placeholder counter
            // so the card is not left with the XAML's design-time text.
            _txtStepCounter.Text = "Step 1 of 6";
            _txtIcon.Text = step.Icon;
            _txtTitle.Text = step.Title;
            _txtDescription.Text = step.Description;

            _btnSupport.IsVisible = step.Id == "support";
            _btnSkip.IsVisible = !step.IsFollowUpCard;
            _btnSkipStep.IsVisible = step.AllowManualSkip && !step.IsFollowUpCard;

            // Manual advance trigger -> show Next; otherwise auto-advance handles it.
            bool isManual = step.AdvanceTrigger == TutorialAdvanceTrigger.Manual;
            _btnNext.IsVisible = isManual && !step.IsFollowUpCard;
            // ponytail: needs TutorialService.IsLastStep for the "Finish" label.
            _btnNext.Content = "Next";

            // Hide Previous in rails mode (going back is messy with state) and on follow-up cards.
            // ponytail: needs TutorialService.IsFirstStep; the placeholder step is not the first.
            _btnPrevious.IsVisible = isManual && !step.IsFollowUpCard;

            // Follow-up card mode renders a stacked button list inside the panel.
            if (step.IsFollowUpCard)
            {
                _followUpPanel.IsVisible = true;
                ConfigureFollowUpButton(_btnFollowUp1, step.FollowUpButton1Text, step.FollowUpAction1);
                ConfigureFollowUpButton(_btnFollowUp2, step.FollowUpButton2Text, step.FollowUpAction2);
                ConfigureFollowUpButton(_btnFollowUp3, step.FollowUpButton3Text, step.FollowUpAction3);
            }
            else
            {
                _followUpPanel.IsVisible = false;
            }

            // Run the spotlight synchronously rather than deferring it: on Windows the region that
            // lets clicks reach the highlighted control MUST be in place before the user's
            // MouseDown, or the button below never sets IsPressed and its Click never fires.
            // UpdateSpotlight has its own timer-based retry for the "target not laid out yet" case.
            try { UpdateSpotlight(step); } catch { /* a step must never take the window down */ }

            // Deferred, exactly as in WPF: UpdateStep can run inside the target's own click
            // handling, and focusing here would steal keyboard focus mid-click and break the
            // button's Click sequence. This is what makes Escape -> Skip reachable.
            Dispatcher.UIThread.Post(() =>
            {
                try { Focus(); } catch { /* window already closing */ }
            }, DispatcherPriority.Background);

            // ponytail: needs TutorialService — SubscribeAdvanceTrigger hooked the target control
            // for OnButtonClick (Click + PreviewMouseLeftButtonUp + the parent window's Closing),
            // OnTextEquals (TextBox.TextChanged, with the numeric/substring match),
            // OnSelectionEquals (Selector.SelectionChanged, by Content or Tag) and
            // OnSliderAtLeast (advance on pointer release, not on every ValueChanged), and
            // OnEvent came off the static TutorialEventBus. Without a service to call Next() on,
            // every one of those subscriptions has nothing to do.
        }

        /// <summary>The follow-up label comes from step data, which may contain an underscore, so
        /// it goes in a TextBlock: Avalonia parses "_" in a Button's string Content as an access
        /// key and would swallow it (CLAUDE.md trap 1).</summary>
        private static void ConfigureFollowUpButton(Button btn, string? text, Action<TutorialStep>? handler)
        {
            if (string.IsNullOrEmpty(text) || handler == null)
            {
                btn.IsVisible = false;
                return;
            }
            btn.Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap };
            btn.IsVisible = true;
        }

        // ---- Spotlight --------------------------------------------------------------

        private void UpdateSpotlight(TutorialStep step)
        {
            // Cancel any retry from a previous step — otherwise rapid step changes queue up
            // overlapping ticks that paint stale bounds over the new step's layout.
            try { _spotlightDelayTimer?.Stop(); } catch { /* already stopped */ }
            _spotlightDelayTimer = null;

            _spotlightCanvas.Children.Clear();

            // The sidebar keeps a door's entries collapsed until the door is open, so any step that
            // needs a tab must open that door FIRST — a collapsed entry has no measurable bounds.
            bool doorOpened = PrepareDoorForStep(step);

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

            // Sample mode: no target window to measure, so draw the seeded rect. This is the path
            // the render proof takes.
            if (_targetWindow is null)
            {
                if (_sampleTargetBounds is { } sample)
                {
                    DrawSpotlightOverlay(sample, clickThroughHole);
                    PositionTextPanel(sample, step.TextPosition);
                }
                else
                {
                    DrawFullOverlay(step.BlockBackgroundClicks);
                    CenterTextPanel();
                }
                return;
            }

            // Give the target window a chance to prepare itself (e.g. expand a collapsed drawer
            // that contains TargetElementName). Wrapped so a buggy callback can't break the tour.
            try { step.PrepareTargetWindowAction?.Invoke(_targetWindow); } catch { /* caller's bug */ }
            try { _targetWindow.UpdateLayout(); } catch { /* not laid out yet */ }

            var targetElement = FindElementByName(_targetWindow, step.TargetElementName);
            if (targetElement == null)
            {
                DrawFullOverlay(step.BlockBackgroundClicks);
                CenterTextPanel();
                return;
            }

            // A Manual step pointing at a TextBox almost always means "type something here, then
            // click Next." Without click-through the overlay swallows the click and the box never
            // gets focus.
            if (targetElement is TextBox) clickThroughHole = true;

            try { targetElement.BringIntoView(); } catch { /* not in a scroller */ }
            try { _targetWindow.UpdateLayout(); } catch { /* not laid out yet */ }

            var bounds = GetElementBounds(targetElement);
            bool unmeasured = bounds.X == 0 && bounds.Y == 0 && bounds.Width <= 100;

            if (unmeasured)
            {
                DrawFullOverlay(step.BlockBackgroundClicks);
                CenterTextPanel();
            }
            else
            {
                DrawSpotlightOverlay(bounds, clickThroughHole);
                PositionTextPanel(bounds, step.TextPosition);
            }

            // Re-measure when the target wasn't laid out yet, and also whenever we just opened a
            // door: an accordion animates, so the entry's bounds keep moving for a few frames
            // after the door "is" open and the first measure would pin the spotlight to a stale
            // rect.
            if (unmeasured || doorOpened)
            {
                ScheduleSpotlightSettle(step, targetElement, clickThroughHole, allowDegenerateDraw: unmeasured);
            }
        }

        /// <summary>
        /// Polls the target's bounds a few times and redraws the spotlight until they stop moving.
        /// Replaces the old single 120ms retry: same first tick, but it now converges instead of
        /// painting whatever the one shot happened to catch.
        /// </summary>
        private void ScheduleSpotlightSettle(TutorialStep currentStep, Control targetElement,
                                             bool clickThroughHole, bool allowDegenerateDraw)
        {
            const int maxTicks = 4;
            int ticks = 0;
            Rect lastBounds = default;

            // Held in a local as well as the field: a later step replaces the field, and this
            // closure must always stop ITS OWN timer, never the successor's.
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
            _spotlightDelayTimer = timer;

            void StopSelf()
            {
                try { timer.Stop(); } catch { /* already stopped */ }
                if (ReferenceEquals(_spotlightDelayTimer, timer)) _spotlightDelayTimer = null;
            }

            timer.Tick += (_, _) =>
            {
                ticks++;
                bool last = ticks >= maxTicks;

                if (!ReferenceEquals(_step, currentStep))
                {
                    StopSelf();
                    return;
                }

                var b = GetElementBounds(targetElement);
                bool degenerate = b.X == 0 && b.Y == 0 && b.Width <= 100;

                // Keep a good spotlight rather than replacing it with a bad measurement; only the
                // never-measured case is allowed to draw a degenerate rect (old behaviour).
                if (!degenerate || (allowDegenerateDraw && last))
                {
                    _spotlightCanvas.Children.Clear();
                    DrawSpotlightOverlay(b, clickThroughHole);
                    PositionTextPanel(b, currentStep.TextPosition);
                }

                // Settled (two identical measurements) or out of tries.
                if (last || (!degenerate && b == lastBounds))
                {
                    StopSelf();
                    return;
                }
                lastBounds = b;
            };
            timer.Start();
        }

        /// <summary>Opens the sidebar door that owns this step's target before we try to measure
        /// it. Returns true when a door was addressed (the caller then re-measures once it
        /// settles).</summary>
        private bool PrepareDoorForStep(TutorialStep step)
        {
            // ponytail: needs TutorialService.DoorTabKeyFor(step) and MainWindow.ExpandDoorForTab,
            // wired when they move to Core. Until then no door is opened, so a step whose target
            // sits behind a collapsed door falls through to the full-overlay path below — which is
            // the same thing WPF does when the element cannot be found.
            _ = step;
            return false;
        }

        private void DrawFullOverlay(bool blockClicks = true)
        {
            // When blockClicks=false the user needs to interact with something ON TOP of our
            // overlay (e.g. an OS save dialog), so drop the dim entirely and hand the whole
            // window's input away; the card alone signals the wait state.
            byte alpha = blockClicks ? DimAlpha() : (byte)0x00;
            var overlay = new Rectangle
            {
                Width = Bounds.Width,
                Height = Bounds.Height,
                Fill = new SolidColorBrush(Color.FromArgb(alpha, 0x00, 0x00, 0x00)),
                IsHitTestVisible = blockClicks,
            };
            Canvas.SetLeft(overlay, 0);
            Canvas.SetTop(overlay, 0);
            _spotlightCanvas.Children.Add(overlay);

            // ponytail: needs the same rect input region as the spotlight hole (see
            // ApplyWindowSpotlightRegion). WPF got its pass-through here for free, from alpha-0
            // pixels on a layered window; X11 does not honour pixel alpha for input, and
            // X11Overlay.SetClickThrough is whole-window, so handing input away would take the
            // card's Skip Tutorial / Skip step with it and strand the user with no way out — the
            // exact failure #443 was about. So the region is cleared in BOTH branches and the
            // pass-through dim (used while the user works an OS file dialog on top of us) is lost
            // until X11Overlay gains that entry point. The dim still drops to alpha 0, so the card
            // alone signals the wait state as it does on Windows.
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

            var fullRect = new RectangleGeometry(new Rect(0, 0, Bounds.Width, Bounds.Height));
            var spotlightRect = new RectangleGeometry(glowBounds) { RadiusX = 8, RadiusY = 8 };

            // When clickThroughHole=true the dark fill is the full rect MINUS the spotlight rect,
            // so the hole has no geometry and clicks fall through to the underlying control.
            // Otherwise a full opaque rect blocks everything (standard manual-mode overlay).
            Geometry darkGeometry = clickThroughHole
                ? new CombinedGeometry(GeometryCombineMode.Exclude, fullRect, spotlightRect)
                : fullRect;

            var darkPath = new Path
            {
                Data = darkGeometry,
                Fill = new SolidColorBrush(Color.FromArgb(DimAlpha(), 0x00, 0x00, 0x00)),
                IsHitTestVisible = true,
            };
            _spotlightCanvas.Children.Add(darkPath);

            var glowBorder = new Border
            {
                Width = glowBounds.Width,
                Height = glowBounds.Height,
                CornerRadius = new CornerRadius(8),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0x69, 0xB4)),
                BorderThickness = new Thickness(2),
                Background = Brushes.Transparent,
                IsHitTestVisible = false,
                Effect = new DropShadowEffect
                {
                    Color = Color.FromRgb(0xFF, 0x69, 0xB4),
                    BlurRadius = 15,
                    OffsetX = 0,
                    OffsetY = 0,
                    Opacity = 0.7,
                },
            };
            Canvas.SetLeft(glowBorder, glowBounds.X);
            Canvas.SetTop(glowBorder, glowBounds.Y);
            _spotlightCanvas.Children.Add(glowBorder);

            if (clickThroughHole) ApplyWindowSpotlightRegion(highlightBounds);
            else ClearWindowSpotlightRegion();
        }

        /// <summary>WPF dimmed harder over MainWindow (0xA0) than over a dialog (0x70). Sample mode
        /// has no target, and the overlay's normal home is MainWindow, so it takes the same
        /// value.</summary>
        private byte DimAlpha() => _targetWindow is null or MainWindow ? (byte)0xA0 : (byte)0x70;

        // ---- Window-level click-through ---------------------------------------------

        /// <summary>
        /// WPF punched the spotlight rect out of the window's OS region with
        /// CreateRectRgn + CombineRgn(RGN_DIFF) + SetWindowRgn, because a layered WPF window
        /// receives clicks over its whole client area whatever the pixel alpha, so the drawn hole
        /// alone gave no click-through.
        ///
        /// ponytail: needs a rect-region entry point on CCP.Avalonia/Platform/X11Overlay — it
        /// exposes only SetClickThrough(TopLevel, bool), which is the whole window or nothing, and
        /// no view layer may edit it. XFixes CAN express this exactly (an input region of
        /// everything-minus-the-hole; X11Overlay's own doc-comment says a non-empty region would
        /// be strictly more capable than WS_EX_TRANSPARENT), so this is one small addition to that
        /// class, not a lost feature. Until it lands: the spotlight hole is VISUAL ONLY — the ring
        /// and the lit rect draw correctly, but a click inside the hole is eaten by the overlay
        /// instead of reaching the highlighted control, which strands every auto-advance step that
        /// waits on the user clicking through. The region is cleared here rather than left
        /// half-applied so at least the card's own buttons keep working.
        /// </summary>
        private void ApplyWindowSpotlightRegion(Rect spotlightBoundsDip)
        {
            _ = spotlightBoundsDip;
            ClearWindowSpotlightRegion();
        }

        /// <summary>The exact analogue of SetWindowRgn(hwnd, IntPtr.Zero, true): give the window
        /// its whole input area back.</summary>
        private void ClearWindowSpotlightRegion() => SetWholeWindowClickThrough(false);

        /// <summary>X11Overlay is X11-only by design and returns false everywhere else (Windows,
        /// Wayland, and the headless render), so it is called unconditionally.</summary>
        private void SetWholeWindowClickThrough(bool clickThrough)
            => X11Overlay.SetClickThrough(this, clickThrough);

        // ---- Card placement ---------------------------------------------------------

        private void PositionTextPanel(Rect targetBounds, TutorialStepPosition position)
        {
            _textPanel.HorizontalAlignment = HorizontalAlignment.Left;
            _textPanel.VerticalAlignment = VerticalAlignment.Top;

            _textPanel.UpdateLayout();
            var panelWidth = _textPanel.Bounds.Width > 0 ? _textPanel.Bounds.Width : 460;
            var panelHeight = _textPanel.Bounds.Height > 0 ? _textPanel.Bounds.Height : 220;

            const double margin = 20;

            // Center the panel on the target along the perpendicular axis. With small targets like
            // buttons (width 120) and a much wider card (~460), edge-aligning made the card look
            // "offset" — its center floated way off the target's center.
            var (left, top) = ComputePanelPosition(position, targetBounds, panelWidth, panelHeight, margin);

            // Clamp to overlay extent.
            double clampedLeft = Math.Max(margin, Math.Min(left, Bounds.Width - panelWidth - margin));
            double clampedTop = Math.Max(margin, Math.Min(top, Bounds.Height - panelHeight - margin));

            // If clamping moved us into / over the target (small overlay or target near an edge),
            // flip to the opposite side so the card never sits on top of the spotlighted control.
            var panelRect = new Rect(clampedLeft, clampedTop, panelWidth, panelHeight);
            if (panelRect.Intersects(targetBounds))
            {
                var flipped = FlipPosition(position);
                var (altLeft, altTop) = ComputePanelPosition(flipped, targetBounds, panelWidth, panelHeight, margin);
                double altClampedLeft = Math.Max(margin, Math.Min(altLeft, Bounds.Width - panelWidth - margin));
                double altClampedTop = Math.Max(margin, Math.Min(altTop, Bounds.Height - panelHeight - margin));
                var altRect = new Rect(altClampedLeft, altClampedTop, panelWidth, panelHeight);
                if (!altRect.Intersects(targetBounds))
                {
                    clampedLeft = altClampedLeft;
                    clampedTop = altClampedTop;
                }
            }

            _textPanel.Margin = new Thickness(clampedLeft, clampedTop, 0, 0);
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
            _textPanel.HorizontalAlignment = HorizontalAlignment.Center;
            if (_targetWindow is null or MainWindow)
            {
                _textPanel.VerticalAlignment = VerticalAlignment.Center;
                _textPanel.Margin = new Thickness(0);
            }
            else
            {
                _textPanel.VerticalAlignment = VerticalAlignment.Bottom;
                _textPanel.Margin = new Thickness(0, 0, 0, 30);
            }
        }

        // ---- Target lookup ----------------------------------------------------------

        /// <summary>WPF walked the visual tree with VisualTreeHelper after trying FindName; the
        /// Avalonia twin is GetVisualChildren after FindControl.</summary>
        private static Control? FindElementByName(Visual? parent, string name)
        {
            if (parent == null) return null;

            if (parent is Control fe)
            {
                var found = fe.FindControl<Control>(name);
                if (found != null) return found;
            }

            try
            {
                foreach (var child in parent.GetVisualChildren())
                {
                    if (child is Control element && element.Name == name)
                        return element;

                    var result = FindElementByName(child, name);
                    if (result != null) return result;
                }
            }
            catch { /* the tree changed under us; a missed target just means a full overlay */ }
            return null;
        }

        /// <summary>The target's bounds in this overlay's own coordinates. PointToScreen returns a
        /// PixelPoint on Avalonia (WPF returned a device-independent Point), and PointToClient is
        /// the inverse — the pair replaces WPF's PointToScreen / PointFromScreen.</summary>
        private Rect GetElementBounds(Control element)
        {
            try
            {
                var screenTopLeft = element.PointToScreen(new Point(0, 0));
                var overlayLocal = this.PointToClient(screenTopLeft);
                return new Rect(overlayLocal, element.Bounds.Size);
            }
            catch
            {
                return new Rect(0, 0, 100, 40);
            }
        }

        // ---- Button handlers --------------------------------------------------------

        // ponytail: all four need TutorialService (Next / Previous / Skip and the once-per-step
        // advance guard), wired when it moves to Core.
        private void NextStep() { }
        private void PreviousStep() { }
        private void SkipTutorial() { }
        private void Advance() { }

        // ponytail: needs TutorialService.CurrentStep to reach the live step's actions; the button
        // labels and visibility above already come from the step object.
        private void InvokeFollowUp(int which)
        {
            if (_step is not { } step) return;
            var action = which switch
            {
                1 => step.FollowUpAction1,
                2 => step.FollowUpAction2,
                _ => step.FollowUpAction3,
            };
            action?.Invoke(step);
        }

        /// <summary>Not a stub: UseShellExecute hands the URL to xdg-open on Linux exactly as it
        /// hands it to the shell on Windows.</summary>
        private void BtnSupport_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "https://linktr.ee/CodeBambi",
                    UseShellExecute = true
                });
            }
            catch { /* no browser, or the user has no shell handler */ }
        }
    }
}
