# AutoEditing PowerShell Scripts

This directory contains PowerShell scripts used for development and build automation.

## Scripts Overview

### 🔍 validate-code-style.ps1
**Purpose**: Manual pre-commit validation script
**Usage**: `powershell -ExecutionPolicy Bypass -File .vscode/validate-code-style.ps1`
**Features**:
- Checks for required VS Code extensions
- Validates code style compliance (no 'var' usage)
- Builds project to ensure compilation success
- Provides detailed error reporting

### ⚡ build-validate-style.ps1
**Purpose**: Build-time validation (called by MSBuild)
**Usage**: Automatically executed during `dotnet build`
**Features**:
- Enforces code style during build process
- Fails build on style violations
- Shows detailed violation information with `--verbosity detailed`

### 📦 copy-dll.ps1
**Purpose**: Deploy built DLL to VEGAS Pro Scripts folder
**Usage**: `powershell -ExecutionPolicy Bypass -File .vscode/copy-dll.ps1`
**Features**:
- Copies Core.dll and configuration files
- Supports both Debug and Release configurations
- Validates successful deployment
- Provides clear status reporting

## Integration

- **build-validate-style.ps1** is automatically called by MSBuild pre-build target
- **copy-dll.ps1** is called by the "Copy DLL to VEGAS Scripts" VS Code task
- **validate-code-style.ps1** should be run manually before commits

## Error Handling

All scripts include proper error handling and exit codes:
- Exit code 0: Success
- Exit code 1: Failure/violations found

Use `--verbosity detailed` with dotnet build to see full validation output.
