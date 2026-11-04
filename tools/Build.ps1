<#
.SYNOPSIS
Builds Silverdash360 into an Xbox 360 package.

.DESCRIPTION
Runs the Silverlight360 SDK's Build-XboxApp.ps1 on the dashboard, as one of:

-Debug    with the debug server on (a TCP port for testing on a console, see
          tests\README.md), into src\Silverdash360\bin\Debug\xbox\. What
          tools\deploy.py, tools\Run.ps1 and the tests use.
-Release  with it off until Settings > Debug server turns it on, into
          src\Silverdash360\bin\Release\xbox\. For builds that go to others:
          the debug server listens without a password.

Both then compile Silverdash360.dll to a native image (Silverdash360.ni.dll)
with XboxNGen, through the SDK's -NGen (see its docs\native-images.md), so
the dashboard's code runs compiled instead of through the runtime's IL
interpreter. That runs xenia-edge (from the silverlight-build branch of
jessenic/xenia-edge) for a minute or two, and fails while another xenia-edge
is running. -NoNGen leaves the image out; the package then runs interpreted.
The image is unsigned: it runs in xenia-edge and on consoles that run
unsigned code, as Silverdash360 needs anyway.

-AppOnly writes the dashboard's own files alone, without the runtime and
without a native image, into bin\<Debug|Release>\app\ (see
tools\Release.ps1).

The SDK is the one given with -Sdk, else the SILVERLIGHT360 environment
variable, else a Silverlight360 clone next to this repository.

.EXAMPLE
.\tools\Build.ps1 -Debug -Sdk ..\Silverlight360

.EXAMPLE
.\tools\Build.ps1 -Release

.EXAMPLE
.\tools\Build.ps1 -Debug -NoNGen
#>
param(
    # With the debug server on, for testing.
    [switch]$Debug,
    # With the debug server off, for others.
    [switch]$Release,
    # The dashboard's own files alone, without the runtime.
    [switch]$AppOnly,
    # Without the native image: quicker, and without xenia-edge.
    [switch]$NoNGen,
    [string]$Sdk = $env:SILVERLIGHT360
)

$ErrorActionPreference = 'Stop'
if ($Debug -eq $Release) {
    throw 'Say which build: -Debug (with the debug server on, for testing) or -Release (with it off, for others).'
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
# MSBuild reads environment variables as properties. DebugServer is whether
# the debug server's setting starts on.
$env:DebugServer = if ($Debug) { 'true' } else { 'false' }
# The runtime has to run the whole package to make the image, so not with -AppOnly.
$ngen = -not ($NoNGen -or $AppOnly)
& $buildScript -Project (Join-Path $root 'src\Silverdash360\Silverdash360.csproj') -Configuration $configuration -AppOnly:$AppOnly -NGen:$ngen
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}
