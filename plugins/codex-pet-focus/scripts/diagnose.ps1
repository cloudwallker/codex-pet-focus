[CmdletBinding()]
param([string]$OutputPath = (Join-Path $env:TEMP 'codex-pet-focus-diagnostic.json'))
$ErrorActionPreference = 'Stop'
$app = Join-Path $PSScriptRoot '../app/CodexPetFocus.App.exe'
if (-not (Test-Path -LiteralPath $app)) { throw '缺少已构建的 app 目录。' }
$fullOutput = [IO.Path]::GetFullPath($OutputPath)
$process = Start-Process -FilePath $app -ArgumentList @('--diagnose','--output',('"' + $fullOutput + '"')) -WindowStyle Hidden -Wait -PassThru
if ($process.ExitCode -ne 0) { throw "诊断失败，退出码 $($process.ExitCode)" }
Get-Content -LiteralPath $fullOutput -Encoding UTF8
