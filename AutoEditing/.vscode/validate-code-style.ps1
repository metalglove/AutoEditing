# Manual code style validation script for AutoEditing project
# Run this script before committing changes to ensure code style compliance
# This script checks for required VS Code extensions and validates code standards

Write-Host "AutoEditing Code Style Validation" -ForegroundColor Cyan
Write-Host "==================================" -ForegroundColor Cyan
Write-Host ""

# Check if EditorConfig extension is available in VS Code
Write-Host "Checking VS Code extensions..." -ForegroundColor Yellow
$vscodeExtensions = code --list-extensions 2>$null
$requiredExtensions = @(
    @{Name = "EditorConfig"; Id = "editorconfig.editorconfig" },
    @{Name = "C#"; Id = "ms-dotnettools.csharp" },
    @{Name = "C# Dev Kit"; Id = "ms-dotnettools.csdevkit" }
)

$missingExtensions = @()
foreach ($ext in $requiredExtensions) {
    if ($vscodeExtensions -contains $ext.Id) {
        Write-Host "[OK] $($ext.Name) extension is installed" -ForegroundColor Green
    }
    else {
        Write-Host "[MISSING] $($ext.Name) extension is NOT installed" -ForegroundColor Red
        $missingExtensions += $ext
    }
}

if ($missingExtensions.Count -gt 0) {
    Write-Host ""
    Write-Host "Please install the missing extensions:" -ForegroundColor Yellow
    foreach ($ext in $missingExtensions) {
        Write-Host "  code --install-extension $($ext.Id)" -ForegroundColor Yellow
    }
    exit 1
}

# Check for var usage in C# files
Write-Host ""
Write-Host "Checking for 'var' usage in C# files..." -ForegroundColor Yellow

$varUsages = Select-String -Path "Core\**\*.cs" -Pattern "^\s*var\s+" -AllMatches

if ($varUsages.Count -gt 0) {
    Write-Host "[ERROR] Found 'var' usage in the following locations:" -ForegroundColor Red
    foreach ($usage in $varUsages) {
        $fileName = Split-Path $usage.Filename -Leaf
        Write-Host "  ${fileName}:$($usage.LineNumber) - $($usage.Line.Trim())" -ForegroundColor Yellow
    }
    Write-Host ""
    Write-Host "Please replace 'var' with explicit types according to project style guidelines." -ForegroundColor Yellow
    exit 1
}
else {
    Write-Host "[OK] No 'var' usage found - code style compliant!" -ForegroundColor Green
}

# Build the project to ensure no compilation errors
Write-Host ""
Write-Host "Building project..." -ForegroundColor Yellow
dotnet build Core/Core.csproj --configuration Debug --verbosity quiet

if ($LASTEXITCODE -eq 0) {
    Write-Host "[OK] Project builds successfully!" -ForegroundColor Green
}
else {
    Write-Host "[ERROR] Project build failed!" -ForegroundColor Red
    Write-Host "Run 'dotnet build Core/Core.csproj --verbosity detailed' for more information." -ForegroundColor Yellow
    exit 1
}

Write-Host ""
Write-Host "[SUCCESS] All validation checks passed!" -ForegroundColor Green
Write-Host "Your code is ready for commit." -ForegroundColor Green
