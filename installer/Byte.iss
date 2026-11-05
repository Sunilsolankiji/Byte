#ifndef ByteVersion
  #error Supply /DByteVersion with the published application version.
#endif

[Setup]
AppId={{98511899-8AC7-42F0-947C-750F85F5D827}
AppName=Byte
AppVersion={#ByteVersion}
AppPublisher=Sunilsolankiji
AppPublisherURL=https://github.com/Sunilsolankiji/Byte
AppSupportURL=https://github.com/Sunilsolankiji/Byte/issues
AppUpdatesURL=https://github.com/Sunilsolankiji/Byte/releases
DefaultDirName={localappdata}\Programs\Byte
DefaultGroupName=Byte
PrivilegesRequired=lowest
ArchitecturesAllowed=x64os
ArchitecturesInstallIn64BitMode=x64os
MinVersion=10.0.22000
AppMutex=Local\Byte.DesktopCompanion
CloseApplications=no
RestartApplications=no
UninstallDisplayIcon={app}\Byte.exe
LicenseFile=..\LICENSE
OutputDir=..\dist
OutputBaseFilename=Byte-Setup-{#ByteVersion}-win-x64
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
DisableProgramGroupPage=no
SetupLogging=yes

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked

[Files]
Source: "..\dist\Byte\Byte.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\dist\Byte\README.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\dist\Byte\LICENSE"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\Byte"; Filename: "{app}\Byte.exe"
Name: "{autodesktop}\Byte"; Filename: "{app}\Byte.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\Byte.exe"; Description: "Launch Byte"; Flags: nowait postinstall skipifsilent

[Code]
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  StartupCommand: String;
begin
  if CurUninstallStep = usUninstall then
  begin
    { Leave another portable copy's login entry and all user preferences alone. }
    if RegQueryStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run',
      'ByteDesktopCompanion', StartupCommand) then
      if CompareText(StartupCommand, '"' + ExpandConstant('{app}\Byte.exe') + '"') = 0 then
        RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run',
          'ByteDesktopCompanion');
  end;
end;
