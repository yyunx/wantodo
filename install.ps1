#Requires -Version 5.1
<#
.SYNOPSIS
    把 wantodo 安装到当前用户的程序目录，并建开始菜单（可选桌面）快捷方式。

.DESCRIPTION
    只写当前用户范围内的位置：
      - 程序：    %LOCALAPPDATA%\Programs\wantodo\Wantodo.exe
      - 快捷方式：%APPDATA%\Microsoft\Windows\Start Menu\Programs\wantodo.lnk
    不写 HKLM、不写 Program Files、不需要管理员权限。

.PARAMETER Source
    要安装的 exe 路径。默认用仓库里的自包含单文件发布产物 publish\sc\Wantodo.exe。

.PARAMETER DesktopShortcut
    额外在桌面建一个快捷方式。

.PARAMETER StartWithWindows
    额外写 HKCU\...\Run（等同设置面板里的「开机自启」）。

.EXAMPLE
    .\install.ps1
    .\install.ps1 -DesktopShortcut
#>
[CmdletBinding()]
param(
    [string]$Source = '',
    [switch]$DesktopShortcut,
    [switch]$StartWithWindows
)

$ErrorActionPreference = 'Stop'

function Write-Step([string]$Text) { Write-Host "==> $Text" -ForegroundColor Cyan }
function Write-Ok([string]$Text) { Write-Host "    $Text" -ForegroundColor DarkGray }

# $PSScriptRoot 在某些启动方式下是空的，这里兜底自己算
if ([string]::IsNullOrWhiteSpace($Source)) {
    $scriptDir = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $MyInvocation.MyCommand.Path }
    $Source = Join-Path $scriptDir 'publish\sc\Wantodo.exe'
}

if (-not (Test-Path -LiteralPath $Source)) {
    throw "找不到 $Source。先在仓库根目录跑：dotnet publish src\Wantodo.App\Wantodo.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish\sc"
}

$Source = (Resolve-Path -LiteralPath $Source).Path
$targetDir = Join-Path $env:LOCALAPPDATA 'Programs\wantodo'
$targetExe = Join-Path $targetDir 'Wantodo.exe'
$startMenuDir = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs'
$startMenuLnk = Join-Path $startMenuDir 'wantodo.lnk'
$desktopLnk = Join-Path ([Environment]::GetFolderPath('Desktop')) 'wantodo.lnk'

Write-Step '关闭正在运行的 wantodo'
Get-Process -Name Wantodo -ErrorAction SilentlyContinue | ForEach-Object {
    $_.CloseMainWindow() | Out-Null
    Start-Sleep -Milliseconds 500
    if (-not $_.HasExited) { $_.Kill() }
}
Write-Ok '已确保没有实例在跑（否则会锁住目标文件）'

Write-Step "复制到 $targetDir"
New-Item -ItemType Directory -Path $targetDir -Force | Out-Null
Copy-Item -LiteralPath $Source -Destination $targetExe -Force
Write-Ok ("{0:N0} KB" -f ((Get-Item -LiteralPath $targetExe).Length / 1KB))

function New-Shortcut([string]$Path) {
    $shell = New-Object -ComObject WScript.Shell
    $lnk = $shell.CreateShortcut($Path)
    $lnk.TargetPath = $targetExe
    $lnk.WorkingDirectory = $targetDir
    $lnk.IconLocation = "$targetExe,0"
    $lnk.Description = 'wantodo — 桌面便签板'
    $lnk.Save()
}

Write-Step '创建开始菜单快捷方式'
New-Item -ItemType Directory -Path $startMenuDir -Force | Out-Null
New-Shortcut $startMenuLnk
Write-Ok $startMenuLnk

if ($DesktopShortcut) {
    Write-Step '创建桌面快捷方式'
    New-Shortcut $desktopLnk
    Write-Ok $desktopLnk
}

if ($StartWithWindows) {
    Write-Step '写入开机自启（HKCU\...\Run）'
    Set-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'Wantodo' -Value "`"$targetExe`""
    Write-Ok "`"$targetExe`""
}

Write-Host ''
Write-Host '安装完成。开始菜单里搜 wantodo 即可打开（或双击上面的快捷方式）。' -ForegroundColor Green
Write-Host '数据不在这个目录里：程序启动后「设置 → 数据位置」会显示当前数据根，默认在非系统盘。' -ForegroundColor DarkGray
Write-Host '卸载默认可保留数据；下次登录要自动启动，用程序里「设置 → 开机自启」。' -ForegroundColor DarkGray
