# Asset Manager Fix & Preset System Implementation Plan

## PART 1: Fix Current Checkbox Issues

### Root Cause
The checkbox system has **inconsistent data sources**:
- `BuildFolderTree()` uses `ActiveAssetPaths` (legacy whitelist)
- `LoadFolderThumbnails()` uses `DisabledAssetPaths` (current blacklist)
- `CountAssetsRecursive()` uses `ActiveAssetPaths` (wrong)

Since `UseAssetWhitelist = false` by default, the blacklist system (`DisabledAssetPaths`) should be used everywhere.

### Fix 1: Update BuildFolderTree()
**File:** `MainWindow.xaml.cs` around line 8156

**Current (WRONG):**
```csharp
if (App.Settings.Current.UseAssetWhitelist && App.Settings.Current.ActiveAssetPaths.Count > 0)
{
    node.CheckedFileCount = files.Count(f =>
    {
        var relativePath = Path.GetRelativePath(basePath, f);
        return App.Settings.Current.ActiveAssetPaths.Contains(relativePath);
    });
}
else
{
    node.CheckedFileCount = files.Count; // All active by default
}
```

**Fixed:**
```csharp
// Count checked files using blacklist (DisabledAssetPaths)
node.CheckedFileCount = files.Count(f =>
{
    var relativePath = Path.GetRelativePath(basePath, f);
    return !App.Settings.Current.DisabledAssetPaths.Contains(relativePath);
});
```

### Fix 2: Update CountAssetsRecursive()
**File:** `MainWindow.xaml.cs` around line 8599

**Current (WRONG):**
```csharp
var isActive = App.Settings.Current.ActiveAssetPaths.Contains(relativePath);
```

**Fixed:**
```csharp
var isActive = !App.Settings.Current.DisabledAssetPaths.Contains(relativePath);
```

### Fix 3: Simplify to 2-State Checkboxes
**File:** `MainWindow.xaml` line 4644

**Current:**
```xaml
IsThreeState="True"
```

**Change to:**
```xaml
IsThreeState="False"
```

This removes the "mixed" (null) state and keeps only checked/unchecked.

**Also update AssetTreeItem.cs:**
- Change `IsChecked` from `bool?` to `bool`
- Simplify `UpdateCheckState()` to just use true/false

---

## PART 2: Asset Presets System

### Data Model

**New class: `Models/AssetPreset.cs`**
```csharp
public class AssetPreset : INotifyPropertyChanged
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "New Preset";
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime LastUsed { get; set; } = DateTime.Now;

    // The disabled paths for this preset (blacklist)
    public HashSet<string> DisabledAssetPaths { get; set; } = new();

    // Stats for display
    public int EnabledImageCount { get; set; }
    public int EnabledVideoCount { get; set; }
}
```

**Add to AppSettings.cs:**
```csharp
// Asset presets
private List<AssetPreset> _assetPresets = new();
[JsonProperty]
public List<AssetPreset> AssetPresets
{
    get => _assetPresets;
    set { _assetPresets = value ?? new(); OnPropertyChanged(); }
}

private string? _currentAssetPresetId;
[JsonProperty]
public string? CurrentAssetPresetId
{
    get => _currentAssetPresetId;
    set { _currentAssetPresetId = value; OnPropertyChanged(); }
}
```

### UI Changes

**Add to MainWindow.xaml (above the TreeView, in Assets tab):**
```xaml
<!-- Asset Preset Selector -->
<Grid Margin="0,0,0,10">
    <Grid.ColumnDefinitions>
        <ColumnDefinition Width="Auto"/>
        <ColumnDefinition Width="*"/>
        <ColumnDefinition Width="Auto"/>
        <ColumnDefinition Width="Auto"/>
        <ColumnDefinition Width="Auto"/>
    </Grid.ColumnDefinitions>

    <TextBlock Text="Preset:" VerticalAlignment="Center"
               Foreground="#B0B0C0" Margin="0,0,8,0"/>

    <ComboBox x:Name="CmbAssetPresets" Grid.Column="1"
              ItemsSource="{Binding AssetPresets}"
              DisplayMemberPath="Name"
              SelectedValuePath="Id"
              SelectionChanged="CmbAssetPresets_SelectionChanged"
              Style="{StaticResource SettingsComboBox}"/>

    <Button x:Name="BtnSaveAssetPreset" Grid.Column="2"
            Content="Save" Margin="8,0,0,0"
            Click="BtnSaveAssetPreset_Click"
            ToolTip="Save current selection as new preset"/>

    <Button x:Name="BtnUpdateAssetPreset" Grid.Column="3"
            Content="Update" Margin="4,0,0,0"
            Click="BtnUpdateAssetPreset_Click"
            ToolTip="Update selected preset with current selection"/>

    <Button x:Name="BtnDeleteAssetPreset" Grid.Column="4"
            Content="Delete" Margin="4,0,0,0"
            Click="BtnDeleteAssetPreset_Click"
            ToolTip="Delete selected preset"/>
</Grid>
```

### Event Handlers

**MainWindow.xaml.cs:**

