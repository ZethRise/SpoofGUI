#define AppVersion GetEnv("SPOOFGUI_VERSION")
#define RepoRoot GetEnv("SPOOFGUI_ROOT")
#define SourceDir GetEnv("SPOOFGUI_PUBLISH_DIR")
#define OutputDir GetEnv("SPOOFGUI_DIST_DIR")

[Setup]
AppId={{E5398958-4C72-4EE0-9D52-D8EBC16E9739}
AppName=SpoofGUI
AppVersion={#AppVersion}
AppPublisher=ZethRise
AppPublisherURL=https://github.com/ZethRise/SpoofGUI
AppSupportURL=https://github.com/ZethRise/SpoofGUI/issues
AppUpdatesURL=https://github.com/ZethRise/SpoofGUI/releases
DefaultDirName={autopf}\SpoofGUI
DefaultGroupName=SpoofGUI
UninstallDisplayIcon={app}\SpoofGUI.exe
OutputDir={#OutputDir}
OutputBaseFilename=SpoofGUI-Setup
Compression=lzma2/ultra64
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
WizardStyle=modern dark windows11 includetitlebar
WizardBackColor=#1B1E25
WizardImageBackColor=#1B1E25
DisableProgramGroupPage=yes
LicenseFile={#RepoRoot}\LICENSE

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Icons]
Name: "{group}\SpoofGUI"; Filename: "{app}\SpoofGUI.exe"; WorkingDir: "{app}"
Name: "{autodesktop}\SpoofGUI"; Filename: "{app}\SpoofGUI.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\SpoofGUI.exe"; Description: "{cm:LaunchProgram,SpoofGUI}"; Flags: nowait postinstall skipifsilent runascurrentuser
