using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ConditioningControlPanel;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services.Settings;
using Newtonsoft.Json;

namespace ConditioningControlPanel.Avalonia.Services.Mod;

/// <summary>
/// Mod-aware theming/text service for the Avalonia head.
/// Loads built-in mods, discovers user-installed .ccpmod packages, and supports
/// install/uninstall/activate while keeping the legacy text helpers working.
/// </summary>
public sealed class AvaloniaModService : IModService
{
    private readonly ISettingsService _settings;
    private readonly IAppEnvironment _environment;
    private readonly ILogger<AvaloniaModService>? _logger;

    private readonly List<ModPackage> _builtInMods;
    private readonly List<ModPackage> _installedMods = new();
    private ModPackage _activeMod = null!;

    public AvaloniaModService(ISettingsService settings, IAppEnvironment environment, ILogger<AvaloniaModService>? logger = null)
    {
        _settings = settings;
        _environment = environment;
        _logger = logger;

        _builtInMods = new List<ModPackage>
        {
            new ModPackage(BuiltInMods.CCPDefault, null, true),
            new ModPackage(BuiltInMods.BambiSleep, null, true),
            new ModPackage(BuiltInMods.SissyHypno, null, true),
            new ModPackage(BuiltInMods.Dronification, null, true),
            new ModPackage(BuiltInMods.Locked, null, true),
        };
    }

    /// <inheritdoc />
    public IReadOnlyList<ModPackage> InstalledMods => _installedMods;

    /// <inheritdoc />
    public ModPackage ActiveMod => _activeMod;

    /// <inheritdoc />
    public event EventHandler<ModPackage>? ActiveModChanged;

    /// <inheritdoc />
    public void Initialize(string? activeModId)
    {
        EnsureModsDirectoryExists();
        RefreshInstalledMods();

        // Extract bundled .ccpmod packages (e.g. Drone, Locked resources) and register
        // them as built-in mods with an InstalledPath so mod-aware art/sounds resolve.
        ExtractBundledBuiltInMods();
        ExtractBundledResourceMods();

        var target = ResolveMod(activeModId);
        if (target == null)
        {
            target = ResolveMod(BuiltInMods.CCPDefaultId)!;
        }

        if (_settings.Current.ActiveModId != target.Id)
        {
            _settings.Current.ActiveModId = target.Id;
            _settings.Save();
        }

        _activeMod = target;
        _logger?.LogInformation("AvaloniaModService initialized — active mod: {ModId} ({ModName})", _activeMod.Id, _activeMod.Name);
    }

    /// <inheritdoc />
    public bool ActivateMod(string modId)
    {
        var target = ResolveMod(modId);
        if (target == null) return false;

        if (_activeMod.Id == target.Id) return true;

        _activeMod = target;
        _settings.Current.ActiveModId = target.Id;
        _settings.Save();
        ActiveModChanged?.Invoke(this, target);
        _logger?.LogInformation("Active mod changed to {ModId} ({ModName})", target.Id, target.Name);
        return true;
    }

