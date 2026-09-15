[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$app = Join-Path $PSScriptRoot '../app/CodexPetFocus.App.exe'
if (-not (Test-Path -LiteralPath $app)) { throw '缺少已构建的 app 目录，请使用发布包或运行仓库 tools/build.ps1。' }
Start-Process -FilePath $app -WorkingDirectory (Split-Path $app) -WindowStyle Hidden
