# Closes Nimbus, builds the project and starts the new build.
# Usage: pwsh -File dev.ps1 [-Configuration Release] [-NoStart]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',
    [switch]$NoStart
)

$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'Nimbus.csproj'
$exe = Join-Path $PSScriptRoot "bin\$Configuration\net10.0-windows10.0.19041.0\win-x64\Nimbus.exe"

# Every copy is closed, including the installed one: only one instance can run,
# so a copy left running would receive the new build's files.
$running = @(Get-Process -Name Nimbus -ErrorAction SilentlyContinue)
if ($running.Count -gt 0) {
    Write-Host "Closing Nimbus ($($running.Count) running)..."
    $running | Stop-Process -Force
    $running | Wait-Process -Timeout 15
}

Write-Host "Building ($Configuration)..."
dotnet build $project -c $Configuration -v q -nologo
if ($LASTEXITCODE -ne 0) {
    Write-Host 'Build failed. Nimbus was not restarted.' -ForegroundColor Red
    exit $LASTEXITCODE
}

if ($NoStart) {
    Write-Host "Built: $exe"
    exit 0
}

Start-Process -FilePath $exe
Write-Host "Started: $exe" -ForegroundColor Green