    /// <inheritdoc />
    public async Task<ModInstallResult> InstallModAsync(string ccpmodPath)
    {
        if (string.IsNullOrWhiteSpace(ccpmodPath) || !File.Exists(ccpmodPath))
            return ModInstallResult.Failure(ModInstallStatus.InvalidPackage, "Mod package file not found.");

        if (!Path.GetExtension(ccpmodPath).Equals(".ccpmod", StringComparison.OrdinalIgnoreCase))
            return ModInstallResult.Failure(ModInstallStatus.InvalidPackage, "File must have a .ccpmod extension.");

        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(tempDir);
            await Task.Run(() => ZipFile.ExtractToDirectory(ccpmodPath, tempDir)).ConfigureAwait(false);

            var modJsonPath = Directory.GetFiles(tempDir, "mod.json", SearchOption.AllDirectories).FirstOrDefault();
            if (modJsonPath == null)
                return ModInstallResult.Failure(ModInstallStatus.InvalidManifest, "mod.json was not found in the package.");

            var manifest = JsonConvert.DeserializeObject<ModManifest>(await File.ReadAllTextAsync(modJsonPath).ConfigureAwait(false));
            if (manifest == null)
                return ModInstallResult.Failure(ModInstallStatus.InvalidManifest, "mod.json could not be parsed.");

            if (string.IsNullOrWhiteSpace(manifest.Id))
                return ModInstallResult.Failure(ModInstallStatus.InvalidManifest, "Mod ID is required.");

            if (!IsValidModId(manifest.Id))
                return ModInstallResult.Failure(ModInstallStatus.InvalidId, "Mod ID must be lowercase alphanumeric with hyphens and cannot start with 'builtin-'.");

            if (string.IsNullOrWhiteSpace(manifest.Name))
                return ModInstallResult.Failure(ModInstallStatus.InvalidManifest, "Mod name is required.");

            if (string.IsNullOrWhiteSpace(manifest.Version))
                return ModInstallResult.Failure(ModInstallStatus.InvalidManifest, "Mod version is required.");

            if (string.IsNullOrWhiteSpace(manifest.Author))
                return ModInstallResult.Failure(ModInstallStatus.InvalidManifest, "Mod author is required.");

            // Min app version compatibility gate — reject a mod that needs a newer app (mirrors WPF).
            if (!string.IsNullOrEmpty(manifest.MinAppVersion)
                && Version.TryParse(manifest.MinAppVersion, out var minVer)
                && Version.TryParse(ConditioningControlPanel.Core.Services.Update.UpdateService.AppVersion, out var appVer)
                && appVer < minVer)
            {
                return ModInstallResult.Failure(ModInstallStatus.InvalidManifest,
                    $"This mod requires app version {manifest.MinAppVersion} or later.");
            }

            // === SECURITY TRUST BOUNDARY === validate + sanitize before persisting/registering.
            var sanitizeError = SanitizeManifest(manifest);
            if (sanitizeError != null)
                return ModInstallResult.Failure(ModInstallStatus.InvalidManifest, sanitizeError);

            var modRoot = Path.GetDirectoryName(modJsonPath)!;
            var destDir = Path.Combine(GetModsDirectory(), manifest.Id);

            await Task.Run(() =>
            {
                if (Directory.Exists(destDir))
                    Directory.Delete(destDir, recursive: true);

                Directory.Move(modRoot, destDir);
            }).ConfigureAwait(false);

            RefreshInstalledMods();

            var installed = _installedMods.FirstOrDefault(m => m.Id == manifest.Id);
            if (installed != null)
                return ModInstallResult.Success(installed);

            return ModInstallResult.Failure(ModInstallStatus.UnknownError, "The mod was extracted but could not be discovered.");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to install mod from {Path}", ccpmodPath);
            return ModInstallResult.Failure(ModInstallStatus.IOFailure, $"Installation failed: {ex.Message}");
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, recursive: true);
            }
            catch { /* best effort cleanup */ }
        }
    }

    /// <inheritdoc />
    public bool UninstallMod(string modId)
    {
        if (string.IsNullOrWhiteSpace(modId)) return false;

        var existing = _installedMods.FirstOrDefault(m => m.Id == modId);
        if (existing == null || existing.IsBuiltIn) return false;

        var modDir = Path.Combine(GetModsDirectory(), modId);
        try
        {
            if (Directory.Exists(modDir))
                Directory.Delete(modDir, recursive: true);

            RefreshInstalledMods();

            if (_activeMod.Id == modId)
            {
                ActivateMod(BuiltInMods.CCPDefaultId);
            }

            _logger?.LogInformation("Uninstalled mod {ModId}", modId);
            return true;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to uninstall mod {ModId}", modId);
            return false;
        }
    }

    /// <inheritdoc />
    public async Task ExportCurrentAsModAsync(string outputPath, string modName, string author)
    {
        var settings = _settings.Current;
        var active = ActiveManifest;

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
            Identity = CloneIdentity(active.Identity),
            Triggers = CloneTriggers(active.Triggers),
            Messages = CloneMessages(active.Messages),
            Browser = CloneBrowser(active.Browser)
        };

        manifest.SubliminalPool = settings?.SubliminalPool != null
            ? new Dictionary<string, bool>(settings.SubliminalPool)
            : active.SubliminalPool != null
                ? new Dictionary<string, bool>(active.SubliminalPool)
                : null;

        manifest.LockCardPhrases = settings?.LockCardPhrases != null
            ? new Dictionary<string, bool>(settings.LockCardPhrases)
            : active.LockCardPhrases != null
                ? new Dictionary<string, bool>(active.LockCardPhrases)
                : null;

        manifest.CustomTriggers = settings?.CustomTriggers != null
            ? new List<string>(settings.CustomTriggers)
            : active.CustomTriggers != null
                ? new List<string>(active.CustomTriggers)
                : null;

        if (active.Phrases != null)
            manifest.Phrases = new Dictionary<string, string[]>(active.Phrases);

        if (active.TextReplacements != null && active.TextReplacements.Count > 0)
            manifest.TextReplacements = new Dictionary<string, string>(active.TextReplacements);

        var tempDir = Path.Combine(Path.GetTempPath(), "ccp_mod_export_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(tempDir);
        Directory.CreateDirectory(Path.Combine(tempDir, "resources"));

        try
        {
            var json = JsonConvert.SerializeObject(manifest, Formatting.Indented);
            await File.WriteAllTextAsync(Path.Combine(tempDir, "mod.json"), json).ConfigureAwait(false);

            if (!string.IsNullOrEmpty(_activeMod.InstalledPath))
            {
                var srcResources = Path.Combine(_activeMod.InstalledPath, "resources");
                if (Directory.Exists(srcResources))
                    CopyDirectory(srcResources, Path.Combine(tempDir, "resources"));
            }

            if (File.Exists(outputPath))
                File.Delete(outputPath);

            await Task.Run(() => ZipFile.CreateFromDirectory(tempDir, outputPath)).ConfigureAwait(false);
            _logger?.LogInformation("Mod exported to {Path}", outputPath);
        }
        finally
        {
            try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true); }
            catch { /* best-effort cleanup */ }
        }
    }

    #region Text / theming helpers (legacy IModService contract)

    private ModManifest ActiveManifest => _activeMod?.Manifest ?? BuiltInMods.CCPDefault;

    public string GetModeDisplayName()
        => ActiveManifest.Identity?.ModeDisplayName ?? ActiveManifest.Name ?? "Conditioning Control Panel";

    public IReadOnlyDictionary<string, string> GetVideoLinks()
    {
        var activeId = ActiveManifest.Id;
        var settings = _settings.Current;
        if (settings?.VideoLinksByMod != null &&
            settings.VideoLinksByMod.TryGetValue(activeId, out var overrideLinks) &&
            overrideLinks != null &&
            overrideLinks.Count > 0)
        {
            // Defense-in-depth: drop any non-HTTPS entry (javascript:/file:/data:/http:).
            return FilterHttpsVideoLinks(overrideLinks);
        }

        var defaults = ActiveManifest.Browser?.DefaultVideoLinks;
        if (defaults == null || defaults.Count == 0)
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Defense-in-depth: drop any non-HTTPS entry (javascript:/file:/data:/http:).
        return FilterHttpsVideoLinks(defaults);
    }

    public string GetAffirmation()
        => ActiveManifest.Identity?.Affirmation ?? "Subject";

    public double GetAvatarScale()
        => Math.Clamp(ActiveManifest.TubeLayout?.AvatarScale ?? 1.0, 0.1, 3.0);

    public int GetAvatarOffsetX()
        => Math.Clamp(ActiveManifest.TubeLayout?.AvatarOffsetX ?? 0, -1000, 1000);

    public int GetAvatarOffsetY()
        => Math.Clamp(ActiveManifest.TubeLayout?.AvatarOffsetY ?? 0, -500, 500);

    public int GetAvatarDetachedOffsetX()
        => Math.Clamp(ActiveManifest.TubeLayout?.AvatarDetachedOffsetX ?? 0, -1000, 1000);

    public int GetAvatarDetachedOffsetY()
        => Math.Clamp(ActiveManifest.TubeLayout?.AvatarDetachedOffsetY ?? 0, -500, 500);

    public bool IsAvatarSetSupported(int setNumber)
    {
        var supported = ActiveManifest.SupportedAvatarSets;
        if (supported == null || supported.Count == 0) return true;
        return supported.Contains(setNumber);
    }

    public IReadOnlyList<ConditioningControlPanel.Models.CustomAvatarSet> GetCustomAvatarSets()
    {
        var custom = ActiveManifest.CustomAvatarSets;
        return custom == null
            ? Array.Empty<ConditioningControlPanel.Models.CustomAvatarSet>()
            : new List<ConditioningControlPanel.Models.CustomAvatarSet>(custom);
    }

    public string MakeModAware(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;

        // No replacements registered → return the text unchanged (mod-agnostic; matches WPF,
        // which does NOT force "Bambi"→UserTerm under CCP Default).
        var replacements = ActiveManifest.TextReplacements;
        if (replacements == null || replacements.Count == 0) return text;

        var result = text;
        // Longest keys first so a shorter key can't clobber a longer one; ordinal
        // (case-sensitive) Replace, exactly like the WPF ModService.
        foreach (var kvp in replacements.OrderByDescending(r => r.Key.Length))
            result = result.Replace(kvp.Key, kvp.Value);
        return result;
    }

    public string GetAccentColorHex()
        => ActiveManifest.Theme?.AccentColor ?? "#FF69B4";

    public string GetAccentLightColorHex()
        => ActiveManifest.Theme?.AccentLightColor ?? "#FF8FAF";

    public string GetAccentDarkColorHex()
        => ActiveManifest.Theme?.AccentDarkColor ?? "#FF1493";

    public string GetSecondaryColorHex()
        => ActiveManifest.Theme?.AccentDarkColor ?? "#9B59B6";

    public string GetBackgroundColorHex()
        => ActiveManifest.Theme?.BackgroundColor ?? "#1A1A2E";

    public string GetPanelColorHex()
        => ActiveManifest.Theme?.PanelColor ?? "#252542";

    public string GetSurfaceColorHex()
        => ActiveManifest.Theme?.SurfaceColor ?? "#1E1E3A";

    public string GetFilterColorHex()
        => ActiveManifest.Theme?.FilterColor ?? GetAccentColorHex();

    public string GetPinkRushName()
        => MakeModAware(ActiveManifest.EnhancementOverrides?.PinkRushName ?? "PINK RUSH!");

    public string GetPinkRushDescription()
        => MakeModAware(ActiveManifest.EnhancementOverrides?.PinkRushDescription ?? "3x XP for 60 seconds!");

    public string[] GetPhrases(string category)
    {
        if (string.IsNullOrEmpty(category)) return Array.Empty<string>();

        var phrases = ActiveManifest.Phrases;
        if (phrases != null && phrases.TryGetValue(category, out var pool))
            return pool ?? Array.Empty<string>();

        return category.ToLowerInvariant() switch
        {
            "idle" => new[] { "Good girl~" },
            "thinking" => new[] { "Thinking..." },
            "bubblecountmercy" => new[] { "Aww, try again~" },
            _ => Array.Empty<string>()
        };
    }

    public string GetAttentionCheckFailMessage()
        => MakeModAware(ActiveManifest.Messages?.AttentionCheckFail ?? "DUMB BAMBI!\nTRY AGAIN");

    public string GetAttentionCheckMercyMessage()
        => MakeModAware(ActiveManifest.Messages?.AttentionCheckMercy ?? "BAMBI GETS MERCY");

    private static ModIdentity? CloneIdentity(ModIdentity? source)
    {
        if (source == null) return null;
        return new ModIdentity
        {
            CompanionName = source.CompanionName,
            UserTerm = source.UserTerm,
            ModeDisplayName = source.ModeDisplayName,
            TalkToLabel = source.TalkToLabel,
            TakeoverLabel = source.TakeoverLabel,
            Affirmation = source.Affirmation,
            RankSubject = source.RankSubject
        };
    }

    private static ModTriggers? CloneTriggers(ModTriggers? source)
    {
        if (source == null) return null;
        return new ModTriggers
        {
            Freeze = source.Freeze,
            Reset = source.Reset,
            CumAndCollapse = source.CumAndCollapse,
            AutonomyOn = source.AutonomyOn
        };
    }

    private static ModMessages? CloneMessages(ModMessages? source)
    {
        if (source == null) return null;
        return new ModMessages
        {
            AttentionCheckFail = source.AttentionCheckFail,
            AttentionCheckMercy = source.AttentionCheckMercy,
            BubbleCountRetry = source.BubbleCountRetry
        };
    }

    private static ModBrowser? CloneBrowser(ModBrowser? source)
    {
        if (source == null) return null;
        var clone = new ModBrowser
        {
            DefaultUrl = source.DefaultUrl,
            ShowBambiCloudOption = source.ShowBambiCloudOption
        };
        if (source.DefaultVideoLinks != null)
            clone.DefaultVideoLinks = new Dictionary<string, string>(source.DefaultVideoLinks);
        return clone;
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
            File.Copy(file, Path.Combine(destinationDir, Path.GetFileName(file)), overwrite: true);
        foreach (var dir in Directory.GetDirectories(sourceDir))
            CopyDirectory(dir, Path.Combine(destinationDir, Path.GetFileName(dir)));
    }

    #endregion

    #region Bundled built-in mod extraction

    private static readonly (string RelativePath, string BuiltInId)[] BundledBuiltInMods =
    {
        ("DroneMod/drone-mode.ccpmod", BuiltInMods.DronificationId),
    };

    private static readonly (string RelativePath, string BuiltInId, ModManifest Manifest)[] BundledResourceMods =
    {
        ("LockedMod/locked-resources.ccpmod", BuiltInMods.LockedId, BuiltInMods.Locked),
    };

    private void ExtractBundledBuiltInMods()
    {
        var builtInRoot = Path.Combine(_environment.UserDataPath, "builtin_mods");
        Directory.CreateDirectory(builtInRoot);

        foreach (var (relativePath, builtInId) in BundledBuiltInMods)
        {
            try
            {
                var bundledPath = Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory,
                    relativePath.Replace('/', Path.DirectorySeparatorChar));

                if (!File.Exists(bundledPath))
                {
                    _logger?.LogWarning("Bundled built-in mod missing on disk: {Path}", bundledPath);
                    continue;
                }

                var extractDir = Path.Combine(builtInRoot, builtInId);
                var manifestPath = Path.Combine(extractDir, "mod.json");

                var needsExtract = !File.Exists(manifestPath)
                    || File.GetLastWriteTimeUtc(bundledPath) > File.GetLastWriteTimeUtc(manifestPath);

                if (needsExtract)
                {
                    if (Directory.Exists(extractDir))
                        Directory.Delete(extractDir, recursive: true);
                    Directory.CreateDirectory(extractDir);
                    ZipFile.ExtractToDirectory(bundledPath, extractDir);
                    _logger?.LogInformation("Extracted bundled built-in mod {BuiltInId} from {Path}", builtInId, bundledPath);
                }

                var json = File.ReadAllText(manifestPath);
                var manifest = JsonConvert.DeserializeObject<ModManifest>(json);
                if (manifest == null || string.IsNullOrWhiteSpace(manifest.Id))
                {
                    _logger?.LogWarning("Bundled built-in mod {BuiltInId} has invalid mod.json", builtInId);
                    continue;
                }

                // Sanitize shipped built-ins too (defense-in-depth even though we ship them).
                // Mirrors WPF ExtractBundledBuiltInMods; runs before the ID force-stamp.
                var sanitizeError = SanitizeManifest(manifest);
                if (sanitizeError != null)
                {
                    _logger?.LogWarning("Bundled built-in mod {BuiltInId} failed sanitization: {Error}", builtInId, sanitizeError);
                    continue;
                }

                manifest.Id = builtInId;
                var idx = _installedMods.FindIndex(m => m.Id == builtInId);
                var package = new ModPackage(manifest, extractDir, true);
                if (idx >= 0)
                    _installedMods[idx] = package;
                else
                    _installedMods.Add(package);
                _logger?.LogInformation("Registered bundled built-in mod {BuiltInId} from {Path}", builtInId, extractDir);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to extract bundled built-in mod {BuiltInId} (falling back to hardcoded manifest)", builtInId);
            }
        }
    }

    private void ExtractBundledResourceMods()
    {
        var builtInRoot = Path.Combine(_environment.UserDataPath, "builtin_mods");
        Directory.CreateDirectory(builtInRoot);

        foreach (var (relativePath, builtInId, manifest) in BundledResourceMods)
        {
            try
            {
                var bundledPath = Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory,
                    relativePath.Replace('/', Path.DirectorySeparatorChar));

                if (!File.Exists(bundledPath))
                {
                    _logger?.LogWarning("Bundled resource mod missing on disk: {Path}", bundledPath);
                    continue;
                }

                var extractDir = Path.Combine(builtInRoot, builtInId);
                var resourcesDir = Path.Combine(extractDir, "resources");

                var needsExtract = !Directory.Exists(resourcesDir)
                    || File.GetLastWriteTimeUtc(bundledPath) > Directory.GetLastWriteTimeUtc(extractDir);

                if (needsExtract)
                {
                    if (Directory.Exists(extractDir))
                        Directory.Delete(extractDir, recursive: true);
                    Directory.CreateDirectory(extractDir);
                    ZipFile.ExtractToDirectory(bundledPath, extractDir);
                    _logger?.LogInformation("Extracted bundled resource mod {BuiltInId} from {Path}", builtInId, bundledPath);
                }

                var idx = _installedMods.FindIndex(m => m.Id == builtInId);
                var package = new ModPackage(manifest, extractDir, true);
                if (idx >= 0)
                    _installedMods[idx] = package;
                else
                    _installedMods.Add(package);
                _logger?.LogInformation("Registered resource mod {BuiltInId} with assets at {Path}", builtInId, extractDir);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to extract bundled resource mod {BuiltInId} (falling back to baseline assets)", builtInId);
            }
        }
    }

    #endregion

    #region Discovery helpers

    private string GetModsDirectory() => Path.Combine(_environment.UserDataPath, "mods");

    private void EnsureModsDirectoryExists()
    {
        var modsDir = GetModsDirectory();
        if (!Directory.Exists(modsDir))
            Directory.CreateDirectory(modsDir);
    }

    private void RefreshInstalledMods()
    {
        _installedMods.Clear();
        _installedMods.AddRange(_builtInMods);

        var modsDir = GetModsDirectory();
        if (!Directory.Exists(modsDir)) return;

        foreach (var dir in Directory.GetDirectories(modsDir))
        {
            var modJson = Path.Combine(dir, "mod.json");
            if (!File.Exists(modJson)) continue;

            try
            {
                var json = File.ReadAllText(modJson);
                var manifest = JsonConvert.DeserializeObject<ModManifest>(json);
                if (manifest == null || string.IsNullOrWhiteSpace(manifest.Id)) continue;

                // Defense-in-depth: re-validate every disk-loaded mod against the trust
                // boundary (guards against a mod.json tampered after install). Mirrors WPF
                // LoadInstalledMods.
                var sanitizeError = SanitizeManifest(manifest);
                if (sanitizeError != null)
                {
                    _logger?.LogWarning("Mod {ModId} failed re-validation on load: {Error}", manifest.Id, sanitizeError);
                    continue;
                }

                var package = new ModPackage(manifest, dir, false);
                var existingIdx = _installedMods.FindIndex(m => m.Id == package.Id);
                if (existingIdx >= 0)
                    _installedMods[existingIdx] = package; // user mod overrides built-in with the same ID
                else
                    _installedMods.Add(package);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to load user mod from {Path}", dir);
            }
        }
    }

    private ModPackage? ResolveMod(string? modId)
    {
        if (string.IsNullOrWhiteSpace(modId)) return null;
        return _installedMods.FirstOrDefault(m => m.Id == modId)
            ?? _builtInMods.FirstOrDefault(m => m.Id == modId);
    }

    private static bool IsValidModId(string id)
    {
        if (id.StartsWith("builtin-", StringComparison.OrdinalIgnoreCase)) return false;
        return Regex.IsMatch(id, "^[a-z0-9\\-]+$");
    }

    #endregion

    #region Manifest sanitization (security trust boundary)

    /// <summary>
    /// Validates and sanitizes a mod manifest against the security trust boundary.
    /// Mirrors WPF <c>ModService.SanitizeManifest</c>: caps field lengths, validates
    /// <c>#RRGGBB</c> theme colors, enforces HTTPS-only browser/video URLs, strips
    /// control + bidi-override characters from user-facing strings, applies per-collection
    /// count caps, clamps numeric bounds with a finite-double guard, and validates
    /// avatar-set ranges (1–7 / custom ≥8). Returns <c>null</c> when acceptable, or an
    /// error message describing the first rejection (clamps mutate the manifest in place).
    /// </summary>
    private static string? SanitizeManifest(ModManifest manifest)
    {
        // --- Field length caps ---
        if (manifest.Name.Length > 100) return "Mod name is too long (max 100 characters).";
        if (manifest.Id.Length > 50) return "Mod ID is too long (max 50 characters).";
        if (manifest.Author.Length > 100) return "Author name is too long (max 100 characters).";
        if (manifest.Description?.Length > 1000) manifest.Description = manifest.Description[..1000];

        // --- Theme color validation (#RRGGBB) ---
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
            if (!Uri.TryCreate(manifest.Browser.DefaultUrl, UriKind.Absolute, out var browserUri)
                || browserUri.Scheme != "https")
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
                if (!Uri.TryCreate(kvp.Value, UriKind.Absolute, out var linkUri) || linkUri.Scheme != "https")
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

                if (string.IsNullOrWhiteSpace(key)) continue;

                if (key.Length > 200) return $"Text replacement key is too long (max 200): '{key[..30]}...'";
                if (val.Length > 500) return $"Text replacement value is too long (max 500): '{key}'";

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
            if (manifest.Identity.Affirmation?.Length > 200) manifest.Identity.Affirmation = manifest.Identity.Affirmation[..200];
            if (manifest.Identity.RankSubject?.Length > 200) manifest.Identity.RankSubject = manifest.Identity.RankSubject[..200];
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

        // --- Supported avatar sets sanitization (only valid set numbers 1-7) ---
        if (manifest.SupportedAvatarSets != null)
        {
            if (manifest.SupportedAvatarSets.Count > 20)
                return "Too many supported avatar sets (max 20).";
            manifest.SupportedAvatarSets = manifest.SupportedAvatarSets.Where(s => s >= 1 && s <= 7).Distinct().ToList();
        }

        // --- Custom avatar sets sanitization (set numbers >= 8) ---
        if (manifest.CustomAvatarSets != null)
        {
            if (manifest.CustomAvatarSets.Count > 20)
                return "Too many custom avatar sets (max 20).";
            var seenSetNums = new HashSet<int>();
            foreach (var cs in manifest.CustomAvatarSets)
            {
                if (cs.SetNumber < 8) return $"Custom avatar set number must be 8 or higher (got {cs.SetNumber}).";
                if (!seenSetNums.Add(cs.SetNumber)) return $"Duplicate custom avatar set number: {cs.SetNumber}.";
                if (cs.UnlockLevel < 1 || cs.UnlockLevel > 9999) return "Custom avatar set unlock level must be 1-9999.";
                if (cs.Label.Length > 100) cs.Label = cs.Label[..100];
            }
        }

        // --- Tube layout sanitization (finite-double guard + clamps) ---
        if (manifest.TubeLayout != null)
        {
            manifest.TubeLayout.AvatarOffsetX = Math.Clamp(manifest.TubeLayout.AvatarOffsetX, -1000, 1000);
            manifest.TubeLayout.AvatarDetachedOffsetX = Math.Clamp(manifest.TubeLayout.AvatarDetachedOffsetX, -1000, 1000);
            manifest.TubeLayout.AvatarOffsetY = Math.Clamp(manifest.TubeLayout.AvatarOffsetY, -500, 500);
            manifest.TubeLayout.AvatarDetachedOffsetY = Math.Clamp(manifest.TubeLayout.AvatarDetachedOffsetY, -500, 500);
            if (manifest.TubeLayout.AvatarScale.HasValue)
            {
                var scale = manifest.TubeLayout.AvatarScale.Value;
                if (double.IsNaN(scale) || double.IsInfinity(scale))
                    return "Avatar scale must be a finite number.";
                manifest.TubeLayout.AvatarScale = Math.Clamp(scale, 0.1, 3.0);
            }
        }

        // --- Enhancement overrides sanitization ---
        if (manifest.EnhancementOverrides != null)
        {
            var eo = manifest.EnhancementOverrides;
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

    /// <summary>
    /// Strips control characters (except newline, carriage-return and tab) plus Unicode
    /// bidirectional-override / directional-formatting characters (Trojan-Source vectors)
    /// from a user-facing string. Mirrors the WPF <c>ModService.StripControlChars</c> strip
    /// set, hardened with bidi-override removal.
    /// </summary>
    private static string StripControlChars(string input)
    {
        var sb = new System.Text.StringBuilder(input.Length);
        foreach (var c in input)
        {
            if (char.IsControl(c) && c != '\n' && c != '\r' && c != '\t')
                continue;
            // Drop bidirectional override / directional-isolate formatting characters.
            if (c is '\u202A' or '\u202B' or '\u202C' or '\u202D' or '\u202E'
                or '\u2066' or '\u2067' or '\u2068' or '\u2069'
                or '\u200E' or '\u200F')
                continue;
            sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>
    /// Use-time defense-in-depth: returns a copy of <paramref name="source"/> keeping only
    /// entries whose URL is an absolute HTTPS link, dropping <c>javascript:</c>/<c>file:</c>/
    /// <c>data:</c>/<c>http:</c> values that could slip past install-time validation.
    /// </summary>
    private static Dictionary<string, string> FilterHttpsVideoLinks(IReadOnlyDictionary<string, string> source)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in source)
        {
            if (string.IsNullOrEmpty(kv.Value)) continue;
            if (Uri.TryCreate(kv.Value, UriKind.Absolute, out var uri) && uri.Scheme == "https")
                result[kv.Key] = kv.Value;
        }
        return result;
    }

    #endregion
}
