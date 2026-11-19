[CmdletBinding()]
param([switch]$Development, [switch]$Hidden, [switch]$Autostart)
$exe = Join-Path $PSScriptRoot $(if ($Development) { 'work\build\ToolManager.exe' } else { 'outputs\ToolManager\ToolManager.exe' })
if (!(Test-Path -LiteralPath $exe)) { throw 'Build first with .\Build.ps1 for development, or .\Build.ps1 -Publish for production.' }
$arguments = @()
if ($Hidden) { $arguments += '--hidden' }
if ($Autostart) { $arguments += '--autostart' }
$start = @{ FilePath = $exe; WorkingDirectory = [IO.Path]::GetDirectoryName($exe) }
if ($arguments.Count) { $start.ArgumentList = $arguments }
Start-Process @start
