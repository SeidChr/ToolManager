[CmdletBinding()]
param()
$exe = Join-Path $PSScriptRoot 'outputs\ToolManager\ToolManager.exe'
if (!(Test-Path -LiteralPath $exe)) { throw 'Build the portable application first with .\Build.ps1 -Publish.' }
Start-Process -FilePath $exe -WorkingDirectory ([IO.Path]::GetDirectoryName($exe))
