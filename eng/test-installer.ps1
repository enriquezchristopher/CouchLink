<#
.SYNOPSIS
    Silent install, repair and uninstall of a CouchLink setup, checking files and firewall rules
    after each. Needs admin, and changes this PC: run it in CI or on a test PC.
.EXAMPLE
    ./eng/test-installer.ps1 -Setup artifacts/CouchLink-Setup-v1.6.0.exe
#>
param([Parameter(Mandatory)][string]$Setup)

$ErrorActionPreference = 'Stop'
$dir = Join-Path $env:ProgramFiles 'CouchLink'
$app = Join-Path $dir 'CouchLink.App.exe'
$rules = [ordered]@{
    'CouchLink (UDP 47800, discovery)'      = 'UDP', '47800'
    'CouchLink (TCP 47801, sessions)'       = 'TCP', '47801'
    'CouchLink (UDP 47802, video and audio)' = 'UDP', '47802'
    'CouchLink (UDP 47803, input)'          = 'UDP', '47803'
}

function Fail([string]$message) { throw "Installer check failed: $message" }

function Install([string]$what) {
    $log = Join-Path ([IO.Path]::GetTempPath()) "couchlink-setup-$what.log"
    $p = Start-Process (Resolve-Path $Setup) -Wait -PassThru `
        -ArgumentList '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', "/LOG=`"$log`""
    Write-Host "$what exited with code $($p.ExitCode) (log: $log)"
    if ($p.ExitCode -eq 10) {
        Write-Warning "ViGEmBus didn't install on this machine (code 10); the driver install stays a manual check."
    } elseif ($p.ExitCode -ne 0) {
        Get-Content $log -Tail 30 | Write-Host
        Fail "$what exited with code $($p.ExitCode)"
    }
}

function Check-Installed {
    if (-not (Test-Path $app)) { Fail "$app is missing" }
    $found = @(Get-NetFirewallRule -DisplayName 'CouchLink (*' -ErrorAction SilentlyContinue)
    if ((Get-Service MpsSvc).Status -ne 'Running') {
        # With Windows Firewall off nothing is blocked; setup adds no rules and still succeeds.
        Write-Warning 'Windows Firewall is not running here, so the rules themselves are not checked.'
        if ($found.Count -ne 0) { Fail "the firewall is off, but setup left rules: $($found.DisplayName -join '; ')" }
        return
    }
    if ($found.Count -ne $rules.Count) {
        Fail "expected $($rules.Count) CouchLink firewall rules, found $($found.Count): $($found.DisplayName -join '; ')"
    }
    foreach ($name in $rules.Keys) {
        $rule = $found | Where-Object DisplayName -eq $name
        if (-not $rule) { Fail "no firewall rule '$name'" }
        $port = $rule | Get-NetFirewallPortFilter
        $address = $rule | Get-NetFirewallAddressFilter
        $program = ($rule | Get-NetFirewallApplicationFilter).Program
        if ($rule.Direction -ne 'Inbound' -or $rule.Action -ne 'Allow' -or $rule.Enabled -ne 'True') { Fail "'$name' isn't an enabled inbound allow rule" }
        if ($rule.Profile -ne 'Any') { Fail "'$name' applies to $($rule.Profile), not every profile" }
        if ($port.Protocol -ne $rules[$name][0] -or $port.LocalPort -ne $rules[$name][1]) { Fail "'$name' is $($port.Protocol) $($port.LocalPort)" }
        if ($address.RemoteAddress -ne 'LocalSubnet') { Fail "'$name' allows $($address.RemoteAddress), not LocalSubnet" }
        if ($program -ne $app) { Fail "'$name' is for $program, not $app" }
    }
}

Install 'install'
Check-Installed
Install 'repair'   # a second run (repair or upgrade) must not double the rules
Check-Installed

$uninstaller = Join-Path $dir 'unins000.exe'
Start-Process $uninstaller -Wait -ArgumentList '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART'
# The uninstaller re-launches itself from %TEMP% and returns at once; wait for it to finish.
$deadline = (Get-Date).AddMinutes(2)
while ((Test-Path $uninstaller) -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 500 }
if (Test-Path $app) { Fail "$app is still there after uninstalling" }
$left = @(Get-NetFirewallRule -DisplayName 'CouchLink (*' -ErrorAction SilentlyContinue)
if ($left.Count -gt 0) { Fail "uninstall left firewall rules: $($left.DisplayName -join '; ')" }
Write-Host 'Installer round trip passed.'
