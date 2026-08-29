; Inno Setup script for DisplayTiler.
;
; Produces a per-user installer. Per-user is deliberate: DisplayTiler installs a low-level keyboard
; hook and writes its "start with Windows" entry under HKCU, so it has no reason to ask for
; administrator rights or to place anything machine-wide. That also means it installs and updates
; without a UAC prompt.
;
; Build:  ISCC.exe installer\DisplayTiler.iss /DAppVersion=1.0.0

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif

#define AppName "DisplayTiler"
#define AppPublisher "DisplayTiler contributors"
#define AppUrl "https://github.com/mwdlyt/DisplayTiler"
#define AppExeName "DisplayTiler.exe"

[Setup]
AppId={{8B1F5E42-3C97-4C1E-9E1A-6D2A7C4B9F03}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}/issues
AppUpdatesURL={#AppUrl}/releases
VersionInfoVersion={#AppVersion}

; Per-user install, so no UAC prompt and no machine-wide state.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
DisableDirPage=auto

; Windows 11 only. The switcher relies on DWM thumbnail composition and modern window attributes,
; and the layout is designed against Windows 11 metrics.
MinVersion=10.0.22000

OutputDir=..\dist\installer
OutputBaseFilename=DisplayTiler-{#AppVersion}-setup
SetupIconFile=..\native\DisplayTiler.Host\DisplayTiler.ico
UninstallDisplayIcon={app}\{#AppExeName}
UninstallDisplayName={#AppName}
WizardStyle=modern
; The payload is one large, deliberately uncompressed self-contained executable, so this is where the
; download size is won back: ~115 MB on disk compresses to ~45 MB here. Compressing at install time
; costs the user once, whereas compressing inside the executable would cost memory at every launch -
; see the note in DisplayTiler.Host.csproj.
Compression=lzma2/max
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked
Name: "startupentry"; Description: "Start {#AppName} when I sign in to Windows"; GroupDescription: "Startup:"

[Files]
Source: "..\dist\win-x64\{#AppExeName}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Registry]
; DisplayTiler manages this key itself from its Settings dialog, so the installer only seeds it and
; always removes it on uninstall. A stale Run entry pointing at a deleted executable produces a
; failed-startup error at every sign-in, which is a genuinely annoying thing to leave behind.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; \
    ValueName: "{#AppName}"; ValueData: """{app}\{#AppExeName}"" --startup"; \
    Flags: uninsdeletevalue; Tasks: startupentry
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; \
    ValueName: "{#AppName}"; Flags: deletevalue uninsdeletevalue; Tasks: not startupentry

[Run]
; Opens Settings rather than starting silently to the tray. DisplayTiler has no main window, so a
; silent start leaves the installer finishing with nothing on screen and no confirmation that it
; worked. The Settings dialog doubles as that confirmation and as the first look at what is
; configurable; closing it returns the app to the tray.
Filename: "{app}\{#AppExeName}"; Parameters: "--settings"; Description: "Start {#AppName} and open Settings"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; The running instance holds its own executable open, and it owns a system-wide keyboard hook.
; Stop it before removing files so the uninstall does not fail or leave the hook installed.
Filename: "{sys}\taskkill.exe"; Parameters: "/f /im {#AppExeName}"; Flags: runhidden; RunOnceId: "StopDisplayTiler"

[Code]
// Settings live in %LOCALAPPDATA%\DisplayTiler. They are left alone on upgrade, and removed only if
// the person uninstalling says they want them gone.
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  SettingsPath: String;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    SettingsPath := ExpandConstant('{localappdata}\{#AppName}');
    if DirExists(SettingsPath) then
      if MsgBox('Remove your DisplayTiler settings as well?', mbConfirmation, MB_YESNO) = IDYES then
        DelTree(SettingsPath, True, True, True);
  end;
end;

// Stop a running copy before overwriting the executable it is currently using.
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
begin
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/f /im {#AppExeName}', '',
       SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Result := '';
end;
