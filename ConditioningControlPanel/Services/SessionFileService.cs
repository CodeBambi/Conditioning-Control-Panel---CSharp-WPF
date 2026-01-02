using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// Handles loading, saving, and exporting session files (.session.json)
    /// </summary>
    public class SessionFileService
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
        };

        /// <summary>
        /// Path to custom sessions folder in AppData
        /// </summary>
        public static string CustomSessionsFolder
        {
            get
            {
                var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                return Path.Combine(appData, "ConditioningControlPanel", "CustomSessions");
            }
        }

        /// <summary>
        /// Path to built-in sessions in app assets
        /// </summary>
        public static string BuiltInSessionsFolder
        {
            get
            {
                var appDir = AppDomain.CurrentDomain.BaseDirectory;
                return Path.Combine(appDir, "assets", "sessions");
            }
        }

        /// <summary>
        /// Ensure the custom sessions folder exists
        /// </summary>
        public void EnsureCustomFolderExists()
        {
            if (!Directory.Exists(CustomSessionsFolder))
            {
                Directory.CreateDirectory(CustomSessionsFolder);
            }
        }

        /// <summary>
        /// Export a session to a .session.json file
        /// </summary>
        public void ExportSession(SessionDefinition session, string filePath)
        {
            var json = JsonSerializer.Serialize(session, JsonOptions);
            File.WriteAllText(filePath, json);
        }

        /// <summary>
        /// Export a Session object to a .session.json file
        /// </summary>
        public void ExportSession(Session session, string filePath)
        {
            var definition = SessionDefinition.FromSession(session);
            ExportSession(definition, filePath);
        }

        /// <summary>
        /// Import a session from a .session.json file
        /// </summary>
        public SessionDefinition? ImportSession(string filePath)
        {
            if (!File.Exists(filePath))
                return null;

            try
            {
                var json = File.ReadAllText(filePath);
                var session = JsonSerializer.Deserialize<SessionDefinition>(json, JsonOptions);
                if (session != null)
                {
                    session.Source = SessionSource.Imported;
                    session.SourceFilePath = filePath;
                }
                return session;
            }
            catch (JsonException)
            {
                return null;
            }
        }

        /// <summary>
        /// Validate a session file before importing
        /// </summary>
        public bool ValidateSessionFile(string filePath, out string errorMessage)
        {
            errorMessage = "";

            if (!File.Exists(filePath))
            {
                errorMessage = "File not found";
                return false;
            }

            if (!filePath.EndsWith(".session.json", StringComparison.OrdinalIgnoreCase))
            {
                errorMessage = "File must be a .session.json file";
                return false;
            }

            try
            {
                var json = File.ReadAllText(filePath);
                var session = JsonSerializer.Deserialize<SessionDefinition>(json, JsonOptions);

                if (session == null)
                {
                    errorMessage = "Failed to parse session file";
                    return false;
                }

                if (string.IsNullOrWhiteSpace(session.Id))
                {
                    errorMessage = "Session must have an ID";
                    return false;
                }

                if (string.IsNullOrWhiteSpace(session.Name))
                {
                    errorMessage = "Session must have a name";
                    return false;
                }

                if (session.DurationMinutes <= 0)
                {
                    errorMessage = "Session duration must be greater than 0";
                    return false;
                }

                return true;
            }
            catch (JsonException ex)
            {
                errorMessage = $"Invalid JSON: {ex.Message}";
                return false;
            }
            catch (Exception ex)
            {
                errorMessage = $"Error reading file: {ex.Message}";
                return false;
            }
        }

        /// <summary>
        /// Load all custom sessions from the CustomSessions folder
        /// </summary>
        public List<SessionDefinition> LoadCustomSessions()
        {
            EnsureCustomFolderExists();
            var sessions = new List<SessionDefinition>();

            foreach (var file in Directory.GetFiles(CustomSessionsFolder, "*.session.json"))
            {
                var session = ImportSession(file);
                if (session != null)
                {
                    session.Source = SessionSource.Custom;
                    session.SourceFilePath = file;
                    sessions.Add(session);
                }
            }

            return sessions;
        }

        /// <summary>
        /// Load all built-in sessions from the Assets folder
        /// </summary>
        public List<SessionDefinition> LoadBuiltInSessions()
        {
            var sessions = new List<SessionDefinition>();

            if (!Directory.Exists(BuiltInSessionsFolder))
                return sessions;

            foreach (var file in Directory.GetFiles(BuiltInSessionsFolder, "*.session.json"))
            {
                var session = ImportSession(file);
                if (session != null)
                {
                    session.Source = SessionSource.BuiltIn;
                    session.SourceFilePath = file;
                    sessions.Add(session);
                }
            }

            return sessions;
        }

        /// <summary>
        /// Save a custom session to the CustomSessions folder
        /// </summary>
        public string SaveCustomSession(SessionDefinition session)
        {
            EnsureCustomFolderExists();
            session.Source = SessionSource.Custom;

            var fileName = SanitizeFileName(session.Id) + ".session.json";
            var filePath = Path.Combine(CustomSessionsFolder, fileName);

            session.SourceFilePath = filePath;
            ExportSession(session, filePath);

            return filePath;
        }

        /// <summary>
        /// Copy an imported session to the CustomSessions folder
        /// </summary>
        public string CopyToCustomSessions(string importedFilePath, SessionDefinition session)
        {
            EnsureCustomFolderExists();

            var fileName = SanitizeFileName(session.Id) + ".session.json";
            var destPath = Path.Combine(CustomSessionsFolder, fileName);

            // Handle duplicate filenames
            var counter = 1;
            while (File.Exists(destPath))
            {
                fileName = $"{SanitizeFileName(session.Id)}_{counter}.session.json";
                destPath = Path.Combine(CustomSessionsFolder, fileName);
                counter++;
            }

            // Copy and update source
            session.Source = SessionSource.Custom;
            session.SourceFilePath = destPath;
            ExportSession(session, destPath);

            return destPath;
        }

        /// <summary>
        /// Delete a custom session file
        /// </summary>
        public bool DeleteCustomSession(string filePath)
        {
            if (!File.Exists(filePath))
                return false;

            // Only allow deleting from custom folder for safety
            if (!filePath.StartsWith(CustomSessionsFolder, StringComparison.OrdinalIgnoreCase))
                return false;

            try
            {
                File.Delete(filePath);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Open the custom sessions folder in Explorer
        /// </summary>
        public void OpenCustomSessionsFolder()
        {
            EnsureCustomFolderExists();
            System.Diagnostics.Process.Start("explorer.exe", CustomSessionsFolder);
        }

        /// <summary>
        /// Get a safe filename from session ID
        /// </summary>
        private static string SanitizeFileName(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            return string.Join("_", name.Split(invalid, StringSplitOptions.RemoveEmptyEntries));
        }

        /// <summary>
        /// Get the default export filename for a session
        /// </summary>
        public static string GetExportFileName(Session session)
        {
            return SanitizeFileName(session.Name.Replace(" ", "_").ToLowerInvariant()) + ".session.json";
        }
    }
}
