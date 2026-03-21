using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ConditioningControlPanel.Models;
using Newtonsoft.Json;
using Serilog;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// Result of a mod installation attempt.
    /// </summary>
    public class ModInstallResult
    {
        public bool Success { get; set; }
        public string? ModId { get; set; }
        public string? ErrorMessage { get; set; }
    }

    /// <summary>
    /// Core service for the modular mod system.
    /// Manages installed mods, active mod selection, and provides all data accessors
    /// with a fallback chain: ActiveMod → BaseMod (BambiSleep).
    /// </summary>
    public class ModService
    {
        private static readonly ILogger? _log = App.Logger;

        private ModPackage _activeMod;
        private readonly ModPackage _baseMod; // Always BambiSleep
        private readonly Dictionary<string, ModPackage> _installedMods = new(StringComparer.OrdinalIgnoreCase);
        private readonly string _modsFolder;

        /// <summary>
        /// Fired when the active mod changes.
        /// </summary>
        public event EventHandler<ModPackage>? ModChanged;

        /// <summary>
        /// The currently active mod package.
        /// </summary>
        public ModPackage ActiveMod => _activeMod;

        /// <summary>
        /// The active mod's ID.
        /// </summary>
        public string ActiveModId => _activeMod.Id;

        /// <summary>
        /// All installed mods (built-in + user-installed).
        /// </summary>
        public IReadOnlyDictionary<string, ModPackage> InstalledMods => _installedMods;

        public ModService()
        {
            _modsFolder = Path.Combine(App.UserDataPath, "mods");
            Directory.CreateDirectory(_modsFolder);

            // Register built-in mods
            _baseMod = new ModPackage(BuiltInMods.BambiSleep, null, isBuiltIn: true);
            var sissyMod = new ModPackage(BuiltInMods.SissyHypno, null, isBuiltIn: true);
            _installedMods[_baseMod.Id] = _baseMod;
            _installedMods[sissyMod.Id] = sissyMod;

            // Load user-installed mods from disk
            LoadInstalledMods();

            // Default to base mod until Initialize is called
            _activeMod = _baseMod;
        }

        /// <summary>
        /// Initialize with the persisted active mod ID from settings.
        /// </summary>
        public void Initialize(string? activeModId)
        {
            if (!string.IsNullOrEmpty(activeModId) && _installedMods.TryGetValue(activeModId, out var mod))
            {
                _activeMod = mod;
            }
            else
            {
                _activeMod = _baseMod;
            }
            _log?.Information("ModService initialized — active mod: {ModId} ({ModName})", _activeMod.Id, _activeMod.Name);
        }

        #region Install / Uninstall / Activate

        /// <summary>
        /// Install a .ccpmod file. Extracts, validates manifest, registers.
        /// </summary>
        public async Task<ModInstallResult> InstallModAsync(string ccpmodPath)
        {
            try
            {
                if (!File.Exists(ccpmodPath))
                    return new ModInstallResult { ErrorMessage = "File not found." };

                // Extract to temp first for validation
                var tempDir = Path.Combine(Path.GetTempPath(), "ccp_mod_install_" + Guid.NewGuid().ToString("N")[..8]);
                Directory.CreateDirectory(tempDir);

                try
                {
                    await Task.Run(() => ZipFile.ExtractToDirectory(ccpmodPath, tempDir));

                    // Find and validate manifest
                    var manifestPath = Path.Combine(tempDir, "mod.json");
                    if (!File.Exists(manifestPath))
                        return new ModInstallResult { ErrorMessage = "No mod.json found in package." };

                    var json = await File.ReadAllTextAsync(manifestPath);
                    var manifest = JsonConvert.DeserializeObject<ModManifest>(json);

                    if (manifest == null)
                        return new ModInstallResult { ErrorMessage = "Failed to parse mod.json." };

                    // Validate required fields
                    if (string.IsNullOrWhiteSpace(manifest.Id))
                        return new ModInstallResult { ErrorMessage = "Mod ID is required." };
                    if (string.IsNullOrWhiteSpace(manifest.Name))
                        return new ModInstallResult { ErrorMessage = "Mod name is required." };
                    if (string.IsNullOrWhiteSpace(manifest.Version))
                        return new ModInstallResult { ErrorMessage = "Mod version is required." };
                    if (string.IsNullOrWhiteSpace(manifest.Author))
                        return new ModInstallResult { ErrorMessage = "Mod author is required." };

                    // Validate ID format (lowercase alphanumeric + hyphens)
                    if (!Regex.IsMatch(manifest.Id, @"^[a-z0-9][a-z0-9\-]*[a-z0-9]$") && manifest.Id.Length > 1
                        || (manifest.Id.Length == 1 && !Regex.IsMatch(manifest.Id, @"^[a-z0-9]$")))
                        return new ModInstallResult { ErrorMessage = "Mod ID must be lowercase alphanumeric with hyphens (e.g. 'my-cool-mod')." };

                    // Prevent overwriting built-in mods
                    if (manifest.Id.StartsWith("builtin-"))
                        return new ModInstallResult { ErrorMessage = "Cannot install a mod with a 'builtin-' prefix." };

                    // Check min app version
                    if (!string.IsNullOrEmpty(manifest.MinAppVersion))
                    {
                        if (Version.TryParse(manifest.MinAppVersion, out var minVer) &&
                            Version.TryParse(UpdateService.AppVersion, out var appVer) &&
                            appVer < minVer)
                        {
                            return new ModInstallResult { ErrorMessage = $"This mod requires app version {manifest.MinAppVersion} or later." };
                        }
                    }

                    // === SANITIZE MANIFEST FIELDS ===
                    var sanitizeResult = SanitizeManifest(manifest);
                    if (sanitizeResult != null)
                        return new ModInstallResult { ErrorMessage = sanitizeResult };

                    // Move to permanent location (overwrite existing if same ID)
                    var installDir = Path.Combine(_modsFolder, manifest.Id);
                    if (Directory.Exists(installDir))
                        Directory.Delete(installDir, recursive: true);

                    Directory.Move(tempDir, installDir);

                    // Register
                    var package = new ModPackage(manifest, installDir, isBuiltIn: false);
                    _installedMods[manifest.Id] = package;

                    _log?.Information("Mod installed: {ModId} v{Version} by {Author}", manifest.Id, manifest.Version, manifest.Author);
                    return new ModInstallResult { Success = true, ModId = manifest.Id };
                }
                finally
                {
                    // Clean up temp if it still exists (move failed or validation failed)
                    if (Directory.Exists(tempDir))
                    {
                        try { Directory.Delete(tempDir, recursive: true); } catch { }
                    }
                }
            }
            catch (Exception ex)
            {
                _log?.Error(ex, "Failed to install mod from {Path}", ccpmodPath);
                return new ModInstallResult { ErrorMessage = $"Installation failed: {ex.Message}" };
            }
        }

        /// <summary>
        /// Validates and sanitizes a mod manifest on install.
        /// Returns null if valid, or an error message string if rejected.
        /// </summary>
        private static string? SanitizeManifest(ModManifest manifest)
        {
            // --- Field length caps ---
            if (manifest.Name.Length > 100) return "Mod name is too long (max 100 characters).";
            if (manifest.Id.Length > 50) return "Mod ID is too long (max 50 characters).";
            if (manifest.Author.Length > 100) return "Author name is too long (max 100 characters).";
            if (manifest.Description?.Length > 1000) manifest.Description = manifest.Description[..1000];

            // --- Theme color validation ---
            if (manifest.Theme != null)
            {
                var hexPattern = new Regex(@"^#[0-9A-Fa-f]{6}$");
                if (manifest.Theme.AccentColor != null && !hexPattern.IsMatch(manifest.Theme.AccentColor))
                    return "Accent color must be a valid #RRGGBB hex code.";
                if (manifest.Theme.AccentLightColor != null && !hexPattern.IsMatch(manifest.Theme.AccentLightColor))
                    return "Light accent color must be a valid #RRGGBB hex code.";
                if (manifest.Theme.AccentDarkColor != null && !hexPattern.IsMatch(manifest.Theme.AccentDarkColor))
                    return "Dark accent color must be a valid #RRGGBB hex code.";
                if (manifest.Theme.BackgroundColor != null && !hexPattern.IsMatch(manifest.Theme.BackgroundColor))
                    return "Background color must be a valid #RRGGBB hex code.";
                if (manifest.Theme.PanelColor != null && !hexPattern.IsMatch(manifest.Theme.PanelColor))
                    return "Panel color must be a valid #RRGGBB hex code.";
                if (manifest.Theme.SurfaceColor != null && !hexPattern.IsMatch(manifest.Theme.SurfaceColor))
                    return "Surface color must be a valid #RRGGBB hex code.";
                if (manifest.Theme.FilterColor != null && !hexPattern.IsMatch(manifest.Theme.FilterColor))
                    return "Filter color must be a valid #RRGGBB hex code.";
            }

            // --- URL validation: only HTTPS allowed ---
            if (!string.IsNullOrEmpty(manifest.Browser?.DefaultUrl))
            {
                if (!Uri.TryCreate(manifest.Browser.DefaultUrl, UriKind.Absolute, out var uri)
                    || uri.Scheme != "https")
                    return "Browser URL must be a valid HTTPS URL.";
            }
            if (manifest.Browser?.DefaultVideoLinks != null)
            {
                if (manifest.Browser.DefaultVideoLinks.Count > 100)
                    return "Too many video links (max 100).";
                foreach (var kvp in manifest.Browser.DefaultVideoLinks)
                {
                    if (kvp.Key.Length > 200) return "Video link name is too long (max 200).";
                    if (kvp.Value.Length > 500) return "Video link URL is too long (max 500).";
                    if (!Uri.TryCreate(kvp.Value, UriKind.Absolute, out var uri) || uri.Scheme != "https")
                        return $"Video link URL must be HTTPS: '{kvp.Key}'";
                }
            }

            // --- TextReplacements sanitization ---
            if (manifest.TextReplacements != null)
            {
                if (manifest.TextReplacements.Count > 200)
                    return "Too many text replacements (max 200).";

                var sanitized = new Dictionary<string, string>();
                foreach (var kvp in manifest.TextReplacements)
                {
                    var key = kvp.Key;
                    var val = kvp.Value;

                    // Skip empty keys
                    if (string.IsNullOrWhiteSpace(key)) continue;

                    // Cap key/value lengths
                    if (key.Length > 200) return $"Text replacement key is too long (max 200): '{key[..30]}...'";
                    if (val.Length > 500) return $"Text replacement value is too long (max 500): '{key}'";

                    // Strip control characters (except newline/tab)
                    val = StripControlChars(val);
                    key = StripControlChars(key);

                    sanitized[key] = val;
                }
                manifest.TextReplacements = sanitized;
            }

            // --- Phrase pool sanitization ---
            if (manifest.SubliminalPool != null)
            {
                if (manifest.SubliminalPool.Count > 500) return "Too many subliminal phrases (max 500).";
                if (manifest.SubliminalPool.Keys.Any(k => k.Length > 500))
                    return "Subliminal phrase too long (max 500 characters).";
            }
            if (manifest.LockCardPhrases != null)
            {
                if (manifest.LockCardPhrases.Count > 200) return "Too many lock card phrases (max 200).";
                if (manifest.LockCardPhrases.Keys.Any(k => k.Length > 500))
                    return "Lock card phrase too long (max 500 characters).";
            }
            if (manifest.CustomTriggers != null)
            {
                if (manifest.CustomTriggers.Count > 50) return "Too many custom triggers (max 50).";
                for (int i = 0; i < manifest.CustomTriggers.Count; i++)
                    if (manifest.CustomTriggers[i].Length > 200)
                        manifest.CustomTriggers[i] = manifest.CustomTriggers[i][..200];
            }

            // --- Phrases dictionary sanitization ---
            if (manifest.Phrases != null)
            {
                if (manifest.Phrases.Count > 50) return "Too many phrase categories (max 50).";
                foreach (var (cat, arr) in manifest.Phrases)
                {
                    if (cat.Length > 100) return "Phrase category name too long (max 100).";
                    if (arr.Length > 500) return $"Too many phrases in category '{cat}' (max 500).";
                    for (int i = 0; i < arr.Length; i++)
                    {
                        if (arr[i].Length > 500) arr[i] = arr[i][..500];
                        arr[i] = StripControlChars(arr[i]);
                    }
                }
            }

            // --- Identity/Messages/Triggers string length caps ---
            if (manifest.Identity != null)
            {
                if (manifest.Identity.CompanionName?.Length > 200) manifest.Identity.CompanionName = manifest.Identity.CompanionName[..200];
                if (manifest.Identity.UserTerm?.Length > 200) manifest.Identity.UserTerm = manifest.Identity.UserTerm[..200];
                if (manifest.Identity.ModeDisplayName?.Length > 200) manifest.Identity.ModeDisplayName = manifest.Identity.ModeDisplayName[..200];
                if (manifest.Identity.TalkToLabel?.Length > 200) manifest.Identity.TalkToLabel = manifest.Identity.TalkToLabel[..200];
                if (manifest.Identity.TakeoverLabel?.Length > 200) manifest.Identity.TakeoverLabel = manifest.Identity.TakeoverLabel[..200];
            }
            if (manifest.Messages != null)
            {
                if (manifest.Messages.AttentionCheckFail?.Length > 500) manifest.Messages.AttentionCheckFail = manifest.Messages.AttentionCheckFail[..500];
                if (manifest.Messages.AttentionCheckMercy?.Length > 500) manifest.Messages.AttentionCheckMercy = manifest.Messages.AttentionCheckMercy[..500];
                if (manifest.Messages.BubbleCountRetry?.Length > 500) manifest.Messages.BubbleCountRetry = manifest.Messages.BubbleCountRetry[..500];
            }
            if (manifest.Triggers != null)
            {
                if (manifest.Triggers.Freeze?.Length > 200) manifest.Triggers.Freeze = manifest.Triggers.Freeze[..200];
                if (manifest.Triggers.Reset?.Length > 200) manifest.Triggers.Reset = manifest.Triggers.Reset[..200];
                if (manifest.Triggers.CumAndCollapse?.Length > 200) manifest.Triggers.CumAndCollapse = manifest.Triggers.CumAndCollapse[..200];
                if (manifest.Triggers.AutonomyOn?.Length > 200) manifest.Triggers.AutonomyOn = manifest.Triggers.AutonomyOn[..200];
            }

            // --- Tags sanitization ---
            if (manifest.Tags != null)
            {
                if (manifest.Tags.Count > 20) return "Too many tags (max 20).";
                for (int i = 0; i < manifest.Tags.Count; i++)
                    if (manifest.Tags[i].Length > 50) manifest.Tags[i] = manifest.Tags[i][..50];
            }

            if (manifest.Personalities != null && manifest.Personalities.Count > 20)
                return "Too many personalities (max 20).";

            // --- Personality prompt settings: cap sizes ---
            if (manifest.Personalities != null)
            {
                foreach (var p in manifest.Personalities)
                {
                    if (p.Name.Length > 100) return $"Personality name too long: '{p.Name[..30]}...'";
                    if (p.PromptSettings != null)
                    {
                        foreach (var kvp in p.PromptSettings)
                        {
                            if (kvp.Value.Length > 5000)
                                return $"Personality prompt setting value too long for '{p.Name}'.";
                        }
                    }
                }
            }

            // --- Supported avatar sets sanitization ---
            if (manifest.SupportedAvatarSets != null)
            {
                if (manifest.SupportedAvatarSets.Count > 20)
                    return "Too many supported avatar sets (max 20).";
                // Only allow valid set numbers 1-7
                manifest.SupportedAvatarSets = manifest.SupportedAvatarSets.Where(s => s >= 1 && s <= 7).Distinct().ToList();
            }

            // --- Custom avatar sets sanitization ---
            if (manifest.CustomAvatarSets != null)
            {
                if (manifest.CustomAvatarSets.Count > 20)
                    return "Too many custom avatar sets (max 20).";
                var seenSetNums = new HashSet<int>();
                foreach (var cs in manifest.CustomAvatarSets)
                {
                    if (cs.SetNumber < 8) return $"Custom avatar set number must be 8 or higher (got {cs.SetNumber}).";
                    if (!seenSetNums.Add(cs.SetNumber)) return $"Duplicate custom avatar set number: {cs.SetNumber}.";
                    if (cs.UnlockLevel < 1 || cs.UnlockLevel > 9999) return $"Custom avatar set unlock level must be 1-9999.";
                    if (cs.Label.Length > 100) cs.Label = cs.Label[..100];
                }
            }

            // --- Tube layout sanitization ---
            if (manifest.TubeLayout != null)
            {
                manifest.TubeLayout.AvatarOffsetX = Math.Clamp(manifest.TubeLayout.AvatarOffsetX, -1000, 1000);
                manifest.TubeLayout.AvatarDetachedOffsetX = Math.Clamp(manifest.TubeLayout.AvatarDetachedOffsetX, -1000, 1000);
                if (manifest.TubeLayout.AvatarScale.HasValue)
                    manifest.TubeLayout.AvatarScale = Math.Clamp(manifest.TubeLayout.AvatarScale.Value, 0.1, 3.0);
                manifest.TubeLayout.AvatarOffsetY = Math.Clamp(manifest.TubeLayout.AvatarOffsetY, -500, 500);
                manifest.TubeLayout.AvatarDetachedOffsetY = Math.Clamp(manifest.TubeLayout.AvatarDetachedOffsetY, -500, 500);
            }

            // --- Enhancement overrides sanitization ---
            if (manifest.EnhancementOverrides != null)
            {
                var eo = manifest.EnhancementOverrides;
                // Cap string field lengths (200 chars for labels)
                if (eo.TreeTitle?.Length > 200) eo.TreeTitle = eo.TreeTitle[..200];
                if (eo.TreeSubtitle?.Length > 200) eo.TreeSubtitle = eo.TreeSubtitle[..200];
                if (eo.TreeWarning?.Length > 200) eo.TreeWarning = eo.TreeWarning[..200];
                if (eo.PointsLabel?.Length > 200) eo.PointsLabel = eo.PointsLabel[..200];
                if (eo.StatsTitle?.Length > 200) eo.StatsTitle = eo.StatsTitle[..200];
                if (eo.TabTooltip?.Length > 200) eo.TabTooltip = eo.TabTooltip[..200];
                if (eo.PinkRushName?.Length > 200) eo.PinkRushName = eo.PinkRushName[..200];
                if (eo.PinkRushDescription?.Length > 200) eo.PinkRushDescription = eo.PinkRushDescription[..200];
                if (eo.LuckyFlashLabel?.Length > 200) eo.LuckyFlashLabel = eo.LuckyFlashLabel[..200];
                if (eo.LuckyBubbleLabel?.Length > 200) eo.LuckyBubbleLabel = eo.LuckyBubbleLabel[..200];

                // Tooltip dictionaries (max 30 entries, 500 chars per value)
                if (eo.BoostTooltips != null)
                {
                    if (eo.BoostTooltips.Count > 30) return "Too many boost tooltips (max 30).";
                    foreach (var kvp in eo.BoostTooltips)
                        if (kvp.Value.Length > 500) return $"Boost tooltip too long for '{kvp.Key}' (max 500).";
                }
                if (eo.StatPillTooltips != null)
                {
                    if (eo.StatPillTooltips.Count > 30) return "Too many stat pill tooltips (max 30).";
                    foreach (var kvp in eo.StatPillTooltips)
                        if (kvp.Value.Length > 500) return $"Stat pill tooltip too long for '{kvp.Key}' (max 500).";
                }
            }

            return null; // All good
        }

        private static string StripControlChars(string input)
        {
            var sb = new System.Text.StringBuilder(input.Length);
            foreach (var c in input)
            {
                if (char.IsControl(c) && c != '\n' && c != '\r' && c != '\t')
                    continue;
                sb.Append(c);
            }
            return sb.ToString();
        }

        /// <summary>
        /// Uninstall a user-installed mod. Cannot uninstall built-in mods.
        /// If the uninstalled mod is active, falls back to BambiSleep.
        /// </summary>
        public bool UninstallMod(string modId)
        {
            if (!_installedMods.TryGetValue(modId, out var mod))
                return false;
            if (mod.IsBuiltIn)
                return false;

            // If this was active, fall back to base
            if (_activeMod.Id == modId)
            {
                ActivateMod(BuiltInMods.BambiSleepId);
            }

            // Remove from disk
            if (!string.IsNullOrEmpty(mod.InstalledPath) && Directory.Exists(mod.InstalledPath))
            {
                try { Directory.Delete(mod.InstalledPath, recursive: true); } catch (Exception ex)
                {
                    _log?.Warning(ex, "Failed to delete mod folder for {ModId}", modId);
                }
            }

            _installedMods.Remove(modId);
            _log?.Information("Mod uninstalled: {ModId}", modId);
            return true;
        }

        /// <summary>
        /// Switch the active mod. Saves the current pools to settings, loads new mod's defaults.
        /// </summary>
        public void ActivateMod(string modId)
        {
            if (!_installedMods.TryGetValue(modId, out var mod))
            {
                _log?.Warning("Cannot activate unknown mod: {ModId}", modId);
                return;
            }

            var oldModId = _activeMod.Id;
            if (oldModId == modId) return;

            // Save current pool customizations before switching
            SaveCurrentPoolsToSettings(oldModId);

            _activeMod = mod;

            // Restore pool customizations for the new mod (if any were saved previously)
            RestorePoolsFromSettings(modId);

            // Clear resource cache
            ModResourceResolver.ClearCache();

            // If the active companion isn't supported by the new mod, fall back to first supported companion
            if (App.Companion != null && !IsCompanionSupported(App.Companion.ActiveCompanion))
            {
                // Find first supported companion
                foreach (Models.CompanionId cid in Enum.GetValues(typeof(Models.CompanionId)))
                {
                    if (IsCompanionSupported(cid))
                    {
                        App.Companion.SwitchCompanion(cid);
                        _log?.Information("Auto-switched companion to {CompanionId} (previous not supported by new mod)", cid);
                        break;
                    }
                }
            }

            _log?.Information("Mod activated: {ModId} (was {OldModId})", modId, oldModId);
            ModChanged?.Invoke(this, mod);
        }

        #endregion

        #region Data Accessors (fallback chain: ActiveMod → BaseMod)

        // Helper: get value from active mod, fall back to base mod
        private T GetValue<T>(Func<ModManifest, T?> accessor, Func<ModManifest, T> baseFallback) where T : class
        {
            var val = accessor(_activeMod.Manifest);
            if (val != null) return val;
            return baseFallback(_baseMod.Manifest);
        }

        private string GetStringValue(Func<ModManifest, string?> accessor, Func<ModManifest, string> baseFallback)
        {
            var val = accessor(_activeMod.Manifest);
            if (!string.IsNullOrEmpty(val)) return val;
            return baseFallback(_baseMod.Manifest);
        }

        // Theme colors
        public string GetAccentColorHex() =>
            GetStringValue(m => m.Theme?.AccentColor, m => m.Theme!.AccentColor!);

        public (byte R, byte G, byte B) GetAccentColorRgb()
        {
            var hex = GetAccentColorHex();
            return ParseHexColor(hex);
        }

        public string GetAccentLightColorHex() =>
            GetStringValue(m => m.Theme?.AccentLightColor, m => m.Theme!.AccentLightColor!);

        public string GetAccentDarkColorHex() =>
            GetStringValue(m => m.Theme?.AccentDarkColor, m => m.Theme!.AccentDarkColor!);

        // Background colors
        public string GetBackgroundColorHex() =>
            GetStringValue(m => m.Theme?.BackgroundColor, m => m.Theme?.BackgroundColor ?? "#1A1A2E");

        public string GetPanelColorHex() =>
            GetStringValue(m => m.Theme?.PanelColor, m => m.Theme?.PanelColor ?? "#252542");

        public string GetSurfaceColorHex() =>
            GetStringValue(m => m.Theme?.SurfaceColor, m => m.Theme?.SurfaceColor ?? "#1E1E3A");

        public string GetFilterColorHex() =>
            GetStringValue(m => m.Theme?.FilterColor, m => m.Theme?.FilterColor ?? m.Theme!.AccentColor!);

        public (byte R, byte G, byte B) GetFilterColorRgb()
        {
            var hex = GetFilterColorHex();
            return ParseHexColor(hex);
        }

        /// <summary>
        /// Returns the secondary/purple color for the active mod.
        /// Built-in mods use their defined purple; custom mods auto-compute from accent via hue shift.
        /// </summary>
        public string GetSecondaryColorHex()
        {
            // Built-in mods have predefined secondary colors
            if (_activeMod.Id == BuiltInMods.BambiSleepId) return "#9B59B6";
            if (_activeMod.Id == BuiltInMods.SissyHypnoId) return "#7B68EE";

            return ComputeSecondaryFromAccent(GetAccentColorHex());
        }

        /// <summary>
        /// Returns the accent color with a specified alpha (0-255).
        /// </summary>
        public string GetTransparentAccentHex(byte alpha)
        {
            var (r, g, b) = GetAccentColorRgb();
            return $"#{alpha:X2}{r:X2}{g:X2}{b:X2}";
        }

        // Identity
        public string GetCompanionName() =>
            GetStringValue(m => m.Identity?.CompanionName, m => m.Identity!.CompanionName!);

        public string GetUserTerm() =>
            GetStringValue(m => m.Identity?.UserTerm, m => m.Identity!.UserTerm!);

        public string GetModeDisplayName() =>
            GetStringValue(m => m.Identity?.ModeDisplayName, m => m.Identity!.ModeDisplayName!);

        public string GetTalkToLabel() =>
            GetStringValue(m => m.Identity?.TalkToLabel, m => m.Identity!.TalkToLabel!);

        public string GetTakeoverLabel() =>
            GetStringValue(m => m.Identity?.TakeoverLabel, m => m.Identity!.TakeoverLabel!);

        // Pool defaults
        public Dictionary<string, bool> GetDefaultSubliminalPool() =>
            GetValue(m => m.SubliminalPool, m => m.SubliminalPool!) ?? new Dictionary<string, bool>();

        public Dictionary<string, bool> GetDefaultLockCardPhrases() =>
            GetValue(m => m.LockCardPhrases, m => m.LockCardPhrases!) ?? new Dictionary<string, bool>();

        public List<string> GetDefaultCustomTriggers() =>
            GetValue(m => m.CustomTriggers, m => m.CustomTriggers!) ?? new List<string>();

        // Triggers
        public string GetFreezeTriggerText() =>
            GetStringValue(m => m.Triggers?.Freeze, m => m.Triggers!.Freeze!);

        public string GetResetTriggerText() =>
            GetStringValue(m => m.Triggers?.Reset, m => m.Triggers!.Reset!);

        public string GetCumAndCollapseTrigger() =>
            GetStringValue(m => m.Triggers?.CumAndCollapse, m => m.Triggers!.CumAndCollapse!);

        public string GetAutonomyOnPhrase() =>
            GetStringValue(m => m.Triggers?.AutonomyOn, m => m.Triggers!.AutonomyOn!);

        // Messages
        public string GetAttentionCheckFailMessage() =>
            GetStringValue(m => m.Messages?.AttentionCheckFail, m => m.Messages!.AttentionCheckFail!);

        public string GetAttentionCheckMercyMessage() =>
            GetStringValue(m => m.Messages?.AttentionCheckMercy, m => m.Messages!.AttentionCheckMercy!);

        public string GetBubbleCountRetryMessage() =>
            GetStringValue(m => m.Messages?.BubbleCountRetry, m => m.Messages!.BubbleCountRetry!);

        // Browser (defense-in-depth: validate URL at point of use, not just at install)
        public string GetDefaultBrowserUrl()
        {
            var url = GetStringValue(m => m.Browser?.DefaultUrl, m => m.Browser!.DefaultUrl!);
            // Only allow HTTPS URLs — reject javascript:, file:, data:, etc.
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == "https")
                return url;
            _log?.Warning("Rejected non-HTTPS browser URL from mod: {Url}", url);
            return _baseMod.Manifest.Browser?.DefaultUrl ?? "https://hypnotube.com/";
        }

        public Dictionary<string, string>? GetVideoLinks()
        {
            var links = _activeMod.Manifest.Browser?.DefaultVideoLinks;
            if (links != null && links.Count > 0) return links;
            return _baseMod.Manifest.Browser?.DefaultVideoLinks;
        }

        public bool ShowBambiCloudOption() =>
            _activeMod.Manifest.Browser?.ShowBambiCloudOption ?? _baseMod.Manifest.Browser?.ShowBambiCloudOption ?? true;

        // Phrases (27 categories)
        public string[] GetPhrases(string category)
        {
            // Check active mod first
            if (_activeMod.Manifest.Phrases != null &&
                _activeMod.Manifest.Phrases.TryGetValue(category, out var phrases) &&
                phrases.Length > 0)
            {
                return phrases;
            }

            // Fallback to base mod
            if (_baseMod.Manifest.Phrases != null &&
                _baseMod.Manifest.Phrases.TryGetValue(category, out var basePhrases))
            {
                return basePhrases;
            }

            return Array.Empty<string>();
        }

        /// <summary>
        /// Get personality display name adapted for active mod.
        /// </summary>
        public string GetPersonalityDisplayName(string presetName)
        {
            // If active mod has text replacements, apply them
            if (_activeMod.Manifest.TextReplacements != null && _activeMod.Manifest.TextReplacements.Count > 0)
            {
                var result = presetName;
                foreach (var kvp in _activeMod.Manifest.TextReplacements)
                {
                    result = result.Replace(kvp.Key, kvp.Value);
                }
                return result;
            }
            return presetName;
        }

        /// <summary>
        /// Replaces terminology in text based on the active mod's text replacements.
        /// This replaces Session.MakeModeAware().
        /// </summary>
        public string MakeModAware(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;

            // If active mod is the base mod (BambiSleep), no replacements needed
            if (_activeMod.Id == BuiltInMods.BambiSleepId) return text;

            var replacements = _activeMod.Manifest.TextReplacements;
            if (replacements == null || replacements.Count == 0) return text;

            var result = text;
            // Apply replacements in order — longer strings first to avoid partial matches
            foreach (var kvp in replacements.OrderByDescending(r => r.Key.Length))
            {
                result = result.Replace(kvp.Key, kvp.Value);
            }
            return result;
        }

        // Avatar set support — when specified, only listed sets appear in UI
        public bool IsAvatarSetSupported(int setNumber)
        {
            var supported = _activeMod.Manifest.SupportedAvatarSets;
            return supported == null || supported.Count == 0 || supported.Contains(setNumber);
        }

        public bool IsCompanionSupported(Models.CompanionId companionId)
        {
            var setNumber = companionId switch
            {
                Models.CompanionId.OGBambiSprite => 3,
                Models.CompanionId.CultBunny => 4,
                Models.CompanionId.BrainParasite => 5,
                Models.CompanionId.BambiTrainer => 6,
                Models.CompanionId.BimboCow => 7,
                _ => 1
            };
            return IsAvatarSetSupported(setNumber);
        }

        // Custom avatar sets (8+)
        public List<Models.CustomAvatarSet>? GetCustomAvatarSets() => _activeMod.Manifest.CustomAvatarSets;

        public int? GetCustomAvatarSetUnlockLevel(int setNumber) =>
            _activeMod.Manifest.CustomAvatarSets?.FirstOrDefault(c => c.SetNumber == setNumber)?.UnlockLevel;

        // Tube layout offsets
        public int GetAvatarOffsetX() => _activeMod.Manifest.TubeLayout?.AvatarOffsetX ?? 0;
        public int GetAvatarDetachedOffsetX() => _activeMod.Manifest.TubeLayout?.AvatarDetachedOffsetX ?? 0;
        public double GetAvatarScale() => _activeMod.Manifest.TubeLayout?.AvatarScale ?? 1.0;
        public int GetAvatarOffsetY() => _activeMod.Manifest.TubeLayout?.AvatarOffsetY ?? 0;
        public int GetAvatarDetachedOffsetY() => _activeMod.Manifest.TubeLayout?.AvatarDetachedOffsetY ?? 0;

        // Enhancement overrides — check explicit override first, then fall back to MakeModAware(default)
        public string GetEnhancementTreeTitle() =>
            _activeMod.Manifest.EnhancementOverrides?.TreeTitle ?? MakeModAware("Bimbo Enhancement Tree");

        public string GetEnhancementTreeSubtitle() =>
            _activeMod.Manifest.EnhancementOverrides?.TreeSubtitle ?? MakeModAware("you earn sparkle points from leveling up + every 100 bubbles popped~");

        public string GetEnhancementTreeWarning() =>
            _activeMod.Manifest.EnhancementOverrides?.TreeWarning ?? MakeModAware("once you pick a path, there's no going back~");

        public string GetPointsLabel() =>
            _activeMod.Manifest.EnhancementOverrides?.PointsLabel ?? MakeModAware("Sparkle Points");

        public string GetStatsTitle() =>
            _activeMod.Manifest.EnhancementOverrides?.StatsTitle ?? MakeModAware("Ditzy Data Stats");

        public string GetTabTooltip() =>
            _activeMod.Manifest.EnhancementOverrides?.TabTooltip ?? MakeModAware("Bimbo Enhancement Tree");

        public string GetPinkRushName() =>
            _activeMod.Manifest.EnhancementOverrides?.PinkRushName ?? MakeModAware("PINK RUSH!");

        public string GetPinkRushDescription() =>
            _activeMod.Manifest.EnhancementOverrides?.PinkRushDescription ?? MakeModAware("3x XP for 60 seconds!");

        public string GetLuckyFlashLabel() =>
            _activeMod.Manifest.EnhancementOverrides?.LuckyFlashLabel ?? MakeModAware("Lucky Flash");

        public string GetLuckyBubbleLabel() =>
            _activeMod.Manifest.EnhancementOverrides?.LuckyBubbleLabel ?? MakeModAware("Lucky Bubble");

        public string? GetBoostTooltip(string skillId) =>
            _activeMod.Manifest.EnhancementOverrides?.BoostTooltips?.TryGetValue(skillId, out var tip) == true ? tip : null;

        public string? GetStatPillTooltip(string skillId) =>
            _activeMod.Manifest.EnhancementOverrides?.StatPillTooltips?.TryGetValue(skillId, out var tip) == true ? tip : null;

        /// <summary>
        /// Whether the active mod is the base (BambiSleep) mod.
        /// Used for backward compat where code checks IsBambiMode.
        /// </summary>
        public bool IsBaseMod => _activeMod.Id == BuiltInMods.BambiSleepId;

        /// <summary>
        /// Whether the active mod is the SissyHypno built-in.
        /// Used for backward compat where code checks IsSissyMode.
        /// </summary>
        public bool IsSissyMod => _activeMod.Id == BuiltInMods.SissyHypnoId;

        #endregion

        #region Export / Template

        /// <summary>
        /// Export the current configuration as a .ccpmod file.
        /// </summary>
        public async Task ExportCurrentAsModAsync(string outputPath, string modName, string author)
        {
            var manifest = new ModManifest
            {
                Id = SanitizeModId(modName),
                Name = modName,
                Version = "1.0.0",
                Author = author,
                Description = $"Exported from {GetModeDisplayName()} configuration.",
                Theme = new ModTheme
                {
                    AccentColor = GetAccentColorHex(),
                    AccentLightColor = GetAccentLightColorHex(),
                    AccentDarkColor = GetAccentDarkColorHex(),
                    BackgroundColor = GetBackgroundColorHex(),
                    PanelColor = GetPanelColorHex(),
                    SurfaceColor = GetSurfaceColorHex(),
                    FilterColor = GetFilterColorHex()
                },
                Identity = new ModIdentity
                {
                    CompanionName = GetCompanionName(),
                    UserTerm = GetUserTerm(),
                    ModeDisplayName = GetModeDisplayName(),
                    TalkToLabel = GetTalkToLabel(),
                    TakeoverLabel = GetTakeoverLabel()
                },
                Triggers = new ModTriggers
                {
                    Freeze = GetFreezeTriggerText(),
                    Reset = GetResetTriggerText(),
                    CumAndCollapse = GetCumAndCollapseTrigger(),
                    AutonomyOn = GetAutonomyOnPhrase()
                },
                Messages = new ModMessages
                {
                    AttentionCheckFail = GetAttentionCheckFailMessage(),
                    AttentionCheckMercy = GetAttentionCheckMercyMessage(),
                    BubbleCountRetry = GetBubbleCountRetryMessage()
                },
                Browser = new ModBrowser
                {
                    DefaultUrl = GetDefaultBrowserUrl(),
                    ShowBambiCloudOption = ShowBambiCloudOption()
                }
            };

            // Include current subliminal pool from settings
            manifest.SubliminalPool = App.Settings?.Current?.SubliminalPool != null
                ? new Dictionary<string, bool>(App.Settings.Current.SubliminalPool)
                : GetDefaultSubliminalPool();

            manifest.LockCardPhrases = App.Settings?.Current?.LockCardPhrases != null
                ? new Dictionary<string, bool>(App.Settings.Current.LockCardPhrases)
                : GetDefaultLockCardPhrases();

            manifest.CustomTriggers = App.Settings?.Current?.CustomTriggers != null
                ? new List<string>(App.Settings.Current.CustomTriggers)
                : GetDefaultCustomTriggers();

            // Include all phrase categories from active mod
            if (_activeMod.Manifest.Phrases != null)
            {
                manifest.Phrases = new Dictionary<string, string[]>(_activeMod.Manifest.Phrases);
            }

            if (_activeMod.Manifest.TextReplacements != null && _activeMod.Manifest.TextReplacements.Count > 0)
            {
                manifest.TextReplacements = new Dictionary<string, string>(_activeMod.Manifest.TextReplacements);
            }

            // Create temp dir, write manifest, zip
            var tempDir = Path.Combine(Path.GetTempPath(), "ccp_mod_export_" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(tempDir);
            Directory.CreateDirectory(Path.Combine(tempDir, "resources"));

            try
            {
                var json = JsonConvert.SerializeObject(manifest, Formatting.Indented);
                await File.WriteAllTextAsync(Path.Combine(tempDir, "mod.json"), json);

                // Copy resource overrides from active mod if it has any
                if (_activeMod.InstalledPath != null)
                {
                    var srcResources = Path.Combine(_activeMod.InstalledPath, "resources");
                    if (Directory.Exists(srcResources))
                    {
                        CopyDirectory(srcResources, Path.Combine(tempDir, "resources"));
                    }
                }

                // Create the .ccpmod (zip)
                if (File.Exists(outputPath))
                    File.Delete(outputPath);
                await Task.Run(() => ZipFile.CreateFromDirectory(tempDir, outputPath));

                _log?.Information("Mod exported to {Path}", outputPath);
            }
            finally
            {
                try { Directory.Delete(tempDir, recursive: true); } catch { }
            }
        }

        /// <summary>
        /// Generate a starter mod template folder.
        /// </summary>
        public void GenerateModTemplate(string outputFolder)
        {
            Directory.CreateDirectory(outputFolder);

            var template = new ModManifest
            {
                Id = "my-custom-mod",
                Name = "My Custom Mod",
                Version = "1.0.0",
                Author = "YourName",
                Description = "A custom themed experience.",
                Theme = new ModTheme
                {
                    AccentColor = "#FF69B4",
                    AccentLightColor = "#FFB6C1",
                    AccentDarkColor = "#FF1493"
                },
                Identity = new ModIdentity
                {
                    CompanionName = "BambiSprite",
                    UserTerm = "Bambi",
                    ModeDisplayName = "My Custom Mode",
                    TalkToLabel = "Talk to Companion",
                    TakeoverLabel = "Takeover"
                }
            };

            var json = JsonConvert.SerializeObject(template, Formatting.Indented);
            File.WriteAllText(Path.Combine(outputFolder, "mod.json"), json);

            // Create resource subdirectories
            var resourcesDir = Path.Combine(outputFolder, "resources");
            Directory.CreateDirectory(resourcesDir);
            Directory.CreateDirectory(Path.Combine(resourcesDir, "achievements"));
            Directory.CreateDirectory(Path.Combine(resourcesDir, "features"));
            Directory.CreateDirectory(Path.Combine(resourcesDir, "skills"));
            Directory.CreateDirectory(Path.Combine(resourcesDir, "spirals"));
            Directory.CreateDirectory(Path.Combine(resourcesDir, "Cards"));

            _log?.Information("Mod template generated at {Path}", outputFolder);
        }

        #endregion

        #region Private Helpers

        private void LoadInstalledMods()
        {
            if (!Directory.Exists(_modsFolder)) return;

            foreach (var dir in Directory.GetDirectories(_modsFolder))
            {
                var manifestPath = Path.Combine(dir, "mod.json");
                if (!File.Exists(manifestPath)) continue;

                try
                {
                    var json = File.ReadAllText(manifestPath);
                    var manifest = JsonConvert.DeserializeObject<ModManifest>(json);
                    if (manifest != null && !string.IsNullOrWhiteSpace(manifest.Id))
                    {
                        // Re-validate on load (defense-in-depth against tampered mod.json)
                        var sanitizeError = SanitizeManifest(manifest);
                        if (sanitizeError != null)
                        {
                            _log?.Warning("Mod {ModId} failed re-validation on load: {Error}", manifest.Id, sanitizeError);
                            continue;
                        }
                        _installedMods[manifest.Id] = new ModPackage(manifest, dir, isBuiltIn: false);
                        _log?.Information("Loaded installed mod: {ModId} v{Version}", manifest.Id, manifest.Version);
                    }
                }
                catch (Exception ex)
                {
                    _log?.Warning(ex, "Failed to load mod from {Path}", dir);
                }
            }
        }

        private void SaveCurrentPoolsToSettings(string modId)
        {
            var settings = App.Settings?.Current;
            if (settings == null) return;

            settings.SubliminalPoolByMod ??= new Dictionary<string, Dictionary<string, bool>>();
            settings.AttentionPoolByMod ??= new Dictionary<string, Dictionary<string, bool>>();
            settings.LockCardPhrasesByMod ??= new Dictionary<string, Dictionary<string, bool>>();
            settings.CustomTriggersByMod ??= new Dictionary<string, List<string>>();

            if (settings.SubliminalPool != null)
                settings.SubliminalPoolByMod[modId] = new Dictionary<string, bool>(settings.SubliminalPool);
            if (settings.AttentionPool != null)
                settings.AttentionPoolByMod[modId] = new Dictionary<string, bool>(settings.AttentionPool);
            if (settings.LockCardPhrases != null)
                settings.LockCardPhrasesByMod[modId] = new Dictionary<string, bool>(settings.LockCardPhrases);
            if (settings.CustomTriggers != null)
                settings.CustomTriggersByMod[modId] = new List<string>(settings.CustomTriggers);
        }

        private void RestorePoolsFromSettings(string modId)
        {
            var settings = App.Settings?.Current;
            if (settings == null) return;

            // Restore saved customizations, or fall back to mod defaults
            if (settings.SubliminalPoolByMod?.TryGetValue(modId, out var savedPool) == true)
                settings.SubliminalPool = new Dictionary<string, bool>(savedPool);
            else
                settings.SubliminalPool = new Dictionary<string, bool>(GetDefaultSubliminalPool());

            if (settings.LockCardPhrasesByMod?.TryGetValue(modId, out var savedLock) == true)
                settings.LockCardPhrases = new Dictionary<string, bool>(savedLock);
            else
                settings.LockCardPhrases = new Dictionary<string, bool>(GetDefaultLockCardPhrases());

            if (settings.CustomTriggersByMod?.TryGetValue(modId, out var savedTriggers) == true)
                settings.CustomTriggers = new List<string>(savedTriggers);
            else
                settings.CustomTriggers = new List<string>(GetDefaultCustomTriggers());
        }

        /// <summary>
        /// Auto-computes a secondary color from the accent by shifting hue ~60 degrees toward blue/purple.
        /// </summary>
        private static string ComputeSecondaryFromAccent(string hex)
        {
            var (r, g, b) = ParseHexColor(hex);

            // Convert RGB to HSL
            double rd = r / 255.0, gd = g / 255.0, bd = b / 255.0;
            double max = Math.Max(rd, Math.Max(gd, bd));
            double min = Math.Min(rd, Math.Min(gd, bd));
            double h = 0, s, l = (max + min) / 2.0;

            if (max == min)
            {
                h = s = 0;
            }
            else
            {
                double d = max - min;
                s = l > 0.5 ? d / (2.0 - max - min) : d / (max + min);
                if (max == rd) h = (gd - bd) / d + (gd < bd ? 6 : 0);
                else if (max == gd) h = (bd - rd) / d + 2;
                else h = (rd - gd) / d + 4;
                h /= 6.0;
            }

            // Shift hue ~60 degrees toward purple/blue
            h = (h + 60.0 / 360.0) % 1.0;
            // Slightly desaturate for a complementary feel
            s = Math.Min(1.0, s * 0.85);

            // Convert HSL back to RGB
            double r2, g2, b2;
            if (s == 0)
            {
                r2 = g2 = b2 = l;
            }
            else
            {
                double q = l < 0.5 ? l * (1 + s) : l + s - l * s;
                double p = 2 * l - q;
                r2 = HueToRgb(p, q, h + 1.0 / 3.0);
                g2 = HueToRgb(p, q, h);
                b2 = HueToRgb(p, q, h - 1.0 / 3.0);
            }

            return $"#{(byte)(r2 * 255):X2}{(byte)(g2 * 255):X2}{(byte)(b2 * 255):X2}";
        }

        private static double HueToRgb(double p, double q, double t)
        {
            if (t < 0) t += 1;
            if (t > 1) t -= 1;
            if (t < 1.0 / 6.0) return p + (q - p) * 6.0 * t;
            if (t < 1.0 / 2.0) return q;
            if (t < 2.0 / 3.0) return p + (q - p) * (2.0 / 3.0 - t) * 6.0;
            return p;
        }

        private static (byte R, byte G, byte B) ParseHexColor(string hex)
        {
            hex = hex.TrimStart('#');
            if (hex.Length != 6) return (255, 105, 180); // fallback to hot pink
            try
            {
                var r = Convert.ToByte(hex[..2], 16);
                var g = Convert.ToByte(hex[2..4], 16);
                var b = Convert.ToByte(hex[4..6], 16);
                return (r, g, b);
            }
            catch
            {
                return (255, 105, 180);
            }
        }

        private static string SanitizeModId(string name)
        {
            var id = name.ToLowerInvariant();
            id = Regex.Replace(id, @"[^a-z0-9\-]", "-");
            id = Regex.Replace(id, @"-+", "-");
            id = id.Trim('-');
            if (string.IsNullOrEmpty(id)) id = "custom-mod";
            return id;
        }

        private static void CopyDirectory(string sourceDir, string destinationDir)
        {
            Directory.CreateDirectory(destinationDir);
            foreach (var file in Directory.GetFiles(sourceDir))
            {
                File.Copy(file, Path.Combine(destinationDir, Path.GetFileName(file)), overwrite: true);
            }
            foreach (var dir in Directory.GetDirectories(sourceDir))
            {
                CopyDirectory(dir, Path.Combine(destinationDir, Path.GetFileName(dir)));
            }
        }

        #endregion
    }
}
