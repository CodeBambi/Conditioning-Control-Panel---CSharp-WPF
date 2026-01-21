using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media.Imaging;
using Newtonsoft.Json;

namespace ConditioningControlPanel.Models
{
    /// <summary>
    /// Represents a downloadable content pack from community creators.
    /// </summary>
    public class ContentPack : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        // Preview images for rotating thumbnail (cached from installed pack)
        private List<BitmapImage> _previewImages = new();
        public List<BitmapImage> PreviewImages
        {
            get => _previewImages;
            set { _previewImages = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasPreviewImages)); OnPropertyChanged(nameof(HasAnyPreview)); }
        }

        // Current preview image index for rotation
        private int _currentPreviewIndex;
        public int CurrentPreviewIndex
        {
            get => _currentPreviewIndex;
            set
            {
                _currentPreviewIndex = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CurrentPreviewImage));
            }
        }

        // Currently displayed preview image (bound to UI)
        public BitmapImage? CurrentPreviewImage =>
            _previewImages.Count > 0 && _currentPreviewIndex < _previewImages.Count
                ? _previewImages[_currentPreviewIndex]
                : null;

        public bool HasPreviewImages => _previewImages.Count > 0;
        public bool HasAnyPreview => _previewImages.Count > 0 || !string.IsNullOrEmpty(_previewImageUrl);

        /// <summary>
        /// Advances to the next preview image in rotation.
        /// </summary>
        public void AdvancePreviewImage()
        {
            if (_previewImages.Count == 0) return;
            CurrentPreviewIndex = (_currentPreviewIndex + 1) % _previewImages.Count;
        }

        private string _id = "";
        public string Id
        {
            get => _id;
            set { _id = value; OnPropertyChanged(); }
        }

        private string _name = "";
        public string Name
        {
            get => _name;
            set { _name = value; OnPropertyChanged(); }
        }

        private string _description = "";
        public string Description
        {
            get => _description;
            set { _description = value; OnPropertyChanged(); }
        }

        private string _author = "";
        public string Author
        {
            get => _author;
            set { _author = value; OnPropertyChanged(); }
        }

        private string _previewImageUrl = "";
        public string PreviewImageUrl
        {
            get => _previewImageUrl;
            set { _previewImageUrl = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasAnyPreview)); }
        }

        // Server-provided preview URLs for rotating thumbnails (before download)
        private List<string> _previewUrls = new();
        [JsonProperty("previewUrls")]
        public List<string> PreviewUrls
        {
            get => _previewUrls;
            set { _previewUrls = value ?? new(); OnPropertyChanged(); }
        }

        private string _downloadUrl = "";
        public string DownloadUrl
        {
            get => _downloadUrl;
            set { _downloadUrl = value; OnPropertyChanged(); }
        }

        private string? _patreonUrl;
        public string? PatreonUrl
        {
            get => _patreonUrl;
            set { _patreonUrl = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasPatreon)); }
        }

        private string? _upgradeUrl;
        public string? UpgradeUrl
        {
            get => _upgradeUrl;
            set { _upgradeUrl = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasUpgrade)); }
        }

        private string _version = "1.0.0";
        public string Version
        {
            get => _version;
            set { _version = value; OnPropertyChanged(); }
        }

        private int _imageCount;
        public int ImageCount
        {
            get => _imageCount;
            set { _imageCount = value; OnPropertyChanged(); }
        }

        private int _videoCount;
        public int VideoCount
        {
            get => _videoCount;
            set { _videoCount = value; OnPropertyChanged(); }
        }

        private long _sizeBytes;
        public long SizeBytes
        {
            get => _sizeBytes;
            set { _sizeBytes = value; OnPropertyChanged(); OnPropertyChanged(nameof(SizeDisplay)); }
        }

        private DateTime _createdAt = DateTime.Now;
        public DateTime CreatedAt
        {
            get => _createdAt;
            set { _createdAt = value; OnPropertyChanged(); }
        }

        private bool _isDownloaded;
        public bool IsDownloaded
        {
            get => _isDownloaded;
            set { _isDownloaded = value; OnPropertyChanged(); OnPropertyChanged(nameof(DownloadButtonText)); }
        }

        private bool _isActive;
        public bool IsActive
        {
            get => _isActive;
            set { _isActive = value; OnPropertyChanged(); OnPropertyChanged(nameof(DownloadButtonText)); }
        }

        private bool _isDownloading;
        public bool IsDownloading
        {
            get => _isDownloading;
            set { _isDownloading = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsNotDownloading)); OnPropertyChanged(nameof(DownloadButtonText)); }
        }

        private double _downloadProgress;
        public double DownloadProgress
        {
            get => _downloadProgress;
            set { _downloadProgress = value; OnPropertyChanged(); OnPropertyChanged(nameof(DownloadButtonText)); }
        }

        private string _localPath = "";
        public string LocalPath
        {
            get => _localPath;
            set { _localPath = value; OnPropertyChanged(); }
        }

        // Computed properties for UI binding
        public bool IsNotDownloading => !IsDownloading;
        public bool HasPatreon => !string.IsNullOrEmpty(PatreonUrl);
        public bool HasUpgrade => !string.IsNullOrEmpty(UpgradeUrl);

        public string DownloadButtonText
        {
            get
            {
                if (IsDownloading)
                {
                    if (DownloadProgress >= 100)
                        return "Installing...";
                    return $"Downloading... {DownloadProgress:F0}%";
                }
                if (IsDownloaded) return "Uninstall";
                return "Download";
            }
        }

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
}
