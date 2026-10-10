using System;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Tours;
using HeadStep = ConditioningControlPanel.Avalonia.Tours.TutorialStep;
using HeadTrigger = ConditioningControlPanel.Avalonia.Tours.TutorialAdvanceTrigger;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    // The LIVE half of the coach mark.
    //
    // WPF's overlay is a second, topmost, full-screen layered window with a hole cut out of its OS
    // region. On this head a live tour never opens a window at all: the card and the dim are moved
    // into the OverlayLayer of the window the tour is about. Three rules fall out of that for free,
    // on Windows and Linux alike, with no platform call:
    //   - it never takes the pointer from another app (it is not on the desktop, only in our window);
    //   - it never steals focus from another app (nothing is shown or activated);
    //   - the spotlight hole is real (the dim is a Path with the hole excluded; an empty Canvas does
    //     not hit-test, so a click in the hole lands on the control under it).
    // Inside our own window the dim still absorbs clicks outside the hole and the card, as WPF's does
    // (TutorialStep.BlockBackgroundClicks).
    //
    // The parameterless render constructor still shows the real window (RenderProof needs a Window).
    public partial class TutorialOverlay
    {
        private Control? _stage;                 // RootGrid once it lives in a window's OverlayLayer
        private OverlayLayer? _layer;
        private IDisposable? _layerBoundsSub;
        private bool _tornDown;

        /// <summary>The tour's card left the screen (finished, skipped, panic, host closed). Hides
        /// Window.Closed: a live overlay is never a shown window, so the base event would never fire.</summary>
        public new event EventHandler? Closed;

        /// <summary>True while the live card is on a window.</summary>
        internal bool IsOnStage => _stage != null && _layer != null;

        /// <summary>The window the card is on right now.</summary>
        internal Window? StageWindow => _targetWindow;

        private double StageW => _stage != null ? _stage.Bounds.Width : Bounds.Width;
        private double StageH => _stage != null ? _stage.Bounds.Height : Bounds.Height;

        /// <summary>Live: put the card on the target window. Sample: show the render window.</summary>
        public new void Show()
        {
            if (!_live || _targetWindow == null) { base.Show(); return; }
            if (!AttachTo(_targetWindow))
            {
                Serilog.Log.Warning("Tutorial: {Window} has no overlay layer, the tour cannot draw; ending it", _targetWindow.GetType().Name);
                Close();
                return;
            }
            _loaded = true;
            if (CoreTutorial.CurrentStep is { } first) UpdateStep(first);
        }

        /// <summary>Take the card down. Ends a tour that is still running (the abandon route).</summary>
        public new void Close()
        {
            if (!_live) { base.Close(); return; }
            Teardown();
        }

        private void Teardown()
        {
            if (_tornDown) return;
            _tornDown = true;
            try { _spotlightDelayTimer?.Stop(); } catch { /* already stopped */ }
            _spotlightDelayTimer = null;
            UnsubscribeAdvanceTrigger();
            CoreTutorial.StepChanged -= OnSeamStepChanged;
            CoreTutorialEvents.Event -= OnBusEvent;
            CoreTutorial.Finished -= OnSeamFinished;
            Detach();
            // The host window closing must not strand a running tour (WPF OnClosed does the same).
            if (CoreTutorial.IsActive) CoreTutorial.Skip();
            try { Closed?.Invoke(this, EventArgs.Empty); } catch { /* a subscriber's fault is not the tour's */ }
        }

        private bool AttachTo(Window window)
        {
            OverlayLayer? layer = null;
            try { layer = OverlayLayer.GetOverlayLayer(window); } catch { /* template not applied */ }
            if (layer == null) return false;

            var root = _stage ?? (Content as Control);
            if (root == null) return false;
            if (_stage == null) Content = null;     // out of the never-shown window, once

            _stage = root;
            _layer = layer;
            root.Width = layer.Bounds.Width;
            root.Height = layer.Bounds.Height;
            layer.Children.Add(root);
            _layerBoundsSub = layer.GetObservable(BoundsProperty).Subscribe(new BoundsObserver(this));

            window.AddHandler(KeyDownEvent, OnStageKeyDown, RoutingStrategies.Tunnel);
            window.Closed += OnStageWindowClosed;
            return true;
        }

        private void Detach()
        {
            try { _layerBoundsSub?.Dispose(); } catch { }
            _layerBoundsSub = null;
            if (_targetWindow != null)
            {
                try { _targetWindow.RemoveHandler(KeyDownEvent, OnStageKeyDown); } catch { }
                _targetWindow.Closed -= OnStageWindowClosed;
            }
            if (_layer != null && _stage != null)
            {
                try { _layer.Children.Remove(_stage); } catch { }
            }
            _layer = null;
        }

        private sealed class BoundsObserver : IObserver<Rect>
        {
            private readonly TutorialOverlay _o;
            public BoundsObserver(TutorialOverlay o) => _o = o;
            public void OnCompleted() { }
            public void OnError(Exception error) { }
            public void OnNext(Rect value) => _o.OnLayerBounds(value);
        }

        private void OnLayerBounds(Rect b)
        {
            if (_stage == null || _tornDown) return;
            _stage.Width = b.Width;
            _stage.Height = b.Height;
            // Re-measure after this layout pass: the target moved with the window's size.
            Dispatcher.UIThread.Post(() =>
            {
                if (_tornDown || _step == null) return;
                try { UpdateSpotlight(_step); } catch { /* a step must never take the window down */ }
            }, DispatcherPriority.Background);
        }

        private void OnStageWindowClosed(object? sender, EventArgs e) => Teardown();

        // WPF TutorialOverlay.OnKeyDown: Escape abandons the tour. Tunnelled on the HOST window, since
        // the card never takes keyboard focus.
        private void OnStageKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key != Key.Escape || _tornDown) return;
            SkipTutorial();
            e.Handled = true;
        }

        // ---- retarget (WPF RetargetToWindow / FindWindowByTypeName) --------------------------

        private static Window? FindWindowByTypeName(string typeName)
        {
            try
            {
                var windows = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Windows;
                return windows?.LastOrDefault(w => w.GetType().Name == typeName && w.IsVisible);
            }
            catch { return null; }
        }

        /// <summary>Move the card onto another window (a step names the window its target is in).</summary>
        internal void RetargetToWindow(Window newWindow)
        {
            if (_tornDown || ReferenceEquals(newWindow, _targetWindow)) return;
            UnsubscribeAdvanceTrigger();
            Detach();
            _targetWindow = newWindow;
            if (!AttachTo(newWindow)) { Teardown(); return; }
            if (CoreTutorial.CurrentStep is { } step) UpdateStep(step);
        }

        /// <summary>True when the step's window is not the one the card is on and it could be moved
        /// there (UpdateStep ran again from RetargetToWindow, so the caller returns).</summary>
        private bool RetargetIfNeeded(CoreTutorial.Step step)
        {
            if (!_live || step.TargetWindowTypeName == null || _targetWindow == null) return false;
            if (_targetWindow.GetType().Name == step.TargetWindowTypeName) return false;
            var w = FindWindowByTypeName(step.TargetWindowTypeName);
            if (w == null || ReferenceEquals(w, _targetWindow)) return false;   // WindowLoaded:* brings us later
            RetargetToWindow(w);
            return true;
        }

        private void OnWindowLoadedEvent(string typeName)
        {
            if (CoreTutorial.CurrentStep is { } step && step.TargetWindowTypeName == typeName
                && FindWindowByTypeName(typeName) is { } w)
                RetargetToWindow(w);
        }

        // ---- the head step behind the card ---------------------------------------------------

        private HeadStep? HeadStepFor(CoreTutorial.Step step)
        {
            var head = TutorialHead.CurrentHeadStep;
            return head != null && head.Id == step.Id ? head : null;
        }

        /// <summary>WPF UpdateSpotlight: run the step's prep so its target can be measured.</summary>
        private void PrepareTarget(CoreTutorial.Step step)
        {
            if (_targetWindow == null) return;
            try { HeadStepFor(step)?.PrepareTargetWindowAction?.Invoke(_targetWindow); }
            catch { /* a tour never blocks on UI quirks */ }
        }

        // ---- auto-advance (WPF SubscribeAdvanceTrigger :1026) --------------------------------

        private HeadStep? _subscribedStep;
        private Button? _subButton;
        private TextBox? _subTextBox;
        private SelectingItemsControl? _subSelector;
        private Slider? _subSlider;
        private Window? _subParentWindow;
        private IDisposable? _subTextSub;

        private void SubscribeAdvanceTrigger(CoreTutorial.Step coreStep)
        {
            UnsubscribeAdvanceTrigger();
            _advanceFiredThisStep = false;
            var step = HeadStepFor(coreStep);
            if (step == null || _targetWindow == null) return;
            _subscribedStep = step;

            if (step.AdvanceTrigger is HeadTrigger.Manual or HeadTrigger.OnEvent) return;
            if (string.IsNullOrEmpty(step.TargetElementName)) return;
            var target = FindElementByName(_targetWindow, step.TargetElementName);
            if (target == null) return;

            switch (step.AdvanceTrigger)
            {
                case HeadTrigger.OnButtonClick:
                    if (target is Button btn)
                    {
                        _subButton = btn;
                        btn.AddHandler(Button.ClickEvent, OnSubButtonClick, RoutingStrategies.Bubble, handledEventsToo: true);
                        // A dialog's OK closes the window before Click bubbles anywhere useful.
                        if (TopLevel.GetTopLevel(btn) is Window parent)
                        {
                            _subParentWindow = parent;
                            parent.Closing += OnSubParentClosing;
                        }
                    }
                    break;
                case HeadTrigger.OnTextEquals:
                    if (target is TextBox tb)
                    {
                        _subTextBox = tb;
                        _subTextSub = tb.GetObservable(TextBox.TextProperty).Subscribe(new TextObserver(this));
                    }
                    break;
                case HeadTrigger.OnSelectionEquals:
                    if (target is SelectingItemsControl sel)
                    {
                        _subSelector = sel;
                        sel.SelectionChanged += OnSubSelectionChanged;
                    }
                    break;
                case HeadTrigger.OnSliderAtLeast:
                    if (target is Slider sl)
                    {
                        _subSlider = sl;
                        sl.AddHandler(PointerReleasedEvent, OnSubSliderReleased, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
                    }
                    break;
            }
        }

        private void UnsubscribeAdvanceTrigger()
        {
            if (_subButton != null)
            {
                try { _subButton.RemoveHandler(Button.ClickEvent, OnSubButtonClick); } catch { }
                _subButton = null;
            }
            if (_subParentWindow != null)
            {
                _subParentWindow.Closing -= OnSubParentClosing;
                _subParentWindow = null;
            }
            try { _subTextSub?.Dispose(); } catch { }
            _subTextSub = null;
            _subTextBox = null;
            if (_subSelector != null)
            {
                _subSelector.SelectionChanged -= OnSubSelectionChanged;
                _subSelector = null;
            }
            if (_subSlider != null)
            {
                try { _subSlider.RemoveHandler(PointerReleasedEvent, OnSubSliderReleased); } catch { }
                _subSlider = null;
            }
            _subscribedStep = null;
        }

        private void OnSubButtonClick(object? sender, RoutedEventArgs e) => Advance();

        private void OnSubParentClosing(object? sender, WindowClosingEventArgs e)
        {
            // WPF advances only on DialogResult == true. An Avalonia dialog has no such flag to read
            // while closing; the Click handler above covers the confirm button, so a close by any
            // other route leaves the step where it is.
        }

        private sealed class TextObserver : IObserver<string?>
        {
            private readonly TutorialOverlay _o;
            public TextObserver(TutorialOverlay o) => _o = o;
            public void OnCompleted() { }
            public void OnError(Exception error) { }
            public void OnNext(string? value)
            {
                if (_o._subscribedStep is { } step && TextMatches(value ?? "", step.AdvanceValue ?? "")) _o.Advance();
            }
        }

        private void OnSubSelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (_subscribedStep is not { } step || _subSelector == null) return;
            if (string.IsNullOrEmpty(step.AdvanceValue)) { Advance(); return; }
            var actual = GetSelectorValue(_subSelector, step.MatchByTag);
            if (!string.IsNullOrEmpty(actual) && string.Equals(actual, step.AdvanceValue, StringComparison.OrdinalIgnoreCase))
                Advance();
        }

        private void OnSubSliderReleased(object? sender, PointerReleasedEventArgs e)
        {
            if (_subscribedStep is not { } step || _subSlider == null) return;
            var v = _subSlider.Value;
            if (v < step.AdvanceMinValue) return;
            if (!double.IsNaN(step.AdvanceMaxValue) && v > step.AdvanceMaxValue) return;
            Advance();
        }

        /// <summary>WPF TextMatches: equal, numerically within a half, or contains.</summary>
        internal static bool TextMatches(string actual, string expected)
        {
            actual = (actual ?? "").Trim();
            expected = (expected ?? "").Trim();
            if (expected.Length == 0) return false;
            if (string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase)) return true;
            if (double.TryParse(actual, NumberStyles.Any, CultureInfo.InvariantCulture, out var av) &&
                double.TryParse(expected, NumberStyles.Any, CultureInfo.InvariantCulture, out var ev))
                return Math.Abs(av - ev) < 0.5;
            return actual.Length >= expected.Length &&
                   actual.IndexOf(expected, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string? GetSelectorValue(SelectingItemsControl sel, bool matchByTag)
        {
            var selected = sel.SelectedItem;
            if (selected is ContentControl item)
                return matchByTag ? item.Tag?.ToString() : item.Content?.ToString();
            return selected?.ToString();
        }

        // ---- test seams ----------------------------------------------------------------------

        internal Control? StageForTests => _stage;
        internal Border CardForTests => _textPanel;
        internal Canvas SpotlightForTests => _spotlightCanvas;
        internal Button NextButtonForTests => _btnNext;
        internal Button SkipButtonForTests => _btnSkip;
        internal Button PreviousButtonForTests => _btnPrevious;
        internal string CounterForTests => _txtStepCounter.Text ?? "";
        internal string TitleForTests => _txtTitle.Text ?? "";
    }
}
