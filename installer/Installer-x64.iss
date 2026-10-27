#define MyAppName "Study Cafe Companion"
#define MyAppPublisher "Study Cafe"
#define MyAppURL "https://github.com/ahmadtsu/study-cafe-companion"
#define MyAppExeName "StudyCafe.Companion.exe"
#define PublishDir "..\publish"

#ifndef MyAppVersion
  #define MyAppVersion "1.0.5"
#endif

#define ArchitecturesAllowed "x64os"
#define ArchitecturesInstallIn64BitMode "x64os"
#define OutputBaseName "StudyCafeCompanion-Setup-x64"

#include "InnoDependencyInstaller\CodeDependencies.iss"
#include "DotNetInstaller.iss"

[Setup]
AppId={{B92C5E78-2E55-4D1D-A14C-063CE47BF48E}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
UninstallDisplayIcon={app}\{#MyAppExeName}
ArchitecturesAllowed={#ArchitecturesAllowed}
ArchitecturesInstallIn64BitMode={#ArchitecturesInstallIn64BitMode}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
OutputDir=output
OutputBaseFilename={#OutputBaseName}
SetupIconFile=..\assets\logo.ico
SolidCompression=yes
Compression=lzma2/ultra64
WizardStyle=modern
DisableWelcomePage=no
CloseApplications=force

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#PublishDir}\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion restartreplace uninsrestartdelete
Source: "..\assets\logo.ico"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\logo.ico"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\logo.ico"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "taskkill.exe"; Parameters: "/F /IM {#MyAppExeName}"; Flags: runhidden; RunOnceId: "KillAppOnUninstall"

[UninstallDelete]
Type: filesandordirs; Name: "{app}"

[Code]
procedure KillAppIfRunning;
var
  ResultCode: Integer;
begin
  Exec('taskkill.exe', '/F /IM "{#MyAppExeName}"', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

function InitializeSetup: Boolean;
begin
  KillAppIfRunning;
  AddDotNet90DesktopDependency;
  Result := True;
end;

function InitializeUninstall: Boolean;
begin
  KillAppIfRunning;
  Result := True;
end;
