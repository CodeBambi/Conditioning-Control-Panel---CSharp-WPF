// PORTED from ConditioningControlPanel/Services/Session/SessionEngine.cs (7.1.5): the session-scoped corner
// GIF. :214 start, :552 pause (hide only), :596 resume, :909-940 tick (live admission, delayed start, end
// minute), :372 stop, :1848 CanRaiseCornerGif, :1872 RefreshCornerGifPolicy, :2267 the handback debt.
using System;
using ConditioningControlPanel.Models;
using Serilog;

namespace ConditioningControlPanel.Services
{
    public sealed partial class SessionRunner
    {
        private bool _cornerShown;
        /// <summary>This session took the corner and still owes the user's own slots a handback. A
        /// hide-only close (pause, panic) keeps the debt; the terminal close pays it.</summary>
        private bool _cornerHandbackOwed;
        /// <summary>The user's own Spiral master as it stood BEFORE the session overrode it.</summary>
        private bool _userSpiralAllowed = true;
        private bool _refreshingCornerGifPolicy;
        private bool _cornerHooked;

        /// <summary>The session's corner GIF is raised (the head's overlay may still be decoding).</summary>
        public bool IsCornerGifShown => _cornerShown;

        private bool CanRaiseCornerGif(SessionSettings ss)
            => ss != null && CornerGifMedia.AllowSessionCornerGif(
                   ss.CornerGifEnabled,
                   CoreSettings.Current?.SessionCornerGifAllowed != false,
                   CoreCornerGif.StandaloneActive,
                   CornerGifMedia.SessionCornerArtIsSpiral(ss.CornerGifPath),
                   _userSpiralAllowed,
                   CoreCornerGif.SpiralVisible);

        private static bool InCornerWindow(SessionSettings ss, double minutes)
            => minutes >= ss.CornerGifStartMinute && (ss.CornerGifEndMinute <= 0 || minutes < ss.CornerGifEndMinute);

        /// <summary>Session start, before the overrides: remember the user's Spiral master.</summary>
        private void CaptureCornerGifUserState(AppSettings s)
        {
            _userSpiralAllowed = s.SpiralEnabled;
            _cornerShown = _cornerHandbackOwed = false;
            if (!_cornerHooked) { CoreCornerGif.StandaloneChanged += RefreshCornerGifPolicy; _cornerHooked = true; }
        }

        /// <summary>WPF :214: raised at once when its start minute is 0; a later one waits for its minute.</summary>
        private void StartCornerGif(SessionSettings ss)
        {
            if (ss.CornerGifStartMinute == 0 && CanRaiseCornerGif(ss)) ShowCornerGif(ss);
        }

        private void ShowCornerGif(SessionSettings ss)
        {
            // Last line of defence: every caller asks first, and a corner overlay that ignores the user's master is the ticket.
            if (!CanRaiseCornerGif(ss))
            {
                Log.Debug("Corner GIF not shown: the user master is off or a standalone corner overlay owns the corner");
                return;
            }
            _cornerShown = true;
            _cornerHandbackOwed = true;
            CoreCornerGif.SessionActive = true;
            CoreCornerGif.ShowSession(ss.CornerGifPath ?? "", ss.CornerGifPosition, ss.CornerGifSize > 0 ? ss.CornerGifSize : 300, ss.CornerGifOpacity);
        }

        private void CloseCornerGif(bool handBackCorner = true)
        {
            bool wasShown = _cornerShown;
            _cornerShown = false;
            CoreCornerGif.SessionActive = false;
            // Only hand the corner back if this session ever took it: a close that closed nothing must
            // not fire a rebuild of every standalone slot.
            bool handBack = handBackCorner && _cornerHandbackOwed;
            if (handBack) _cornerHandbackOwed = false;
            if (wasShown || handBack) CoreCornerGif.HideSession(handBack);
        }

        /// <summary>WPF :552: a pause hides it and keeps the debt.</summary>
        private void PauseCornerGif() => CloseCornerGif(handBackCorner: false);

        /// <summary>WPF :596: the pause closed it, so put it back if it is still inside its window.
        /// Admission is re-asked from scratch.</summary>
        private void ResumeCornerGif(SessionSettings ss)
        {
            if (!_cornerShown && CanRaiseCornerGif(ss) && InCornerWindow(ss, Elapsed.TotalMinutes)) ShowCornerGif(ss);
        }

        /// <summary>WPF :909-940: admission is honoured LIVE, then the delayed start, then the end minute.</summary>
        private void TickCornerGif(SessionSettings ss, double minutes)
        {
            if (_cornerShown && !CanRaiseCornerGif(ss))
            {
                CloseCornerGif();
                Log.Information("Corner GIF hidden mid-session (user master, a standalone corner overlay, or the fullscreen spiral now owns the corner)");
            }
            if (!_cornerShown && ss.CornerGifStartMinute > 0 && CanRaiseCornerGif(ss) && minutes >= ss.CornerGifStartMinute
                && (ss.CornerGifEndMinute <= 0 || minutes < ss.CornerGifEndMinute))
            {
                ShowCornerGif(ss);
                Log.Information("Corner GIF activated at {Minutes:F1} minutes (target was {Target})", minutes, ss.CornerGifStartMinute);
            }
            if (ss.CornerGifEnabled && _cornerShown && ss.CornerGifEndMinute > 0 && minutes >= ss.CornerGifEndMinute)
            {
                CloseCornerGif();
                Log.Information("Corner GIF deactivated at {Minutes:F1} minutes (target was {Target})", minutes, ss.CornerGifEndMinute);
            }
        }

        /// <summary>WPF :372: the terminal close pays the handback.</summary>
        private void StopCornerGif()
        {
            CloseCornerGif();
            if (_cornerHooked) { CoreCornerGif.StandaloneChanged -= RefreshCornerGifPolicy; _cornerHooked = false; }
        }

        /// <summary>WPF PanicCloseCornerGif: off the screen now, the session keeps its claim.</summary>
        public void PanicCloseCornerGif()
        {
            try { CloseCornerGif(handBackCorner: false); }
            catch (Exception ex) { Log.Debug("PanicCloseCornerGif: {E}", ex.Message); }
        }

        /// <summary>
        /// Re-ask the admission now (the Spiral card's switch, a standalone slot toggled, the fullscreen
        /// spiral shown or hidden). Never raises while paused: a pause means "nothing on my screen".
        /// </summary>
        public void RefreshCornerGifPolicy()
        {
            if (_refreshingCornerGifPolicy) return;
            _refreshingCornerGifPolicy = true;
            try
            {
                var ss = CurrentSession?.Settings;
                if (ss == null) return;
                if (_cornerShown && !CanRaiseCornerGif(ss)) { CloseCornerGif(); return; }
                if (!_cornerShown && IsRunning && !IsPaused && CanRaiseCornerGif(ss)
                    && ss.CornerGifStartMinute == 0
                    && (ss.CornerGifEndMinute <= 0 || Elapsed.TotalMinutes < ss.CornerGifEndMinute))
                    ShowCornerGif(ss);
            }
            catch (Exception ex) { Log.Warning(ex, "RefreshCornerGifPolicy failed"); }
            finally { _refreshingCornerGifPolicy = false; }
        }
    }
}
