# PowerShell script to copy Core.dll to VEGAS Pro Scripts folder
$source = "Core\bin\Debug\Core.dll"
$destination = Join-Path $env:APPDATA "VEGAS Pro\Scripts"

# Ensure destination directory exists
if (-not (Test-Path $destination)) {
    New-Item -ItemType Directory -Path $destination -Force | Out-Null
}

# Copy the DLL
Copy-Item -Path $source -Destination $destination -Force

# Verify the copy
if (Test-Path (Join-Path $destination "Core.dll")) {
    Write-Host "DLL copied successfully to VEGAS Pro Scripts folder" -ForegroundColor Green
    Write-Host "Location: $destination" -ForegroundColor Cyan
} else {
    Write-Host "Failed to copy DLL" -ForegroundColor Red
    exit 1
}
