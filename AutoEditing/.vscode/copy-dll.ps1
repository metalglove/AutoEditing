# PowerShell script to copy Core.dll and configuration files to VEGAS Pro Scripts folder
# This script is called by the VS Code task to deploy the built DLL for testing

param(
    [string]$Configuration = "Debug"
)

$sourceDir = "Core\bin\$Configuration"
$destination = Join-Path $env:APPDATA "VEGAS Pro\Scripts"

Write-Host "AutoEditing DLL Deployment" -ForegroundColor Cyan
Write-Host "=========================" -ForegroundColor Cyan
Write-Host "Source: $sourceDir" -ForegroundColor White
Write-Host "Destination: $destination" -ForegroundColor White
Write-Host ""

# Files to copy
$filesToCopy = @(
    "Core.dll",
    "appsettings.json"
)

# Optional files (copy if they exist)
$optionalFiles = @(
    "appsettings.local.json"
)

# Ensure destination directory exists
if (-not (Test-Path $destination)) {
    Write-Host "Creating destination directory..." -ForegroundColor Yellow
    New-Item -ItemType Directory -Path $destination -Force | Out-Null
}

# Verify source directory exists
if (-not (Test-Path $sourceDir)) {
    Write-Host "[ERROR] Source directory not found: $sourceDir" -ForegroundColor Red
    Write-Host "Please build the project first: dotnet build Core/Core.csproj" -ForegroundColor Yellow
    exit 1
}

# Copy required files
Write-Host "Copying required files..." -ForegroundColor Yellow
foreach ($file in $filesToCopy) {
    $sourcePath = Join-Path $sourceDir $file
    if (Test-Path $sourcePath) {
        Copy-Item -Path $sourcePath -Destination $destination -Force
        Write-Host "[OK] Copied: $file" -ForegroundColor Green
    }
    else {
        Write-Host "[ERROR] Required file not found: $file" -ForegroundColor Red
        exit 1
    }
}

# Copy optional files if they exist
Write-Host "Checking for optional files..." -ForegroundColor Yellow
foreach ($file in $optionalFiles) {
    $sourcePath = Join-Path $sourceDir $file
    if (Test-Path $sourcePath) {
        Copy-Item -Path $sourcePath -Destination $destination -Force
        Write-Host "[OK] Copied: $file" -ForegroundColor Green
    }
    else {
        Write-Host "[INFO] Optional file not found (skipping): $file" -ForegroundColor Cyan
    }
}

# Verify the main DLL was copied
Write-Host ""
if (Test-Path (Join-Path $destination "Core.dll")) {
    Write-Host "[SUCCESS] DLL and configuration files copied successfully!" -ForegroundColor Green
    Write-Host "Destination: $destination" -ForegroundColor Cyan

    # List what was actually copied
    Write-Host ""
    Write-Host "Deployed files:" -ForegroundColor Cyan
    Get-ChildItem -Path $destination -Filter "*.dll" | ForEach-Object { Write-Host "  - $($_.Name)" -ForegroundColor White }
    Get-ChildItem -Path $destination -Filter "*.json" | ForEach-Object { Write-Host "  - $($_.Name)" -ForegroundColor White }

    Write-Host ""
    Write-Host "The AutoEditing script is now ready to use in VEGAS Pro!" -ForegroundColor Green
}
else {
    Write-Host "[ERROR] Failed to copy Core.dll" -ForegroundColor Red
    exit 1
}
