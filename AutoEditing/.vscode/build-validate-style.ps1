# Build-time code style validation script for AutoEditing project
# This script is called during the build process to enforce coding standards
# It fails the build if any style violations are detected

param(
    [string]$ProjectDir = "."
)

# Change to project directory
Set-Location $ProjectDir

# Check for var usage in C# files
$varUsages = Select-String -Path "**\*.cs" -Pattern "^\s*var\s+" -AllMatches

if ($varUsages.Count -gt 0) {
    # Output detailed error information for MSBuild
    Write-Host ""
    Write-Host "BUILD ERROR: Code style violation: Found $($varUsages.Count) 'var' usage(s). Use explicit types instead." -ForegroundColor Red
    Write-Host ""
    Write-Host "Violations found in:" -ForegroundColor Yellow
    
    foreach ($usage in $varUsages) {
        $fileName = Split-Path $usage.Filename -Leaf
        Write-Host "  ${fileName}:$($usage.LineNumber) - $($usage.Line.Trim())" -ForegroundColor White
    }
    
    Write-Host ""
    Write-Host "Please replace 'var' with explicit types and rebuild." -ForegroundColor Red
    Write-Host ""
    
    exit 1
}

Write-Host "Code style validation passed." -ForegroundColor Green
exit 0
