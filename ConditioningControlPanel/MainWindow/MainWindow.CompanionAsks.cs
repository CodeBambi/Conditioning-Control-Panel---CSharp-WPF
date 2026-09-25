using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using ConditioningControlPanel.Services.Companion.Asks;

namespace ConditioningControlPanel
{
    /// <summary>What companion ask cards need from the Sessions page: the list, the running flag and the normal start.</summary>
    public partial class MainWindow
    {
        internal bool CompanionSessionRunning => _sessionEngine?.IsRunning == true;

        private IEnumerable<Models.Session> CompanionSessionPool()
        {
            IEnumerable<Models.Session> all = Models.Session.GetAllSessions();
            if (_sessionManager != null) all = all.Concat(_sessionManager.CustomSessions);
            return all.Where(s => s != null && s.IsAvailable && !string.IsNullOrEmpty(s.Id));
        }

        /// <summary>Sessions the user can start right now. Access is asked again at click time.</summary>
        internal IReadOnlyList<AskOption> CompanionSessionOptions()
        {
            try
            {
                return CompanionSessionPool().Select(s =>
                {
                    var id = s.Id;
                    return new AskOption(id, s.GetModeAwareName(), () => !CompanionSessionRunning
                        && CompanionSessionPool().Any(x => x.Id == id));
                }).ToArray();
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("Companion session options failed: {E}", ex.Message);
                return Array.Empty<AskOption>();
            }
        }

        /// <summary>
        /// Starts a session through the Sessions page's own Start button path, so the usual
        /// "Start ...?" confirmation shows and nothing runs without the user saying yes.
        /// </summary>
        internal bool StartSessionFromCompanion(string sessionId)
        {
            if (CompanionSessionRunning) return false;
            var session = CompanionSessionPool().FirstOrDefault(s => s.Id == sessionId);
            if (session == null) return false;
            _selectedSession = session;
            BtnStartSession_Click(this, new RoutedEventArgs());
            return true;
        }
    }
}
