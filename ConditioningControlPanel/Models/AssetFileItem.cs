using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ConditioningControlPanel.Models
{
    /// <summary>
    /// Represents a file in the thumbnail preview grid with checkbox support.
    /// </summary>
    public class AssetFileItem : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        private static readonly string[] VideoExtensions = { ".mp4", ".avi", ".mkv", ".mov", ".wmv", ".webm", ".m4v", ".flv" };
        private static readonly string[] ImageExtensions = { ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp", ".tiff", ".tif" };

        private string _name = "";
        public string Name
        {
            get => _name;
            set { _name = value; OnPropertyChanged(); }
        }

        private string _fullPath = "";
        public string FullPath
        {
            get => _fullPath;
            set
            {
                _fullPath = value;
                OnPropertyChanged();
                // Update derived properties
                Name = Path.GetFileName(value);
                Extension = Path.GetExtension(value).ToLowerInvariant();
                IsVideo = IsVideoExtension(Extension);
            }
        }

        private string _relativePath = "";
        public string RelativePath
        {
            get => _relativePath;
            set { _relativePath = value; OnPropertyChanged(); }
        }

        private string _extension = "";
        public string Extension
        {
            get => _extension;
            set { _extension = value; OnPropertyChanged(); OnPropertyChanged(nameof(TypeIcon)); }
        }

        private bool _isChecked = true;
        public bool IsChecked
        {
            get => _isChecked;
            set { _isChecked = value; OnPropertyChanged(); }
        }

        private bool _isVideo;
        public bool IsVideo
        {
            get => _isVideo;
            set { _isVideo = value; OnPropertyChanged(); OnPropertyChanged(nameof(TypeIcon)); }
        }

        private ImageSource? _thumbnail;
        public ImageSource? Thumbnail
        {
            get => _thumbnail;
            set { _thumbnail = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasThumbnail)); }
        }

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; OnPropertyChanged(); }
        }

        private bool _isLoadingThumbnail;
        public bool IsLoadingThumbnail
        {
            get => _isLoadingThumbnail;
            set { _isLoadingThumbnail = value; OnPropertyChanged(); }
        }

        private long _sizeBytes;
        public long SizeBytes
        {
            get => _sizeBytes;
            set { _sizeBytes = value; OnPropertyChanged(); OnPropertyChanged(nameof(SizeDisplay)); }
        }

        /// <summary>
        /// Whether this file is from an encrypted content pack.
        /// </summary>
        public bool IsPackFile { get; set; }

        /// <summary>
        /// The pack ID if this is a pack file.
        /// </summary>
        public string? PackId { get; set; }

        /// <summary>
        /// Reference to the pack file entry for encrypted file access.
        /// </summary>
        public Services.PackFileEntry? PackFileEntry { get; set; }

        // Computed properties
        public bool HasThumbnail => Thumbnail != null;

        public string TypeIcon => IsVideo ? "🎬" : "🖼";

        public string SizeDisplay
        {
            get
            {
                if (SizeBytes < 1024) return $"{SizeBytes} B";
                if (SizeBytes < 1024 * 1024) return $"{SizeBytes / 1024.0:F1} KB";
                return $"{SizeBytes / (1024.0 * 1024):F1} MB";
            }
        }

        private static bool IsVideoExtension(string ext)
        {
            foreach (var videoExt in VideoExtensions)
            {
                if (ext == videoExt) return true;
            }
            return false;
        }

        public static bool IsSupportedExtension(string ext)
        {
            ext = ext.ToLowerInvariant();
            foreach (var e in VideoExtensions)
                if (ext == e) return true;
            foreach (var e in ImageExtensions)
                if (ext == e) return true;
            return false;
        }
    }
}
