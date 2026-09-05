#define AppVersion "2.0.30"
[Setup]
AppId={{145126A1-604E-4935-9E3A-395D765DAC22}
AppName=Softlight
AppVersion={#AppVersion}
AppPublisher=Artllex
AppPublisherURL=https://github.com/Artllex/Softlight
DefaultDirName={localappdata}\Programs\Softlight
DefaultGroupName=Softlight
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
OutputDir=..\dist
OutputBaseFilename=Softlight-{#AppVersion}-Setup-x64
SetupIconFile=..\assets\Softlight.ico
UninstallDisplayIcon={app}\Softlight.exe
LicenseFile=..\LICENSE
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
[Files]
Source: "..\Softlight.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\Softlight.exe.config"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\NocnyFiltr.Engine.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\assets\*"; DestDir: "{app}\assets"; Flags: ignoreversion recursesubdirs
Source: "..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\README.md"; DestDir: "{app}"; Flags: ignoreversion
[Icons]
Name: "{group}\Softlight"; Filename: "{app}\Softlight.exe"
[Run]
Filename: "{app}\Softlight.exe"; Description: "Launch Softlight"; Flags: nowait postinstall skipifsilent
[UninstallRun]
Filename: "{app}\Softlight.exe"; Parameters: "--exit"; Flags: runhidden waituntilterminated; RunOnceId: "StopSoftlight"
[Code]
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var Value: String;
begin
  if CurUninstallStep = usUninstall then
    if RegQueryStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'NocnyFiltrWindows', Value) then
      if Pos(Lowercase(ExpandConstant('{app}\Softlight.exe')), Lowercase(Value)) > 0 then
        RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'NocnyFiltrWindows');
end;
