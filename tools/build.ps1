[CmdletBinding()]
param([string]$Configuration = 'Release')
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$sourcePlugin = Join-Path $repo 'plugins/codex-pet-focus'
$artifacts = Join-Path $repo 'artifacts'
$releaseName = 'codex-pet-focus-0.1.0-win-x64.zip'
$nonce = [Guid]::NewGuid().ToString('N')
$stageRoot = Join-Path $artifacts ('.package-' + $nonce)
$plugin = Join-Path $stageRoot 'codex-pet-focus'
$tempArchive = Join-Path $artifacts ('.package-' + $nonce + '.zip')
$archive = Join-Path $artifacts $releaseName
$localSdk = Join-Path $repo '.tools/dotnet/dotnet.exe'
$dotnet = if (Test-Path -LiteralPath $localSdk) { $localSdk } else { 'dotnet' }
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
Push-Location $repo
try {
    New-Item -ItemType Directory -Force -Path $plugin | Out-Null
    foreach ($relative in @(
        '.codex-plugin/plugin.json',
        'scripts/create-shortcut.ps1',
        'scripts/diagnose.ps1',
        'scripts/start.ps1',
        'skills/codex-pet-focus/SKILL.md'
    )) {
        $source = Join-Path $sourcePlugin $relative
        $destination = Join-Path $plugin $relative
        New-Item -ItemType Directory -Force -Path (Split-Path $destination -Parent) | Out-Null
        Copy-Item -LiteralPath $source -Destination $destination
    }

    & $dotnet publish src/CodexPetFocus.App/CodexPetFocus.App.csproj -c $Configuration --self-contained false -o (Join-Path $plugin 'app')
    if ($LASTEXITCODE -ne 0) { throw '发布构建失败；需要 .NET 10 SDK 和 Windows Desktop targeting pack。' }
    $readme = [IO.File]::ReadAllText((Join-Path $repo 'README.md'))
    $readme = $readme.Replace('(docs/compatibility.md)', '(COMPATIBILITY.md)').Replace('(docs/references.md)', '(REFERENCES.md)')
    [IO.File]::WriteAllText((Join-Path $plugin 'README.md'), $readme, (New-Object Text.UTF8Encoding $false))
    $readmeEnglish = [IO.File]::ReadAllText((Join-Path $repo 'README_EN.md'))
    $readmeEnglish = $readmeEnglish.Replace('(docs/compatibility.md)', '(COMPATIBILITY.md)').Replace('(docs/references.md)', '(REFERENCES.md)')
    [IO.File]::WriteAllText((Join-Path $plugin 'README_EN.md'), $readmeEnglish, (New-Object Text.UTF8Encoding $false))
    Copy-Item -LiteralPath (Join-Path $repo 'docs/compatibility.md') -Destination (Join-Path $plugin 'COMPATIBILITY.md')
    Copy-Item -LiteralPath (Join-Path $repo 'docs/references.md') -Destination (Join-Path $plugin 'REFERENCES.md')
    Copy-Item -LiteralPath (Join-Path $repo 'LICENSE') -Destination $plugin

    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = [IO.Compression.ZipFile]::Open($tempArchive, [IO.Compression.ZipArchiveMode]::Create)
    try {
        $pluginRoot = [IO.Path]::GetFullPath($plugin)
        foreach ($file in (Get-ChildItem -LiteralPath $plugin -File -Recurse -Force | Sort-Object FullName)) {
            $relative = $file.FullName.Substring($pluginRoot.Length + 1).Replace('\', '/')
            $entryName = "codex-pet-focus/$relative"
            [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $file.FullName, $entryName, [IO.Compression.CompressionLevel]::Optimal) | Out-Null
        }
    } finally { $zip.Dispose() }
    Move-Item -LiteralPath $tempArchive -Destination $archive -Force
    Write-Output "发布包：artifacts/$releaseName"
} finally {
    Pop-Location
    if (Test-Path -LiteralPath $stageRoot) {
        $resolvedArtifacts = [IO.Path]::GetFullPath($artifacts)
        $resolvedStage = (Resolve-Path -LiteralPath $stageRoot).Path
        if (-not $resolvedStage.StartsWith($resolvedArtifacts + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
            throw '拒绝清理仓库 artifacts 目录之外的暂存目录。'
        }
        Remove-Item -LiteralPath $resolvedStage -Recurse -Force
    }
    if (Test-Path -LiteralPath $tempArchive) { Remove-Item -LiteralPath $tempArchive -Force }
}
