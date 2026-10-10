using System;
using System.Collections.Generic;
using System.ComponentModel;
using Newtonsoft.Json;

namespace ConditioningControlPanel.Models
{
    /// <summary>
    /// A content pack the server (or the built-in list) offers. PORTED from WPF 7.1.5
    /// Models/ContentPack.cs minus the BitmapImage preview list: the decoded previews are a head
    /// concern (CCP.Avalonia PackCardViewModel), so Core keeps only the wire shape and the card state.
    /// The button and size strings are English on WPF too.
    /// </summary>
    public class ContentPack : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        protected void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public string Author { get; set; } = "";
        public string PreviewImageUrl { get; set; } = "";

        [JsonProperty("previewUrls")]
        public List<string> PreviewUrls { get; set; } = new();

        public string DownloadUrl { get; set; } = "";
        public string? PatreonUrl { get; set; }
        public string? UpgradeUrl { get; set; }

        [JsonProperty("externalUrl")]
        public string? ExternalUrl { get; set; }

        [JsonProperty("isExternal")]
        public bool IsExternalFlag { get; set; }

        public bool IsExternal => IsExternalFlag || !string.IsNullOrEmpty(ExternalUrl);
        public bool ShowExternalButtons => IsExternal && !IsDownloaded;

        public string Version { get; set; } = "1.0.0";
        public int ImageCount { get; set; }
        public int VideoCount { get; set; }
        public long SizeBytes { get; set; }
        public DateTime CreatedAt { get; set; }
        public string LocalPath { get; set; } = "";

        private bool _isDownloaded;
        public bool IsDownloaded
        {
            get => _isDownloaded;
            set { if (_isDownloaded == value) return; _isDownloaded = value; Raise(nameof(IsDownloaded)); Raise(nameof(DownloadButtonText)); Raise(nameof(ShowExternalButtons)); }
        }

        private bool _isActive;
        public bool IsActive
        {
            get => _isActive;
            set { if (_isActive == value) return; _isActive = value; Raise(nameof(IsActive)); Raise(nameof(ActivateButtonText)); }
        }

        private bool _isDownloading;
        public bool IsDownloading
        {
            get => _isDownloading;
            set { if (_isDownloading == value) return; _isDownloading = value; Raise(nameof(IsDownloading)); Raise(nameof(IsNotDownloading)); Raise(nameof(DownloadButtonText)); }
        }

        private double _downloadProgress;
        public double DownloadProgress
        {
            get => _downloadProgress;
            set { if (_downloadProgress.Equals(value)) return; _downloadProgress = value; Raise(nameof(DownloadProgress)); Raise(nameof(DownloadButtonText)); }
        }

        public bool IsNotDownloading => !IsDownloading;
        public bool HasPatreon => !string.IsNullOrEmpty(PatreonUrl);
        public bool HasUpgrade => !string.IsNullOrEmpty(UpgradeUrl);

        public string DownloadButtonText
        {
            get
            {
                if (IsDownloading)
                    return DownloadProgress >= 100 ? "Installing..." : $"Downloading... {DownloadProgress:F0}%";
                if (IsDownloaded) return "Uninstall";
                if (IsExternal) return "Download";
                return "Install";
            }
        }

        public string ActivateButtonText => IsActive ? "Deactivate" : "Activate";

        public string SizeDisplay
        {
            get
            {
                if (SizeBytes < 1024) return $"{SizeBytes} B";
                if (SizeBytes < 1024 * 1024) return $"{SizeBytes / 1024.0:F1} KB";
                if (SizeBytes < 1024 * 1024 * 1024) return $"{SizeBytes / (1024.0 * 1024):F1} MB";
                return $"{SizeBytes / (1024.0 * 1024 * 1024):F2} GB";
            }
        }
    }

    /// <summary>The server's <c>/packs/manifest</c> body.</summary>
    public class PacksManifest
    {
        public string Version { get; set; } = "1.0";
        public List<ContentPack> Packs { get; set; } = new();
    }
}
