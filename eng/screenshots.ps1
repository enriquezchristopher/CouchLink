<#
.SYNOPSIS
  Captures the README and guide screenshots from the built app with UI Automation.
.DESCRIPTION
  Starts a host copy (--test-pattern --windowed-player skips the single-instance check) and a client
  copy (--windowed-player) on this PC. The host needs ViGEmBus. Saves PNGs into docs/images.
#>
param(
    [string]$Exe = "src/CouchLink.App/bin/Debug/net10.0-windows/CouchLink.App.exe",
    [string]$Out = "docs/images"
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class Native {
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
  [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out RECT rect, int size);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
}
"@
$AE = [System.Windows.Automation.AutomationElement]
$Root = $AE::RootElement
$IdProp = $AE::AutomationIdProperty
$NameProp = $AE::NameProperty
$PidProp = $AE::ProcessIdProperty
$Scope = [System.Windows.Automation.TreeScope]

function Prop($property, $value) { New-Object System.Windows.Automation.PropertyCondition($property, $value) }

function Wait-Element($parent, $condition, [string]$what, [int]$ms = 15000) {
    $sw = [Diagnostics.Stopwatch]::StartNew()
    while ($sw.ElapsedMilliseconds -lt $ms) {
        $found = $parent.FindFirst($Scope::Descendants, $condition)
        if ($found) { return $found }
        Start-Sleep -Milliseconds 200
    }
    throw "Timed out waiting for $what"
}

function Wait-Window([int]$processId, [string]$name) {
    $condition = New-Object System.Windows.Automation.AndCondition((Prop $PidProp $processId), (Prop $NameProp $name))
    $sw = [Diagnostics.Stopwatch]::StartNew()
    while ($sw.ElapsedMilliseconds -lt 15000) {
        $w = $Root.FindFirst($Scope::Children, $condition)
        if ($w) { return $w }
        Start-Sleep -Milliseconds 200
    }
    throw "Window '$name' of process $processId did not appear"
}

function Close-CrashDialog([int]$processId) {
    $sw = [Diagnostics.Stopwatch]::StartNew()
    while ($sw.ElapsedMilliseconds -lt 5000) {
        $crash = $Root.FindAll($Scope::Children, (Prop $PidProp $processId)) | Where-Object { $_.Current.Name -like "CouchLink crashed*" } | Select-Object -First 1
        if ($crash) {
            (Wait-Element $crash (Prop $IdProp "CloseButton") "crash dialog close").GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
            Start-Sleep -Milliseconds 800
            return
        }
        Start-Sleep -Milliseconds 200
    }
}

# Owned windows are listed under their owner, not under the desktop root.
function Wait-Owned($owner, [int]$processId, [string]$name) {
    $condition = New-Object System.Windows.Automation.AndCondition((Prop $NameProp $name), (Prop ($AE::ControlTypeProperty) ([System.Windows.Automation.ControlType]::Window)))
    $sw = [Diagnostics.Stopwatch]::StartNew()
    while ($sw.ElapsedMilliseconds -lt 15000) {
        $w = $owner.FindFirst($Scope::Descendants, $condition)
        if (-not $w) { $w = $Root.FindAll($Scope::Descendants, $condition) | Where-Object { $_.Current.ProcessId -eq $processId } | Select-Object -First 1 }
        if ($w) { return $w }
        Start-Sleep -Milliseconds 200
    }
    throw "Window '$name' did not appear"
}

function Id($window, [string]$id) { Wait-Element $window (Prop $IdProp $id) $id }

function Invoke-Id($window, [string]$id) {
    (Id $window $id).GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    Start-Sleep -Milliseconds 600
}

function Save-Shot($window, [string]$name) {
    $hwnd = [IntPtr]$window.Current.NativeWindowHandle
    [Native]::SetForegroundWindow($hwnd) | Out-Null
    Start-Sleep -Milliseconds 500
    $r = New-Object Native+RECT
    [Native]::DwmGetWindowAttribute($hwnd, 9, [ref]$r, 16) | Out-Null # DWMWA_EXTENDED_FRAME_BOUNDS
    $bmp = New-Object System.Drawing.Bitmap ($r.Right - $r.Left), ($r.Bottom - $r.Top)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($r.Left, $r.Top, 0, 0, $bmp.Size)
    $g.Dispose()
    $bmp.Save((Join-Path (Resolve-Path $Out) "$name.png"), [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    Write-Host "saved $name.png"
}

$host1 = Start-Process $Exe -ArgumentList '--test-pattern', '--windowed-player' -PassThru
$client = $null
try {
    Close-CrashDialog $host1.Id
    $main = Wait-Window $host1.Id 'CouchLink'
    Start-Sleep -Seconds 1
    Save-Shot $main 'start'

    Invoke-Id $main 'HeaderControls'
    $controls = Wait-Owned $main $host1.Id 'Controls'
    Save-Shot $controls 'controls'
    $profile = Id $controls 'Profile'
    $profile.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
    Start-Sleep -Milliseconds 400
    $nba = $Root.FindAll($Scope::Descendants, (Prop $NameProp 'NBA 2K22')) | Where-Object { $_.GetSupportedPatterns() -contains [System.Windows.Automation.SelectionItemPattern]::Pattern } | Select-Object -First 1
    if (-not $nba) { throw 'NBA 2K22 profile item not found' }
    $nba.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    (Id $controls 'ShowLabels').GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle()
    Start-Sleep -Milliseconds 600
    Save-Shot $controls 'controls-profile'
    Invoke-Id $controls 'Reset'
    (Id $controls 'ShowLabels').GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle()
    Invoke-Id $controls 'Done'

    Invoke-Id $main 'HostButton'
    Start-Sleep -Seconds 2
    Save-Shot $main 'host-lobby'
    Save-Shot $main 'host-lobby-quality'

    $client = Start-Process $Exe -ArgumentList '--windowed-player' -PassThru
    Close-CrashDialog $client.Id
    $clientMain = Wait-Window $client.Id 'CouchLink'
    Invoke-Id $clientMain 'JoinButton'
    Start-Sleep -Seconds 4 # the list needs a moment to show the host on this PC
    Save-Shot $clientMain 'join-list'
    (Id $clientMain 'AddressBox').GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue('127.0.0.1')
    Invoke-Id $clientMain 'AddressJoin'
    $toast = Wait-Element $Root (Prop $IdProp 'ApprovalToast') 'approval toast'
    Start-Sleep -Seconds 1
    Save-Shot $clientMain 'session-waiting'
    Save-Shot $toast 'approval'
    Invoke-Id $toast 'Allow'
    Start-Sleep -Seconds 3

    $stats = Id $main 'StreamStats'
    $stats.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
    Start-Sleep -Seconds 1
    # Scroll the lobby to the bottom so the expanded stats are fully in view.
    $walker = [System.Windows.Automation.TreeWalker]::ControlViewWalker
    for ($p = $walker.GetParent($stats); $p; $p = $walker.GetParent($p)) {
        if ($p.GetSupportedPatterns() -contains [System.Windows.Automation.ScrollPattern]::Pattern) {
            $scroll = $p.GetCurrentPattern([System.Windows.Automation.ScrollPattern]::Pattern)
            if ($scroll.Current.VerticallyScrollable) {
                $scroll.SetScrollPercent([System.Windows.Automation.ScrollPattern]::NoScroll, 100)
                break
            }
        }
    }
    Start-Sleep -Milliseconds 600
    Save-Shot $main 'host-lobby-details'

    $stop = Id $main 'StopHosting'
    $stop.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke() # returns once the dialog shows
    $confirm = Wait-Element $main (Prop $IdProp 'DialogOk') 'stop confirm'
    $dialog = [System.Windows.Automation.TreeWalker]::ControlViewWalker.GetParent($confirm)
    while ($dialog.Current.ControlType -ne [System.Windows.Automation.ControlType]::Window) {
        $dialog = [System.Windows.Automation.TreeWalker]::ControlViewWalker.GetParent($dialog)
    }
    Save-Shot $dialog 'stop-confirm'
    $confirm.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
}
finally {
    if ($client -and -not $client.HasExited) { $client.Kill() }
    if (-not $host1.HasExited) { $host1.Kill() }
}
