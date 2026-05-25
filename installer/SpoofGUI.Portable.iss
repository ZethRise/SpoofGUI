#define AppVersion GetEnv("SPOOFGUI_VERSION")
#define RepoRoot GetEnv("SPOOFGUI_ROOT")
#define SourceDir GetEnv("SPOOFGUI_PUBLISH_DIR")
#define OutputDir GetEnv("SPOOFGUI_DIST_DIR")

[Setup]
AppId={{6DA75C23-9F4B-46C8-8DA0-F4BA88F7FE6F}
AppName=SpoofGUI Portable
AppVersion={#AppVersion}
AppPublisher=ZethRise
AppPublisherURL=https://github.com/ZethRise/SpoofGUI
DefaultDirName={src}\SpoofGUI-Portable
AppendDefaultDirName=no
Uninstallable=no
CreateUninstallRegKey=no
OutputDir={#OutputDir}
OutputBaseFilename=SpoofGUI-Portable
Compression=lzma2/ultra64
SolidCompression=yes
ArchitecturesAllowed=x64compatible
PrivilegesRequired=admin
WizardStyle=modern dark windows11 includetitlebar
WizardBackColor=#1B1E25
WizardImageBackColor=#1B1E25
DisableProgramGroupPage=yes
DisableReadyPage=yes
LicenseFile={#RepoRoot}\LICENSE

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Run]
Filename: "{app}\SpoofGUI.exe"; Description: "Run SpoofGUI"; Flags: nowait postinstall skipifsilent runascurrentuser
