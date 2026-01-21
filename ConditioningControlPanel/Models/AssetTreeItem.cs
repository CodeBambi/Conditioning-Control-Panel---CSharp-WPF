using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;

namespace ConditioningControlPanel.Models
{
    /// <summary>
    /// Represents a folder in the asset tree view with tri-state checkbox support.
    /// </summary>
    public class AssetTreeItem : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

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
            set { _fullPath = value; OnPropertyChanged(); }
        }

        private bool _isChecked = true;
        public bool IsChecked
        {
            get => _isChecked;
            set
            {
                if (_isChecked != value)
                {
                    _isChecked = value;
                    OnPropertyChanged();
                }
            }
        }

        private int _fileCount;
        public int FileCount
        {
            get => _fileCount;
            set { _fileCount = value; OnPropertyChanged(); OnPropertyChanged(nameof(FileCountDisplay)); }
        }

        private int _checkedFileCount;
        public int CheckedFileCount
        {
            get => _checkedFileCount;
            set { _checkedFileCount = value; OnPropertyChanged(); }
        }

        public string FileCountDisplay => FileCount > 0 ? $"({FileCount} files)" : "";

        public ObservableCollection<AssetTreeItem> Children { get; } = new();

        public AssetTreeItem? Parent { get; set; }

        private bool _isExpanded;
        public bool IsExpanded
        {
            get => _isExpanded;
            set { _isExpanded = value; OnPropertyChanged(); }
        }

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Whether this is a virtual folder representing a content pack.
        /// </summary>
        public bool IsPackFolder { get; set; }

        /// <summary>
        /// The pack ID if this is a pack folder (images or videos subfolder).
        /// </summary>
        public string? PackId { get; set; }

        /// <summary>
        /// The file type filter for pack folders ("image" or "video").
        /// </summary>
        public string? PackFileType { get; set; }

        /// <summary>
        /// Update this folder's check state based on file counts.
        /// Checked if any files are enabled, unchecked if all disabled.
        /// </summary>
        public void UpdateCheckState()
        {
            // Calculate total checked files including children
            var totalChecked = GetTotalCheckedFileCount();

            // Checked if at least some files are enabled
            IsChecked = totalChecked > 0;
        }

        /// <summary>
        /// Update this folder's check state based on children's states, then propagate to parent.
        /// Used when a child folder changes to update the visual state up the tree.
        /// </summary>
        public void UpdateCheckStateFromChildren()
        {
            // Recalculate based on children
            var totalChecked = GetTotalCheckedFileCount();
            IsChecked = totalChecked > 0;

            // Propagate to parent
            Parent?.UpdateCheckStateFromChildren();
        }

        /// <summary>
        /// Set checked state for this folder and all children recursively.
        /// </summary>
        public void SetCheckedRecursive(bool isChecked)
        {
            IsChecked = isChecked;
            CheckedFileCount = isChecked ? FileCount : 0;

            foreach (var child in Children)
            {
                child.SetCheckedRecursive(isChecked);
            }
        }

        /// <summary>
        /// Get total file count including all subfolders.
        /// </summary>
        public int GetTotalFileCount()
        {
            return FileCount + Children.Sum(c => c.GetTotalFileCount());
        }

        /// <summary>
        /// Get total checked file count including all subfolders.
        /// </summary>
        public int GetTotalCheckedFileCount()
        {
            return CheckedFileCount + Children.Sum(c => c.GetTotalCheckedFileCount());
        }
    }
}
