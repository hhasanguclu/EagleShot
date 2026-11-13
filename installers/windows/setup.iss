#define MyAppName "EagleShot"
#ifndef MyAppVersion
  #define MyAppVersion "3.3.0"
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
Name: "german"; MessagesFile: "compiler:Languages\German.isl"
Name: "french"; MessagesFile: "compiler:Languages\French.isl"
Name: "polish"; MessagesFile: "compiler:Languages\Polish.isl"
Name: "ukrainian"; MessagesFile: "compiler:Languages\Ukrainian.isl"
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "japanese"; MessagesFile: "compiler:Languages\Japanese.isl"
; Korean ships with newer Inno Setup releases only; Chinese isn't bundled (English is used)
#if FileExists(AddBackslash(CompilerPath) + "Languages\Korean.isl")
Name: "korean"; MessagesFile: "compiler:Languages\Korean.isl"
#endif

[CustomMessages]
FullInstall=Full installation
CustomInstall=Custom installation
CompFfmpeg=FFmpeg - needed for screen and GIF recording
CompOcr=Tesseract OCR with 11 languages - needed for text recognition
GroupOther=Other:
TaskStartup=Start with Windows
TaskPrintScreen=Use the PrintScreen key for EagleShot (instead of the Windows Snipping Tool)
turkish.FullInstall=Tam kurulum
turkish.CustomInstall=Özel kurulum
turkish.CompFfmpeg=FFmpeg - ekran ve GIF kaydı için gerekli
turkish.CompOcr=Tesseract OCR (11 dil) - metin tanıma için gerekli
turkish.GroupOther=Diğer:
turkish.TaskStartup=Windows ile başlat
turkish.TaskPrintScreen=PrintScreen tuşunu EagleShot için kullan (Windows Ekran Alıntısı Aracı yerine)
german.FullInstall=Vollständige Installation
german.CustomInstall=Benutzerdefinierte Installation
german.CompFfmpeg=FFmpeg - für Bildschirm- und GIF-Aufnahmen nötig
german.CompOcr=Tesseract OCR mit 11 Sprachen - für die Texterkennung nötig
german.GroupOther=Sonstiges:
german.TaskStartup=Mit Windows starten
german.TaskPrintScreen=Druck-Taste für EagleShot verwenden (statt für das Windows-Snipping-Tool)
french.FullInstall=Installation complète
french.CustomInstall=Installation personnalisée
french.CompFfmpeg=FFmpeg - nécessaire pour l'enregistrement d'écran et GIF
french.CompOcr=Tesseract OCR en 11 langues - nécessaire pour la reconnaissance de texte
french.GroupOther=Autres :
french.TaskStartup=Démarrer avec Windows
french.TaskPrintScreen=Utiliser la touche Impr. écran pour EagleShot (au lieu de l'Outil Capture d'écran)
polish.FullInstall=Instalacja pełna
polish.CustomInstall=Instalacja niestandardowa
polish.CompFfmpeg=FFmpeg - potrzebny do nagrywania ekranu i GIF
polish.CompOcr=Tesseract OCR (11 języków) - potrzebny do rozpoznawania tekstu
polish.GroupOther=Inne:
polish.TaskStartup=Uruchamiaj z systemem Windows
polish.TaskPrintScreen=Używaj klawisza PrintScreen dla EagleShot (zamiast Narzędzia Wycinanie)
ukrainian.FullInstall=Повне встановлення
ukrainian.CustomInstall=Вибіркове встановлення
ukrainian.CompFfmpeg=FFmpeg - потрібен для запису екрана й GIF
ukrainian.CompOcr=Tesseract OCR (11 мов) - потрібен для розпізнавання тексту
ukrainian.GroupOther=Інше:
ukrainian.TaskStartup=Запускати разом із Windows
ukrainian.TaskPrintScreen=Використовувати клавішу PrintScreen для EagleShot (замість «Ножиць»)
russian.FullInstall=Полная установка
russian.CustomInstall=Выборочная установка
russian.CompFfmpeg=FFmpeg - нужен для записи экрана и GIF
russian.CompOcr=Tesseract OCR (11 языков) - нужен для распознавания текста
russian.GroupOther=Другое:
russian.TaskStartup=Запускать вместе с Windows
russian.TaskPrintScreen=Использовать клавишу PrintScreen для EagleShot (вместо «Ножниц»)
japanese.FullInstall=フル インストール
japanese.CustomInstall=カスタム インストール
japanese.CompFfmpeg=FFmpeg - 画面録画と GIF 録画に必要
japanese.CompOcr=Tesseract OCR (11 言語) - 文字認識に必要
japanese.GroupOther=その他:
japanese.TaskStartup=Windows の起動時に開始
japanese.TaskPrintScreen=PrintScreen キーを EagleShot で使う (Snipping Tool の代わりに)
#if FileExists(AddBackslash(CompilerPath) + "Languages\Korean.isl")
korean.FullInstall=전체 설치
korean.CustomInstall=사용자 지정 설치
korean.CompFfmpeg=FFmpeg - 화면 및 GIF 녹화에 필요
korean.CompOcr=Tesseract OCR (11개 언어) - 텍스트 인식에 필요
korean.GroupOther=기타:
korean.TaskStartup=Windows와 함께 시작
korean.TaskPrintScreen=PrintScreen 키를 캡처 도구 대신 EagleShot에 사용
#endif

[Types]
Name: "full"; Description: "{cm:FullInstall}"
Name: "custom"; Description: "{cm:CustomInstall}"; Flags: iscustom

[Components]
Name: "main"; Description: "EagleShot"; Types: full custom; Flags: fixed
; Only offered when the tool isn't already installed on this computer
Name: "ffmpeg"; Description: "{cm:CompFfmpeg}"; Types: full; Check: not FfmpegInstalled
Name: "ocr"; Description: "{cm:CompOcr}"; Types: full; Check: not TesseractInstalled

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "startup"; Description: "{cm:TaskStartup}"; GroupDescription: "{cm:GroupOther}"
Name: "printscreen"; Description: "{cm:TaskPrintScreen}"; GroupDescription: "{cm:GroupOther}"

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
