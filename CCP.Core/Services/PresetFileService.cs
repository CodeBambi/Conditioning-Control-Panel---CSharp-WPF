using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// Handles serializing, exporting, and importing preset files (.preset.json).
    ///
    /// Presets natively live only in settings.json (AppSettings.UserPresets, via
    /// Newtonsoft/PascalCase). For sharing + drag-drop we serialize the standalone
    /// asset with the SAME System.Text.Json camelCase convention used for
    /// .session.json (see SessionFileService) so the catalogue's pristine
    /// .preset.json round-trips cleanly back into the app.
    /// </summary>
    public class PresetFileService
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
        };

        /// <summary>
        /// Path to custom presets folder in AppData (provenance / re-share copy of
        /// imported presets; the carousel still reads from AppSettings.UserPresets).
        /// </summary>
        public static string CustomPresetsFolder
        {
            get
            {
                var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                return Path.Combine(appData, "ConditioningControlPanel", "CustomPresets");
            }
        }

        public void EnsureCustomFolderExists()
        {
            if (!Directory.Exists(CustomPresetsFolder))
            {
                Directory.CreateDirectory(CustomPresetsFolder);
            }
        }

        /// <summary>
        /// Serialize a preset to a pristine .preset.json string (camelCase).
        /// </summary>
        public string SerializePreset(Preset preset)
        {
            return JsonSerializer.Serialize(preset, JsonOptions);
        }

        /// <summary>
        /// Export a preset to a .preset.json file.
        /// </summary>
        public void ExportPreset(Preset preset, string filePath)
        {
            File.WriteAllText(filePath, SerializePreset(preset));
        }

        /// <summary>
        /// Import a preset from a .preset.json file. Returns null on parse failure.
        /// </summary>
        public Preset? ImportPreset(string filePath)
        {
            if (!File.Exists(filePath))
                return null;

            try
            {
                var json = File.ReadAllText(filePath);
                var preset = JsonSerializer.Deserialize<Preset>(json, JsonOptions);
                if (preset != null)
                {
                    // Imported presets are never "default" (those are built-in only).
                    preset.IsDefault = false;

                    // Fall back to the file name if the asset omitted a name.
                    if (string.IsNullOrWhiteSpace(preset.Name))
                    {
                        var fileName = Path.GetFileNameWithoutExtension(filePath);
                        if (fileName.EndsWith(".preset", StringComparison.OrdinalIgnoreCase))
                        {
                            fileName = fileName[..^7]; // Remove ".preset"
                        }
                        preset.Name = fileName;
                    }
                }
                return preset;
            }
            catch (JsonException)
            {
                return null;
            }
        }

        /// <summary>
        /// Validate a preset file before importing.
        /// </summary>
        public bool ValidatePresetFile(string filePath, out string errorMessage)
        {
            errorMessage = "";

            if (!File.Exists(filePath))
            {
                errorMessage = "File not found";
                return false;
            }

            if (!filePath.EndsWith(".preset.json", StringComparison.OrdinalIgnoreCase))
            {
                errorMessage = "File must be a .preset.json file";
                return false;
            }

            try
            {
                var json = File.ReadAllText(filePath);
                var preset = JsonSerializer.Deserialize<Preset>(json, JsonOptions);

                if (preset == null)
                {
                    errorMessage = "Failed to parse preset file";
                    return false;
                }

                if (string.IsNullOrWhiteSpace(preset.Id))
                {
                    errorMessage = "Preset must have an ID";
                    return false;
                }

                if (string.IsNullOrWhiteSpace(preset.Name))
                {
                    errorMessage = "Preset must have a name";
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
        /// Copy an imported preset file into the CustomPresets folder for provenance,
        /// de-duplicating the file name when needed. Returns the destination path.
        /// </summary>
        public string CopyToCustomPresets(string importedFilePath, Preset preset)
        {
            EnsureCustomFolderExists();

            var baseName = SanitizeFileName(preset.Id);
            var fileName = baseName + ".preset.json";
            var destPath = Path.Combine(CustomPresetsFolder, fileName);

            var counter = 1;
            while (File.Exists(destPath))
            {
                fileName = $"{baseName}_{counter}.preset.json";
                destPath = Path.Combine(CustomPresetsFolder, fileName);
                counter++;
            }

            ExportPreset(preset, destPath);
            return destPath;
        }

        /// <summary>
        /// Default export filename for a preset: sanitized lowercase name.
        /// </summary>
        public static string GetExportFileName(Preset preset)
        {
            return SanitizeFileName(preset.Name.Replace(" ", "_").ToLowerInvariant()) + ".preset.json";
        }

        private static string SanitizeFileName(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            return string.Join("_", name.Split(invalid, StringSplitOptions.RemoveEmptyEntries));
        }
    }
}
