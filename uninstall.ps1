#Requires -Version 5.1
<#
.SYNOPSIS
    卸载 wantodo：停进程、删程序目录与快捷方式、清开机自启。

.DESCRIPTION
    默认**保留**用户数据（%APPDATA%\wantodo），只有显式加 -RemoveData 才删。
    这样「重装一次」不会把便签弄丢。

.PARAMETER RemoveData
    连 %APPDATA%\wantodo（data.json、data.bak、app.log）一起删掉。

.EXAMPLE
    .\uninstall.ps1
    .\uninstall.ps1 -RemoveData
#>
[CmdletBinding()]
param(
    [switch]$RemoveData
)

$ErrorActionPreference = 'Stop'

function Write-Step([string]$Text) { Write-Host "==> $Text" -ForegroundColor Cyan }
function Write-Ok([string]$Text) { Write-Host "    $Text" -ForegroundColor DarkGray }

$targetDir = Join-Path $env:LOCALAPPDATA 'Programs\wantodo'
$startMenuLnk = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\wantodo.lnk'
$desktopLnk = Join-Path ([Environment]::GetFolderPath('Desktop')) 'wantodo.lnk'
$dataDir = Join-Path $env:APPDATA 'wantodo'
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'

Write-Step '关闭 wantodo'
Get-Process -Name Wantodo -ErrorAction SilentlyContinue | ForEach-Object {
    $_.CloseMainWindow() | Out-Null
    Start-Sleep -Milliseconds 500
    if (-not $_.HasExited) { $_.Kill() }
}
Write-Ok '进程已结束'

Write-Step '清理开机自启'
$existing = (Get-ItemProperty -Path $runKey -Name Wantodo -ErrorAction SilentlyContinue).Wantodo
if ($existing) {
    Remove-ItemProperty -Path $runKey -Name Wantodo -ErrorAction SilentlyContinue
    Write-Ok "已删除 Run 值：$existing"
}
else {
    Write-Ok 'Run 里没有条目'
}

Write-Step '删除快捷方式'
foreach ($lnk in @($startMenuLnk, $desktopLnk)) {
    if (Test-Path -LiteralPath $lnk) {
        Remove-Item -LiteralPath $lnk -Force
        Write-Ok $lnk
    }
}

Write-Step "删除程序目录 $targetDir"
if (Test-Path -LiteralPath $targetDir) {
    Remove-Item -LiteralPath $targetDir -Recurse -Force
    Write-Ok '已删除'
}
else {
    Write-Ok '目录不存在'
}

if ($RemoveData) {
    Write-Step "删除用户数据 $dataDir"
    if (Test-Path -LiteralPath $dataDir) {
        Remove-Item -LiteralPath $dataDir -Recurse -Force
        Write-Ok '已删除（便签内容一起没了）'
    }
    else {
        Write-Ok '目录不存在'
    }
}
else {
    Write-Host ''
    Write-Host "用户数据保留在 $dataDir（要一起删就加 -RemoveData）" -ForegroundColor DarkGray
}

Write-Host ''
Write-Host '卸载完成。' -ForegroundColor Green
