[CmdletBinding()]
param([string]$Configuration = 'Release')
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$localSdk = Join-Path $repo '.tools/dotnet/dotnet.exe'
$dotnet = if (Test-Path -LiteralPath $localSdk) { $localSdk } else { 'dotnet' }
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
Push-Location $repo
try {
    & $dotnet publish src/CodexPetFocus.App/CodexPetFocus.App.csproj -c $Configuration --self-contained false -o plugins/codex-pet-focus/app
    if ($LASTEXITCODE -ne 0) { throw '发布构建失败；需要 .NET 10 SDK 和 Windows Desktop targeting pack。' }
    $plugin = Join-Path $repo 'plugins/codex-pet-focus'
    $readme = [IO.File]::ReadAllText((Join-Path $repo 'README.md'))
    [IO.File]::WriteAllText((Join-Path $plugin 'README.md'), $readme, (New-Object Text.UTF8Encoding $false))
    $readmeEnglish = [IO.File]::ReadAllText((Join-Path $repo 'README_EN.md'))
    [IO.File]::WriteAllText((Join-Path $plugin 'README_EN.md'), $readmeEnglish, (New-Object Text.UTF8Encoding $false))
    Copy-Item -LiteralPath (Join-Path $repo 'LICENSE') -Destination $plugin
    New-Item -ItemType Directory -Force -Path (Join-Path $repo 'artifacts') | Out-Null
    Compress-Archive -Path $plugin -DestinationPath (Join-Path $repo 'artifacts/codex-pet-focus-0.1.0-win-x64.zip') -Force
    Write-Output '发布包：artifacts/codex-pet-focus-0.1.0-win-x64.zip'
} finally { Pop-Location }
