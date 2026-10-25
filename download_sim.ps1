# 模拟下载进度条脚本 —— 8 小时后到达 100%
# 在 PowerShell 终端运行: powershell -ExecutionPolicy Bypass -File download_sim.ps1

$ErrorActionPreference = "Stop"

# 总时长：8 小时 = 28800 秒
$totalSeconds = 8 * 3600

# 进度条宽度
$barWidth = 40

# 块字符
$block = [string]::new([char]0x2588)   # █ 已完成
$empty = [string]::new([char]0x2591)   # ░ 未完成

$startTime = Get-Date

Write-Host ""
Write-Host "  正在下载：代码包.zip" -ForegroundColor Cyan
Write-Host "  请勿关闭 PowerShell 终端，否则下载将被中断"
Write-Host ""

while ($true) {
    $elapsed = ((Get-Date) - $startTime).TotalSeconds

    if ($elapsed -ge $totalSeconds) {
        $percent = 100.0
    } else {
        $percent = [math]::Round(($elapsed / $totalSeconds) * 100, 2)
    }

    $filled = [math]::Floor(($percent / 100) * $barWidth)
    if ($filled -gt $barWidth) { $filled = $barWidth }
    $bar = ($block * $filled) + ($empty * ($barWidth - $filled))

    $elapsedSpan = [TimeSpan]::FromSeconds([math]::Min($elapsed, $totalSeconds))
    $elapsedStr = "{0:hh\:mm\:ss}" -f $elapsedSpan

    $remaining = [math]::Max(0, $totalSeconds - $elapsed)
    $remainingSpan = [TimeSpan]::FromSeconds($remaining)
    $remainingStr = "{0:hh\:mm\:ss}" -f $remainingSpan

    $line = "  [$bar] {0,6:N2}%  " -f $percent
    Write-Host -NoNewline "`r$line"

    if ($percent -ge 100) {
        Write-Host ""
        Write-Host ""
        Write-Host "  下载完成！" -ForegroundColor Green
        break
    }

    Start-Sleep -Seconds 1
}
