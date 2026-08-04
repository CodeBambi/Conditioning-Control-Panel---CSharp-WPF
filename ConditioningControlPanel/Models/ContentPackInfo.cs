using System.Collections.Generic;
using Newtonsoft.Json;

namespace ConditioningControlPanel.Models
{
    /// <summary>
    /// One entry of <c>content-manifest.json</c>, the asset uploaded alongside the release-hosted
    /// content packs on each <c>vX.Y.0</c> GitHub release.
    ///
    /// These are PLAIN zips of version-stable payload (audio, mod media) that used to ship inside
    /// the installer — deliberately not the encrypted <see cref="ContentPack"/> pipeline (that AES is
    /// machine-bound and renames files, which breaks every direct-path consumer).
    /// </summary>
    public class ContentPackInfo
    {
        /// <summary>Stable pack id, e.g. <c>audio-base</c>, <c>mod-bambi</c>.</summary>
        [JsonProperty("id")]
        public string Id { get; set; } = "";

        /// <summary>Release asset file name, e.g. <c>audio-base.zip</c>. Appended to the cycle base URL.</summary>
        [JsonProperty("file")]
        public string File { get; set; } = "";

        /// <summary>Uncompressed-on-the-wire size of the zip asset, for UI size badges and progress.</summary>
        [JsonProperty("sizeBytes")]
        public long SizeBytes { get; set; }

        /// <summary>Lowercase hex SHA-256 of the zip asset. Verified after download.</summary>
        [JsonProperty("sha256")]
        public string Sha256 { get; set; } = "";

        /// <summary>
        /// Bumped only when the pack's bytes actually change — NOT per app release. A user who
        /// already holds the 6.6-cycle packs and updates into 6.7 re-downloads nothing unless this
        /// moved.
        /// </summary>
        [JsonProperty("contentVersion")]
        public int ContentVersion { get; set; } = 1;

        /// <summary>
        /// Where the zip's tree merges under <c>%LOCALAPPDATA%\ConditioningControlPanel\content\</c>,
        /// mirroring the install-dir layout (e.g. <c>Resources\sounds\flashes_audio</c>). Empty means
        /// the zip already carries the full relative path and merges at the content root.
        /// </summary>
        [JsonProperty("targetRoot")]
        public string TargetRoot { get; set; } = "";

        /// <summary>
        /// In-zip entry paths of any <c>.ccpmod</c> archives this pack carries, e.g.
        /// <c>packs/drone-mode.ccpmod</c> (plan §8.5). They extract like every other entry — relative
        /// to the content root — but are consumed by <c>ModService</c> rather than read in place, so
        /// the manifest lists them explicitly instead of leaving the client to guess. Empty for packs
        /// that carry only loose media.
        /// </summary>
        [JsonProperty("ccpmods")]
        public List<string> Ccpmods { get; set; } = new();
    }

    /// <summary>
    /// Envelope form of <c>content-manifest.json</c>. The file may also be a bare JSON array of
    /// <see cref="ContentPackInfo"/>; the service accepts both.
    /// </summary>
    public class ReleaseContentManifest
    {
        [JsonProperty("schemaVersion")]
        public int SchemaVersion { get; set; } = 1;

        /// <summary>Cycle tag the manifest was built for, e.g. <c>v6.6.0</c>. Informational only.</summary>
        [JsonProperty("cycle")]
        public string? Cycle { get; set; }

        [JsonProperty("packs")]
        public List<ContentPackInfo> Packs { get; set; } = new();
    }

    /// <summary>
    /// Persisted proof that a release-hosted pack is installed locally, keyed by pack id in
    /// <c>AppSettings.InstalledContentPacks</c>. A set, not a bool — the stamp is what lets us tell
    /// "installed and current" from "installed but the bytes moved".
    /// </summary>
    public class InstalledPackStamp
    {
        [JsonProperty("contentVersion")]
        public int ContentVersion { get; set; }

        [JsonProperty("sha256")]
        public string Sha256 { get; set; } = "";
    }
}
