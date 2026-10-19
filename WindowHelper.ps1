param([string]$ProcessName = 'MajdataSync')

Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class WindowActivation {
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
}
'@

$process = Get-Process -Name $ProcessName -ErrorAction SilentlyContinue |
    Where-Object { $_.MainWindowHandle -ne 0 } |
    Select-Object -First 1
if ($null -eq $process) {
    throw "No visible $ProcessName window."
}

[WindowActivation]::ShowWindow($process.MainWindowHandle, 9) | Out-Null
[WindowActivation]::SetForegroundWindow($process.MainWindowHandle) | Out-Null
Write-Output "Activated $($process.Id)"
