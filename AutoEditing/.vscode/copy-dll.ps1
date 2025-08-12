# PowerShell script to copy Core.dll and configuration files to VEGAS Pro Scripts folder
$sourceDir = "Core\bin\Debug"
$destination = Join-Path $env:APPDATA "VEGAS Pro\Scripts"

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
    New-Item -ItemType Directory -Path $destination -Force | Out-Null
}

# Copy required files
foreach ($file in $filesToCopy) {
    $sourcePath = Join-Path $sourceDir $file
    if (Test-Path $sourcePath) {
        Copy-Item -Path $sourcePath -Destination $destination -Force
        Write-Host "Copied: $file" -ForegroundColor Green
    } else {
        Write-Host "Warning: Required file not found: $file" -ForegroundColor Yellow
    }
}

# Copy optional files if they exist
foreach ($file in $optionalFiles) {
    $sourcePath = Join-Path $sourceDir $file
    if (Test-Path $sourcePath) {
        Copy-Item -Path $sourcePath -Destination $destination -Force
        Write-Host "Copied: $file" -ForegroundColor Green
    } else {
        Write-Host "Optional file not found (skipping): $file" -ForegroundColor Cyan
    }
}

# Verify the main DLL was copied
if (Test-Path (Join-Path $destination "Core.dll")) {
    Write-Host "`nDLL and configuration files copied successfully to VEGAS Pro Scripts folder" -ForegroundColor Green
    Write-Host "Location: $destination" -ForegroundColor Cyan
    
    # List what was actually copied
    Write-Host "`nFiles in destination:" -ForegroundColor Cyan
    Get-ChildItem -Path $destination -Filter "*.dll" | ForEach-Object { Write-Host "  - $($_.Name)" -ForegroundColor White }
    Get-ChildItem -Path $destination -Filter "*.json" | ForEach-Object { Write-Host "  - $($_.Name)" -ForegroundColor White }
} else {
    Write-Host "Failed to copy DLL" -ForegroundColor Red
    exit 1
}
