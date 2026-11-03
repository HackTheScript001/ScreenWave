; ScreenWave installer (Inno Setup 6)
; Built by GitHub Actions: iscc /DMyAppVersion=x.y.z installer\ScreenWave.iss

#ifndef MyAppVersion
  #define MyAppVersion "1.0.0"
#endif

#define MyAppName "ScreenWave"
#define MyAppPublisher "SecondWave IT Systems"
#define MyAppExe "ScreenWave.exe"
#define PublishDir "..\publish"

[Setup]
AppId={{3F6C2B7E-9A41-4D8E-B2C5-7E1A9D4F6B20}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL=https://secondwaveitsystems.cloud
AppCopyright=(c) SecondWave IT Systems. All rights reserved.
AppContact=liam@secondwaveitsystems.cloud
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
OutputDir=..\output
OutputBaseFilename=ScreenWave-{#MyAppVersion}-Setup
SetupIconFile=..\src\ScreenWave\Assets\ScreenWave.ico
UninstallDisplayIcon={app}\{#MyAppExe}
UninstallDisplayName={#MyAppName}
VersionInfoVersion={#MyAppVersion}
VersionInfoProductName={#MyAppName}
VersionInfoCompany={#MyAppPublisher}
VersionInfoCopyright=(c) SecondWave IT Systems
VersionInfoDescription={#MyAppName} - a SecondWave IT Systems product
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
LicenseFile=..\LICENSE
CloseApplications=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Messages]
SetupWindowTitle=%1 Setup - SecondWave IT Systems
WelcomeLabel2=This will install [name/ver] on your computer.%n%nScreenWave is a SecondWave IT Systems product. It speeds up Samsung TVs by removing the apps you don't use, over your own network. It never connects to the internet.%n%nIt is recommended that you close all other applications before continuing.
FinishedLabel=Setup has finished installing [name] on your computer.%n%nScreenWave is a SecondWave IT Systems product. Thanks for using it.

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "{#PublishDir}\{#MyAppExe}"; DestDir: "{app}"; Flags: ignoreversion
; Samsung's sdb tool (unmodified) and its licence
Source: "{#PublishDir}\sdb\*"; DestDir: "{app}\sdb"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExe}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; Make sure sdb's helper isn't left running
Filename: "{app}\sdb\sdb.exe"; Parameters: "kill-server"; Flags: runhidden skipifdoesntexist; RunOnceId: "KillSdb"

[UninstallDelete]
Type: filesandordirs; Name: "{app}"
