[CmdletBinding()]
param([switch]$Test, [switch]$Publish, [switch]$FrameworkDependent)
$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    $buildRoot = Join-Path $PSScriptRoot 'work\build'
    $packageName = if ($FrameworkDependent) { 'ToolManager-framework-dependent' } else { 'ToolManager' }
    $package = Join-Path $PSScriptRoot "outputs\$packageName"
    $settings = @{}
    $backup = Join-Path $PSScriptRoot ('work\settings-backups\' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
    foreach ($name in @('tools.json', 'tools.json.bak')) {
        $path = Join-Path $package $name
        if (Test-Path -LiteralPath $path) {
            New-Item -ItemType Directory -Path $backup -Force | Out-Null
            Copy-Item -LiteralPath $path -Destination (Join-Path $backup $name)
            $settings[$path] = (Get-FileHash -LiteralPath $path).Hash
        }
    }
    dotnet build ToolManager.csproj -c Release -o $buildRoot
    if ($LASTEXITCODE) { throw 'Build failed.' }
    if ($Test) {
        dotnet build tests/Fixture.csproj -c Release -o (Join-Path $buildRoot 'fixtures')
        if ($LASTEXITCODE) { throw 'Test fixture build failed.' }
        $run = Start-Process -FilePath (Join-Path $buildRoot 'ToolManager.exe') -ArgumentList '--self-test' -WindowStyle Hidden -PassThru -Wait
        Get-Content -LiteralPath (Join-Path $buildRoot 'self-test-results.txt')
        if ($run.ExitCode) { throw 'Integration checks failed.' }
        $ui = Start-Process -FilePath (Join-Path $buildRoot 'ToolManager.exe') -ArgumentList '--render-self-test' -WindowStyle Hidden -PassThru -Wait
        Get-Content -LiteralPath (Join-Path $buildRoot 'ui-check\result.txt')
        if ($ui.ExitCode) { throw 'WPF rendering checks failed.' }
    }
    if ($Publish) {
        $staging = Join-Path $PSScriptRoot ('work\publish-' + [guid]::NewGuid().ToString('N'))
        $selfContained = (!$FrameworkDependent).ToString().ToLowerInvariant()
        dotnet publish ToolManager.csproj -c Release -r win-x64 --self-contained $selfContained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=$selfContained -p:DebugType=None -p:DebugSymbols=false -o $staging
        if ($LASTEXITCODE) { throw 'Publish failed.' }
        $publishedFiles = @(Get-ChildItem -LiteralPath $staging -File -Recurse)
        if ($publishedFiles.Count -ne 1 -or $publishedFiles[0].Name -ne 'ToolManager.exe') {
            throw "Expected a single executable in $staging."
        }
        $running = Get-Process ToolManager -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq (Join-Path $package 'ToolManager.exe') }
        if ($running) { throw "Close the running Tool Manager before updating. The build is ready in $staging. Settings have not been replaced." }
        New-Item -ItemType Directory -Path $package -Force | Out-Null
        foreach ($file in Get-ChildItem -LiteralPath $staging -File -Recurse) {
            $relative = [IO.Path]::GetRelativePath($staging, $file.FullName)
            if ($relative -match '(^|[\\/])(tools\.json(?:\..*)?|data)([\\/]|$)') { throw "Settings/data must not be present in build artifacts: $relative" }
            $target = Join-Path $package $relative
            New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($target)) -Force | Out-Null
            Copy-Item -LiteralPath $file.FullName -Destination $target -Force
        }
        Copy-Item -LiteralPath "$PSScriptRoot\README.md" -Destination (Join-Path $package 'README.md') -Force
        New-Item -ItemType Directory -Path (Join-Path $package 'tools'), (Join-Path $package 'scripts') -Force | Out-Null
        Write-Output "Published application: $package\ToolManager.exe (self-contained: $selfContained)"
    }
    foreach ($path in $settings.Keys) {
        if (!(Test-Path -LiteralPath $path) -or (Get-FileHash -LiteralPath $path).Hash -ne $settings[$path]) {
            throw "Settings changed during the build: $path. Preserved pre-build copy: $backup. No automatic restore was performed."
        }
    }
} finally { Pop-Location }
