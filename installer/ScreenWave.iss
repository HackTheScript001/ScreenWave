; ScreenWave installer (Inno Setup 6)
; Built by GitHub Actions: iscc /DMyAppVersion=x.y.z installer\ScreenWave.iss

#ifndef MyAppVersion
  #define MyAppVersion "1.0.0"
#endif

#define MyAppName "ScreenWave"
#define MyAppPublisher "SecondWave IT Systems"
#define MyAppExe "ScreenWave.exe"
#define PublishDir "..\publish"

; Everything that ISN'T your own network: ScreenWave and sdb are blocked from reaching these.
; Allowed: 10.x, 172.16-31.x, 192.168.x, 169.254.x, 127.x (this PC), and private/link-local IPv6.
#define PublicV4 "0.0.0.0-9.255.255.255,11.0.0.0-126.255.255.255,128.0.0.0-169.253.255.255,169.255.0.0-172.15.255.255,172.32.0.0-192.167.255.255,192.169.0.0-255.255.255.255"
#define PublicV6 "::2-fbff:ffff:ffff:ffff:ffff:ffff:ffff:ffff,fe00::-fe7f:ffff:ffff:ffff:ffff:ffff:ffff:ffff,fec0::-ffff:ffff:ffff:ffff:ffff:ffff:ffff:ffff"
#define FwName "ScreenWave - local network only"

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
; Windows Firewall: ScreenWave and sdb can only ever talk to your local network, never the internet
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""{#FwName}"""; Flags: runhidden; StatusMsg: "Locking ScreenWave to your local network..."
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall add rule name=""{#FwName}"" dir=out action=block program=""{app}\ScreenWave.exe"" remoteip={#PublicV4},{#PublicV6} enable=yes profile=any"; Flags: runhidden
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall add rule name=""{#FwName}"" dir=in action=block program=""{app}\ScreenWave.exe"" remoteip={#PublicV4},{#PublicV6} enable=yes profile=any"; Flags: runhidden
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall add rule name=""{#FwName}"" dir=out action=block program=""{app}\sdb\sdb.exe"" remoteip={#PublicV4},{#PublicV6} enable=yes profile=any"; Flags: runhidden
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall add rule name=""{#FwName}"" dir=in action=block program=""{app}\sdb\sdb.exe"" remoteip={#PublicV4},{#PublicV6} enable=yes profile=any"; Flags: runhidden
Filename: "{app}\{#MyAppExe}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent shellexec

[UninstallRun]
; Make sure sdb's helper isn't left running
Filename: "{app}\sdb\sdb.exe"; Parameters: "kill-server"; Flags: runhidden skipifdoesntexist; RunOnceId: "KillSdb"
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""{#FwName}"""; Flags: runhidden; RunOnceId: "RemoveFirewall"

[UninstallDelete]
Type: filesandordirs; Name: "{app}"
