# Deploys the built AutoEditing extension to the VEGAS Pro "Application Extensions"
# folder so VEGAS loads it at startup (View > Extensions > AutoEditing Shot Review).
# VEGAS locks a loaded extension DLL, so close VEGAS before deploying.
param(
    [string]$Configuration = "Debug",
    [string]$ProjectDir = ""
)

if ([string]::IsNullOrEmpty($ProjectDir)) { $ProjectDir = Join-Path $PSScriptRoot ".." }
$sourceDir = Join-Path $ProjectDir "Core\bin\$Configuration"

# Resolve the localized Documents folder (e.g. "Documenten" on a Dutch profile),
# which is where VEGAS discovers per-user Application Extensions.
$docs = [Environment]::GetFolderPath('MyDocuments')
$destination = Join-Path $docs "Vegas Application Extensions"

Write-Host "AutoEditing extension deployment" -ForegroundColor Cyan
Write-Host "Source:      $sourceDir" -ForegroundColor White
Write-Host "Destination: $destination" -ForegroundColor White

if (-not (Test-Path $sourceDir)) {
    Write-Host "[ERROR] Build output not found. Build the project first." -ForegroundColor Red
    exit 1
}
if (-not (Test-Path $destination)) {
    New-Item -ItemType Directory -Path $destination -Force | Out-Null
}

# Runtime assemblies (extension + its dependencies) and the shared config.
# appsettings.local.json is intentionally NOT copied so machine-local overrides survive.
$required = @("Core.dll", "NAudio.Core.dll", "NAudio.Wasapi.dll", "Newtonsoft.Json.dll", "appsettings.json")

foreach ($file in $required) {
    $src = Join-Path $sourceDir $file
    if (-not (Test-Path $src)) {
        Write-Host "[ERROR] Missing build artifact: $file" -ForegroundColor Red
        exit 1
    }
    try {
        Copy-Item -Path $src -Destination $destination -Force -ErrorAction Stop
        Write-Host "[OK] $file" -ForegroundColor Green
    } catch {
        Write-Host "[WARN] Could not copy $file (is VEGAS running and locking it?): $($_.Exception.Message)" -ForegroundColor Yellow
        exit 2
    }
}

Write-Host "[SUCCESS] Extension deployed. Restart VEGAS Pro to load the new build." -ForegroundColor Green
