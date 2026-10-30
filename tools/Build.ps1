<#
.SYNOPSIS
Builds Silverdash360 into an Xbox 360 package.

.DESCRIPTION
Runs the Silverlight360 SDK's Build-XboxApp.ps1 on the dashboard. The package
lands in src\Silverdash360\bin\<Configuration>\xbox\.

The SDK is the one given with -Sdk, else the SILVERLIGHT360 environment
variable, else a Silverlight360 clone next to this repository.

.EXAMPLE
.\tools\Build.ps1 -Sdk ..\Silverlight360
#>
param(
    [string]$Sdk = $env:SILVERLIGHT360,
    [ValidateSet('Debug', 'Release')] [string]$Configuration = 'Release'
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
& $buildScript -Project (Join-Path $root 'src\Silverdash360\Silverdash360.csproj') -Configuration $Configuration
