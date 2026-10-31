<#
.SYNOPSIS
Runs the built dashboard in xenia-edge, presses buttons and takes screenshots.

.DESCRIPTION
Uses the Silverlight360 SDK's Test-XboxApp.ps1 (see its testing guide for the
steps and buttons). -ContentRoot gives xenia a content folder of its own, so
the dashboard can be tried on a known set of games and apps without touching
xenia's own. Copy a profile's folder into it to have someone signed in.

.EXAMPLE
.\tools\Run.ps1 -ContentRoot $env:TEMP\testcontent -Steps '15:shot','16:RightShoulder','18:shot'
#>
param(
    [string]$Sdk = $env:SILVERLIGHT360,
    [string]$Output = (Join-Path $env:TEMP 'silverdash-run'),
    [string]$ContentRoot = '',
    [string[]]$Steps = @('16:shot'),
    [int]$Seconds = 20,
    # xenia_edge.exe: this, else the XENIA_EDGE environment variable, else PATH.
    [string]$Xenia = $env:XENIA_EDGE,
    # More xenia options, such as --systemlink_transport=off for the FTP server.
    [string[]]$ExtraArgs = @()
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if (-not $Sdk) {
    $Sdk = Join-Path (Split-Path -Parent $root) 'Silverlight360'
}
$testScript = Join-Path $Sdk 'tools\Test-XboxApp.ps1'
if (-not (Test-Path -LiteralPath $testScript)) {
    throw "Silverlight360 not found at $Sdk. Pass -Sdk or set SILVERLIGHT360."
}
$xex = Join-Path $root 'src\Silverdash360\bin\Debug\xbox\default.xex'
if (-not (Test-Path -LiteralPath $xex)) {
    throw "Build the dashboard first: .\tools\Build.ps1 -Debug"
}

$extra = @($ExtraArgs)
if ($ContentRoot) {
    $extra += "--content_root=$((Resolve-Path -LiteralPath $ContentRoot).Path)"
}
$arguments = @{ Xex = $xex; Output = $Output; Steps = $Steps; Seconds = $Seconds; ExtraArgs = $extra }
if ($Xenia) {
    $arguments.Xenia = $Xenia
}
& $testScript @arguments
