<#
.SYNOPSIS
    一键发布「精锐计量称重软件」：Release + 自包含（win-x64）发布，打包 zip，可选生成安装包。
.DESCRIPTION
    产物：
      发布目录  src\CZRWeighSystem\bin\Release\net10.0-windows\win-x64\publish
      zip 包    dist\CZRWeighSystem_yyyyMMdd_HHmm.zip
      安装包    dist\精锐计量称重软件_安装包_V<版本>.exe   （加 -Installer 参数时）
    现场部署：用安装包安装（向导可选目录、建桌面快捷方式），或解压 zip 后整目录拷到目标机。
    自包含发布无需目标机安装 .NET；libvlc\win-x64 必须与 exe 同目录，不可只拷 exe。
.PARAMETER Installer
    同时用 Inno Setup 生成 setup.exe 安装包（需本机已装 Inno Setup 6，编译器默认在
    C:\Program Files (x86)\Inno Setup 6\ISCC.exe）。
.EXAMPLE
    powershell -ExecutionPolicy Bypass -File publish.ps1 -Installer
#>
param([switch]$Installer)

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

if (-not $Installer) { return }

Write-Host '==> 生成安装包（Inno Setup）'
$iscc = @(
    'C:\Program Files (x86)\Inno Setup 6\ISCC.exe',
    'C:\Program Files\Inno Setup 6\ISCC.exe'
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw '未找到 Inno Setup 编译器 ISCC.exe，请先安装 Inno Setup 6（winget install JRSoftware.InnoSetup）' }

# 版本号以 csproj 为准，避免安装包与程序标题版本不一致
$version = ([xml](Get-Content $project)).Project.PropertyGroup.Version |
    Where-Object { $_ } | Select-Object -First 1

# 编译前先清掉同名旧安装包，便于确认本次产物
Get-ChildItem $distDir -Filter '*_安装包_V*.exe' -ErrorAction SilentlyContinue | Remove-Item -Force

& $iscc "/DAppVersion=$version" (Join-Path $root 'installer\CZRWeighSystem.iss')
if ($LASTEXITCODE -ne 0) { throw "Inno Setup 编译失败，退出码 $LASTEXITCODE" }

$setup = Get-ChildItem $distDir -Filter '*_安装包_V*.exe' | Select-Object -First 1
Write-Host ('完成：{0}（{1:N1} MB）' -f $setup.FullName, ($setup.Length / 1MB))