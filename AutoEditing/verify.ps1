[CmdletBinding()]
param(
	[ValidateSet("Debug", "Release")]
	[string]$Configuration = "Debug",
	[switch]$NoRestore
)

$ErrorActionPreference = "Stop"
$solution = Join-Path $PSScriptRoot "AutoEditing.sln"

function Invoke-Checked
{
	param(
		[Parameter(Mandatory = $true)]
		[string]$Label,
		[Parameter(Mandatory = $true)]
		[scriptblock]$Command
	)

	Write-Host ""
	Write-Host "== $Label ==" -ForegroundColor Cyan
	& $Command
	if ($LASTEXITCODE -ne 0)
	{
		throw "$Label failed with exit code $LASTEXITCODE."
	}
}

$buildArguments = @(
	"build",
	$solution,
	"--configuration",
	$Configuration,
	"--maxcpucount:1",
	"-p:DeployToVegas=false"
)
if ($NoRestore)
{
	$buildArguments += "--no-restore"
}

Invoke-Checked "Build complete solution" {
	& dotnet @buildArguments
}

$contractNet8 = Join-Path $PSScriptRoot (
	"Iteration.Contracts.Tests\bin\$Configuration\net8.0\" +
	"AutoEditing.Iteration.Contracts.Tests.dll")
$contractNet48 = Join-Path $PSScriptRoot (
	"Iteration.Contracts.Tests\bin\$Configuration\net48\" +
	"AutoEditing.Iteration.Contracts.Tests.exe")
$llmEditor = Join-Path $PSScriptRoot (
	"LlmEditor\bin\$Configuration\net8.0\AutoEditing.LlmEditor.dll")
$automationBrokerTests = Join-Path $PSScriptRoot (
	"AutomationBroker.Tests\bin\$Configuration\net8.0\" +
	"AutoEditing.AutomationBroker.Tests.dll")
$coreTests = Join-Path $PSScriptRoot (
	"Core.Tests\bin\$Configuration\AutoEditing.Core.Tests.exe")
$analysisHarness = Join-Path $PSScriptRoot (
	"Tools\AnalysisHarness\bin\$Configuration\net48\AnalysisHarness.exe")

Invoke-Checked "Iteration contracts (.NET 8)" {
	& dotnet $contractNet8
}
Invoke-Checked "Iteration contracts (.NET Framework 4.8)" {
	& $contractNet48
}
Invoke-Checked "LLM editor, inference, and VEGAS client self-tests" {
	& dotnet $llmEditor --self-test
}
Invoke-Checked "VEGAS automation broker tests" {
	& dotnet $automationBrokerTests
}
Invoke-Checked "VEGAS workbench projection tests" {
	& $coreTests
}
Invoke-Checked "Song-analysis and montage-planner tests" {
	& $analysisHarness --self-test-song-analysis
}
Invoke-Checked "Patch whitespace validation" {
	& git diff --check
}

Write-Host ""
Write-Host "All deterministic verification suites passed." -ForegroundColor Green
