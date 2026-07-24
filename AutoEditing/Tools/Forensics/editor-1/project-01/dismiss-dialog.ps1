param([string]$RadioTextContains = $null)

Add-Type @"
using System;
using System.Text;
using System.Runtime.InteropServices;
public class W32 {
    [DllImport("user32.dll", CharSet=CharSet.Auto)] public static extern IntPtr FindWindow(string lpClassName, string lpWindowName);
    [DllImport("user32.dll", CharSet=CharSet.Auto)] public static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter, string lpszClass, string lpszWindow);
    [DllImport("user32.dll", CharSet=CharSet.Auto)] public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);
    [DllImport("user32.dll", CharSet=CharSet.Auto)] public static extern int GetClassName(IntPtr hWnd, StringBuilder lpString, int nMaxCount);
    [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr hWnd);
}
"@

$BM_CLICK = 0x00F5

function Get-ChildButtons($hwndParent) {
    $result = New-Object System.Collections.Generic.List[object]
    $child = [IntPtr]::Zero
    while ($true) {
        $child = [W32]::FindWindowEx($hwndParent, $child, $null, $null)
        if ($child -eq [IntPtr]::Zero) { break }
        $sbClass = New-Object System.Text.StringBuilder 256
        [W32]::GetClassName($child, $sbClass, 256) | Out-Null
        $sbText = New-Object System.Text.StringBuilder 256
        [W32]::GetWindowText($child, $sbText, 256) | Out-Null
        $result.Add([PSCustomObject]@{ hwnd = $child; class = $sbClass.ToString(); text = $sbText.ToString() })
    }
    return $result
}

$dlg = [W32]::FindWindow($null, "VEGAS Pro 20.0")
if ($dlg -eq [IntPtr]::Zero) {
    Write-Output "NO_DIALOG_FOUND"
    exit 3
}

Write-Output ("Found dialog hwnd=" + $dlg)
$buttons = Get-ChildButtons $dlg
foreach ($b in $buttons) { Write-Output ("  child: class=" + $b.class + " text='" + $b.text + "'") }

if ($RadioTextContains) {
    $radio = $buttons | Where-Object { $_.class -like "Button*" -and $_.text -like "*$RadioTextContains*" } | Select-Object -First 1
    if ($radio) {
        [W32]::SendMessage($radio.hwnd, $BM_CLICK, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
        Write-Output ("Clicked radio: " + $radio.text)
        Start-Sleep -Milliseconds 200
    } else {
        Write-Output "Radio button not found (may not be that dialog type)"
    }
}

$ok = $buttons | Where-Object { $_.class -like "Button*" -and $_.text -eq "OK" } | Select-Object -First 1
if ($ok) {
    [W32]::SendMessage($ok.hwnd, $BM_CLICK, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
    Write-Output "Clicked OK"
    exit 0
} else {
    Write-Output "OK button not found"
    exit 4
}
