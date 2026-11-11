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
#if Arch == "x64"
Name: "vbcable"; Description: "Installa il cavo audio virtuale VB-CABLE (VB-Audio, donationware), scaricato dal sito del produttore. Saltato se è già presente."; GroupDescription: "Programmi digitali sul client (WSJT-X, JTDX, fldigi…):"; Check: not VbCablePresent
#endif

[Files]
Source: "{#SourceDir}\IP-TRX-CTRL.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\..\THIRD-PARTY-NOTICES.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\..\LICENSE"; DestDir: "{app}"; DestName: "LICENSE.txt"; Flags: ignoreversion
Source: "..\..\README.md"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\IP-TRX-CTRL"; Filename: "{app}\IP-TRX-CTRL.exe"
Name: "{autodesktop}\IP-TRX-CTRL"; Filename: "{app}\IP-TRX-CTRL.exe"; Tasks: desktopicon

[Run]
; firewall di Windows: toglie le regole vecchie del programma (anche i blocchi creati premendo «Annulla»
; alla prima richiesta di Windows) e consente le connessioni in arrivo su tutte le reti
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=all program=""{app}\IP-TRX-CTRL.exe"""; Flags: runhidden waituntilterminated; Check: IsAdminInstallMode; StatusMsg: "Firewall di Windows: tolgo le regole vecchie…"
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall add rule name=""IP-TRX-CTRL"" dir=in action=allow program=""{app}\IP-TRX-CTRL.exe"" enable=yes profile=any"; Flags: runhidden waituntilterminated; Check: IsAdminInstallMode; StatusMsg: "Firewall di Windows: consento IP-TRX-CTRL…"
Filename: "{app}\IP-TRX-CTRL.exe"; Description: "{cm:LaunchProgram,IP-TRX-CTRL}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=all program=""{app}\IP-TRX-CTRL.exe"""; Flags: runhidden waituntilterminated; Check: IsAdminInstallMode; RunOnceId: "TogliRegoleFirewall"

#if Arch == "x64"
[Code]
// VB-CABLE non viene ridistribuito: lo scarica l'installatore dal sito di VB-Audio solo se l'utente lo chiede.
const
  VbCableUrl = 'https://download.vb-audio.com/Download_CABLE/VBCABLE_Driver_Pack45.zip';

function VbCablePresent: Boolean;
begin
  Result := DirExists(ExpandConstant('{commonpf64}\VB\CABLE')) or DirExists(ExpandConstant('{commonpf32}\VB\CABLE'))
            or FileExists(ExpandConstant('{sys}\drivers\vbaudio_cable64_win10.sys'))
            or FileExists(ExpandConstant('{sys}\drivers\vbaudio_cable64_win7.sys'));
end;

procedure InstallVbCable;
var
  Zip, Dir: String;
  Code: Integer;
begin
  Zip := ExpandConstant('{tmp}\vbcable.zip');
  Dir := ExpandConstant('{tmp}\vbcable');
  try
    DownloadTemporaryFile(VbCableUrl, 'vbcable.zip', '', nil);
  except
    MsgBox('Non riesco a scaricare VB-CABLE (' + GetExceptionMessage + '). Puoi installarlo più tardi da https://vb-audio.com/Cable/', mbInformation, MB_OK);
    exit;
  end;
  Exec('powershell.exe', '-NoProfile -ExecutionPolicy Bypass -Command "Expand-Archive -Force -LiteralPath ''' + Zip + ''' -DestinationPath ''' + Dir + '''"',
       '', SW_HIDE, ewWaitUntilTerminated, Code);
  if not FileExists(Dir + '\VBCABLE_Setup_x64.exe') then
  begin
    MsgBox('Pacchetto VB-CABLE non riconosciuto. Puoi installarlo più tardi da https://vb-audio.com/Cable/', mbInformation, MB_OK);
    exit;
  end;
  if not ShellExec('runas', Dir + '\VBCABLE_Setup_x64.exe', '-i -h', Dir, SW_SHOW, ewWaitUntilTerminated, Code) then
    MsgBox('Installazione di VB-CABLE non avviata. Puoi installarlo più tardi da https://vb-audio.com/Cable/', mbInformation, MB_OK)
  else
    MsgBox('VB-CABLE installato. Riavvia il PC prima di usarlo con i programmi digitali.', mbInformation, MB_OK);
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if (CurStep = ssPostInstall) and WizardIsTaskSelected('vbcable') then
    InstallVbCable;
end;
#endif
