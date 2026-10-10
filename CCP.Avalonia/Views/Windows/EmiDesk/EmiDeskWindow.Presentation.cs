using System;
using Avalonia;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk
{
    // PORTED from ConditioningControlPanel/Windows/EmiDesk/EmiDeskWindow.Presentation.cs (WPF 7.1.5).
    // Desk presentation mode: the welcome show borrows the REAL widget (never a stand-in), drives her
    // position, bob, turn, pose and face frame by frame, and hands her back exactly where she was.
    //
    // Head differences, each on purpose:
    //   - WPF re-owned her to the stage (Owner = stage) so she sat above it. An Avalonia window cannot be
    //     re-owned once shown; she is raised above the stage instead (both are topmost, last raised wins)
    //     and her owner is never touched, so there is nothing to restore.
    //   - WPF's "presentation: true" flag on deferred FX steps is the epoch alone here: After() drops any
    //     step scheduled in the other world (see After in EmiDeskWindow.axaml.cs).
    public partial class EmiDeskWindow
    {
        internal bool PresentationActive { get; private set; }
        internal bool PresentationArriving { get; private set; }
        private Action? _stopPresentation;
        private PixelPoint _beforePresentation;
        private string? _presentationFace;
        private string? _presentationPose;
        private int _presentationEpoch;

        internal void BeginPresentation(Action stop)
        {
            _beforePresentation = Position;
            PresentationActive = true; _presentationEpoch++; _presentationFace = null; _presentationPose = null;
            _stopPresentation = stop;
            try
            {
                FinishSummon(); CancelChain();
                CloseRing(); CloseOptionsPanel(); CloseBook();
                StopIdleBeats(); StopAlive(); DisarmPet();
                _dragging = false;
                SweepFx(all: true); TearDownReactions();
            }
            catch (Exception ex) { Log.Debug(ex, "[EmiDesk] presentation setup step failed"); }
            _bodyRoot.IsVisible = true;
            SetCrt(1, 1);
            InputLocked = false; _transiting = false;
            _btnGear.IsVisible = false;
            _btnHelp.IsVisible = false;
            RefreshOutfit();
            Show();
            Opacity = 1;
            RaiseAboveStage();
            SetPose("idle"); DrawFace("^_^");
        }

        /// <summary>Last raised wins inside the topmost band: call after the stage is on screen.</summary>
        internal void RaiseAboveStage()
        {
            try { Topmost = false; Topmost = true; }
            catch (Exception ex) { Log.Debug(ex, "[EmiDesk] raise above the stage failed"); }
        }

        internal void RunPresentationEntrance()
        {
            if (!PresentationActive) return;
            PresentationArriving = true;
            if (global::ConditioningControlPanel.Avalonia.Controls.AmbientFxCanvas.Env.Level == ConditioningControlPanel.Models.MotionLevel.Off) { PresentationArriving = false; return; }
            int epoch = _presentationEpoch;
            RunSummon(() => { if (epoch == _presentationEpoch) { PresentationArriving = false; _presentationFace = null; _presentationPose = null; } });
        }

        /// <summary>Her centre in PHYSICAL screen pixels, plus this frame's bob (DIPs), turn (degrees), pose and face.</summary>
        internal void PresentAt(Point centerPixels, double bob, double turn, string pose, string face)
        {
            if (!PresentationActive) return;
            double scale = Math.Max(.1, DipScale);
            double bw = _bodyWidth * scale, bh = _bodyWidth * BodyAspect * scale;
            SetBodyPhysical(centerPixels.X - bw / 2, centerPixels.Y - bh / 2);
            if (PresentationArriving) return;
            _moveShift.Y = bob; _wobbleRotate.Angle = turn;
            if (_presentationPose != pose) { _presentationPose = pose; SetPose(pose); }
            if (_presentationFace != face) { _presentationFace = face; DrawFace(face); }
        }

        internal void StopPresentation() => _stopPresentation?.Invoke();

        internal void EndPresentation()
        {
            if (!PresentationActive) return;
            PresentationActive = false; PresentationArriving = false; _presentationEpoch++;
            try { FinishSummon(); CancelChain(); SweepFx(all: true); }
            catch (Exception ex) { Log.Debug(ex, "[EmiDesk] presentation teardown step failed"); }
            _bodyRoot.IsVisible = true; SetCrt(1, 1);
            InputLocked = false; _transiting = false; _stopPresentation = null;
            Position = _beforePresentation;
            _moveShift.Y = 0; _wobbleRotate.Angle = 0;
            _btnGear.IsVisible = true; _btnHelp.IsVisible = true;
            SetPose("idle"); DrawFace("^_^"); ClampIntoWorkArea();
            RestartIdleBeats(); StartAlive();
        }
    }
}
