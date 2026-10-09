; CouchLink setup. Built by eng/package.ps1:
;   ISCC.exe /DAppVersion=1.6.0 /DStageDir=<staged app> /DViGEmBusSetup=<ViGEmBus exe> /DOutputDir=<dir> installer\CouchLink.iss
; Spec: docs/superpowers/specs/2026-10-08-couchlink-cafe-install-design.md

#ifndef AppVersion
  #error Pass /DAppVersion=<version>
#endif
#ifndef StageDir
  #error Pass /DStageDir=<the staged app folder>
#endif
#ifndef ViGEmBusSetup
  #error Pass /DViGEmBusSetup=<path to ViGEmBus_1.22.0_x64_x86_arm64.exe>
#endif
#ifndef OutputDir
  #define OutputDir "."
#endif
#define GuideUrl "https://github.com/enriquezchristopher/CouchLink/blob/main/docs/cafe-setup-guide.md"

[Setup]
; Never change AppId: it is how a new setup finds the installed CouchLink.
AppId={{31D75DA0-B0D5-4551-A9AE-F461A7851EE1}
AppName=CouchLink
AppVersion={#AppVersion}
AppVerName=CouchLink {#AppVersion}
AppPublisher=CouchLink contributors
AppPublisherURL=https://github.com/enriquezchristopher/CouchLink
AppSupportURL=https://github.com/enriquezchristopher/CouchLink/issues
VersionInfoVersion={#AppVersion}
VersionInfoProductVersion={#AppVersion}
DefaultDirName={autopf}\CouchLink
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
LicenseFile={#StageDir}\LICENSE
OutputDir={#OutputDir}
OutputBaseFilename=CouchLink-Setup-v{#AppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
SetupLogging=yes
UninstallDisplayName=CouchLink
UninstallDisplayIcon={app}\CouchLink.App.exe
; The setup program's own icon; the installed exe carries the same one.
SetupIconFile={#SourcePath}\..\assets\couchlink.ico

[Messages]
WindowsVersionNotSupported=CouchLink needs 64-bit Windows 10 or later.
OnlyOnTheseArchitectures=CouchLink needs 64-bit Windows 10 or later.

[Tasks]
Name: desktopicon; Description: "Create a desktop shortcut"

[Files]
Source: "{#StageDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#ViGEmBusSetup}"; Flags: dontcopy

[Icons]
Name: "{autoprograms}\CouchLink"; Filename: "{app}\CouchLink.App.exe"
Name: "{autodesktop}\CouchLink"; Filename: "{app}\CouchLink.App.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\CouchLink.App.exe"; Description: "Start CouchLink"; Flags: postinstall nowait skipifsilent runasoriginaluser
Filename: "{#GuideUrl}"; Description: "Open the setup guide"; Flags: postinstall nowait skipifsilent shellexec runasoriginaluser

[Code]
const
  UninstallKey = 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{31D75DA0-B0D5-4551-A9AE-F461A7851EE1}_is1';
  ExitNewerInstalled = 12;
  ViGEmBusFile = 'ViGEmBus_1.22.0_x64_x86_arm64.exe';
  ExitViGEmBusFailed = 10;
  ExitFirewallFailed = 11;

procedure ExitProcess(ExitCode: Cardinal); external 'ExitProcess@kernel32.dll stdcall';

{ -1, 0 or 1 as dotted version A is older than, the same as or newer than B. }
function CompareVersions(A, B: String): Integer;
var
  DotA, DotB, PartA, PartB: Integer;
begin
  Result := 0;
  while (Result = 0) and ((A <> '') or (B <> '')) do
  begin
    DotA := Pos('.', A);
    if DotA = 0 then DotA := Length(A) + 1;
    DotB := Pos('.', B);
    if DotB = 0 then DotB := Length(B) + 1;
    PartA := StrToIntDef(Copy(A, 1, DotA - 1), 0);
    PartB := StrToIntDef(Copy(B, 1, DotB - 1), 0);
    if PartA < PartB then Result := -1
    else if PartA > PartB then Result := 1;
    Delete(A, 1, DotA);
    Delete(B, 1, DotB);
  end;
end;

{ Installing over a newer CouchLink is refused; the same version reinstalls (a repair). }
function InitializeSetup(): Boolean;
var
  Installed: String;
begin
  Result := True;
  if RegQueryStringValue(HKLM, UninstallKey, 'DisplayVersion', Installed)
     and (CompareVersions(Installed, '{#AppVersion}') > 0) then
  begin
    Log('CouchLink ' + Installed + ' is installed, newer than this setup ({#AppVersion}).');
    if WizardSilent() then
      ExitProcess(ExitNewerInstalled);
    MsgBox('A newer CouchLink (v' + Installed + ') is already installed.', mbError, MB_OK);
    Result := False;
  end;
end;

var
  CustomExitCode: Integer;
  RestartNeeded: Boolean;

{ The four firewall rules, by index 0-3. Names and ports: section 3 of the main design. }
procedure FirewallRule(Index: Integer; var Name, Protocol, Port: String);
begin
  case Index of
    0: begin Name := 'CouchLink (UDP 47800, discovery)'; Protocol := 'UDP'; Port := '47800'; end;
    1: begin Name := 'CouchLink (TCP 47801, sessions)'; Protocol := 'TCP'; Port := '47801'; end;
    2: begin Name := 'CouchLink (UDP 47802, video and audio)'; Protocol := 'UDP'; Port := '47802'; end;
    3: begin Name := 'CouchLink (UDP 47803, input)'; Protocol := 'UDP'; Port := '47803'; end;
  end;
end;

function Netsh(Params: String): Integer;
begin
  if not Exec(ExpandConstant('{sys}\netsh.exe'), Params, '', SW_HIDE, ewWaitUntilTerminated, Result) then
    Log('netsh did not start: ' + SysErrorMessage(Result));
  Log(Format('netsh %s -> %d', [Params, Result]));
end;

procedure Fail(ExitCode: Integer; Message: String);
begin
  if CustomExitCode = 0 then
    CustomExitCode := ExitCode;
  Log(Message);
  SuppressibleMsgBox(Message, mbError, MB_OK, IDOK);
end;

{ Deletes then adds each rule, so a repair or an upgrade never doubles them. With the Windows
  Firewall service off nothing is blocked and netsh can't add rules, so there is nothing to do;
  running setup again after the firewall is turned on adds them. }
procedure AddFirewallRules();
var
  I: Integer;
  Name, Protocol, Port: String;
begin
  if Netsh('advfirewall show currentprofile state') <> 0 then
  begin
    Log('Windows Firewall is not running on this PC, so no rules are needed. '
        + 'If it is turned on later, run this setup again to add them.');
    exit;
  end;
  for I := 0 to 3 do
  begin
    FirewallRule(I, Name, Protocol, Port);
    Netsh('advfirewall firewall delete rule name="' + Name + '"');
    if Netsh('advfirewall firewall add rule name="' + Name + '" dir=in action=allow enable=yes'
             + ' profile=any remoteip=localsubnet protocol=' + Protocol + ' localport=' + Port
             + ' program="' + ExpandConstant('{app}\CouchLink.App.exe') + '"') <> 0 then
      Fail(ExitFirewallFailed, 'CouchLink is installed, but Windows Firewall didn''t accept the rule "'
           + Name + '". Other PCs may not find or reach this one. See the setup guide.');
  end;
end;

procedure RemoveFirewallRules();
var
  I: Integer;
  Name, Protocol, Port: String;
begin
  for I := 0 to 3 do
  begin
    FirewallRule(I, Name, Protocol, Port);
    Netsh('advfirewall firewall delete rule name="' + Name + '"');
  end;
end;

function ViGEmBusInstalled(): Boolean;
begin
  Result := RegKeyExists(HKLM, 'SYSTEM\CurrentControlSet\Services\ViGEmBus');
end;

procedure InstallViGEmBus();
var
  Code: Integer;
begin
  if ViGEmBusInstalled() then
  begin
    Log('ViGEmBus is already installed.');
    exit;
  end;
  WizardForm.StatusLabel.Caption := 'Installing the ViGEmBus driver...';
  ExtractTemporaryFile(ViGEmBusFile);
  if not Exec(ExpandConstant('{tmp}\') + ViGEmBusFile, '/exenoui /qn /norestart', '', SW_HIDE,
              ewWaitUntilTerminated, Code) then
    Log('The ViGEmBus installer did not start: ' + SysErrorMessage(Code));
  Log(Format('The ViGEmBus installer exited with code %d.', [Code]));
  if Code = 3010 then
    RestartNeeded := True
  else if (Code <> 0) or not ViGEmBusInstalled() then
    Fail(ExitViGEmBusFailed, Format('CouchLink is installed, but the ViGEmBus driver didn''t install (code %d). '
         + 'Virtual controllers won''t work on this PC until it is installed. See the setup guide.', [Code]));
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
  begin
    InstallViGEmBus();
    AddFirewallRules();
  end;
end;

function NeedRestart(): Boolean;
begin
  Result := RestartNeeded;
end;

function GetCustomSetupExitCode(): Integer;
begin
  Result := CustomExitCode;
end;

{ Uninstall: ask CouchLink to close, end it if it hasn't after 3 s, and remove the rules.
  ViGEmBus and %LOCALAPPDATA%\CouchLink stay. }
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Code: Integer;
begin
  if CurUninstallStep = usUninstall then
  begin
    Exec(ExpandConstant('{sys}\taskkill.exe'), '/IM CouchLink.App.exe', '', SW_HIDE, ewWaitUntilTerminated, Code);
    Sleep(3000);
    Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM CouchLink.App.exe', '', SW_HIDE, ewWaitUntilTerminated, Code);
    RemoveFirewallRules();
  end;
end;
