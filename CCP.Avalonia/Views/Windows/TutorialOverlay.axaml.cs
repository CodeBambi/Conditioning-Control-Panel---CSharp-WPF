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

// The step model now comes from the tutorial seam. Aliased to the names the WPF original uses so
// this file still reads against ConditioningControlPanel/Windows/TutorialOverlay.xaml.cs line for
// line - and so the mapping (Models.TutorialStep -> CoreTutorial.Step) is stated once, here.
using TutorialStep = ConditioningControlPanel.CoreTutorial.Step;
using TutorialStepPosition = ConditioningControlPanel.CoreTutorial.StepPosition;
using TutorialAdvanceTrigger = ConditioningControlPanel.CoreTutorial.AdvanceTrigger;

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
    ///   <item><b>Live against the tutorial seam.</b> The card, the counter, the Next/Finish label,
    ///     Previous, Skip and the follow-up buttons all read <c>CoreTutorial</c>, and the two
    ///     verbs plus Escape call into it. On a head that seeds the seam (the WPF one does, from
    ///     <c>App.SeedTutorialSeam</c>) the overlay walks a real tour; unseeded — which is this
    ///     head today, because TutorialService and its step lists are still in the WPF head — the
    ///     seam answers "no tour" and nothing is drawn but the sample card. Use the
    ///     <see cref="TutorialOverlay(Window)"/> constructor for the live overlay; the
    ///     parameterless one is the render proof's.</item>
    ///
    ///   <item><b>The auto-advance subscriptions are still stubs</b> (OnButtonClick, OnTextEquals,
    ///     OnSelectionEquals, OnSliderAtLeast, OnEvent), and so is the WindowLoaded:* retarget.
    ///     Those are not seam calls: each hooks a real control in a real window and the OnEvent one
    ///     needs <c>TutorialEventBus</c>, which stayed in the WPF head. A step whose
    ///     <c>Advance</c> is anything but Manual therefore shows no Next button and nothing
    ///     advances it — it is honest about waiting and wrong about what it waits for, so it stops
    ///     the tour rather than skipping a card. <c>AllowManualSkip</c> steps still have their
    ///     "Skip step" button, which is the WPF escape hatch for exactly this.</item>
    ///
    ///   <item><b><c>TutorialStep.PrepareTargetWindowAction</c> did not cross.</b> It is an
    ///     <c>Action&lt;System.Windows.Window&gt;</c> on Windows (DeeperTutorialPrep,
    ///     CompanionTutorialPrep, AppSettingsTutorialPrep in Services/TutorialService.cs), so it
    ///     cannot be seam data. Without it a step pointing INSIDE a default-collapsed drawer
    ///     resolves its element but measures 0×0, and the settle timer runs out and draws the plain
    ///     dim — the degraded path WPF already takes when an element cannot be found, not a
    ///     crash.</item>
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

        /// <summary>True when this overlay is driving a real tour off <c>CoreTutorial</c>. False in
        /// the render constructor, which owns its own sample step and must not ask the seam
        /// anything — an unseeded seam would answer "0 of 0" and blank the sample card.</summary>
        private readonly bool _live;

        /// <summary>Once-per-step latch for <see cref="Advance"/>, exactly as WPF's
        /// <c>_advanceFiredThisStep</c>: overlay-instance state, reset on every step change. The
        /// brief called this portable; the original disagrees and it stays here, because the thing
        /// being de-duplicated is one overlay's view of one card, not the service's cursor.</summary>
        private bool _advanceFiredThisStep;

        /// <summary>Sample spotlight rect used by the render constructor, so the PNG exercises the
        /// spotlight path (geometry + glow ring + panel placement) and not just the plain dim.</summary>
        private Rect? _sampleTargetBounds;

        /// <summary>The counter text the render constructor shows, since the seam has no tour to
        /// count. Null on the live overlay, where the real "Step n of m" always wins.</summary>
        private string? _sampleCounter;

        // Retry timer used by UpdateSpotlight when the target's bounds aren't measured yet. Held
        // so consecutive UpdateSpotlight calls can cancel the prior tick (otherwise they pile up
        // and fire stale layouts) and so teardown stops it on close.
        private DispatcherTimer? _spotlightDelayTimer;

        /// <summary>
        /// Render constructor. Sample step data, and NOT connected to the seam: RenderProof needs a
        /// parameterless ctor, and a headless render must not be able to walk a live tour.
        /// </summary>
        public TutorialOverlay() : this(null, live: false)
        {
            // Placeholder: the first step of the full tour, re-pointed at a sample rect so the
            // render draws a spotlight rather than the Center-positioned plain card.
            _sampleCounter = "Step 1 of 6";
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
                Advance = TutorialAdvanceTrigger.Manual,
                AllowManualSkip = true,
            };
            _sampleTargetBounds = new Rect(120, 90, 300, 64);
        }

        /// <summary>
        /// The live overlay: it draws whatever tour <c>CoreTutorial</c> is running and measures its
        /// spotlight against <paramref name="targetWindow"/>. Show it AFTER the tour has started,
        /// as WPF does (MainWindow.StartTutorial calls Start then constructs) — the first card is
        /// read on Loaded, and StepChanged carries every card after it.
        ///
        /// <para>Nothing on this head calls this yet: starting a tour is
        /// <c>MainShellWindow.StartTutorial</c>, which is a different layer's file, and there is no
        /// step source here to start. It is the entry point that layer needs.</para>
        /// </summary>
        public TutorialOverlay(Window targetWindow) : this(targetWindow, live: true)
        {
        }

        private TutorialOverlay(Window? targetWindow, bool live)
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

            _live = live;
            _targetWindow = targetWindow;

            if (_live)
            {
                CoreTutorial.StepChanged += OnSeamStepChanged;
                CoreTutorial.Finished += OnSeamFinished;
            }

            Loaded += (_, _) =>
            {
                _loaded = true;
                UpdateOverlayPosition();
                // Live: the tour has already started, so the first card is read here (WPF does the
                // same on its own Loaded). Sample: _step was set by the render constructor.
                var first = _live ? CoreTutorial.CurrentStep : _step;
                if (first != null) UpdateStep(first);
            };
        }

        /// <summary>The tour moved. Raised synchronously by the head that owns it, and possibly off
        /// the UI thread, so it hops back before touching a control.</summary>
        private void OnSeamStepChanged(object? sender, TutorialStep step)
        {
            if (Dispatcher.UIThread.CheckAccess()) UpdateStep(step);
            else Dispatcher.UIThread.Post(() => UpdateStep(step));
        }

        /// <summary>The tour ended, by any route. The overlay goes with it — WPF fades out over
        /// 200ms and closes; the fade is dropped here for the reason in the header.</summary>
        private void OnSeamFinished(object? sender, bool completed)
        {
            _ = completed;
            if (Dispatcher.UIThread.CheckAccess()) Close();
            else Dispatcher.UIThread.Post(Close);
        }

        protected override void OnClosed(EventArgs e)
        {
            try { _spotlightDelayTimer?.Stop(); } catch { /* already stopped */ }
            _spotlightDelayTimer = null;

            if (_live)
            {
                CoreTutorial.StepChanged -= OnSeamStepChanged;
                CoreTutorial.Finished -= OnSeamFinished;

                // The window going away while the tour is still running is exactly what WPF's
                // OnAppExit / OnMainWindowClosed handlers cover, and they Skip() for a reason: a
                // tour with no overlay left is not running, and an IsActive stuck true would shut
                // every "not while a tutorial is up" gate in the app for the rest of the session.
                // Skip never latches the tour as completed, and the head guards re-entry, so the
                // ordinary close-after-Finished path lands on a no-op.
                if (CoreTutorial.IsActive) CoreTutorial.Skip();
            }

            base.OnClosed(e);
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
            // ponytail: CurrentStepIndex is NOT the blocker - CoreTutorial.CurrentStepIndex is in
            // Core and StepCounterText below already reads it. What is missing is the STEP LIST
            // (CoreTutorial exposes CurrentStep only, so "the next step's TargetWindowTypeName"
            // cannot be looked ahead) and the WindowLoaded:* bus event that says the other window
            // has opened - ConditioningControlPanel/Services/TutorialEventBus.cs, which has no Core
            // seam to emit into. Both live with
            // ConditioningControlPanel/Services/TutorialService.cs.

            // A new card: the once-per-step advance latch reopens. WPF does this inside
            // SubscribeAdvanceTrigger, which is the only thing UpdateStep calls after this point
            // that is still a stub here, so the reset moves up rather than getting lost with it.
            _advanceFiredThisStep = false;

            _txtStepCounter.Text = StepCounterText();
            _txtIcon.Text = step.Icon;
            _txtTitle.Text = step.Title;
            _txtDescription.Text = step.Description;

            _btnSupport.IsVisible = step.Id == "support";
            _btnSkip.IsVisible = !step.IsFollowUpCard;
            _btnSkipStep.IsVisible = step.AllowManualSkip && !step.IsFollowUpCard;

            // Manual advance trigger -> show Next; otherwise auto-advance handles it.
            bool isManual = step.Advance == TutorialAdvanceTrigger.Manual;
            _btnNext.IsVisible = isManual && !step.IsFollowUpCard;
            // "Finish" only when the seam says there IS a last card to be on: unseeded, TotalSteps
            // is 0 and IsLastStep is false, so the sample card keeps the honest "Next".
            _btnNext.Content = _live && CoreTutorial.IsLastStep ? "Finish" : "Next";

            // Hide Previous in rails mode (going back is messy with state) and on follow-up cards.
            // On the sample card there is nowhere to go back to either, so it hides there too.
            _btnPrevious.IsVisible = isManual && !step.IsFollowUpCard && _live && !CoreTutorial.IsFirstStep;

            // Follow-up card mode renders a stacked button list inside the panel.
            if (step.IsFollowUpCard)
            {
                _followUpPanel.IsVisible = true;
                ConfigureFollowUpButton(_btnFollowUp1, step.FollowUp1Text, step.FollowUp1);
                ConfigureFollowUpButton(_btnFollowUp2, step.FollowUp2Text, step.FollowUp2);
                ConfigureFollowUpButton(_btnFollowUp3, step.FollowUp3Text, step.FollowUp3);
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

            // ponytail: SubscribeAdvanceTrigger is head work, not a seam call — it hooks the target control
            // for OnButtonClick (Click + PreviewMouseLeftButtonUp + the parent window's Closing),
            // OnTextEquals (TextBox.TextChanged, with the numeric/substring match),
            // OnSelectionEquals (Selector.SelectionChanged, by Content or Tag) and
            // OnSliderAtLeast (advance on pointer release, not on every ValueChanged), and
            // OnEvent came off the static TutorialEventBus, which stayed in the WPF head. Every
            // one of them needs a real control in a real window, which is why none of it crossed
            // with the seam. The consequence is written into the class header.
        }

        /// <summary>The follow-up label comes from step data, which may contain an underscore, so
        /// it goes in a TextBlock: Avalonia parses "_" in a Button's string Content as an access
        /// key and would swallow it (CLAUDE.md trap 1).</summary>
        private static void ConfigureFollowUpButton(Button btn, string? text, Action? handler)
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
            bool clickThroughHole = step.Advance != TutorialAdvanceTrigger.Manual &&
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

            // WPF gave the target window a chance to prepare itself here (expand the collapsed
            // drawer that holds TargetElementName) via step.PrepareTargetWindowAction. That hook
            // is an Action<System.Windows.Window> and did not cross with the seam - see the class
            // header for what a step behind a collapsed drawer degrades to.
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
            // ponytail: needs MainShellWindow.ExpandDoorForTab, which does not exist on this head
            // (MainShellWindow.TabNavigation.cs has ShowTab, which also NAVIGATES — and a step can
            // spotlight a nav entry filed under a door other than the tab it requires, so ShowTab
            // would jump the user off the page the card is talking about). The door key itself is
            // deliberately not on CoreTutorial.Step either: TutorialService.DoorTabKeyFor is a pure
            // function of the step, but with no way to act on the answer a field carrying it would
            // just be seam surface nothing reads.
            //
            // Until that method exists, no door is opened, so a step whose target sits behind a
            // collapsed door falls through to the full-overlay path below — the same degraded card
            // WPF draws when an element cannot be found.
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
        private byte DimAlpha() => _targetWindow is null or MainWindow or MainShellWindow ? (byte)0xA0 : (byte)0x70;

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
            // MainShellWindow beside MainWindow: on this head the app shell is the tour's home
            // and the diagnostics MainWindow is not, so the centred card must follow the shell.
            if (_targetWindow is null or MainWindow or MainShellWindow)
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

        /// <summary>"Step n of m" from the seam. The sample card has no tour behind it, so it keeps
        /// the placeholder the render constructor set rather than reading 0 of 0.</summary>
        private string StepCounterText()
        {
            if (!_live) return _sampleCounter ?? "";
            return $"Step {CoreTutorial.CurrentStepIndex + 1} of {CoreTutorial.TotalSteps}";
        }

        private void NextStep() => CoreTutorial.Next();
        private void PreviousStep() => CoreTutorial.Previous();
        private void SkipTutorial() => CoreTutorial.Skip();

        /// <summary>
        /// "Skip step", and the entry point an auto-advance trigger would use. Once per step, then
        /// deferred: WPF cannot run Next() synchronously here because UpdateStep would then rewrite
        /// the card in the middle of the click that asked for it, and the bubbling half of that
        /// click never reaches the button underneath. The latch is set synchronously, so a second
        /// press before the post lands does nothing.
        /// </summary>
        private void Advance()
        {
            if (_advanceFiredThisStep) return;
            _advanceFiredThisStep = true;
            Dispatcher.UIThread.Post(() =>
            {
                if (CoreTutorial.IsActive) CoreTutorial.Next();
            });
        }

        /// <summary>Runs the live step's follow-up action, not the card's — a branch card can be
        /// clicked after the tour has moved on, and WPF reads CurrentStep here for that reason.
        /// Falls back to the drawn step when no tour is running (the render's sample card).</summary>
        private void InvokeFollowUp(int which)
        {
            var step = (_live ? CoreTutorial.CurrentStep : null) ?? _step;
            if (step is null) return;
            var action = which switch
            {
                1 => step.FollowUp1,
                2 => step.FollowUp2,
                _ => step.FollowUp3,
            };
            action?.Invoke();
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
