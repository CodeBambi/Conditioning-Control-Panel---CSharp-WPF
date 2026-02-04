using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using ConditioningControlPanel.Models;
using Newtonsoft.Json;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// Manages community-created AI personality prompts.
    /// Handles downloading, installing, activating, and sharing prompts.
    /// </summary>
    public class CommunityPromptService : IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly string _promptsFolder;
        private readonly string _manifestCachePath;
        private List<CommunityPromptManifestEntry> _availablePrompts = new();
        private bool _disposed;

        // Server endpoint for prompts manifest
        private const string PromptsManifestUrl = "https://codebambi-proxy.vercel.app/prompts/manifest";
        private const string PromptsDownloadUrl = "https://codebambi-proxy.vercel.app/prompts";

        // Events
        public event EventHandler<CommunityPrompt>? PromptInstalled;
        public event EventHandler<CommunityPrompt>? PromptActivated;
        public event EventHandler<string>? PromptRemoved;
        public event EventHandler<Exception>? Error;

        /// <summary>
        /// Gets the list of installed prompt IDs.
        /// </summary>
        public IReadOnlyList<string> InstalledPromptIds =>
            App.Settings?.Current?.InstalledCommunityPromptIds ?? new List<string>();

        /// <summary>
        /// Gets the currently active community prompt ID (null = using built-in).
        /// </summary>
        public string? ActivePromptId => App.Settings?.Current?.ActiveCommunityPromptId;

        public CommunityPromptService()
        {
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromSeconds(30);

            _promptsFolder = Path.Combine(App.UserDataPath, "community-prompts");
            _manifestCachePath = Path.Combine(_promptsFolder, "manifest-cache.json");

            // Create prompts folder if needed
            if (!Directory.Exists(_promptsFolder))
            {
                Directory.CreateDirectory(_promptsFolder);
            }

            // Load cached manifest
            LoadCachedManifest();

            App.Logger?.Information("CommunityPromptService initialized. Prompts folder: {Folder}", _promptsFolder);
        }

        #region Manifest & Discovery

        /// <summary>
        /// Fetches available community prompts from the server.
        /// </summary>
        public async Task<List<CommunityPromptManifestEntry>> GetAvailablePromptsAsync(bool forceRefresh = false)
        {
            // Skip network request if offline mode is enabled
            if (App.Settings?.Current?.OfflineMode == true)
            {
                App.Logger?.Debug("Offline mode enabled, using cached prompts only");
                return _availablePrompts;
            }

            if (!forceRefresh && _availablePrompts.Count > 0)
            {
                return _availablePrompts;
            }

            try
            {
                var response = await _httpClient.GetAsync(PromptsManifestUrl);
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    var manifest = JsonConvert.DeserializeObject<CommunityPromptsManifest>(json);

                    if (manifest?.Prompts != null)
                    {
                        _availablePrompts = new List<CommunityPromptManifestEntry>(manifest.Prompts);

                        // Cache the manifest
                        await File.WriteAllTextAsync(_manifestCachePath, json);

                        App.Logger?.Information("Fetched {Count} community prompts from server", _availablePrompts.Count);
                    }
                }
                else
                {
                    App.Logger?.Warning("Failed to fetch community prompts manifest: {Status}", response.StatusCode);
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Warning("Error fetching community prompts manifest: {Error}", ex.Message);
                Error?.Invoke(this, ex);
            }

            return _availablePrompts;
        }

        private void LoadCachedManifest()
        {
            try
            {
                if (File.Exists(_manifestCachePath))
                {
                    var json = File.ReadAllText(_manifestCachePath);
                    var manifest = JsonConvert.DeserializeObject<CommunityPromptsManifest>(json);
                    if (manifest?.Prompts != null)
                    {
                        _availablePrompts = new List<CommunityPromptManifestEntry>(manifest.Prompts);
                        App.Logger?.Debug("Loaded {Count} prompts from manifest cache", _availablePrompts.Count);
                    }
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("Could not load manifest cache: {Error}", ex.Message);
            }
        }

        #endregion

        #region Install & Remove

        /// <summary>
        /// Downloads and installs a community prompt by ID.
        /// </summary>
        public async Task<CommunityPrompt?> InstallPromptAsync(string promptId)
        {
            // Block downloads in offline mode
            if (App.Settings?.Current?.OfflineMode == true)
            {
                App.Logger?.Information("Offline mode enabled, prompt download blocked");
                return null;
            }

            try
            {
                var url = $"{PromptsDownloadUrl}/{promptId}";
                var response = await _httpClient.GetAsync(url);

                if (!response.IsSuccessStatusCode)
                {
                    App.Logger?.Warning("Failed to download prompt {Id}: {Status}", promptId, response.StatusCode);
                    return null;
                }

                var json = await response.Content.ReadAsStringAsync();
                var prompt = JsonConvert.DeserializeObject<CommunityPrompt>(json);

                if (prompt == null)
                {
                    App.Logger?.Warning("Failed to parse prompt {Id}", promptId);
                    return null;
                }

                // Save to local storage
                var filePath = GetPromptFilePath(promptId);
                await File.WriteAllTextAsync(filePath, json);

                // Add to installed list
                var settings = App.Settings?.Current;
                if (settings != null && !settings.InstalledCommunityPromptIds.Contains(promptId))
                {
                    settings.InstalledCommunityPromptIds.Add(promptId);
                    App.Settings?.Save();
                }

                prompt.IsInstalled = true;
                PromptInstalled?.Invoke(this, prompt);

                App.Logger?.Information("Installed community prompt: {Name} by {Author}", prompt.Name, prompt.Author);
                return prompt;
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "Error installing prompt {Id}", promptId);
                Error?.Invoke(this, ex);
                return null;
            }
        }

        /// <summary>
        /// Removes an installed prompt.
        /// </summary>
        public void RemovePrompt(string promptId)
        {
            try
            {
                // Remove file
                var filePath = GetPromptFilePath(promptId);
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                }

                // Remove from installed list
                var settings = App.Settings?.Current;
                if (settings != null)
                {
                    settings.InstalledCommunityPromptIds.Remove(promptId);

                    // Deactivate if this was the active prompt
                    if (settings.ActiveCommunityPromptId == promptId)
                    {
                        settings.ActiveCommunityPromptId = null;
                    }

                    App.Settings?.Save();
                }

                PromptRemoved?.Invoke(this, promptId);
                App.Logger?.Information("Removed community prompt: {Id}", promptId);
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "Error removing prompt {Id}", promptId);
                Error?.Invoke(this, ex);
            }
        }

        /// <summary>
        /// Gets an installed prompt by ID.
        /// </summary>
        public CommunityPrompt? GetInstalledPrompt(string promptId)
        {
            try
            {
                var filePath = GetPromptFilePath(promptId);
                if (!File.Exists(filePath))
                    return null;

                var json = File.ReadAllText(filePath);
                var prompt = JsonConvert.DeserializeObject<CommunityPrompt>(json);

                if (prompt != null)
                {
                    prompt.IsInstalled = true;
                    prompt.IsActive = promptId == ActivePromptId;
                }

                return prompt;
            }
            catch (Exception ex)
            {
                App.Logger?.Warning("Error loading prompt {Id}: {Error}", promptId, ex.Message);
                return null;
            }
        }

        /// <summary>
        /// Gets all installed prompts.
        /// </summary>
        public List<CommunityPrompt> GetInstalledPrompts()
        {
            var prompts = new List<CommunityPrompt>();

            foreach (var id in InstalledPromptIds)
            {
                var prompt = GetInstalledPrompt(id);
                if (prompt != null)
                {
                    prompts.Add(prompt);
                }
            }

            return prompts;
        }

        #endregion

        #region Activate & Deactivate

        /// <summary>
        /// Activates a community prompt, applying its settings.
        /// </summary>
        public bool ActivatePrompt(string promptId)
        {
            var prompt = GetInstalledPrompt(promptId);
            if (prompt == null)
            {
                App.Logger?.Warning("Cannot activate prompt {Id} - not installed", promptId);
                return false;
            }

            var settings = App.Settings?.Current;
            if (settings == null) return false;

            // Apply the prompt settings
            settings.CompanionPrompt = prompt.PromptSettings;
            settings.CompanionPrompt.UseCustomPrompt = true;
            settings.ActiveCommunityPromptId = promptId;
            App.Settings?.Save();

            prompt.IsActive = true;
            PromptActivated?.Invoke(this, prompt);

            App.Logger?.Information("Activated community prompt: {Name}", prompt.Name);
            return true;
        }

        /// <summary>
        /// Deactivates the current community prompt, reverting to defaults.
        /// </summary>
        public void DeactivatePrompt()
        {
            var settings = App.Settings?.Current;
            if (settings == null) return;

            settings.ActiveCommunityPromptId = null;
            // Don't reset CompanionPrompt - let user keep their custom settings if they have any
            App.Settings?.Save();

            App.Logger?.Information("Deactivated community prompt, using default/custom settings");
        }

        #endregion

        #region Export & Import

        /// <summary>
        /// Exports the current companion settings as a shareable community prompt.
        /// </summary>
        public CommunityPrompt ExportCurrentSettings(string name, string author, string description)
        {
            var prompt = CommunityPrompt.FromCurrentSettings(name, author, description);
            return prompt;
        }

        /// <summary>
        /// Saves an exported prompt to a file for sharing.
        /// </summary>
        public async Task<string> SavePromptToFileAsync(CommunityPrompt prompt, string filePath)
        {
            var json = JsonConvert.SerializeObject(prompt, Formatting.Indented);
            await File.WriteAllTextAsync(filePath, json);
            App.Logger?.Information("Exported prompt to: {Path}", filePath);
            return filePath;
        }

        /// <summary>
        /// Imports a prompt from a JSON file.
        /// Uses the file name (minus .json) as the prompt name.
        /// </summary>
        public CommunityPrompt? ImportFromFile(string filePath)
        {
            try
            {
                if (!File.Exists(filePath))
                {
                    App.Logger?.Warning("Import file not found: {Path}", filePath);
                    return null;
                }

                var json = File.ReadAllText(filePath);
                var prompt = JsonConvert.DeserializeObject<CommunityPrompt>(json);

                if (prompt == null)
                {
                    App.Logger?.Warning("Failed to parse prompt file: {Path}", filePath);
                    return null;
                }

                // Generate new ID to avoid conflicts
                prompt.Id = Guid.NewGuid().ToString("N");

                // Use file name as prompt name (minus .json extension)
                prompt.Name = Path.GetFileNameWithoutExtension(filePath);

                // Save to local prompts folder
                var localPath = GetPromptFilePath(prompt.Id);
                File.WriteAllText(localPath, JsonConvert.SerializeObject(prompt, Formatting.Indented));

                // Add to installed list
                var settings = App.Settings?.Current;
                if (settings != null && !settings.InstalledCommunityPromptIds.Contains(prompt.Id))
                {
                    settings.InstalledCommunityPromptIds.Add(prompt.Id);
                    App.Settings?.Save();
                }

                prompt.IsInstalled = true;
                PromptInstalled?.Invoke(this, prompt);

                App.Logger?.Information("Imported prompt: {Name} by {Author}", prompt.Name, prompt.Author);
                return prompt;
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "Error importing prompt from {Path}", filePath);
                Error?.Invoke(this, ex);
                return null;
            }
        }

        #endregion

        #region Helpers

        private string GetPromptFilePath(string promptId)
        {
            return Path.Combine(_promptsFolder, $"{promptId}.json");
        }

        #endregion

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _httpClient?.Dispose();
        }
    }
}
