using System;

namespace ConditioningControlPanel.Models;

/// <summary>
/// Information about an available application update
/// </summary>
public class UpdateInfo
{
    public string Version { get; set; } = "";
    public string ReleaseNotes { get; set; } = "";
    public long FileSizeBytes { get; set; }
    public DateTime ReleaseDate { get; set; }
    public bool IsNewer { get; set; }

    /// <summary>
    /// True if this update was detected via GitHub API fallback (Velopack failed)
    /// </summary>
    public bool IsGitHubFallback { get; set; }

    /// <summary>
    /// Gets the file size formatted for display (e.g., "305.2 MB")
    /// </summary>
    public string FormattedFileSize
    {
        get
        {
            if (FileSizeBytes >= 1024 * 1024 * 1024)
                return $"{FileSizeBytes / (1024.0 * 1024.0 * 1024.0):F1} GB";
            if (FileSizeBytes >= 1024 * 1024)
                return $"{FileSizeBytes / (1024.0 * 1024.0):F1} MB";
            if (FileSizeBytes >= 1024)
                return $"{FileSizeBytes / 1024.0:F1} KB";
            return $"{FileSizeBytes} bytes";
        }
    }
}
