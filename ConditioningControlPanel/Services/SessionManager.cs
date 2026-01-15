using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// Central manager for all sessions (built-in and custom)
    /// </summary>
    public class SessionManager
    {
        private readonly SessionFileService _fileService;
        private readonly List<Session> _sessions = new();

        /// <summary>
        /// All available sessions (built-in + custom)
        /// </summary>
        public ObservableCollection<Session> AllSessions { get; } = new();

        /// <summary>
        /// Built-in sessions only
        /// </summary>
        public IEnumerable<Session> BuiltInSessions => _sessions.Where(s => s.Source == SessionSource.BuiltIn);

        /// <summary>
        /// Custom/imported sessions only
        /// </summary>
        public IEnumerable<Session> CustomSessions => _sessions.Where(s => s.Source != SessionSource.BuiltIn);

        /// <summary>
        /// Fired when a session is added
        /// </summary>
        public event Action<Session>? SessionAdded;

        /// <summary>
        /// Fired when a session is removed
        /// </summary>
        public event Action<Session>? SessionRemoved;

        /// <summary>
        /// Fired when sessions are reloaded
        /// </summary>
        public event Action? SessionsReloaded;

        public SessionManager()
        {
            _fileService = new SessionFileService();
        }

        /// <summary>
        /// Load all sessions from built-in assets and custom folder
        /// </summary>
        public void LoadAllSessions()
        {
            _sessions.Clear();
            AllSessions.Clear();

            // Load built-in sessions from JSON files first
            var builtInFromFiles = _fileService.LoadBuiltInSessions();
            foreach (var def in builtInFromFiles)
            {
                var session = def.ToSession();
                _sessions.Add(session);
            }

            // If no JSON files found, fall back to hardcoded sessions
            if (!builtInFromFiles.Any())
            {
                var hardcodedSessions = Session.GetAllSessions();
                foreach (var session in hardcodedSessions)
                {
                    session.Source = SessionSource.BuiltIn;
                    _sessions.Add(session);
                }
            }

            // Load custom sessions
            var customSessions = _fileService.LoadCustomSessions();
            foreach (var def in customSessions)
            {
                var session = def.ToSession();
                _sessions.Add(session);
            }

            // Update observable collection
            foreach (var session in _sessions)
            {
                AllSessions.Add(session);
            }

            SessionsReloaded?.Invoke();
        }

        /// <summary>
        /// Import a session from a file path (drag & drop)
        /// </summary>
        public (bool success, string message, Session? session) ImportSession(string filePath)
        {
            // Validate first
            if (!_fileService.ValidateSessionFile(filePath, out var errorMessage))
            {
                return (false, errorMessage, null);
            }

            // Import
            var definition = _fileService.ImportSession(filePath);
            if (definition == null)
            {
                return (false, "Failed to import session", null);
            }

            // Check for duplicate ID
            if (_sessions.Any(s => s.Id == definition.Id))
            {
                // Generate a unique ID
                var baseId = definition.Id;
                var counter = 1;
                while (_sessions.Any(s => s.Id == definition.Id))
                {
                    definition.Id = $"{baseId}_{counter}";
                    counter++;
                }
            }

            // Copy to custom sessions folder
            var savedPath = _fileService.CopyToCustomSessions(filePath, definition);
            definition.SourceFilePath = savedPath;
            definition.Source = SessionSource.Custom;

            // Add to collections
            var session = definition.ToSession();
            _sessions.Add(session);
            AllSessions.Add(session);

            SessionAdded?.Invoke(session);

            return (true, $"Imported '{session.Name}'", session);
        }

        /// <summary>
        /// Export a session to a file
        /// </summary>
        public void ExportSession(Session session, string filePath)
        {
            _fileService.ExportSession(session, filePath);
        }

        public void UpdateCustomSession(Session updatedSession)
        {
            var definition = SessionDefinition.FromSession(updatedSession);
            _fileService.SaveCustomSession(definition);

            var existingSession = _sessions.FirstOrDefault(s => s.Id == updatedSession.Id);
            if (existingSession != null)
            {
                _sessions.Remove(existingSession);
                AllSessions.Remove(existingSession);
            }
            _sessions.Add(updatedSession);
            AllSessions.Add(updatedSession);

            // Let the UI decide what to do
            SessionRemoved?.Invoke(existingSession);
            SessionAdded?.Invoke(updatedSession);
        }

        /// <summary>
        /// Adds a new session created in the editor
        /// </summary>
        public void AddNewSession(Session session, string filePath)
        {
            // Ensure folder exists and set source
            _fileService.EnsureCustomFolderExists();
            session.Source = SessionSource.Custom;
            session.SourceFilePath = filePath;

            // Check for duplicate ID and generate a new one if needed
            if (_sessions.Any(s => s.Id == session.Id))
            {
                session.Id = Guid.NewGuid().ToString();
            }

            // Save the session to the specified file path
            var definition = SessionDefinition.FromSession(session);
            _fileService.ExportSession(definition, filePath);

            // Add to collections
            _sessions.Add(session);
            AllSessions.Add(session);

            SessionAdded?.Invoke(session);
        }

        /// <summary>
        /// Delete a custom/imported session
        /// </summary>
        public bool DeleteSession(Session session)
        {
            // Can't delete built-in sessions
            if (session.Source == SessionSource.BuiltIn)
                return false;

            // Delete file
            if (!string.IsNullOrEmpty(session.SourceFilePath))
            {
                _fileService.DeleteCustomSession(session.SourceFilePath);
            }

            // Remove from collections
            _sessions.Remove(session);
            AllSessions.Remove(session);

            SessionRemoved?.Invoke(session);

            return true;
        }

        /// <summary>
        /// Get a session by ID
        /// </summary>
        public Session? GetSession(string id)
        {
            return _sessions.FirstOrDefault(s => s.Id == id);
        }

        /// <summary>
        /// Check if a session can be deleted (not built-in)
        /// </summary>
        public bool CanDelete(Session session)
        {
            return session.Source != SessionSource.BuiltIn;
        }

        /// <summary>
        /// Open the custom sessions folder in Explorer
        /// </summary>
        public void OpenCustomSessionsFolder()
        {
            _fileService.OpenCustomSessionsFolder();
        }

        /// <summary>
        /// Get the default export filename for a session
        /// </summary>
        public string GetExportFileName(Session session)
        {
            return SessionFileService.GetExportFileName(session);
        }
    }
}
