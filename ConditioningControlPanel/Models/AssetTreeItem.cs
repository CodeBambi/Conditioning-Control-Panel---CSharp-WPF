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

        private bool? _isChecked = true;
        public bool? IsChecked
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
        /// Update this folder's check state based on children.
        /// Returns true if all checked, false if none, null if mixed.
        /// </summary>
        public void UpdateCheckState()
        {
            if (Children.Count == 0 && FileCount == 0)
            {
                IsChecked = true;
                return;
            }

            var childStates = Children.Select(c => c.IsChecked).ToList();

            // Also consider file counts if any
            if (FileCount > 0)
            {
                if (CheckedFileCount == FileCount)
                    childStates.Add(true);
                else if (CheckedFileCount == 0)
                    childStates.Add(false);
                else
                    childStates.Add(null);
            }

            if (childStates.Count == 0)
            {
                IsChecked = true;
            }
            else if (childStates.All(s => s == true))
            {
                IsChecked = true;
            }
            else if (childStates.All(s => s == false))
            {
                IsChecked = false;
            }
            else
            {
                IsChecked = null; // Mixed state
            }

            // Propagate to parent
            Parent?.UpdateCheckState();
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
