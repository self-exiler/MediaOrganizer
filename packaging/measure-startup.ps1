# 冷/热启动耗时测量：从进程创建到主窗口句柄出现（time-to-first-window）
#
# 用法：
#   .\measure-startup.ps1 -ExePath ..\publish\win-x64\MediaOrganizer.Desktop.exe
#   .\measure-startup.ps1 -ExePath <exe> -Runs 7

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$ExePath,
    [int]$Runs = 5
)

$ErrorActionPreference = 'Stop'
$exe = (Resolve-Path $ExePath).Path

Add-Type -Namespace Win32 -Name Native -MemberDefinition @'
[System.Runtime.InteropServices.DllImport("user32.dll")]
public static extern bool SetForegroundWindow(System.IntPtr hWnd);
'@ | Out-Null

$times = New-Object System.Collections.Generic.List[double]

for ($i = 1; $i -le $Runs; $i++) {
    $sw = [System.Diagnostics.Stopwatch]::StartNew()

    $p = Start-Process -FilePath $exe -PassThru
    try {
        # 轮询主窗口句柄，比 WaitForInputIdle 更贴近“用户看到界面”的时刻
        $deadline = [DateTime]::Now.AddSeconds(30)
        while ($p.MainWindowHandle -eq 0 -and [DateTime]::Now -lt $deadline) {
            Start-Sleep -Milliseconds 15
            $p.Refresh()
        }
        $sw.Stop()
        if ($p.MainWindowHandle -eq 0) { throw "第 $i 次运行 30s 内未出现主窗口" }
    }
    finally {
        Start-Sleep -Milliseconds 200
        if (-not $p.HasExited) { $p.Kill(); $p.WaitForExit(5000) | Out-Null }
    }

    $ms = $sw.Elapsed.TotalMilliseconds
    $times.Add($ms)
    Write-Host ("  第 {0} 次：{1,7:N0} ms" -f $i, $ms)
    Start-Sleep -Milliseconds 600
}

$sorted = ($times | Sort-Object)
$median = $sorted[[math]::Floor($sorted.Count / 2)]
Write-Host ""
Write-Host ("首跑（最冷）：{0,7:N0} ms" -f $times[0])
Write-Host ("中位（热）  ：{0,7:N0} ms" -f $median)
Write-Host ("最优        ：{0,7:N0} ms" -f ($sorted[0]))
