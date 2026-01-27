; Conditioning Control Panel - Inno Setup Installer Script
; This creates a proper Windows installer with install path selection
;
; Requirements:
; 1. Install Inno Setup from https://jrsoftware.org/isinfo.php
; 2. Build the app first: dotnet publish -c Release
; 3. Compile this script with Inno Setup Compiler
;
; The installer will:
; - Allow users to choose installation directory
; - Create Start Menu and Desktop shortcuts
; - Register uninstaller
; - Store install path in registry for Velopack updates

#define MyAppName "Conditioning Control Panel"
#define MyAppVersion "5.3.3"
#define MyAppPublisher "CodeBambi"
#define MyAppURL "https://github.com/CodeBambi/Conditioning-Control-Panel---CSharp-WPF"
#define MyAppExeName "ConditioningControlPanel.exe"
#define MyAppDescription "A professional visual conditioning application with gamification features"

; Path to the published output (adjust if needed)
#define PublishDir "ConditioningControlPanel\bin\Release\net8.0-windows\win-x64\publish"

[Setup]
; Application identity
AppId={{A7B9C3D1-E5F2-4A8B-9C1D-2E3F4A5B6C7D}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}/issues
AppUpdatesURL={#MyAppURL}/releases

; Default installation directory (user can change)
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}

; Allow user to change install directory
DisableDirPage=no
DirExistsWarning=auto

; Output settings
OutputDir=.\installer-output
OutputBaseFilename=ConditioningControlPanel-{#MyAppVersion}-Setup
SetupIconFile=ConditioningControlPanel\Resources\app.ico

; Compression
Compression=lzma2/ultra64
SolidCompression=yes
LZMAUseSeparateProcess=yes

; Privileges - allow per-user or admin install
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog

; Appearance
WizardStyle=modern
WizardSizePercent=120

; Uninstaller
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}

; Other settings
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
DisableProgramGroupPage=yes
AllowNoIcons=yes
ShowLanguageDialog=auto

; License and info pages (optional - create these files if desired)
; LicenseFile=LICENSE.txt
; InfoBeforeFile=INSTALL_INFO.txt

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "startmenuicon"; Description: "Create a Start Menu shortcut"; GroupDescription: "{cm:AdditionalIcons}"; Flags: checkedonce

[Files]
; Main executable
Source: "{#PublishDir}\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion

; All other files from publish directory
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.pdb"

; NOTE: Don't include user data files - those go to %APPDATA%

[Icons]
; Start Menu shortcut
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: startmenuicon
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"; Tasks: startmenuicon

