# Deploys the built AutoEditing extension to the VEGAS Pro "Application Extensions"
# folder so VEGAS loads it at startup (View > Extensions > AutoEditing Shot Review).
# VEGAS locks a loaded extension DLL, so close VEGAS before deploying.
param(
    [string]$Configuration = "Debug",
    [string]$ProjectDir = ""
)

if ([string]::IsNullOrEmpty($ProjectDir)) { $ProjectDir = Join-Path $PSScriptRoot ".." }
$sourceDir = Join-Path $ProjectDir "Core\bin\$Configuration"

# Use VEGAS's system-wide, unversioned application-extension search path. VEGAS
# Pro 20 on the development machine does not scan the redirected Documents or
# version-specific Local AppData locations reliably.
$programData = [Environment]::GetFolderPath('CommonApplicationData')
$destination = Join-Path $programData "Vegas Pro\Application Extensions"
$legacyDestination = Join-Path ([Environment]::GetFolderPath('MyDocuments')) "Vegas Application Extensions"

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

# Preserve an existing machine-local configuration when moving from the former
# Documents deployment location. Never overwrite the destination override.
$localConfig = Join-Path $destination "appsettings.local.json"
$legacyLocalConfig = Join-Path $legacyDestination "appsettings.local.json"
if (-not (Test-Path $localConfig) -and (Test-Path $legacyLocalConfig)) {
    [System.IO.File]::Copy($legacyLocalConfig, $localConfig, $false)
    Write-Host "[OK] Migrated appsettings.local.json" -ForegroundColor Green
}

# Runtime assemblies (extension + its dependencies) and the shared config.
# appsettings.local.json is intentionally NOT copied so machine-local overrides survive.
$required = @(
    @{ Name = "AutoEditing.Extension.dll"; Source = (Join-Path $ProjectDir "ExtensionBootstrap\bin\$Configuration\AutoEditing.Extension.dll") },
    @{ Name = "Core.dll"; Source = (Join-Path $sourceDir "Core.dll") },
    @{ Name = "AutoEditing.Domain.dll"; Source = (Join-Path $sourceDir "AutoEditing.Domain.dll") },
    @{ Name = "AutoEditing.Iteration.Contracts.dll"; Source = (Join-Path $sourceDir "AutoEditing.Iteration.Contracts.dll") },
    @{ Name = "AutoEditing.AutomaticEditor.dll"; Source = (Join-Path $sourceDir "AutoEditing.AutomaticEditor.dll") },
    @{ Name = "AutoEditing.Vegas.dll"; Source = (Join-Path $sourceDir "AutoEditing.Vegas.dll") },
    @{ Name = "NAudio.Core.dll"; Source = (Join-Path $sourceDir "NAudio.Core.dll") },
    @{ Name = "NAudio.Wasapi.dll"; Source = (Join-Path $sourceDir "NAudio.Wasapi.dll") },
    @{ Name = "Newtonsoft.Json.dll"; Source = (Join-Path $sourceDir "Newtonsoft.Json.dll") },
    @{ Name = "appsettings.json"; Source = (Join-Path $sourceDir "appsettings.json") }
)

foreach ($artifact in $required) {
    $file = $artifact.Name
    $src = $artifact.Source
    $dest = Join-Path $destination $file
    if (-not (Test-Path $src)) {
        Write-Host "[ERROR] Missing build artifact: $file" -ForegroundColor Red
        exit 1
    }
    try {
        # Overwrite the file contents in place so deployment also remains safe
        # when a destination is backed by a filesystem provider.
        [System.IO.File]::Copy($src, $dest, $true)
        Write-Host "[OK] $file" -ForegroundColor Green
    } catch {
        Write-Host "[ERROR] Could not deploy $file. Check file locks and destination permissions: $($_.Exception.Message)" -ForegroundColor Red
        exit 2
    }
}

# Remove the command envelope used by the retired nested-script transport.
$obsoleteCommandEnvelope = Join-Path $destination "AutoEditing.vegas-command.json"
if (Test-Path $obsoleteCommandEnvelope) {
    Remove-Item -LiteralPath $obsoleteCommandEnvelope -Force
    Write-Host "[OK] Removed obsolete nested-script command envelope" -ForegroundColor Green
}

Write-Host "[SUCCESS] Extension deployed. Restart VEGAS Pro to load the new build." -ForegroundColor Green
