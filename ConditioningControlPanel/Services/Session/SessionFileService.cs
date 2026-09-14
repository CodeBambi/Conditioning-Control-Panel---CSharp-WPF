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
        /// Install-dir-relative anchor for the shipped sessions. Resolved through
        /// <see cref="ContentLocator"/> rather than joined onto the base directory, because this
        /// folder is SPLIT: morning_drift ships in the installer, and the three BambiSleep presets
        /// ride in the mod-bambi content pack under the content root.
        /// </summary>
        public static readonly string BuiltInSessionsRelativeDir = Path.Combine("assets", "sessions");

        /// <summary>
        /// Path to built-in sessions in app assets. The install-dir copy when it exists, else the
        /// downloaded-pack copy. Prefer <see cref="LoadBuiltInSessions"/>, which reads BOTH roots -
        /// this single-folder view cannot see a split.
        /// </summary>
        public static string BuiltInSessionsFolder => ContentLocator.ResolveDirectory(BuiltInSessionsRelativeDir);

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
        /// Import a session from a .session.json file.
        /// Uses the file name as the session name (without .session.json extension).
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

                    // Use file name as session name only if the JSON didn't provide one
                    if (string.IsNullOrWhiteSpace(session.Name))
                    {
                        var fileName = Path.GetFileNameWithoutExtension(filePath);
                        if (fileName.EndsWith(".session", StringComparison.OrdinalIgnoreCase))
                        {
                            fileName = fileName[..^8]; // Remove ".session"
                        }

                        session.Name = fileName;
                    }
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
        /// Load all built-in sessions from the Assets folder - the UNION of the install dir and the
        /// downloaded content packs, deduped by file name with the install dir winning. That union
        /// is what makes a pack-provided session appear in the rack: installing mod-bambi drops its
        /// three presets under the content root and the next reload picks them up, while a user who
        /// never installs it sees only the neutral one that ships in the box.
        ///
        /// A root that has no such folder contributes nothing, so "no packs installed" is an
        /// ordinary short list rather than an error.
        /// </summary>
        public List<SessionDefinition> LoadBuiltInSessions()
        {
            var sessions = new List<SessionDefinition>();

            foreach (var file in ContentLocator.EnumerateFiles(BuiltInSessionsRelativeDir, "*.session.json"))
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

            // Use existing file path if set and valid, otherwise create new path
            string filePath;
            if (!string.IsNullOrEmpty(session.SourceFilePath) && File.Exists(session.SourceFilePath))
            {
                filePath = session.SourceFilePath;
            }
            else
            {
                var fileName = SanitizeFileName(session.Id) + ".session.json";
                filePath = Path.Combine(CustomSessionsFolder, fileName);
                session.SourceFilePath = filePath;
            }

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
