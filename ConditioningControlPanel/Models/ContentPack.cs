using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

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
            set { _previewImageUrl = value; OnPropertyChanged(); }
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
            set { _isDownloading = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsNotDownloading)); }
        }

        private double _downloadProgress;
        public double DownloadProgress
        {
            get => _downloadProgress;
            set { _downloadProgress = value; OnPropertyChanged(); }
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
                if (!IsDownloaded) return "Download";
                return IsActive ? "Deactivate" : "Activate";
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
