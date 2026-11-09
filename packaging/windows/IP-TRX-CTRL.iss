; Installatore Windows di IP-TRX-CTRL (Inno Setup 6)
; Uso: ISCC /DAppVersion=0.1.0 /DArch=x64|x86 /DSourceDir=<cartella con IP-TRX-CTRL.exe> /DOutDir=<uscita> IP-TRX-CTRL.iss

#ifndef AppVersion
  #define AppVersion "0.1.0"
#endif
#ifndef Arch
  #define Arch "x64"
#endif

[Setup]
AppId={{7C1E4A2B-5D33-4F0E-9B6A-1A2B3C4D5E6F}
AppName=IP-TRX-CTRL
AppVersion={#AppVersion}
AppVerName=IP-TRX-CTRL {#AppVersion}
AppPublisher=ic8tem
AppPublisherURL=https://github.com/ic8tem/IP-TRX-CTRL
AppSupportURL=https://github.com/ic8tem/IP-TRX-CTRL
DefaultDirName={autopf}\IP-TRX-CTRL
DefaultGroupName=IP-TRX-CTRL
DisableProgramGroupPage=yes
OutputDir={#OutDir}
OutputBaseFilename=IP-TRX-CTRL-{#AppVersion}-win-{#Arch}-setup
SetupIconFile=..\..\src\IpTrxCtrl\Assets\icon.ico
UninstallDisplayIcon={app}\IP-TRX-CTRL.exe
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
PrivilegesRequiredOverridesAllowed=dialog
InfoBeforeFile=..\..\THIRD-PARTY-NOTICES.txt
#if Arch == "x64"
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
#endif

[Languages]
Name: "italian"; MessagesFile: "compiler:Languages\Italian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "{#SourceDir}\IP-TRX-CTRL.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\..\THIRD-PARTY-NOTICES.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\..\README.md"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\IP-TRX-CTRL"; Filename: "{app}\IP-TRX-CTRL.exe"
Name: "{autodesktop}\IP-TRX-CTRL"; Filename: "{app}\IP-TRX-CTRL.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\IP-TRX-CTRL.exe"; Description: "{cm:LaunchProgram,IP-TRX-CTRL}"; Flags: nowait postinstall skipifsilent
