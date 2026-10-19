[CmdletBinding()]
param([string]$GameDir = (Join-Path ${env:ProgramFiles(x86)} 'Steam\steamapps\common\Slay the Spire 2'))
$ErrorActionPreference = 'Stop'
$taskRoot = $PSScriptRoot
if (!(Test-Path -LiteralPath (Join-Path $GameDir 'data_sts2_windows_x86_64\sts2.dll'))) { throw '请指定《杀戮尖塔 2》的安装目录。' }
if (!(Test-Path -LiteralPath (Join-Path $taskRoot 'dist\SpirePvp\SpirePvp.dll'))) { & (Join-Path $taskRoot 'build.ps1') -GameDir $GameDir }
$target = Join-Path $GameDir 'mods\SpirePvp'
New-Item -ItemType Directory -Path $target -Force | Out-Null
foreach ($file in @('SpirePvp.dll', 'SpirePvp.json', 'README.md')) {
    $source = Join-Path $taskRoot "dist\SpirePvp\$file"
    $destination = Join-Path $target $file
    if (Test-Path -LiteralPath $destination) {
        $backup = "$destination.backup-$(Get-Date -Format 'yyyyMMdd-HHmmss')"
        Copy-Item -LiteralPath $destination -Destination $backup
    }
    Copy-Item -LiteralPath $source -Destination $destination -Force
}
Write-Host "已安装到 $target。双方安装相同版本后，重启游戏。"
