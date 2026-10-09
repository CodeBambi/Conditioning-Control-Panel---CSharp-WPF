using System;
using System.Collections.Generic;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// Manifest for an installed encrypted content pack (stored encrypted locally as
    /// <c>&lt;packs&gt;/&lt;guid&gt;/.manifest.enc</c>). Moved verbatim from the WPF
    /// ContentPackService so both heads read the same shape.
    /// </summary>
    public class InstalledPackManifest
    {
        public string PackId { get; set; } = "";
        public string PackGuid { get; set; } = "";
        public string PackName { get; set; } = "";
        public DateTime InstalledDate { get; set; }
        public List<PackFileEntry> Files { get; set; } = new();
    }

    /// <summary>
    /// Entry for a file in an installed pack.
    /// </summary>
    public class PackFileEntry
    {
        public string OriginalName { get; set; } = "";
        public string ObfuscatedName { get; set; } = "";
        public string FileType { get; set; } = ""; // "image" or "video"
        public string Extension { get; set; } = "";
    }
}
