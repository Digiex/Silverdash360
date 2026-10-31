<#
.SYNOPSIS
Builds Silverdash360 into an Xbox 360 package.

.DESCRIPTION
Runs the Silverlight360 SDK's Build-XboxApp.ps1 on the dashboard, as one of:

-Debug    with the debug server (a TCP port for testing on a console, see
          tests\README.md), into src\Silverdash360\bin\Debug\xbox\. What
          tools\deploy.py, tools\Run.ps1 and the tests use.
-Release  without it, into src\Silverdash360\bin\Release\xbox\. For builds
          that go to others: the debug server listens without a password.

-AppOnly writes the dashboard's own files alone, without the runtime, into
bin\<Debug|Release>\app\ (see tools\Release.ps1).

The SDK is the one given with -Sdk, else the SILVERLIGHT360 environment
variable, else a Silverlight360 clone next to this repository.

.EXAMPLE
.\tools\Build.ps1 -Debug -Sdk ..\Silverlight360

.EXAMPLE
.\tools\Build.ps1 -Release
#>
param(
    # With the debug server, for testing.
    [switch]$Debug,
    # Without the debug server, for others.
    [switch]$Release,
    # The dashboard's own files alone, without the runtime.
    [switch]$AppOnly,
    [string]$Sdk = $env:SILVERLIGHT360
)

$ErrorActionPreference = 'Stop'
if ($Debug -eq $Release) {
    throw 'Say which build: -Debug (with the debug server, for testing) or -Release (without it, for others).'
}
$configuration = if ($Debug) { 'Debug' } else { 'Release' }
$root = Split-Path -Parent $PSScriptRoot
if (-not $Sdk) {
    $Sdk = Join-Path (Split-Path -Parent $root) 'Silverlight360'
}
$buildScript = Join-Path $Sdk 'tools\Build-XboxApp.ps1'
if (-not (Test-Path -LiteralPath $buildScript)) {
    throw "Silverlight360 not found at $Sdk. Pass -Sdk, set SILVERLIGHT360 or clone https://github.com/Digiex/Silverlight360 next to this repository."
}

# The project finds the SDK through the same variable.
$env:SILVERLIGHT360 = (Resolve-Path -LiteralPath $Sdk).Path
# MSBuild reads environment variables as properties.
$env:DebugServer = if ($Debug) { 'true' } else { 'false' }
& $buildScript -Project (Join-Path $root 'src\Silverdash360\Silverdash360.csproj') -Configuration $configuration -AppOnly:$AppOnly
