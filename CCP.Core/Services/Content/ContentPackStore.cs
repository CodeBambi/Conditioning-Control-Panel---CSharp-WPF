using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Models;
using Newtonsoft.Json;
using Serilog;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// The installed half of WPF <c>ContentPackService</c> (Services/Content/ContentPackService.cs),
    /// cross-platform: discovery of encrypted creator packs under
    /// <c>&lt;EffectiveAssets&gt;/.packs/&lt;guid&gt;/</c>, activation state exactly as WPF keeps it
    /// (<see cref="AppSettings.InstalledPackIds"/>, <see cref="AppSettings.ActivePackIds"/>,
    /// <see cref="AppSettings.PackGuidMap"/>, per-file opt-outs <c>pack:&lt;id&gt;/&lt;OriginalName&gt;</c>
    /// in <see cref="AppSettings.DisabledAssetPaths"/>), decrypt to stream / to temp file, and the
    /// active image / video pools the flash, video, bubble and asset surfaces draw from.
    ///
    /// <para>The crypto is <see cref="PackEncryptionService"/> (AES-256-CBC, key from
    /// MachineName + UserName via PBKDF2; no DPAPI), so it runs as-is on Linux. It is reached
    /// through two delegates so tests can stand a fake in.</para>
    ///
    /// <para>IDENTITY RULE (AGENTS.md): every decrypt-to-temp mints a FRESH
    /// <c>ccp_temp_&lt;guid&gt;</c> path, so a path string never identifies a picture. Dedupe on
    /// <see cref="SourceKey"/> (WPF FlashService.PackEntryKey) or ask <see cref="TryGetSourceKey"/>
    /// for a temp path this store minted.</para>
    ///
    /// <para>Encrypted packs hold images and videos only. Pack sounds and the built-in
    /// .ccpmod packs (mod-*, infection-control, locked-resources) are release content:
    /// <see cref="ReleaseContentService"/>. Download / purchase (server routes, rate limits,
    /// install from zip) stays in the WPF head for now.</para>
    /// </summary>
    public sealed class ContentPackStore
    {
        /// <summary>The live store a head registered, or null when packs are not wired.</summary>
        public static volatile ContentPackStore? Current;

        public const string ImageType = "image";
        public const string VideoType = "video";
        public const string ManifestFileName = ".manifest.enc";
        public const string ContentFolderName = "content";

        private readonly Func<string> _assetsRoot;
        private readonly Func<AppSettings> _settings;
        private readonly Action _save;
        private readonly Func<string, string> _loadManifest;
        private readonly Func<string, byte[]> _decryptFile;
        private readonly Func<string>? _tempDir;
        private readonly object _gate = new();
        private readonly Dictionary<string, InstalledPackManifest> _manifests = new(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, string> _tempIdentity = new(StringComparer.OrdinalIgnoreCase);
        private string _packsFolder = "";

        /// <summary>Raised after activation, uninstall or a rescan changed what the pools hold.</summary>
        public event Action? PacksChanged;

        /// <param name="assetsRoot">Effective assets folder (default <see cref="CorePaths.EffectiveAssets"/>).</param>
        /// <param name="settings">Settings source (default <see cref="CoreSettings.Current"/>).</param>
        /// <param name="save">Persists settings (default <see cref="CoreSettings.Save"/>).</param>
        /// <param name="loadManifest">Decrypts a manifest file to JSON (default PackEncryptionService).</param>
        /// <param name="decryptFile">Decrypts a content file to bytes (default PackEncryptionService).</param>
        /// <param name="tempDir">Where decrypts land (default <c>&lt;assets&gt;/.temp</c>, WPF GetMediaTempPath).</param>
        public ContentPackStore(
            Func<string>? assetsRoot = null,
            Func<AppSettings>? settings = null,
            Action? save = null,
            Func<string, string>? loadManifest = null,
            Func<string, byte[]>? decryptFile = null,
            Func<string>? tempDir = null)
        {
            _assetsRoot = assetsRoot ?? (() => CorePaths.EffectiveAssets);
            _settings = settings ?? (() => CoreSettings.Current);
            _save = save ?? (() => CoreSettings.Save());
            _loadManifest = loadManifest ?? PackEncryptionService.LoadEncryptedManifest;
            _decryptFile = decryptFile ?? PackEncryptionService.DecryptFile;
            _tempDir = tempDir;
            Rescan();
        }

        /// <summary><c>&lt;assets&gt;/.packs</c> as of the last <see cref="Rescan"/>.</summary>
        public string PacksFolder => _packsFolder;

        // =========================================================================================
        //  identity
        // =========================================================================================

        /// <summary>The per-file selection key in DisabledAssetPaths (WPF: <c>pack:{id}/{OriginalName}</c>).</summary>
        public static string SelectionKey(string packId, PackFileEntry file) => $"pack:{packId}/{file.OriginalName}";

        /// <summary>
        /// The SOURCE identity of a pack file, stable across decrypts (WPF FlashService.PackEntryKey:
        /// <c>pack:{id}/{ObfuscatedName}</c>, falling back to OriginalName). Dedupe on this, never on a
        /// temp path.
        /// </summary>
        public static string SourceKey(string packId, PackFileEntry? file) =>
            $"pack:{packId}/{(string.IsNullOrEmpty(file?.ObfuscatedName) ? file?.OriginalName : file!.ObfuscatedName)}";

        /// <summary>The source key behind a temp path this store minted, or false for any other path.</summary>
        public bool TryGetSourceKey(string tempPath, out string sourceKey)
        {
            sourceKey = "";
            if (string.IsNullOrEmpty(tempPath)) return false;
            if (_tempIdentity.TryGetValue(Path.GetFullPath(tempPath), out var key)) { sourceKey = key; return true; }
            return false;
        }

        // =========================================================================================
        //  discovery
        // =========================================================================================

        /// <summary>
        /// WPF RefreshPacksPath + ScanAndRegisterOrphanedPacks + LoadInstalledManifests: recompute the
        /// packs folder from the assets root, register any pack on disk that settings do not know
        /// (auto-activated, as WPF does), then reload every installed manifest. Never throws.
        /// </summary>
        public void Rescan()
        {
            string root;
            try { root = _assetsRoot() ?? ""; } catch { root = ""; }
            lock (_gate)
            {
                _packsFolder = string.IsNullOrEmpty(root) ? "" : Path.Combine(root, ".packs");
                _manifests.Clear();
            }
            RegisterOrphans();
            foreach (var id in (_settings().InstalledPackIds ?? new List<string>()).ToList())
                LoadManifest(id);
            PacksChanged?.Invoke();
        }

        private void RegisterOrphans()
        {
            var folder = _packsFolder;
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return;
            var s = _settings();
            var registered = 0;
            try
            {
                foreach (var dir in Directory.GetDirectories(folder))
                {
                    var guid = Path.GetFileName(dir);
                    var manifestPath = Path.Combine(dir, ManifestFileName);
                    if (!File.Exists(manifestPath)) continue;
                    try
                    {
                        var manifest = JsonConvert.DeserializeObject<InstalledPackManifest>(_loadManifest(manifestPath));
                        var packId = manifest?.PackId;
                        if (string.IsNullOrEmpty(packId)) continue;

                        s.InstalledPackIds ??= new List<string>();
                        s.PackGuidMap ??= new Dictionary<string, string>();
                        s.ActivePackIds ??= new List<string>();

                        var known = s.InstalledPackIds.Contains(packId)
                            && s.PackGuidMap.TryGetValue(packId, out var existing)
                            && string.Equals(existing, guid, StringComparison.OrdinalIgnoreCase);
                        if (known) continue;

                        if (!s.InstalledPackIds.Contains(packId)) s.InstalledPackIds.Add(packId);
                        s.PackGuidMap[packId] = guid;
                        if (!s.ActivePackIds.Contains(packId)) s.ActivePackIds.Add(packId);
                        registered++;
                        Log.Information("ContentPacks: registered orphaned pack {PackId} ({Name}) -> {Guid}",
                            packId, manifest?.PackName ?? "Unknown", guid);
                    }
                    catch (Exception ex)
                    {
                        Log.Warning(ex, "ContentPacks: unreadable manifest {Path}", manifestPath);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "ContentPacks: scan failed in {Path}", folder);
            }
            if (registered > 0) SafeSave();
        }

        private InstalledPackManifest? LoadManifest(string packId)
        {
            var path = ManifestPath(packId);
            if (path == null || !File.Exists(path)) return null;
            try
            {
                var manifest = JsonConvert.DeserializeObject<InstalledPackManifest>(_loadManifest(path));
                if (manifest != null) lock (_gate) _manifests[packId] = manifest;
                return manifest;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "ContentPacks: failed to load manifest for {PackId}", packId);
                return null;
            }
        }

        private string? PackFolder(string packId)
        {
            var map = _settings().PackGuidMap;
            if (string.IsNullOrEmpty(_packsFolder) || map == null || !map.TryGetValue(packId, out var guid)
                || string.IsNullOrEmpty(guid)) return null;
            return Path.Combine(_packsFolder, guid);
        }

        private string? ManifestPath(string packId) =>
            PackFolder(packId) is { } f ? Path.Combine(f, ManifestFileName) : null;

        // =========================================================================================
        //  state
        // =========================================================================================

        /// <summary>WPF InstalledPacks: ids whose manifest loaded.</summary>
        public IReadOnlyList<string> InstalledPackIds
        {
            get { lock (_gate) return _manifests.Keys.ToList(); }
        }

        /// <summary>WPF IsPackInstalled: in settings, mapped to a guid, and the manifest is on disk.</summary>
        public bool IsPackInstalled(string packId)
        {
            var s = _settings();
            if (!(s.InstalledPackIds?.Contains(packId) ?? false)) return false;
            var path = ManifestPath(packId);
            return path != null && File.Exists(path);
        }

        /// <summary>WPF IsPackActive: active in settings AND installed.</summary>
        public bool IsPackActive(string packId) =>
            (_settings().ActivePackIds?.Contains(packId) ?? false) && IsPackInstalled(packId);

        /// <summary>WPF GetActivePackIds: the active ids that are actually installed, settings order.</summary>
        public List<string> GetActivePackIds() =>
            (_settings().ActivePackIds?.ToList() ?? new List<string>()).Where(IsPackInstalled).ToList();

        /// <summary>The pack's display name from its manifest, else the id.</summary>
        public string GetPackName(string packId) =>
            Manifest(packId)?.PackName is { Length: > 0 } n ? n : packId;

        public void ActivatePack(string packId)
        {
            var s = _settings();
            s.ActivePackIds ??= new List<string>();
            if (s.ActivePackIds.Contains(packId)) return;
            s.ActivePackIds.Add(packId);
            SafeSave();
            Log.Information("ContentPacks: activated {Id}", packId);
            PacksChanged?.Invoke();
        }

        public void DeactivatePack(string packId)
        {
            var s = _settings();
            if (s.ActivePackIds?.Remove(packId) != true) return;
            SafeSave();
            Log.Information("ContentPacks: deactivated {Id}", packId);
            PacksChanged?.Invoke();
        }

        /// <summary>WPF UninstallPack: deactivate, delete the guid folder, forget the id.</summary>
        public void UninstallPack(string packId)
        {
            var s = _settings();
            s.ActivePackIds?.Remove(packId);
            var folder = PackFolder(packId);
            try
            {
                if (folder != null && Directory.Exists(folder)) Directory.Delete(folder, true);
            }
            catch (Exception ex) { Log.Warning(ex, "ContentPacks: could not delete {Folder}", folder); }
            s.PackGuidMap?.Remove(packId);
            s.InstalledPackIds?.Remove(packId);
            lock (_gate) _manifests.Remove(packId);
            SafeSave();
            Log.Information("ContentPacks: uninstalled {Id}", packId);
            PacksChanged?.Invoke();
        }

        // =========================================================================================
        //  files and pools
        // =========================================================================================

        private InstalledPackManifest? Manifest(string packId)
        {
            lock (_gate)
                if (_manifests.TryGetValue(packId, out var m)) return m;
            return LoadManifest(packId);
        }

        /// <summary>WPF GetPackFiles: every file of an installed pack, optionally one type ("image" / "video").</summary>
        public List<PackFileEntry> GetPackFiles(string packId, string? fileType = null)
        {
            var m = Manifest(packId);
            if (m == null) return new List<PackFileEntry>();
            var files = m.Files.AsEnumerable();
            if (!string.IsNullOrEmpty(fileType)) files = files.Where(f => f.FileType == fileType);
            return files.ToList();
        }

        /// <summary>WPF GetAllActivePackImages: images of every active pack, minus the per-file opt-outs.</summary>
        public List<(string PackId, PackFileEntry File)> GetAllActivePackImages() => ActivePool(ImageType);

        /// <summary>WPF GetAllActivePackVideos: videos of every active pack, minus the per-file opt-outs.</summary>
        public List<(string PackId, PackFileEntry File)> GetAllActivePackVideos() => ActivePool(VideoType);

        private List<(string PackId, PackFileEntry File)> ActivePool(string type)
        {
            var result = new List<(string, PackFileEntry)>();
            var disabled = _settings().DisabledAssetPaths;
            foreach (var packId in GetActivePackIds())
                foreach (var f in GetPackFiles(packId, type))
                {
                    if (disabled != null && disabled.Contains(SelectionKey(packId, f))) continue;
                    result.Add((packId, f));
                }
            return result;
        }

        /// <summary>Encrypted path of a pack file, or null when the pack or file is missing.</summary>
        public string? GetEncryptedPath(string packId, PackFileEntry file)
        {
            var folder = PackFolder(packId);
            if (folder == null || string.IsNullOrEmpty(file.ObfuscatedName)) return null;
            var path = Path.Combine(folder, ContentFolderName, file.ObfuscatedName);
            return File.Exists(path) ? path : null;
        }

        /// <summary>WPF GetPackFileStream: decrypted bytes in memory (thumbnails, image decode). Never writes plaintext.</summary>
        public MemoryStream? GetPackFileStream(string packId, PackFileEntry file)
        {
            try
            {
                var path = GetEncryptedPath(packId, file);
                return path == null ? null : new MemoryStream(_decryptFile(path), writable: false);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "ContentPacks: failed to decrypt {Name}", file.OriginalName);
                return null;
            }
        }

        /// <summary>
        /// WPF GetPackFileTempPath: decrypt to a FRESH <c>ccp_temp_&lt;guid&gt;{ext}</c> under the media
        /// temp folder (for players that need a path). The caller owns the file and deletes it
        /// (<see cref="DeleteTempFile"/>). Identity: <see cref="TryGetSourceKey"/>.
        /// </summary>
        public string? GetPackFileTempPath(string packId, PackFileEntry file)
        {
            try
            {
                var encrypted = GetEncryptedPath(packId, file);
                if (encrypted == null) return null;
                var temp = Path.GetFullPath(Path.Combine(MediaTempDir(), $"ccp_temp_{Guid.NewGuid():N}{file.Extension}"));
                File.WriteAllBytes(temp, _decryptFile(encrypted));
                _tempIdentity[temp] = SourceKey(packId, file);
                return temp;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "ContentPacks: failed to create temp file for {Name}", file.OriginalName);
                return null;
            }
        }

        /// <summary>Delete a decrypt this store minted and forget its identity. Never throws.</summary>
        public void DeleteTempFile(string tempPath)
        {
            if (string.IsNullOrEmpty(tempPath)) return;
            try
            {
                var full = Path.GetFullPath(tempPath);
                _tempIdentity.TryRemove(full, out _);
                if (File.Exists(full)) File.Delete(full);
            }
            catch (Exception ex) { Log.Debug("ContentPacks: temp delete failed {Path}: {E}", tempPath, ex.Message); }
        }

        /// <summary>Delete every decrypt still on record (shutdown). Files in use stay.</summary>
        public void CleanupTempFiles()
        {
            foreach (var path in _tempIdentity.Keys.ToList()) DeleteTempFile(path);
        }

        private string MediaTempDir()
        {
            if (_tempDir != null) { var d = _tempDir(); Directory.CreateDirectory(d); return d; }
            try
            {
                var root = _assetsRoot();
                if (!string.IsNullOrEmpty(root))
                {
                    var dir = Path.Combine(root, ".temp");
                    Directory.CreateDirectory(dir);
                    return dir;
                }
            }
            catch (Exception ex) { Log.Debug("ContentPacks: assets temp unavailable: {E}", ex.Message); }
            return Path.GetTempPath();
        }

        private void SafeSave()
        {
            try { _save(); } catch (Exception ex) { Log.Warning(ex, "ContentPacks: settings save failed"); }
        }
    }
}
