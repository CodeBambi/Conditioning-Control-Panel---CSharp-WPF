# Release Workflow

Quick reference for releasing new versions of Conditioning Control Panel.

---

## Version Locations (Update ALL of these)

### Code Files
| File | Location | Example |
|------|----------|---------|
| `ConditioningControlPanel.csproj` | Line 11: `<Version>` | `<Version>5.5.8</Version>` |
| `Services/UpdateService.cs` | Line ~25: `AppVersion` | `public const string AppVersion = "5.5.8";` |
| `Services/UpdateService.cs` | Line ~31: `CurrentPatchNotes` | Update patch notes text |
| `installer.iss` | Line 17: `MyAppVersion` | `#define MyAppVersion "5.5.8"` |
| `build-installer.bat` | Line 10: `VERSION` | `set VERSION=5.5.8` |
| `MainWindow.xaml` | Line ~741: `BtnUpdateAvailable` | `Content="🩷 v5.5.8 IS OUT! 🩷"` |
| `MainWindow.xaml` | Line ~742: `ToolTip` | `ToolTip="v5.5.8 - [message]"` |

### GitHub Pages (docs/index.html)
| Line | What to Update |
|------|----------------|
| ~854 | Download button URL and text: `Download vX.X.X` |
| ~861 | Version banner: `vX.X.X Available Now` |
| ~867-873 | Hero download button URL and text |

Update the download URLs to point to the new release:
```
https://github.com/CodeBambi/Conditioning-Control-Panel---CSharp-WPF/releases/download/vX.X.X/ConditioningControlPanel-X.X.X-Setup.exe
```

### Server Configuration
| Location | Purpose |
|----------|---------|
| `codebambi-proxy.vercel.app/config/update-banner` | Server-side banner for users who haven't updated |

Update the server endpoint to return:
```json
{
  "enabled": true,
  "version": "5.5.8",
  "message": "UPDATE 5.5.8 is live!"
}
```

---

## Quick Release Steps

### 1. Update All Version Locations
Edit all the code locations listed above with the new version number.

### 2. Build Installer (Inno Setup)
```batch
cd C:\Projects\Conditioning-Control-Panel---CSharp-WPF
build-installer.bat
```
Output: `installer-output/ConditioningControlPanel-X.X.X-Setup.exe`

### 3. Update GitHub Pages
Edit `docs/index.html`:
- Update version text in download buttons
- Update download URLs to point to new release tag

### 4. Commit & Push
```bash
git add -A
git commit -m "Bump to vX.X.X with [summary of changes]"
git push
```

### 5. Create GitHub Release
1. Go to: https://github.com/CodeBambi/Conditioning-Control-Panel---CSharp-WPF/releases/new
2. Tag: `vX.X.X` (e.g., `v5.5.8`)
3. Title: `vX.X.X - [Short Description]`
4. Upload files:
   - `installer-output/ConditioningControlPanel-X.X.X-Setup.exe`
5. Write release notes (or copy from `CurrentPatchNotes`)
6. Publish release

### 6. Update Server Banner
Update the proxy server's `/config/update-banner` endpoint with:
```json
{
  "enabled": true,
  "version": "X.X.X",
  "message": "UPDATE X.X.X is live!"
}
```
This shows a banner to users on older versions who haven't updated.

---

## How Updates Work

1. **UpdateService.cs** checks the server banner at `/config/update-banner`
2. Compares `AppVersion` constant with the banner version
3. If newer version available, shows update button in the UI
4. User clicks the button to download the new installer from GitHub

**Important:** The `AppVersion` constant in UpdateService.cs is what the app uses to determine its current version. Always keep this in sync!

---

## Troubleshooting

### Users not seeing updates
- Check that `AppVersion` in UpdateService.cs matches the csproj version
- Verify the GitHub release is published (not draft)
- Update the server banner as a fallback notification
- Verify download URLs in docs/index.html point to the correct release

---

## File Checklist Before Release

### Version Updates
- [ ] `ConditioningControlPanel.csproj` - Version tag
- [ ] `UpdateService.cs` - AppVersion constant
- [ ] `UpdateService.cs` - CurrentPatchNotes
- [ ] `installer.iss` - MyAppVersion
- [ ] `build-installer.bat` - VERSION
- [ ] `MainWindow.xaml` - BtnUpdateAvailable Content & ToolTip
- [ ] `docs/index.html` - Download button text and URLs

### Build & Deploy
- [ ] Installer built (`build-installer.bat`)
- [ ] Changes committed and pushed
- [ ] GitHub release created with installer uploaded

### Post-Release
- [ ] Server banner updated (`/config/update-banner`)
- [ ] Verify GitHub Pages download links work
