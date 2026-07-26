[CmdletBinding()]
param(
	[ValidateSet("Debug", "Release", "Deploy")]
	[string]$Configuration = "Debug",
	[switch]$NoRestore,
	[switch]$Deploy
)

$ErrorActionPreference = "Stop"
$solution = Join-Path $PSScriptRoot "AutoEditing.sln"
$deployRequested = $Deploy.IsPresent -or $Configuration -eq "Deploy"
$arguments = @(
	"build",
	$solution,
	"--configuration",
	$Configuration,
	"--maxcpucount:1",
	"-p:DeployToVegas=$($deployRequested.ToString().ToLowerInvariant())"
)
if ($NoRestore)
{
	$arguments += "--no-restore"
}

& dotnet @arguments
if ($LASTEXITCODE -ne 0)
{
	exit $LASTEXITCODE
}
