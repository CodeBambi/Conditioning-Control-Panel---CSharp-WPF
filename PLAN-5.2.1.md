# v5.2.1 Implementation Plan

## Issue 1: Content Pack ScrollViewer Bug

### Problem
The Content Pack section's horizontal scrollbar is:
- "Tiny, stuck in the middle of the screen"
- Dragging right moves content left (inverted expectation)

### Root Cause Analysis
**Height Mismatch:**
- `PacksScrollViewer` Height: **280px**
- Pack Card Height: **300px**
- Cards are 20px taller than their container, causing vertical clipping
- Scrollbar is fighting for space in the constrained height

**Location:** `MainWindow.xaml` line 4412-4416
```xaml
<ScrollViewer x:Name="PacksScrollViewer" Grid.Row="2"
              HorizontalScrollBarVisibility="Visible"
              VerticalScrollBarVisibility="Disabled"
              PreviewMouseWheel="PacksScrollViewer_PreviewMouseWheel"
              Height="280">  <!-- TOO SHORT for 300px cards -->
```

### Solution
1. **Increase ScrollViewer Height** from 280 to 320 (300px cards + 20px for scrollbar)
2. **Consider Auto Height** with MaxHeight constraint instead of fixed height
3. **Add Padding** inside ScrollViewer for better scrollbar visibility

### Implementation
```xaml
<ScrollViewer x:Name="PacksScrollViewer" Grid.Row="2"
              HorizontalScrollBarVisibility="Auto"
              VerticalScrollBarVisibility="Disabled"
              PreviewMouseWheel="PacksScrollViewer_PreviewMouseWheel"
              Padding="0,0,0,5"
              MaxHeight="340">
```

---

## Issue 2: Asset Selection Presets

### Problem
Users must re-select their custom asset selections every time they want to change what's shown. This is tedious for:
- Single asset selections (cherry-picking specific files)
- Switching between different "moods" or content sets
- Testing different combinations

### Current Architecture
```
AppSettings
├── DisabledAssetPaths: HashSet<string>  ← Global, single selection
├── UserPresets: List<Preset>
│   └── Preset (contains feature settings, NOT asset selection)
```

### Proposed Solution: Asset Profiles

#### Option A: Extend Existing Preset Model (Recommended)
Add asset selection to the existing Preset system:

```csharp
// Models/Preset.cs - Add these properties:
public HashSet<string> DisabledAssetPaths { get; set; } = new();
public bool IncludeAssetSelection { get; set; } = true;
```

**Pros:**
- Leverages existing UI (preset dropdown, save/load)
- Single unified preset concept
- No new models needed

**Cons:**
- Presets become heavier (include both settings + assets)
- Users might want asset profiles independent of settings

#### Option B: Separate Asset Profiles
Create independent asset profile system:

```csharp
// Models/AssetProfile.cs (NEW)
public class AssetProfile : INotifyPropertyChanged
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "New Profile";
    public string Description { get; set; } = "";
    public HashSet<string> DisabledAssetPaths { get; set; } = new();
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime LastUsed { get; set; }

    // Optional: folder-level selections for efficiency
    public HashSet<string> DisabledFolders { get; set; } = new();
}
```

**AppSettings additions:**
```csharp
public List<AssetProfile> AssetProfiles { get; set; } = new();
public string? CurrentAssetProfileId { get; set; }
```

**Pros:**
- Clean separation of concerns
- Can switch asset profiles without changing other settings
- Can link profiles to presets optionally

**Cons:**
- New UI needed (profile selector, save/load buttons)
- More complex implementation

### Recommended Implementation: Option A (Preset Extension)

#### Step 1: Extend Preset.cs
```csharp
// Add to Preset.cs
[JsonProperty]
public HashSet<string> DisabledAssetPaths { get; set; } = new();

[JsonProperty]
public bool SaveAssetSelection { get; set; } = false; // Opt-in
```

#### Step 2: Update ApplyTo() Method
```csharp
public void ApplyTo(AppSettings settings)
{
    // ... existing code ...

    // Apply asset selection if saved with this preset
    if (SaveAssetSelection && DisabledAssetPaths != null)
    {
        settings.DisabledAssetPaths = new HashSet<string>(DisabledAssetPaths);
    }
}
```

#### Step 3: Update FromSettings() Method
```csharp
public static Preset FromSettings(AppSettings settings, string name, bool includeAssets = false)
{
    var preset = new Preset { /* existing ... */ };

    if (includeAssets)
    {
        preset.SaveAssetSelection = true;
        preset.DisabledAssetPaths = new HashSet<string>(settings.DisabledAssetPaths);
    }

    return preset;
}
```

#### Step 4: Add UI in Settings Tab
Add checkbox when saving preset:
```xaml
<CheckBox x:Name="ChkIncludeAssetSelection"
          Content="Include current asset selection"
          ToolTip="Save which images/videos are enabled with this preset"/>
```

#### Step 5: Asset Tab Quick-Save Buttons
Add to Assets tab:
```xaml
<StackPanel Orientation="Horizontal" Margin="0,5">
    <Button Content="Save Selection..." Click="BtnSaveAssetSelection_Click"/>
    <ComboBox x:Name="CmbAssetPresets" Width="150" Margin="5,0"/>
    <Button Content="Load" Click="BtnLoadAssetSelection_Click"/>
</StackPanel>
```

### UI Mockup for Assets Tab

```
┌─────────────────────────────────────────────────────────┐
│ Asset Selection          [Save As...] [▼ Preset] [Load] │
├─────────────────────────────────────────────────────────┤
│ ☑ images/                                               │
│   ☑ category1/                                          │
│     ☐ img001.jpg                                        │
│     ☑ img002.gif                                        │
│   ☐ category2/           (entire folder disabled)       │
│ ☑ videos/                                               │
│   ☑ video1.mp4                                          │
└─────────────────────────────────────────────────────────┘
│ [Select All] [Deselect All]   120 images, 15 videos     │
└─────────────────────────────────────────────────────────┘
```

---

## Implementation Priority

### Phase 1: Bug Fix (5.2.1)
1. Fix ScrollViewer height mismatch
2. Test on multiple DPI settings

### Phase 2: Asset Presets (5.3.0 or later)
1. Extend Preset model with DisabledAssetPaths
2. Update ApplyTo/FromSettings
3. Add UI for saving/loading asset selections
4. Consider separate "Quick Profiles" dropdown in Assets tab

---

## Files to Modify

### ScrollViewer Fix
- `MainWindow.xaml` (line 4412-4416)

### Asset Presets
- `Models/Preset.cs` - Add DisabledAssetPaths property
- `Models/AppSettings.cs` - Possibly add AssetProfiles list
- `MainWindow.xaml` - Add preset UI to Assets tab
- `MainWindow.xaml.cs` - Add save/load handlers

---

## Testing Checklist

### ScrollViewer
- [ ] Scrollbar visible and usable
- [ ] Cards not clipped vertically
- [ ] Mouse wheel scrolls correctly
- [ ] Works at 100%, 125%, 150% DPI scaling
- [ ] Works with 1-10 pack cards

### Asset Presets
- [ ] Save current selection to preset
- [ ] Load preset restores exact selection
- [ ] Works with folders and individual files
- [ ] Handles missing files gracefully (removed assets)
- [ ] JSON serialization round-trips correctly
