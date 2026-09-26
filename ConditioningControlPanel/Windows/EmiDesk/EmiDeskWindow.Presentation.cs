using System;
using System.Windows;
using ConditioningControlPanel.Services.EmiDesk;

namespace ConditioningControlPanel;

public partial class EmiDeskWindow
{
    internal bool PresentationActive { get; private set; }
    internal bool PresentationArriving { get; private set; }
    private Action? _stopPresentation;
    private Point _beforePresentation;
    private Window? _beforePresentationOwner;
    private string? _presentationFace;
    private int _presentationEpoch;

    internal void BeginPresentation(Window stage, Action stop)
    {
        _beforePresentation = new Point(Left, Top);
        _beforePresentationOwner = Owner;
        PresentationActive = true; _presentationEpoch++; _presentationFace = null;
        _stopPresentation = stop;
        FinishSummon(); CancelChain(); CancelAsk("presentation");
        CloseChannel(false, silent: true); CloseRing(); CloseOptionsPanel(); EmiBook.Close();
        StopIdleBeats(); StopAlive(); DisarmPet();
        _dragging = false; BodyRoot.ReleaseMouseCapture();
        SweepFx(all: true); TearDownReactions(); CancelHold();
        BodyRoot.Visibility = Visibility.Visible;
        ResetCrtBase(clearAnimations: true);
        InputLocked = false; _transiting = false;
        BtnGear.Visibility = Visibility.Collapsed;
        BtnHelp.Visibility = Visibility.Collapsed;
        RefreshOutfit(); Owner = stage; Show();
        SetPose("idle"); DrawFace("^_^");
    }

    internal void RunPresentationEntrance()
    {
        if (!PresentationActive) return;
        PresentationArriving = true;
        if (Services.MotionFx.Level == Models.MotionLevel.Off) { PresentationArriving = false; return; }
        RunSummon(() => { PresentationArriving = false; _presentationFace = null; }, presentation: true);
    }

    internal void PresentAt(Point centerPixels, double bob, double turn, string pose, string face)
    {
        if (!PresentationActive) return;
        double scale = Math.Max(.1, DipScale);
        Left = centerPixels.X / scale - OverlayPadX - BodyWidth / 2;
        Top = centerPixels.Y / scale - OverlayPad - BodyWidth * BodyAspect / 2;
        if (PresentationArriving) return;
        MoveShift.Y = bob; WobbleRotate.Angle = turn;
        SetPose(pose);
        if (_presentationFace != face) { _presentationFace = face; DrawFace(face); }
    }

    internal void StopPresentation() => _stopPresentation?.Invoke();

    internal void EndPresentation()
    {
        if (!PresentationActive) return;
        PresentationActive = false; PresentationArriving = false; _presentationEpoch++;
        FinishSummon(); CancelChain(); SweepFx(all: true);
        BodyRoot.Visibility = Visibility.Visible; ResetCrtBase(clearAnimations: true);
        InputLocked = false; _transiting = false; _stopPresentation = null;
        Owner = _beforePresentationOwner;
        Left = _beforePresentation.X; Top = _beforePresentation.Y;
        MoveShift.Y = 0; WobbleRotate.Angle = 0;
        BtnGear.Visibility = Visibility.Visible; BtnHelp.Visibility = Visibility.Visible;
        SetPose("idle"); DrawFace("^_^"); ClampIntoWorkArea();
        RestartIdleBeats(); StartAlive();
    }
}
