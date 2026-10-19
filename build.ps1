[CmdletBinding()]
param([string]$GameDir = (Join-Path ${env:ProgramFiles(x86)} 'Steam\steamapps\common\Slay the Spire 2'), [switch]$SkipTests)
$ErrorActionPreference = 'Stop'
$taskRoot = $PSScriptRoot
$assembly = Join-Path $GameDir 'data_sts2_windows_x86_64\sts2.dll'
if (!(Test-Path -LiteralPath $assembly)) { throw "找不到游戏程序集：$assembly" }
Push-Location $taskRoot
try {
    if (!$SkipTests) {
        & dotnet run --project 'tests/SpirePvp.Tests.csproj' --configuration Release --no-launch-profile --ignore-failed-sources "-p:GameDir=$GameDir" -- $GameDir
        if ($LASTEXITCODE -ne 0) { throw '自动检查失败。' }
    }
    & dotnet build 'SpirePvp/SpirePvp.csproj' --configuration Release --ignore-failed-sources "-p:GameDir=$GameDir" -v minimal
    if ($LASTEXITCODE -ne 0) { throw '编译失败。' }
    $package = Join-Path $taskRoot 'dist\SpirePvp'
    New-Item -ItemType Directory -Path $package -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $taskRoot 'SpirePvp\bin\Release\net9.0\SpirePvp.dll') -Destination $package -Force
    Copy-Item -LiteralPath (Join-Path $taskRoot 'SpirePvp\SpirePvp.json') -Destination $package -Force
    Copy-Item -LiteralPath (Join-Path $taskRoot 'README.md') -Destination (Join-Path $package 'README.md') -Force
    $version = (Get-Content -LiteralPath (Join-Path $taskRoot 'SpirePvp\SpirePvp.json') -Raw | ConvertFrom-Json).version
    $archive = Join-Path $taskRoot "dist\SpirePvp-$version.zip"
    Compress-Archive -LiteralPath $package -DestinationPath $archive -Force
    Write-Host "已生成 $archive"
} finally { Pop-Location }