; Desktop shortcut
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
; Store install path for the application and Velopack to find
Root: HKCU; Subkey: "Software\{#MyAppPublisher}\{#MyAppName}"; ValueType: string; ValueName: "InstallPath"; ValueData: "{app}"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\{#MyAppPublisher}\{#MyAppName}"; ValueType: string; ValueName: "Version"; ValueData: "{#MyAppVersion}"; Flags: uninsdeletekey
; Assets path will be written by [Code] section during install

[Run]
; Option to launch app after interactive installation (shows checkbox)
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent
; Always launch app after silent installation (auto-updates)
Filename: "{app}\{#MyAppExeName}"; Flags: nowait postinstall skipifnotsilent

[UninstallRun]
; Ensure app is closed before uninstall
Filename: "taskkill"; Parameters: "/F /IM {#MyAppExeName}"; Flags: runhidden; RunOnceId: "KillApp"

[UninstallDelete]
; Clean up any generated files (optional)
Type: filesandordirs; Name: "{app}\logs"

[Code]
// Pascal Script for custom installer logic

var
  VelopackPath: String;
  HasOldVelopackInstall: Boolean;
  HasExistingAssets: Boolean;
  AssetsPath: String;
  ImageCount, VideoCount: Integer;

  // Custom page for assets confirmation
  AssetsPage: TWizardPage;
  AssetsPathLabel: TNewStaticText;
  AssetsPathEdit: TNewEdit;
  AssetsBrowseButton: TNewButton;
  AssetsInfoLabel: TNewStaticText;
  AssetsPreserveLabel: TNewStaticText;

// Count files in a directory with specific extensions
function CountFilesInDir(const Dir: String; const Extensions: String): Integer;
var
  FindRec: TFindRec;
  Ext: String;
begin
  Result := 0;
  if FindFirst(Dir + '\*', FindRec) then
  begin
    try
      repeat
        if (FindRec.Attributes and FILE_ATTRIBUTE_DIRECTORY) = 0 then
        begin
          Ext := LowerCase(ExtractFileExt(FindRec.Name));
          if Pos(Ext, Extensions) > 0 then
            Result := Result + 1;
        end;
      until not FindNext(FindRec);
    finally
      FindClose(FindRec);
    end;
  end;
end;

// Check if old Velopack installation exists
function CheckForVelopackInstall(): Boolean;
var
  CurrentPath: String;
begin
  VelopackPath := ExpandConstant('{localappdata}\ConditioningControlPanel');
  CurrentPath := VelopackPath + '\current';

  // Velopack installs to a 'current' subfolder
  Result := DirExists(CurrentPath) and FileExists(CurrentPath + '\{#MyAppExeName}');
end;

// Check for existing assets and count them
procedure DetectExistingAssets();
var
  NewAssetsPath, OldVelopackAssetsPath, ImagesPath, VideosPath: String;
  NewImageCount, NewVideoCount, OldImageCount, OldVideoCount: Integer;
begin
  // New location (AppData root)
  NewAssetsPath := ExpandConstant('{localappdata}\ConditioningControlPanel\assets');
  // Old Velopack location (inside current folder)
  OldVelopackAssetsPath := ExpandConstant('{localappdata}\ConditioningControlPanel\current\assets');

  // Count assets in new location
  NewImageCount := 0;
  NewVideoCount := 0;
  if DirExists(NewAssetsPath) then
  begin
    if DirExists(NewAssetsPath + '\images') then
      NewImageCount := CountFilesInDir(NewAssetsPath + '\images', '.png.jpg.jpeg.gif.webp.bmp');
    if DirExists(NewAssetsPath + '\videos') then
      NewVideoCount := CountFilesInDir(NewAssetsPath + '\videos', '.mp4.webm.mkv.avi.mov.wmv');
  end;

  // Count assets in old Velopack location
  OldImageCount := 0;
  OldVideoCount := 0;
  if DirExists(OldVelopackAssetsPath) then
  begin
    if DirExists(OldVelopackAssetsPath + '\images') then
      OldImageCount := CountFilesInDir(OldVelopackAssetsPath + '\images', '.png.jpg.jpeg.gif.webp.bmp');
    if DirExists(OldVelopackAssetsPath + '\videos') then
      OldVideoCount := CountFilesInDir(OldVelopackAssetsPath + '\videos', '.mp4.webm.mkv.avi.mov.wmv');
  end;

  // Use whichever location has more assets (prefer old Velopack location if equal)
  if (OldImageCount + OldVideoCount) >= (NewImageCount + NewVideoCount) then
  begin
    if (OldImageCount + OldVideoCount) > 0 then
    begin
      AssetsPath := OldVelopackAssetsPath;
      HasExistingAssets := True;
      ImageCount := OldImageCount;
      VideoCount := OldVideoCount;
    end
    else if (NewImageCount + NewVideoCount) > 0 then
    begin
      AssetsPath := NewAssetsPath;
      HasExistingAssets := True;
      ImageCount := NewImageCount;
      VideoCount := NewVideoCount;
    end
    else
    begin
      AssetsPath := NewAssetsPath;
      HasExistingAssets := False;
      ImageCount := 0;
      VideoCount := 0;
    end;
  end
  else
  begin
    AssetsPath := NewAssetsPath;
    HasExistingAssets := True;
    ImageCount := NewImageCount;
    VideoCount := NewVideoCount;
  end;
end;

// Browse button click handler
procedure AssetsBrowseButtonClick(Sender: TObject);
var
  Dir: String;
begin
  Dir := AssetsPathEdit.Text;
  if BrowseForFolder('Select your assets folder:', Dir, False) then
  begin
    AssetsPathEdit.Text := Dir;
    // Recount files in new location
    if DirExists(Dir + '\images') then
      ImageCount := CountFilesInDir(Dir + '\images', '.png.jpg.jpeg.gif.webp.bmp')
    else
      ImageCount := 0;
    if DirExists(Dir + '\videos') then
      VideoCount := CountFilesInDir(Dir + '\videos', '.mp4.webm.mkv.avi.mov.wmv')
    else
      VideoCount := 0;
    AssetsInfoLabel.Caption := 'Found: ' + IntToStr(ImageCount) + ' images, ' + IntToStr(VideoCount) + ' videos';
  end;
end;

// Copy all files from source to dest directory (non-recursive for a single folder)
procedure CopyFilesFromDir(const SourceDir, DestDir: String);
var
  FindRec: TFindRec;
  SourceFile, DestFile: String;
begin
  if not DirExists(SourceDir) then Exit;
  ForceDirectories(DestDir);

  if FindFirst(SourceDir + '\*', FindRec) then
  begin
    try
      repeat
        if (FindRec.Attributes and FILE_ATTRIBUTE_DIRECTORY) = 0 then
        begin
          SourceFile := SourceDir + '\' + FindRec.Name;
          DestFile := DestDir + '\' + FindRec.Name;
          // Only copy if destination doesn't exist (don't overwrite)
          if not FileExists(DestFile) then
            CopyFile(SourceFile, DestFile, False);
        end;
      until not FindNext(FindRec);
    finally
      FindClose(FindRec);
    end;
  end;
end;

// Migrate assets from old Velopack location to new AppData location
procedure MigrateAssetsFromVelopack();
var
  OldAssetsPath, NewAssetsPath: String;
  OldImagesPath, OldVideosPath, NewImagesPath, NewVideosPath: String;
  OldSpiralsPath, NewSpiralsPath: String;
begin
  OldAssetsPath := VelopackPath + '\current\assets';
  NewAssetsPath := VelopackPath + '\assets';
  OldSpiralsPath := VelopackPath + '\current\Spirals';
  NewSpiralsPath := VelopackPath + '\Spirals';

  // Migrate images
  OldImagesPath := OldAssetsPath + '\images';
  NewImagesPath := NewAssetsPath + '\images';
  if DirExists(OldImagesPath) then
  begin
    Log('Migrating images from ' + OldImagesPath + ' to ' + NewImagesPath);
    CopyFilesFromDir(OldImagesPath, NewImagesPath);
  end;

  // Migrate videos
  OldVideosPath := OldAssetsPath + '\videos';
  NewVideosPath := NewAssetsPath + '\videos';
  if DirExists(OldVideosPath) then
  begin
    Log('Migrating videos from ' + OldVideosPath + ' to ' + NewVideosPath);
    CopyFilesFromDir(OldVideosPath, NewVideosPath);
  end;

  // Migrate spirals
  if DirExists(OldSpiralsPath) then
  begin
    Log('Migrating spirals from ' + OldSpiralsPath + ' to ' + NewSpiralsPath);
    CopyFilesFromDir(OldSpiralsPath, NewSpiralsPath);
  end;
end;

// Clean up old Velopack installation (preserves user data)
procedure CleanupVelopackInstall();
var
  CurrentPath, PackagesPath, UpdatePath: String;
begin
  CurrentPath := VelopackPath + '\current';
  PackagesPath := VelopackPath + '\packages';
  UpdatePath := VelopackPath + '\Update.exe';

  // IMPORTANT: Migrate assets BEFORE deleting current folder!
  MigrateAssetsFromVelopack();

  // Remove Velopack-specific folders only (assets already migrated)
  if DirExists(CurrentPath) then
    DelTree(CurrentPath, True, True, True);

  if DirExists(PackagesPath) then
    DelTree(PackagesPath, True, True, True);

  // Remove Velopack's Update.exe if it exists
  if FileExists(UpdatePath) then
    DeleteFile(UpdatePath);

  // Remove any .velopack files
  DelTree(VelopackPath + '\*.velopack', False, True, False);

  // Remove old Velopack uninstall registry entry (shows in Add/Remove Programs)
  RegDeleteKeyIncludingSubkeys(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Uninstall\ConditioningControlPanel');

  // Also clean up old app registry entries if they exist
  RegDeleteKeyIncludingSubkeys(HKCU, 'Software\ConditioningControlPanel');
end;

// Prompt to close app if running
function InitializeSetup(): Boolean;
var
  ErrorCode: Integer;
begin
  Result := True;

  // Check for old Velopack install
  HasOldVelopackInstall := CheckForVelopackInstall();

  // Detect existing assets
  DetectExistingAssets();

  // Check if already running
  if CheckForMutexes('{#MyAppName}_Mutex') then
  begin
    if MsgBox('{#MyAppName} is currently running.' + #13#10 + #13#10 +
              'Please close it before continuing installation.' + #13#10 + #13#10 +
              'Click OK to attempt to close it automatically, or Cancel to exit setup.',
              mbConfirmation, MB_OKCANCEL) = IDOK then
    begin
      // Try to close gracefully first
      ShellExec('', 'taskkill', '/IM {#MyAppExeName}', '', SW_HIDE, ewWaitUntilTerminated, ErrorCode);
      Sleep(2000);

      // Force kill if still running
      if CheckForMutexes('{#MyAppName}_Mutex') then
      begin
        ShellExec('', 'taskkill', '/F /IM {#MyAppExeName}', '', SW_HIDE, ewWaitUntilTerminated, ErrorCode);
        Sleep(1000);
      end;
    end
    else
    begin
      Result := False;
    end;
  end;
end;

// Determine if assets page should be shown
function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := False;
  // ALWAYS show assets page - users need to choose where their content lives
  // This folder stores images, videos, and downloaded packs - survives updates
end;

// Called after installation completes successfully
procedure CurStepChanged(CurStep: TSetupStep);
var
  SelectedAssetsPath: String;
begin
  if CurStep = ssPostInstall then
  begin
    // ALWAYS save the selected assets path to registry (read by app on startup)
    // This ensures the user's choice is respected, whether new or existing install
    SelectedAssetsPath := AssetsPathEdit.Text;
    if SelectedAssetsPath <> '' then
    begin
      RegWriteStringValue(HKEY_CURRENT_USER, 'Software\{#MyAppPublisher}\{#MyAppName}',
        'AssetsPath', SelectedAssetsPath);

      // Create the folder structure if it doesn't exist
      if not DirExists(SelectedAssetsPath) then
        ForceDirectories(SelectedAssetsPath);
      if not DirExists(SelectedAssetsPath + '\images') then
        ForceDirectories(SelectedAssetsPath + '\images');
      if not DirExists(SelectedAssetsPath + '\videos') then
        ForceDirectories(SelectedAssetsPath + '\videos');
    end;

    // Offer to clean up old Velopack installation
    if HasOldVelopackInstall then
    begin
      if MsgBox('A previous installation (via auto-updater) was detected.' + #13#10 + #13#10 +
                'Location: ' + VelopackPath + '\current' + #13#10 + #13#10 +
                'Would you like to remove the old installation?' + #13#10 +
                '(Your settings, assets, and progress will be preserved)',
                mbConfirmation, MB_YESNO) = IDYES then
      begin
        CleanupVelopackInstall();
        MsgBox('Old installation removed successfully!' + #13#10 + #13#10 +
               'Your user data has been preserved in:' + #13#10 +
               VelopackPath,
               mbInformation, MB_OK);
      end;
    end;
  end;
end;

// Clean uninstall - prompt to remove user data
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  UserDataPath: String;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    UserDataPath := ExpandConstant('{localappdata}\ConditioningControlPanel');

    if DirExists(UserDataPath) then
    begin
      if MsgBox('Do you want to remove your user data (settings, progress, logs)?' + #13#10 + #13#10 +
                'Location: ' + UserDataPath + #13#10 + #13#10 +
                'Click Yes to remove all data, or No to keep it.',
                mbConfirmation, MB_YESNO) = IDYES then
      begin
        DelTree(UserDataPath, True, True, True);
      end;
    end;
  end;
end;

// Create the assets confirmation page
procedure CreateAssetsPage();
var
  InfoText: String;
begin
  // Create custom page after the directory selection page
  AssetsPage := CreateCustomPage(wpSelectDir,
    'Content Folder',
    'Choose where to store your images, videos, and downloaded packs');

  // Description label - explain clearly what this folder is for
  AssetsPathLabel := TNewStaticText.Create(AssetsPage);
  AssetsPathLabel.Parent := AssetsPage.Surface;
  AssetsPathLabel.Caption :=
    'Select a folder for your personal content. This folder will contain:' + #13#10 +
    '  - Your images (flash images)' + #13#10 +
    '  - Your videos (mandatory videos)' + #13#10 +
    '  - Downloaded content packs' + #13#10 + #13#10 +
    'IMPORTANT: This folder is separate from the app and survives updates!';
  AssetsPathLabel.Left := 0;
  AssetsPathLabel.Top := 0;
  AssetsPathLabel.Width := AssetsPage.SurfaceWidth;
  AssetsPathLabel.Height := 85;
  AssetsPathLabel.AutoSize := False;
  AssetsPathLabel.WordWrap := True;

  // Path edit box (editable now)
  AssetsPathEdit := TNewEdit.Create(AssetsPage);
  AssetsPathEdit.Parent := AssetsPage.Surface;
  AssetsPathEdit.Left := 0;
  AssetsPathEdit.Top := 95;
  AssetsPathEdit.Width := AssetsPage.SurfaceWidth - 100;
  AssetsPathEdit.Text := AssetsPath;
  AssetsPathEdit.ReadOnly := False;

  // Browse button
  AssetsBrowseButton := TNewButton.Create(AssetsPage);
  AssetsBrowseButton.Parent := AssetsPage.Surface;
  AssetsBrowseButton.Left := AssetsPage.SurfaceWidth - 90;
  AssetsBrowseButton.Top := 93;
  AssetsBrowseButton.Width := 90;
  AssetsBrowseButton.Height := 25;
  AssetsBrowseButton.Caption := 'Browse...';
  AssetsBrowseButton.OnClick := @AssetsBrowseButtonClick;

  // Assets count info (or new folder notice)
  AssetsInfoLabel := TNewStaticText.Create(AssetsPage);
  AssetsInfoLabel.Parent := AssetsPage.Surface;
  AssetsInfoLabel.Left := 0;
  AssetsInfoLabel.Top := 130;
  AssetsInfoLabel.Width := AssetsPage.SurfaceWidth;
  AssetsInfoLabel.Height := 20;
  AssetsInfoLabel.Font.Style := [fsBold];

  if HasExistingAssets then
    AssetsInfoLabel.Caption := 'Found existing content: ' + IntToStr(ImageCount) + ' images, ' + IntToStr(VideoCount) + ' videos'
  else
    AssetsInfoLabel.Caption := 'New installation - folders will be created automatically';

  // Important notice about packs
  AssetsPreserveLabel := TNewStaticText.Create(AssetsPage);
  AssetsPreserveLabel.Parent := AssetsPage.Surface;
  AssetsPreserveLabel.Left := 0;
  AssetsPreserveLabel.Top := 165;
  AssetsPreserveLabel.Width := AssetsPage.SurfaceWidth;
  AssetsPreserveLabel.Height := 100;
  AssetsPreserveLabel.AutoSize := False;
  AssetsPreserveLabel.WordWrap := True;

  if HasExistingAssets then
  begin
    AssetsPreserveLabel.Caption :=
      'Your existing content will be preserved:' + #13#10 +
      '  - All images and videos remain intact' + #13#10 +
      '  - Downloaded packs will NOT need to be re-downloaded' + #13#10 +
      '  - Settings and progress are kept' + #13#10 + #13#10 +
      'Updates only replace app files, never your content!';
    AssetsPreserveLabel.Font.Color := clGreen;
  end
  else
  begin
    AssetsPreserveLabel.Caption :=
      'Tip: Choose a location with enough space for videos and packs.' + #13#10 +
      'Content packs can be several GB each.' + #13#10 + #13#10 +
      'You can change this folder later in Settings > Assets.' + #13#10 +
      'Downloaded packs will follow your content folder.';
    AssetsPreserveLabel.Font.Color := clNavy;
  end;
end;

// Custom welcome text with upgrade notice
procedure InitializeWizard();
var
  WelcomeText: String;
begin
  // Create the assets confirmation page
  CreateAssetsPage();

  // Set welcome text
  WelcomeText := 'This will install {#MyAppName} version {#MyAppVersion} on your computer.' + #13#10 + #13#10 +
                 '{#MyAppDescription}' + #13#10 + #13#10 +
                 'You will be able to choose where to install the application.' + #13#10 + #13#10;

  if HasOldVelopackInstall or HasExistingAssets then
    WelcomeText := WelcomeText +
                   'NOTE: A previous installation was detected. Your settings and assets will be preserved.' + #13#10 + #13#10;

  WelcomeText := WelcomeText + 'Click Next to continue, or Cancel to exit Setup.';

  WizardForm.WelcomeLabel2.Caption := WelcomeText;
end;
