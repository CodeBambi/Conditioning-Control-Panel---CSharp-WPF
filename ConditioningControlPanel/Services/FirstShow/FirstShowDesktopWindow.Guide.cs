using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ConditioningControlPanel.Features;
using ConditioningControlPanel.Services.EmiDesk;

namespace ConditioningControlPanel.Services.FirstShow;

internal sealed partial class FirstShowDesktopWindow
{
    // Steps name real UI targets. This sequence can grow without a second tutorial host.
    private sealed record GuideStep(string Line, string Tab, string? Target, bool Hover = false);
    private static readonly GuideStep[] WelcomeSteps =
    {
        new("guideHelp", "settings", "feature-help", true),
        new("guideMedia", "assets", "RemoteSourceChips"),
        new("guideFolder", "assets", "BtnOpenAssetsFolder"),
        new("guideSelection", "assets", "AssetTreeView")
    };
    private bool _guiding, _waitForHover;
    private int _guideIndex = -1;
    private double _hoverTime, _targetWait;
    private FrameworkElement? _guideTarget;
    private FeatureCard? _previewCard;
    private string? _targetName;
    private readonly FirstShowHighlight _highlight = new();
    private double _highlightWait;

    private void AskVerdict()
    {
        Say(Text("verdict")); _choices.Children.Clear();
        Button(Text("liked"), () => Reply("replyLiked"));
        Button(Text("showoff"), () => Reply("replyShowoff"));
    }

    private void Reply(string key)
    {
        Say(Text(key)); _choices.Children.Clear();
        Button(Text("showAround"), BeginWelcome);
        Button(Text("explore"), Close);
    }

    private void BeginWelcome()
    {
        if (_main == null) { Close(); return; }
        _guiding = true; _ending = false; _logo.Opacity = 0;
        _effects?.Dispose(); _effects = null; _surface.InvalidateVisual();
        // The show uses the primary display. The guide follows the actual app window.
        Left = SystemParameters.VirtualScreenLeft; Top = SystemParameters.VirtualScreenTop;
        Width = SystemParameters.VirtualScreenWidth; Height = SystemParameters.VirtualScreenHeight;
        _surface.Width = Width; _surface.Height = Height;
        if (!_stage.Children.Contains(_highlight)) _stage.Children.Insert(1, _highlight);
        _main.Show();
        if (_main.WindowState == WindowState.Minimized) _main.WindowState = WindowState.Normal;
        FirstShowInput.PassThrough(this);
        _main.Activate();
        NextGuideStep();
    }

    private void NextGuideStep()
    {
        ClearGuideTarget();
        if (_closed || _main == null) return;
        _guideIndex++;
        if (_guideIndex >= WelcomeSteps.Length) { FinishWelcome(); return; }
        var step = WelcomeSteps[_guideIndex];
        _main.ShowTab(step.Tab);
        _targetName = step.Target; _waitForHover = step.Hover; _targetWait = 0; _highlightWait = 0;
        Say(Text(_bundled && step.Line == "guideMedia" ? "guideMediaBundled" : step.Line));
        _choices.Children.Clear();
        if (!step.Hover) Button(Text("next"), NextGuideStep);
        Button(Text("finishTour"), FinishWelcome);
    }

    private void FinishWelcome()
    {
        ClearGuideTarget(); _guideIndex = WelcomeSteps.Length;
        _main?.ShowTab("settings");
        Say(Text("whereNext")); _choices.Children.Clear();
        Button(Text("sessions"), () => LeaveFor("presets"));
        Button(Text("studio"), () => LeaveFor("studio"));
        Button(Text("explore"), Close);
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
        if (_guideTarget != null) Controls.HelpPopover.CloseActive();
        _guideTarget = null; _targetName = null; _waitForHover = false; _hoverTime = 0;
        _highlight.Target = Rect.Empty; _highlight.InvalidateVisual();
    }

    private static IEnumerable<FrameworkElement> Descendants(DependencyObject root)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is FrameworkElement element) yield return element;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    private void GuideFrame(double dt)
    {
        if (_main == null || !_main.IsVisible || _main.WindowState == WindowState.Minimized)
        {
            _highlight.Visibility = Visibility.Collapsed;
            return;
        }
        if (_guideTarget == null && _targetName != null)
        {
            var all = Descendants(_main).Where(x => x.IsVisible).ToArray();
            if (_targetName == "feature-help")
            {
                _previewCard = all.OfType<FeatureCard>().FirstOrDefault(x => !x.IsLocked &&
                    !string.IsNullOrEmpty(x.HelpSectionId) && x.FindName("BtnHelp") is FrameworkElement { IsVisible: true });
                _guideTarget = _previewCard?.FindName("BtnHelp") as FrameworkElement;
                _previewCard?.SetTutorialPreview(true);
            }
            else _guideTarget = all.FirstOrDefault(x => x.Name == _targetName);
            _targetWait += dt;
            // A customized dashboard may have no feature cards. Never strand that player.
            if (_guideTarget == null && _targetWait > 3)
            {
                _targetName = null; _waitForHover = false;
                if (_guideIndex == 0) Button(Text("next"), NextGuideStep);
            }
        }
        _highlight.Visibility = Visibility.Visible;
        _highlight.Target = Rect.Empty;
        _highlight.Width = Width; _highlight.Height = Height;
        _highlight.Viewport = SafeBounds(); _highlight.InvalidateVisual();
        if (_guideTarget?.IsVisible == true && _guideTarget.ActualWidth > 0)
        {
            var top = PointFromScreen(_guideTarget.PointToScreen(new Point(0,0)));
            var bottom = PointFromScreen(_guideTarget.PointToScreen(new Point(_guideTarget.ActualWidth,_guideTarget.ActualHeight)));
            var target = new Rect(top,bottom);
            target.Intersect(SafeBounds());
            _highlight.Target = target;
            _highlightWait += dt;
            _highlight.Waiting = _highlightWait; _highlight.Time = _life.Elapsed.TotalSeconds;
            _highlight.InvalidateVisual();
            if (_waitForHover)
            {
                _hoverTime = _guideTarget.IsMouseOver ? _hoverTime+dt : 0;
                if (_hoverTime >= .65)
                {
                    _waitForHover = false;
                    _audio.Cue("pop"); Say(Text("foundHelp"));
                    _choices.Children.Clear(); Button(Text("next"), NextGuideStep);
                    Button(Text("finishTour"), FinishWelcome);
                }
            }
        }
        var placement = FirstShowLayout.GuideBody(SafeBounds(),BodySize(),SpeechSize(),_highlight.Target);
        double tx = placement.X+placement.Width/2, ty = placement.Y+placement.Height/2;
        double ease = MotionFx.Level == Models.MotionLevel.Off ? 1 : 1-Math.Exp(-dt*7);
        _x += (tx-_x)*ease; _y += (ty-_y)*ease;
    }
}
