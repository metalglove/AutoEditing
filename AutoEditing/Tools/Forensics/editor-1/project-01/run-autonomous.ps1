param(
    [string]$ProjectPath = "",
    [Parameter(Mandatory=$true)][string]$ScriptPath,
    [Parameter(Mandatory=$true)][string]$WatchFile1,
    [string]$WatchFile2 = "",
    [int]$TimeoutSeconds = 240
)
$WatchFiles = @($WatchFile1)
if ($WatchFile2 -ne "") { $WatchFiles += $WatchFile2 }

Add-Type @"
using System;
using System.Text;
using System.Runtime.InteropServices;
public class W32Dismiss {
    [DllImport("user32.dll", CharSet=CharSet.Auto)] public static extern IntPtr FindWindow(string lpClassName, string lpWindowName);
    [DllImport("user32.dll", CharSet=CharSet.Auto)] public static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter, string lpszClass, string lpszWindow);
    [DllImport("user32.dll", CharSet=CharSet.Auto)] public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);
    [DllImport("user32.dll", CharSet=CharSet.Auto)] public static extern int GetClassName(IntPtr hWnd, StringBuilder lpString, int nMaxCount);
    [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);
}
"@

$BM_CLICK = 0x00F5

function Get-ChildControls($hwndParent) {
    $result = New-Object System.Collections.Generic.List[object]
    $child = [IntPtr]::Zero
    while ($true) {
        $child = [W32Dismiss]::FindWindowEx($hwndParent, $child, $null, $null)
        if ($child -eq [IntPtr]::Zero) { break }
        $sbClass = New-Object System.Text.StringBuilder 256
        [W32Dismiss]::GetClassName($child, $sbClass, 256) | Out-Null
        $sbText = New-Object System.Text.StringBuilder 256
        [W32Dismiss]::GetWindowText($child, $sbText, 256) | Out-Null
        $result.Add([PSCustomObject]@{ hwnd = $child; class = $sbClass.ToString(); text = $sbText.ToString() })
    }
    return $result
}

function Try-DismissDialog {
    $dlg = [W32Dismiss]::FindWindow($null, "VEGAS Pro 20.0")
    if ($dlg -eq [IntPtr]::Zero) { return $false }

    $buttons = Get-ChildControls $dlg

    # Missing-media dialog: select "Ignore all missing files" radio BEFORE clicking OK
    # (default radio is "Search for missing file" -- never blind-OK this dialog).
    $radio = $buttons | Where-Object { $_.class -like "Button*" -and $_.text -like "*Ignore all missing files*" } | Select-Object -First 1
    if ($radio) {
        [W32Dismiss]::SendMessage($radio.hwnd, $BM_CLICK, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
        Write-Output ("Dialog: selected radio '" + $radio.text + "'")
        Start-Sleep -Milliseconds 200
    }

    $ok = $buttons | Where-Object { $_.class -like "Button*" -and $_.text -eq "OK" } | Select-Object -First 1
    if ($ok) {
        [W32Dismiss]::SendMessage($ok.hwnd, $BM_CLICK, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
        Write-Output "Dialog: clicked OK"
        return $true
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
        try { Try-DismissDialog | Out-Null } catch { Write-Output ("Dismiss attempt error: " + $_.Exception.Message) }
        $lastAttempt = Get-Date
    }

    Start-Sleep -Milliseconds 400
}

Write-Output "TIMEOUT waiting for watched files."
exit 2
