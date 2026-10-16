<#
.SYNOPSIS
    运行全部单元测试（解决"本机只有 .NET 10 运行时、项目目标 net8.0"的框架不匹配问题）。
.DESCRIPTION
    主工程与测试工程目标框架均为 net8.0-windows，而开发机只装了 .NET 10 运行时，
    直接 `dotnet test` 会因找不到 Microsoft.NETCore.App 8.0.0 而无法启动测试宿主：
        Testhost 进程已退出，但出现错误: You must install or update .NET to run this application.
    本脚本在调用前设置 DOTNET_ROLL_FORWARD=LatestMajor，让 net8.0 产物向上滚动到
    已安装的 .NET 10 运行时执行。发布产物与现场运行不受影响（自包含发布带自己的运行时）。
.PARAMETER Filter
    传给 dotnet test 的过滤表达式，如 -Filter "FullyQualifiedName~FeeCalculator"。
.PARAMETER NoBuild
    跳过重新生成，直接用上次的编译产物跑测试（迭代时更快）。
.EXAMPLE
    powershell -ExecutionPolicy Bypass -File test.ps1
    powershell -ExecutionPolicy Bypass -File test.ps1 -Filter "FullyQualifiedName~Template" -NoBuild
#>
param(
    [string]$Filter = '',
    [switch]$NoBuild
)

$ErrorActionPreference = 'Stop'

# net8.0 产物在仅有 .NET 10 运行时的机器上向上滚动执行（未安装 .NET 8 时的必需项）
$env:DOTNET_ROLL_FORWARD = 'LatestMajor'

$solution = Join-Path $PSScriptRoot 'src\CZRWeighSystem.sln'
$arguments = @('test', $solution, '-c', 'Debug')
if ($NoBuild) { $arguments += '--no-build' }
if ($Filter)  { $arguments += @('--filter', $Filter) }

Write-Host '==> dotnet test（DOTNET_ROLL_FORWARD=LatestMajor）'
& dotnet @arguments
$code = $LASTEXITCODE

if ($code -ne 0) {
    Write-Host ''
    Write-Host "测试未通过（dotnet test 退出码 $code）" -ForegroundColor Red
}
exit $code
