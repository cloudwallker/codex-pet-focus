[CmdletBinding()]
param([string]$Destination = ([Environment]::GetFolderPath('Desktop')))
$ErrorActionPreference = 'Stop'
$app = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../app/CodexPetFocus.App.exe'))
if (-not (Test-Path -LiteralPath $app)) { throw '缺少已构建的 app 目录。' }
$shell = New-Object -ComObject WScript.Shell
try {
    $shortcut = $shell.CreateShortcut((Join-Path $Destination 'Codex Pet Focus.lnk'))
    $shortcut.TargetPath = $app
    $shortcut.WorkingDirectory = Split-Path $app
    $shortcut.Description = '离线任务计时与原桌宠横幅'
    $shortcut.Save()
} finally { [void][Runtime.InteropServices.Marshal]::ReleaseComObject($shell) }