```csharp
// Load preset into UI
private void CmbAssetPresets_SelectionChanged(object sender, SelectionChangedEventArgs e)
{
    if (CmbAssetPresets.SelectedItem is AssetPreset preset)
    {
        // Apply preset's disabled paths
        App.Settings.Current.DisabledAssetPaths = new HashSet<string>(preset.DisabledAssetPaths);
        App.Settings.Current.CurrentAssetPresetId = preset.Id;
        preset.LastUsed = DateTime.Now;

        // Refresh tree to show new state
        RefreshAssetTree();
        UpdateAssetCounts();

        App.Logger?.Information("Loaded asset preset: {Name}", preset.Name);
    }
}

// Save current selection as new preset
private async void BtnSaveAssetPreset_Click(object sender, RoutedEventArgs e)
{
    // Prompt for name
    var dialog = new InputDialog("Save Asset Preset", "Enter a name for this preset:");
    if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.ResponseText))
    {
        var preset = new AssetPreset
        {
            Name = dialog.ResponseText.Trim(),
            DisabledAssetPaths = new HashSet<string>(App.Settings.Current.DisabledAssetPaths),
            EnabledImageCount = _currentImageCount,
            EnabledVideoCount = _currentVideoCount
        };

        App.Settings.Current.AssetPresets.Add(preset);
        App.Settings.Current.CurrentAssetPresetId = preset.Id;

        // Refresh combo
        CmbAssetPresets.ItemsSource = null;
        CmbAssetPresets.ItemsSource = App.Settings.Current.AssetPresets;
        CmbAssetPresets.SelectedValue = preset.Id;

        App.Logger?.Information("Saved asset preset: {Name}", preset.Name);
    }
}

// Update existing preset
private void BtnUpdateAssetPreset_Click(object sender, RoutedEventArgs e)
{
    if (CmbAssetPresets.SelectedItem is AssetPreset preset)
    {
        preset.DisabledAssetPaths = new HashSet<string>(App.Settings.Current.DisabledAssetPaths);
        preset.EnabledImageCount = _currentImageCount;
        preset.EnabledVideoCount = _currentVideoCount;
        preset.LastUsed = DateTime.Now;

        App.Logger?.Information("Updated asset preset: {Name}", preset.Name);
    }
}

// Delete preset
private void BtnDeleteAssetPreset_Click(object sender, RoutedEventArgs e)
{
    if (CmbAssetPresets.SelectedItem is AssetPreset preset)
    {
        var result = MessageBox.Show(
            $"Delete preset '{preset.Name}'?",
            "Delete Preset",
            MessageBoxButton.YesNo);

        if (result == MessageBoxResult.Yes)
        {
            App.Settings.Current.AssetPresets.Remove(preset);
            if (App.Settings.Current.CurrentAssetPresetId == preset.Id)
                App.Settings.Current.CurrentAssetPresetId = null;

            CmbAssetPresets.ItemsSource = null;
            CmbAssetPresets.ItemsSource = App.Settings.Current.AssetPresets;
        }
    }
}
```

### Default "All Assets" Preset

On first run or when no presets exist, create a default:
```csharp
if (App.Settings.Current.AssetPresets.Count == 0)
{
    App.Settings.Current.AssetPresets.Add(new AssetPreset
    {
        Id = "all",
        Name = "All Assets",
        DisabledAssetPaths = new HashSet<string>()
    });
}
```

---

## PART 3: Implementation Order

### Phase 1: Fix Checkboxes (Priority)
1. Fix `BuildFolderTree()` to use `DisabledAssetPaths`
2. Fix `CountAssetsRecursive()` to use `DisabledAssetPaths`
3. Test that checkboxes now correctly show enabled/disabled state
4. Optionally simplify to 2-state checkboxes

### Phase 2: Add Asset Presets
1. Create `AssetPreset.cs` model
2. Add `AssetPresets` and `CurrentAssetPresetId` to `AppSettings.cs`
3. Add UI controls (ComboBox, Save/Update/Delete buttons)
4. Implement event handlers
5. Add default "All Assets" preset on first run

### Phase 3: Polish
1. Add preset count display (e.g., "Preset: My Selection (120 images, 15 videos)")
2. Add "Duplicate" button to clone a preset
3. Add rename functionality
4. Consider import/export presets feature

---

## Files to Modify

| File | Changes |
|------|---------|
| `MainWindow.xaml.cs` | Fix BuildFolderTree, CountAssetsRecursive, add preset handlers |
| `MainWindow.xaml` | Add preset UI above TreeView |
| `Models/AppSettings.cs` | Add AssetPresets, CurrentAssetPresetId |
| `Models/AssetPreset.cs` | NEW FILE - preset data model |
| `Models/AssetTreeItem.cs` | Optional: simplify to bool instead of bool? |

---

## Testing Checklist

### Checkbox Fix
- [ ] Tree shows correct checked/unchecked state on load
- [ ] Checking a folder checks all files inside
- [ ] Unchecking a folder unchecks all files
- [ ] File checkboxes in thumbnail view match folder state
- [ ] Changes persist after app restart
- [ ] Asset counts are accurate

### Presets
- [ ] Can create new preset from current selection
- [ ] Can load a preset and see selection change
- [ ] Can update an existing preset
- [ ] Can delete a preset
- [ ] Presets persist after app restart
- [ ] "All Assets" default preset works
