param(
    [string]$ProjectPath = "",
    [Parameter(Mandatory=$true)][string]$ScriptPath,
    [Parameter(Mandatory=$true)][string]$WatchFile1,
    [string]$WatchFile2 = "",
    [int]$TimeoutSeconds = 240,
    [switch]$AllowExistingInstance
)
# v2 fixes two gaps identified in the automation-safety-audit from the Editor 1 investigation:
# (1) refuses to launch if a VEGAS process already exists (prevents the window-matching
#     ambiguity that caused a real stuck-dialog incident during Editor 1's analysis);
# (2) scopes dialog detection to the PID this script itself launched, not by title alone.
$WatchFiles = @($WatchFile1)
if ($WatchFile2 -ne "") { $WatchFiles += $WatchFile2 }

$existing = Get-Process vegas200 -ErrorAction SilentlyContinue
if ($existing -and -not $AllowExistingInstance) {
    Write-Output ("BLOCKED: existing vegas200 process(es) found: " + ($existing.Id -join ",") + ". Close them first or pass -AllowExistingInstance.")
    exit 3
}

Add-Type @"
using System;
using System.Text;
using System.Runtime.InteropServices;
public class W32DismissV2 {
    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
    [DllImport("user32.dll", CharSet=CharSet.Auto)] public static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter, string lpszClass, string lpszWindow);
    [DllImport("user32.dll", CharSet=CharSet.Auto)] public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);
    [DllImport("user32.dll", CharSet=CharSet.Auto)] public static extern int GetClassName(IntPtr hWnd, StringBuilder lpString, int nMaxCount);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint procId);
    [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);
}
"@

$BM_CLICK = 0x00F5

function Get-ChildControls($hwndParent) {
    $result = New-Object System.Collections.Generic.List[object]
    $child = [IntPtr]::Zero
    while ($true) {
        $child = [W32DismissV2]::FindWindowEx($hwndParent, $child, $null, $null)
        if ($child -eq [IntPtr]::Zero) { break }
        $sbClass = New-Object System.Text.StringBuilder 256
        [W32DismissV2]::GetClassName($child, $sbClass, 256) | Out-Null
        $sbText = New-Object System.Text.StringBuilder 256
        [W32DismissV2]::GetWindowText($child, $sbText, 256) | Out-Null
        $result.Add([PSCustomObject]@{ hwnd = $child; class = $sbClass.ToString(); text = $sbText.ToString() })
    }
    return $result
}

# Find a visible top-level window owned by $targetPid whose title matches "VEGAS Pro 20.0"
# and is NOT the main project frame window (class Vegas.Class.Frame) -- i.e. a dialog.
function Find-OwnedDialog($targetPid) {
    $found = [IntPtr]::Zero
    $callback = {
        param($hWnd, $lParam)
        $procId = 0
        [W32DismissV2]::GetWindowThreadProcessId($hWnd, [ref]$procId) | Out-Null
        if ($procId -eq $targetPid -and [W32DismissV2]::IsWindowVisible($hWnd)) {
            $sb = New-Object System.Text.StringBuilder 256
            [W32DismissV2]::GetWindowText($hWnd, $sb, 256) | Out-Null
            $cls = New-Object System.Text.StringBuilder 256
            [W32DismissV2]::GetClassName($hWnd, $cls, 256) | Out-Null
            if ($sb.ToString() -eq "VEGAS Pro 20.0" -and $cls.ToString() -eq "#32770") {
                $script:foundDlg = $hWnd
                return $false
            }
        }
        return $true
    }
    $script:foundDlg = [IntPtr]::Zero
    [W32DismissV2]::EnumWindows($callback, [IntPtr]::Zero) | Out-Null
    return $script:foundDlg
}

function Try-DismissDialog($targetPid) {
    $dlg = Find-OwnedDialog $targetPid
    if ($dlg -eq [IntPtr]::Zero) { return $false }

    $buttons = Get-ChildControls $dlg

    $radio = $buttons | Where-Object { $_.class -like "Button*" -and $_.text -like "*Ignore all missing files*" } | Select-Object -First 1
    if ($radio) {
        [W32DismissV2]::SendMessage($radio.hwnd, $BM_CLICK, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
        Write-Output ("Dialog (pid=" + $targetPid + "): selected radio '" + $radio.text + "'")
        Start-Sleep -Milliseconds 200
    }

    $ok = $buttons | Where-Object { $_.class -like "Button*" -and $_.text -eq "OK" } | Select-Object -First 1
    if ($ok) {
        [W32DismissV2]::SendMessage($ok.hwnd, $BM_CLICK, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
        Write-Output ("Dialog (pid=" + $targetPid + "): clicked OK")
        return $true
    }
    if ($buttons.Count -eq 0) {
        Write-Output ("Dialog (pid=" + $targetPid + "): found but NO child controls -- not dismissing an unidentified dialog.")
    }
    return $false
}

$exe = "C:\Program Files\VEGAS\VEGAS Pro 20.0\vegas200.exe"
if ($ProjectPath -ne "") {
    $argList = @("`"$ProjectPath`"", "/RUNSCRIPT", "`"$ScriptPath`"")
} else {
    $argList = @("/RUNSCRIPT", "`"$ScriptPath`"")
}
$proc = Start-Process -FilePath $exe -ArgumentList $argList -PassThru
Write-Output ("Launched PID " + $proc.Id)

$deadline = (Get-Date).AddSeconds($TimeoutSeconds)
$lastAttempt = Get-Date

while ((Get-Date) -lt $deadline) {
    foreach ($f in $WatchFiles) {
        if (Test-Path $f) {
            Write-Output ("FOUND: " + $f)
            exit 0
        }
    }

    if ($proc.HasExited) {
        Write-Output "PROCESS EXITED before any watched file appeared."
        exit 1
    }

    if (((Get-Date) - $lastAttempt).TotalSeconds -ge 2) {
        try { Try-DismissDialog $proc.Id | Out-Null } catch { Write-Output ("Dismiss attempt error: " + $_.Exception.Message) }
        $lastAttempt = Get-Date
    }

    Start-Sleep -Milliseconds 400
}

Write-Output "TIMEOUT waiting for watched files."
exit 2
