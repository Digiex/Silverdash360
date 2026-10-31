<#
.SYNOPSIS
Builds Silverdash360 into an Xbox 360 package.

.DESCRIPTION
Runs the Silverlight360 SDK's Build-XboxApp.ps1 on the dashboard. The package
lands in src\Silverdash360\bin\<Configuration>\xbox\.

The SDK is the one given with -Sdk, else the SILVERLIGHT360 environment
variable, else a Silverlight360 clone next to this repository.

The debug server (a TCP port for testing on a console, see tests\README.md)
is built in unless -NoDebugServer is given.

.EXAMPLE
.\tools\Build.ps1 -Sdk ..\Silverlight360

.EXAMPLE
.\tools\Build.ps1 -NoDebugServer
#>
param(
    [string]$Sdk = $env:SILVERLIGHT360,
    [ValidateSet('Debug', 'Release')] [string]$Configuration = 'Release',
    # Leaves out the debug server, which is in by default for now.
    [switch]$NoDebugServer
)

$ErrorActionPreference = 'Stop'
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
$env:DebugServer = if ($NoDebugServer) { 'false' } else { 'true' }
& $buildScript -Project (Join-Path $root 'src\Silverdash360\Silverdash360.csproj') -Configuration $Configuration
if ($LASTEXITCODE) {
    exit $LASTEXITCODE
}

# The donor's default.xex declares the donor's title, which brings its title
# updates; the package's copy declares none (see strip-xex.py).
python (Join-Path $PSScriptRoot 'strip-xex.py') (Join-Path $root "src\Silverdash360\bin\$Configuration\xbox\default.xex")
if ($LASTEXITCODE) {
    exit $LASTEXITCODE
}
