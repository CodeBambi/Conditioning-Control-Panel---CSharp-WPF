using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Features;
using ConditioningControlPanel.Fx;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.FirstShow;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows.WelcomeShow
{
    // PORTED from ConditioningControlPanel/Services/FirstShow/FirstShowDesktopWindow.Guide.cs.
    // Steps name real UI targets (Core FirstShowScript.WelcomeSteps). This sequence can grow without a
    // second tutorial host.
    //
    // Head differences: WPF stretched the stage over the virtual desktop for the guide; a window spanning
    // monitors of different scale is not something Avalonia lays out, so the stage moves to the monitor
    // the app window is on instead. The previewed feature card is lit while its help button is the target
    // (FeatureCard.SetTutorialPreview, appearance only) and goes back to its truth when the target clears.
    internal sealed partial class FirstShowStageWindow
    {
        private bool _guiding, _waitForHover;
        private int _guideIndex = -1;
        private double _hoverTime, _targetWait;
        private Control? _guideTarget;
        private FeatureCard? _previewCard;
        internal FeatureCard? PreviewCard => _previewCard;
        private string? _targetName;
        private readonly FirstShowHighlight _highlight = new();
        private double _highlightWait;

        private RectD TargetRect => _highlight.Target is { } t ? new RectD(t.X, t.Y, t.Width, t.Height) : FirstShowLayout.Empty;
        internal Control? GuideTarget => _guideTarget;
        internal int GuideIndex => _guideIndex;

        private void AskVerdict()
        {
            Say(Text("verdict")); _choices.Children.Clear();
            AddButton(Text("liked"), () => Reply("replyLiked"));
            AddButton(Text("showoff"), () => Reply("replyShowoff"));
        }

        private void Reply(string key)
        {
            Say(Text(key)); _choices.Children.Clear();
            AddButton(Text("showAround"), BeginWelcome);
            AddButton(Text("explore"), Close);
        }

        private void BeginWelcome()
        {
            if (_main == null) { Close(); return; }
            _guiding = true; _ending = false; _logo.Opacity = 0;   // the logo's OUT
            _effects?.Dispose(); _effects = null; _surface.Redraw(); _surface.InvalidateVisual();
            // The show uses the primary display. The guide follows the actual app window.
            try
            {
                if (Screens?.ScreenFromWindow(_main) is { } screen)
                {
                    _screenPx = screen.Bounds; _scale = screen.Scaling > 0 ? screen.Scaling : 1;
                    Position = _screenPx.Position; Width = _screenPx.Width / _scale; Height = _screenPx.Height / _scale;
                }
            }
            catch (Exception ex) { Log.Debug(ex, "First show guide: could not follow the app window"); }
            _surface.Width = Width; _surface.Height = Height;
            if (!_stage.Children.Contains(_highlight)) _stage.Children.Insert(1, _highlight);
            _highlight.Width = Width; _highlight.Height = Height;
            if (!_main.IsVisible) _main.Show();
            if (_main.WindowState == WindowState.Minimized) _main.WindowState = WindowState.Normal;
            try { ClickThrough = X11Overlay.SetClickThrough(this, true); _inputShaped = false; }
            catch (Exception ex) { Log.Debug(ex, "First show guide: click-through failed"); }
            _main.Activate();
            _emi.RaiseAboveStage();
            NextGuideStep();
        }

        private void NextGuideStep()
        {
            ClearGuideTarget();
            if (_closed || _main == null) return;
            _guideIndex++;
            if (_guideIndex >= FirstShowScript.WelcomeSteps.Length) { FinishWelcome(); return; }
            var step = FirstShowScript.WelcomeSteps[_guideIndex];
            _main.ShowTab(step.Tab);
            _targetName = step.Target; _waitForHover = step.Hover; _targetWait = 0; _highlightWait = 0;
            Say(Text(_bundled && step.Line == "guideMedia" ? "guideMediaBundled" : step.Line));
            _choices.Children.Clear();
            if (!step.Hover) AddButton(Text("next"), NextGuideStep);
            AddButton(Text("finishTour"), FinishWelcome);
        }

        private void FinishWelcome()
        {
            ClearGuideTarget(); _guideIndex = FirstShowScript.WelcomeSteps.Length;
            _main?.ShowTab("settings");
            Say(Text("whereNext")); _choices.Children.Clear();
            AddButton(Text("sessions"), () => LeaveFor("presets"));
            AddButton(Text("studio"), () => LeaveFor("studio"));
            AddButton(Text("explore"), Close);
        }

        private void LeaveFor(string tab)
        {
            var main = _main;
            Close();
            main?.ShowTab(tab);
            main?.Activate();
        }

        private void ClearGuideTarget()
        {
            _previewCard?.SetTutorialPreview(false); _previewCard = null;
            if (_guideTarget != null)
            {
                try { global::ConditioningControlPanel.Avalonia.Controls.HelpPopover.CloseActive(); }
                catch (Exception ex) { Log.Debug(ex, "First show guide: help popover close failed"); }
            }
            _guideTarget = null; _targetName = null; _waitForHover = false; _hoverTime = 0;
            _highlight.Target = null; _highlight.InvalidateVisual();
        }

        private void GuideFrame(double dt)
        {
            if (_main == null || !_main.IsVisible || _main.WindowState == WindowState.Minimized)
            {
                _highlight.IsVisible = false;
                return;
            }
            if (_guideTarget == null && _targetName != null)
            {
                var all = _main.GetVisualDescendants().OfType<Control>().Where(x => x.IsEffectivelyVisible).ToArray();
                if (_targetName == "feature-help")
                {
                    var card = all.OfType<FeatureCard>().FirstOrDefault(x => !x.IsLocked &&
                        !string.IsNullOrEmpty(x.HelpSectionId) && x.FindControl<Button>("BtnHelp") is { IsEffectivelyVisible: true });
                    _guideTarget = card?.FindControl<Button>("BtnHelp");
                    _previewCard = card;
                    _previewCard?.SetTutorialPreview(true);
                }
                else _guideTarget = all.FirstOrDefault(x => x.Name == _targetName);
                _targetWait += dt;
                // A customized dashboard may have no feature cards. Never strand that player.
                if (_guideTarget == null && _targetWait > FirstShowScript.TargetGiveUpSeconds)
                {
                    _targetName = null; _waitForHover = false;
                    if (_guideIndex == 0) AddButton(Text("next"), NextGuideStep);
                }
            }
            _highlight.IsVisible = true;
            _highlight.Target = null;
            _highlight.Width = Width; _highlight.Height = Height;
            var safe = SafeBounds();
            _highlight.Viewport = new Rect(safe.Left, safe.Top, safe.Width, safe.Height);
            if (_guideTarget is { IsEffectivelyVisible: true } target && target.Bounds.Width > 0)
            {
                try
                {
                    var a = target.PointToScreen(new Point(0, 0));
                    var b = target.PointToScreen(new Point(target.Bounds.Width, target.Bounds.Height));
                    var rect = new Rect(new Point((a.X - Position.X) / _scale, (a.Y - Position.Y) / _scale),
                        new Point((b.X - Position.X) / _scale, (b.Y - Position.Y) / _scale)).Intersect(_highlight.Viewport);
                    if (rect.Width > 0 && rect.Height > 0) _highlight.Target = rect;
                }
                catch (Exception ex) { Log.Debug(ex, "First show guide: target has no screen position"); }
                _highlightWait += dt;
                _highlight.Waiting = _highlightWait; _highlight.Time = Clock();
                if (_waitForHover)
                {
                    _hoverTime = target.IsPointerOver ? _hoverTime + dt : 0;
                    if (_hoverTime >= FirstShowScript.HoverSeconds) FoundHelp();
                }
            }
            _highlight.InvalidateVisual();
            var placement = FirstShowLayout.GuideBody(safe, BodySize(), SpeechSize(), TargetRect);
            double tx = placement.Left + placement.Width / 2, ty = placement.Top + placement.Height / 2;
            double ease = Motion == MotionLevel.Off ? 1 : 1 - Math.Exp(-dt * 7);
            _x += (tx - _x) * ease; _y += (ty - _y) * ease;
        }

        internal void FoundHelp()
        {
            _waitForHover = false;
            _audio.Cue("pop"); Say(Text("foundHelp"));
            _choices.Children.Clear(); AddButton(Text("next"), NextGuideStep);
            AddButton(Text("finishTour"), FinishWelcome);
        }
    }
}
