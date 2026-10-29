#define MyAppName "EagleShot"
#ifndef MyAppVersion
  #define MyAppVersion "3.1.2"
#endif
#define MyAppPublisher "Hasan Guclu"
#define MyAppURL "https://github.com/hhasanguclu/EagleShot"
#define MyAppExeName "EagleShot.exe"
#ifndef ProjectDir
  #define ProjectDir "C:\Users\Hasan\Documents\GitHub\EagleShot"
#endif

[Setup]
AppId={{EAGLE-SHOT-GUID-12345}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
OutputDir={#ProjectDir}\dist
OutputBaseFilename=EagleShot_Setup_v{#MyAppVersion}_win-x64
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
SetupIconFile={#ProjectDir}\Resources\icon.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "turkish"; MessagesFile: "compiler:Languages\Turkish.isl"

[Types]
Name: "full"; Description: "Full installation"
Name: "custom"; Description: "Custom installation"; Flags: iscustom

[Components]
Name: "main"; Description: "EagleShot"; Types: full custom; Flags: fixed
; Only offered when the tool isn't already installed on this computer
Name: "ffmpeg"; Description: "FFmpeg - needed for screen and GIF recording"; Types: full; Check: not FfmpegInstalled
Name: "ocr"; Description: "Tesseract OCR (English + Turkish) - needed for text recognition"; Types: full; Check: not TesseractInstalled

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "startup"; Description: "Start with Windows"; GroupDescription: "Other:"
Name: "printscreen"; Description: "Use the PrintScreen key for EagleShot (instead of the Windows Snipping Tool)"; GroupDescription: "Other:"

[Files]
Source: "{#ProjectDir}\publish\win-x64\*"; Excludes: "\tools\*"; DestDir: "{app}"; Components: main; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#ProjectDir}\publish\win-x64\tools\ffmpeg\*"; DestDir: "{app}\tools\ffmpeg"; Components: ffmpeg; Flags: ignoreversion recursesubdirs createallsubdirs skipifsourcedoesntexist
Source: "{#ProjectDir}\publish\win-x64\tools\tesseract\*"; DestDir: "{app}\tools\tesseract"; Components: ocr; Flags: ignoreversion recursesubdirs createallsubdirs skipifsourcedoesntexist

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
; Windows 11 opens the Snipping Tool on PrintScreen; turn that off so only EagleShot reacts.
; Removing the value on uninstall restores the Windows default.
Root: HKCU; Subkey: "Control Panel\Keyboard"; ValueType: dword; ValueName: "PrintScreenKeyForSnippingEnabled"; ValueData: 0; Flags: uninsdeletevalue; Tasks: printscreen
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "{#MyAppName}"; ValueData: """{app}\{#MyAppExeName}"""; Flags: uninsdeletevalue; Tasks: startup

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "taskkill"; Parameters: "/F /IM {#MyAppExeName}"; Flags: runhidden; RunOnceId: "KillApp"

[Code]
function OnPath(const Exe: string): Boolean;
begin
  Result := FileSearch(Exe, GetEnv('PATH')) <> '';
end;

function FfmpegInstalled: Boolean;
begin
  Result := OnPath('ffmpeg.exe')
    or FileExists(ExpandConstant('{commonpf}\ffmpeg\bin\ffmpeg.exe'))
    or FileExists(ExpandConstant('{localappdata}\Microsoft\WinGet\Links\ffmpeg.exe'))
    or FileExists('C:\ffmpeg\bin\ffmpeg.exe');
end;

function TesseractInstalled: Boolean;
begin
  Result := OnPath('tesseract.exe')
    or FileExists(ExpandConstant('{commonpf}\Tesseract-OCR\tesseract.exe'))
    or FileExists(ExpandConstant('{localappdata}\Programs\Tesseract-OCR\tesseract.exe'));
end;
