<#
.SYNOPSIS
    一键发布 CZR 智能称重系统：Release + 自包含（win-x64）发布，并打包为 zip。
.DESCRIPTION
    产物：
      发布目录  src\CZRWeighSystem\bin\Release\net10.0-windows\win-x64\publish
      zip 包    dist\CZRWeighSystem_yyyyMMdd_HHmm.zip
    现场部署：解压后把整个文件夹拷到目标机（64 位 Windows），运行 CZRWeighSystem.exe，
    自包含发布无需目标机安装 .NET；libvlc\win-x64 必须与 exe 同目录，不可只拷 exe。
.EXAMPLE
    powershell -ExecutionPolicy Bypass -File publish.ps1
#>
$ErrorActionPreference = 'Stop'

$root       = $PSScriptRoot
$project    = Join-Path $root 'src\CZRWeighSystem\CZRWeighSystem.csproj'
$publishDir = Join-Path $root 'src\CZRWeighSystem\bin\Release\net10.0-windows\win-x64\publish'
$distDir    = Join-Path $root 'dist'
$zipPath    = Join-Path $distDir ("CZRWeighSystem_{0}.zip" -f (Get-Date -Format 'yyyyMMdd_HHmm'))

Write-Host '==> 清理旧发布产物'
if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }

Write-Host '==> 发布（Release / 自包含 win-x64）'
dotnet publish $project -c Release -r win-x64 --self-contained true
if ($LASTEXITCODE -ne 0) { throw "发布失败，退出码 $LASTEXITCODE" }

# 首次运行会自动重建这些内容；清掉以免把本机的测试数据/日志打进安装包
foreach ($item in 'config.json', 'data', 'logs', 'captures', 'backups') {
    $path = Join-Path $publishDir $item
    if (Test-Path $path) { Remove-Item $path -Recurse -Force }
}

# 软件是 x64 进程，只需 libvlc\win-x64；删掉随包带出的 arm64/x86 原生库（省约 180 MB）
foreach ($arch in 'win-arm64', 'win-x86') {
    $path = Join-Path $publishDir "libvlc\$arch"
    if (Test-Path $path) { Remove-Item $path -Recurse -Force }
}

Write-Host '==> 打包 zip'
New-Item -ItemType Directory -Force -Path $distDir | Out-Null
Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory(
    $publishDir, $zipPath, [System.IO.Compression.CompressionLevel]::Optimal, $false)

Write-Host ('完成：{0}（{1:N1} MB）' -f $zipPath, ((Get-Item $zipPath).Length / 1MB))