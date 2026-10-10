using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Moderation;
using Newtonsoft.Json;
using Serilog;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// Community-created AI personality prompts: download, install, activate, import, export. Ported
    /// from WPF 7.1.5 Services/Companion/CommunityPromptService.cs, bodies kept; the head-only parts
    /// (the advisory message box) leave as <see cref="FlaggedAdvisory"/>.
    ///
    /// <para>Named apart from WPF's CommunityPromptService because the in-tree WPF app still has
    /// that class (it reaches App.* statics) and its tests name it; ClearCustomPromptOverride already
    /// lives in Core <see cref="PersonalityService"/>.</para>
    /// </summary>
    internal sealed class CommunityPromptLibrary : IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly string _promptsFolder;
        private readonly string _manifestCachePath;
        private List<CommunityPromptManifestEntry> _availablePrompts = new();
        private bool _disposed;

        private const string PromptsManifestUrl = "https://codebambi-proxy.vercel.app/prompts/manifest";
        private const string PromptsDownloadUrl = "https://codebambi-proxy.vercel.app/prompts";

        public event EventHandler<CommunityPrompt>? PromptInstalled;
        public event EventHandler<CommunityPrompt>? PromptActivated;
        public event EventHandler<string>? PromptRemoved;
        public event EventHandler<Exception>? Error;

        /// <summary>WPF's non-blocking advisory (community_prompt_warning_*): how many fields the
        /// validator flagged on an activation. The activation proceeds either way.</summary>
        public Action<int>? FlaggedAdvisory { get; set; }

        private static AppSettings? Settings => CoreSettings.Service?.Current;

        public IReadOnlyList<string> InstalledPromptIds => Settings?.InstalledCommunityPromptIds ?? new List<string>();

        /// <summary>The active community prompt id (null = the built-in personality).</summary>
        public string? ActivePromptId => Settings?.ActiveCommunityPromptId;

        public static string DefaultFolder => Path.Combine(CorePaths.UserData, "community-prompts");

        /// <param name="handler">Tests: the transport. Null = the network.</param>
        public CommunityPromptLibrary(string? promptsFolder = null, HttpMessageHandler? handler = null)
        {
            _httpClient = handler == null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
            _httpClient.Timeout = TimeSpan.FromSeconds(30);

            _promptsFolder = promptsFolder ?? DefaultFolder;
            _manifestCachePath = Path.Combine(_promptsFolder, "manifest-cache.json");
            if (!Directory.Exists(_promptsFolder)) Directory.CreateDirectory(_promptsFolder);

            LoadCachedManifest();
            Log.Debug("CommunityPromptLibrary initialized. Prompts folder: {Folder}", _promptsFolder);
        }

        #region Manifest & Discovery

        /// <summary>Fetches the available community prompts. Offline mode: the cache only.</summary>
        public async Task<List<CommunityPromptManifestEntry>> GetAvailablePromptsAsync(bool forceRefresh = false)
        {
            if (Settings?.OfflineMode == true)
            {
                Log.Debug("Offline mode enabled, using cached prompts only");
                return _availablePrompts;
            }

            if (!forceRefresh && _availablePrompts.Count > 0) return _availablePrompts;

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
                        await File.WriteAllTextAsync(_manifestCachePath, json);
                        Log.Information("Fetched {Count} community prompts from server", _availablePrompts.Count);
                    }
                }
                else
                {
                    Log.Warning("Failed to fetch community prompts manifest: {Status}", response.StatusCode);
                }
            }
            catch (Exception ex)
            {
                Log.Warning("Error fetching community prompts manifest: {Error}", ex.Message);
                Error?.Invoke(this, ex);
            }

            return _availablePrompts;
        }

        private void LoadCachedManifest()
        {
            try
            {
                if (!File.Exists(_manifestCachePath)) return;
                var manifest = JsonConvert.DeserializeObject<CommunityPromptsManifest>(File.ReadAllText(_manifestCachePath));
                if (manifest?.Prompts != null) _availablePrompts = new List<CommunityPromptManifestEntry>(manifest.Prompts);
            }
            catch (Exception ex)
            {
                Log.Debug("Could not load manifest cache: {Error}", ex.Message);
            }
        }

        #endregion

        #region Install & Remove

        /// <summary>Downloads and installs a community prompt by id. Blocked in offline mode.</summary>
        public async Task<CommunityPrompt?> InstallPromptAsync(string promptId)
        {
            if (Settings?.OfflineMode == true)
            {
                Log.Information("Offline mode enabled, prompt download blocked");
                return null;
            }
            if (!IsSafeId(promptId)) return null;

            try
            {
                var response = await _httpClient.GetAsync($"{PromptsDownloadUrl}/{promptId}");
                if (!response.IsSuccessStatusCode)
                {
                    Log.Warning("Failed to download prompt {Id}: {Status}", promptId, response.StatusCode);
                    return null;
                }

                var json = await response.Content.ReadAsStringAsync();
                var prompt = JsonConvert.DeserializeObject<CommunityPrompt>(json);
                if (prompt == null)
                {
                    Log.Warning("Failed to parse prompt {Id}", promptId);
                    return null;
                }

                await File.WriteAllTextAsync(GetPromptFilePath(promptId), json);

                var settings = Settings;
                if (settings != null && !settings.InstalledCommunityPromptIds.Contains(promptId))
                {
                    settings.InstalledCommunityPromptIds.Add(promptId);
                    CoreSettings.Save();
                }

                prompt.IsInstalled = true;
                PromptInstalled?.Invoke(this, prompt);
                Log.Information("Installed community prompt: {Name} by {Author}", prompt.Name, prompt.Author);
                return prompt;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error installing prompt {Id}", promptId);
                Error?.Invoke(this, ex);
                return null;
            }
        }

        /// <summary>Removes an installed prompt; the active one is deactivated with it.</summary>
        public void RemovePrompt(string promptId)
        {
            try
            {
                if (IsSafeId(promptId))
                {
                    var filePath = GetPromptFilePath(promptId);
                    if (File.Exists(filePath)) File.Delete(filePath);
                }

                var settings = Settings;
                if (settings != null)
                {
                    settings.InstalledCommunityPromptIds.Remove(promptId);
                    if (settings.ActiveCommunityPromptId == promptId) settings.ActiveCommunityPromptId = null;
                    CoreSettings.Save();
                }

                PromptRemoved?.Invoke(this, promptId);
                Log.Information("Removed community prompt: {Id}", promptId);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error removing prompt {Id}", promptId);
                Error?.Invoke(this, ex);
            }
        }

        public CommunityPrompt? GetInstalledPrompt(string promptId)
        {
            try
            {
                if (!IsSafeId(promptId)) return null;
                var filePath = GetPromptFilePath(promptId);
                if (!File.Exists(filePath)) return null;

                var prompt = JsonConvert.DeserializeObject<CommunityPrompt>(File.ReadAllText(filePath));
                if (prompt != null)
                {
                    prompt.IsInstalled = true;
                    prompt.IsActive = promptId == ActivePromptId;
                }
                return prompt;
            }
            catch (Exception ex)
            {
                Log.Warning("Error loading prompt {Id}: {Error}", promptId, ex.Message);
                return null;
            }
        }

        public List<CommunityPrompt> GetInstalledPrompts()
        {
            var prompts = new List<CommunityPrompt>();
            foreach (var id in InstalledPromptIds)
            {
                var prompt = GetInstalledPrompt(id);
                if (prompt != null) prompts.Add(prompt);
            }
            return prompts;
        }

        #endregion

        #region Activate & Deactivate

        /// <summary>Activates a community prompt, applying its settings.</summary>
        public bool ActivatePrompt(string promptId)
        {
            var prompt = GetInstalledPrompt(promptId);
            if (prompt == null)
            {
                Log.Warning("Cannot activate prompt {Id} - not installed", promptId);
                return false;
            }

            var settings = Settings;
            if (settings == null) return false;

            // CCBill AI Addendum: gate prompts that ship a SlutModePersonality when SlutMode is on.
            // The caller owns the modal acknowledgement; the service refuses until it is cleared.
            var probe = new PersonalityPreset { PromptSettings = prompt.PromptSettings };
            if (ExplicitContentGate.RequiresAcknowledgement(probe, settings.SlutModeEnabled)
                && !ExplicitContentGate.IsAlreadyAcknowledged(settings.CompanionPrompt))
            {
                Log.Warning("ActivatePrompt {Id} refused: explicit-content acknowledgement not granted", promptId);
                return false;
            }

            // P2-H10: PromptValidator over the user-content fields BEFORE they are applied. Soft: every
            // flagged field goes to moderation.log (counts only) and a non-blocking advisory is
            // raised; the activation proceeds. ModerationGuard at inference time is the load-bearing layer.
            try
            {
                if (prompt.PromptSettings != null)
                {
                    var validator = new PromptValidator();
                    var ps = prompt.PromptSettings;
                    var fieldsToScan = new (string Name, string Text)[]
                    {
                        ("Personality", ps.Personality ?? string.Empty),
                        ("SlutModePersonality", ps.SlutModePersonality ?? string.Empty),
                        ("KnowledgeBase", ps.KnowledgeBase ?? string.Empty),
                        ("ExplicitReaction", ps.ExplicitReaction ?? string.Empty),
                        ("ContextReactions", ps.ContextReactions ?? string.Empty),
                    };

                    var flaggedFields = new List<string>();
                    foreach (var (name, text) in fieldsToScan)
                    {
                        if (string.IsNullOrWhiteSpace(text)) continue;
                        var result = validator.Validate(text);
                        if (!result.Clean)
                        {
                            flaggedFields.Add(name);
                            CoreModerationLog.RecordEdit(name, result.MatchedPatterns.Count, surface: "community_prompt");
                        }
                    }

                    if (flaggedFields.Count > 0)
                    {
                        Log.Information("CommunityPromptLibrary.ActivatePrompt {Id}: PromptValidator flagged {Count} field(s): {Fields}",
                            promptId, flaggedFields.Count, string.Join(",", flaggedFields));
                        try { FlaggedAdvisory?.Invoke(flaggedFields.Count); }
                        catch (Exception ex) { Log.Debug("Community prompt advisory failed: {Error}", ex.Message); }
                    }
                }
            }
            catch (Exception ex)
            {
                // Validator failures must not block activation - soft surface.
                Log.Debug("CommunityPromptLibrary PromptValidator pass failed: {Error}", ex.Message);
            }

            settings.CompanionPrompt = prompt.PromptSettings;
            settings.CompanionPrompt.UseCustomPrompt = true;
            settings.ActiveCommunityPromptId = promptId;
            CoreSettings.Save();

            prompt.IsActive = true;
            PromptActivated?.Invoke(this, prompt);
            Log.Information("Activated community prompt: {Name}", prompt.Name);
            return true;
        }

        /// <summary>Deactivates the current community prompt; the preset owns the wire again.</summary>
        public void DeactivatePrompt()
        {
            var settings = Settings;
            if (settings == null) return;
            PersonalityService.ClearCustomPromptOverride(settings);
            CoreSettings.Save();
            Log.Information("Deactivated community prompt, using default/custom settings");
        }

        #endregion

        #region Export & Import

        public CommunityPrompt ExportCurrentSettings(string name, string author, string description) =>
            CommunityPrompt.FromCurrentSettings(name, author, description);

        public async Task<string> SavePromptToFileAsync(CommunityPrompt prompt, string filePath)
        {
            await File.WriteAllTextAsync(filePath, JsonConvert.SerializeObject(prompt, Formatting.Indented));
            Log.Information("Exported prompt to: {Path}", filePath);
            return filePath;
        }

        /// <summary>Imports a prompt from a JSON file; the file name (minus .json) becomes its name
        /// and it gets a fresh id.</summary>
        public CommunityPrompt? ImportFromFile(string filePath)
        {
            try
            {
                if (!File.Exists(filePath))
                {
                    Log.Warning("Import file not found: {Path}", filePath);
                    return null;
                }

                var prompt = JsonConvert.DeserializeObject<CommunityPrompt>(File.ReadAllText(filePath));
                if (prompt == null)
                {
                    Log.Warning("Failed to parse prompt file: {Path}", filePath);
                    return null;
                }

                prompt.Id = Guid.NewGuid().ToString("N");
                prompt.Name = Path.GetFileNameWithoutExtension(filePath);
                File.WriteAllText(GetPromptFilePath(prompt.Id), JsonConvert.SerializeObject(prompt, Formatting.Indented));

                var settings = Settings;
                if (settings != null && !settings.InstalledCommunityPromptIds.Contains(prompt.Id))
                {
                    settings.InstalledCommunityPromptIds.Add(prompt.Id);
                    CoreSettings.Save();
                }

                prompt.IsInstalled = true;
                PromptInstalled?.Invoke(this, prompt);
                Log.Information("Imported prompt: {Name} by {Author}", prompt.Name, prompt.Author);
                return prompt;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error importing prompt from {Path}", filePath);
                Error?.Invoke(this, ex);
                return null;
            }
        }

        #endregion

        /// <summary>An id is a file name here: a guid or a slug, never a path.</summary>
        internal static bool IsSafeId(string? promptId) =>
            !string.IsNullOrWhiteSpace(promptId)
            && promptId.IndexOfAny(Path.GetInvalidFileNameChars()) < 0
            && !promptId.Contains("..", StringComparison.Ordinal)
            && promptId.IndexOfAny(new[] { '/', '\\', ':' }) < 0;

        private string GetPromptFilePath(string promptId) => Path.Combine(_promptsFolder, $"{promptId}.json");

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _httpClient.Dispose();
        }
    }
}
