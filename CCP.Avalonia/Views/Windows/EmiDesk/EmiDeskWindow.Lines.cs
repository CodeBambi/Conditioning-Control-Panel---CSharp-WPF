using System;
using Avalonia.Threading;
using ConditioningControlPanel.Services.EmiDesk;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk
{
    /// <summary>
    /// How a drawn line looks: WPF EmiDeskWindow.Bubble.cs SpeakLine :503, HoldFace :545,
    /// CancelHold :574. The engine already decided she may speak. A wordless hold face is a face
    /// held with no bubble, and it acks with <c>spoke: false</c>, so it never stamps the 45 s floor.
    /// not ported: the one-slot park behind an open ask and the channel close (no asks, no glass
    /// channels on this head yet).
    /// </summary>
    public partial class EmiDeskWindow
    {
        /// <summary>How long an untimed hold face sits (WPF IgnoreFaceMs).</summary>
        internal const int HoldFaceDefaultMs = 2000;

        private DispatcherTimer? _holdTimer;

        /// <summary>The last line id she spoke and the last hold face she wore (test seams).</summary>
        internal string? LastSpokenLineId { get; private set; }
        internal string? LastHoldFace { get; private set; }

        /// <summary>Say a drawn line.</summary>
        public void SpeakLine(LineDraw? line)
        {
            if (PresentationActive) return;
            if (line == null) return;
            try
            {
                CancelHold();

                // On screen from the first dot, so spent from the first dot (WPF :520).
                EmiLineEngine.Instance.Ack(line.Id);
                EmiDeskService.Instance.NoteEmiSpoke();
                LastSpokenLineId = line.Id;

                if (string.IsNullOrWhiteSpace(line.Text))
                {
                    // A wordless row: a chain, or just a face. Nothing to type.
                    if (!string.IsNullOrEmpty(line.Chain)) PlayChain(line.Chain!);
                    else DrawFace(line.Face);
                    return;
                }
                Say(line.Text, string.IsNullOrEmpty(line.Face) ? "^_^" : line.Face);
            }
            catch (Exception ex) { Log.Warning(ex, "[EmiDesk] SpeakLine({Id}) failed", line.Id); }
        }

        /// <summary>A HOLD row: a face, held, with no bubble. Visibly present and visibly quiet.</summary>
        public void HoldFace(LineDraw? line)
        {
            if (PresentationActive) return;
            if (line == null) return;
            try
            {
                CancelHold();
                CancelChain();
                StopIdleBeats();
                EmiLineEngine.Instance.Ack(line.Id, spoke: false);

                var face = string.IsNullOrEmpty(line.Face) ? "-_-" : line.Face;
                LastHoldFace = face;
                DrawFace(face);

                int ms = line.HoldMs > 0 ? line.HoldMs : HoldFaceDefaultMs;
                var t = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(ms) };
                t.Tick += (_, _) =>
                {
                    if (!ReferenceEquals(_holdTimer, t)) { t.Stop(); return; }
                    CancelHold();
                    DrawFace(EmiChains.RestFace);
                    RestartIdleBeats();
                };
                _holdTimer = t;
                t.Start();
            }
            catch (Exception ex) { Log.Warning(ex, "[EmiDesk] HoldFace({Id}) failed", line.Id); }
        }

        private void CancelHold()
        {
            var t = _holdTimer;
            _holdTimer = null;
            if (t == null) return;
            try { t.Stop(); } catch { /* already dead */ }
        }
    }
}
