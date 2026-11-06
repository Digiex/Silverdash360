<#
.SYNOPSIS
Sets up building and testing Silverdash360: finds the Silverlight360 SDK,
runs its Configure.ps1, checks what this repository's own tools need, and
saves it for them. Run it first.

.DESCRIPTION
Run it once after cloning, and again after installing something it reports
missing, or to update XboxNGen. It:

  1. Finds Silverlight360: -Sdk, the SILVERLIGHT360 environment variable,
     the last configuration, or a Silverlight360 clone next to this
     repository. With none, it offers to clone
     https://github.com/Digiex/Silverlight360 there (-CloneSdk without
     asking).
  2. Runs Silverlight360's tools\Configure.ps1, which checks MSBuild, the
     Silverlight 3 SDK and the runtime profile, gets XboxNGen and finds
     xenia-edge (see its help). -MsBuild, -Xenia, -DonorApp, -Tag,
     -DownloadXenia, -SkipXboxNGen and -NonInteractive go to it.
  3. Checks what Silverdash360's own tools need:
       Python 3.7 or later  tools\deploy.py, tools\screenshot.py and the
                            tests (from Silverlight360's configuration)
       GitHub CLI (gh)      optional: tools\Release.ps1
       MkDocs               optional: previewing the docs site

The result goes to config.json at the repository's root (git ignores it),
which tools\Config.ps1 reads for Build.ps1, Run.ps1 and Release.ps1, so
they need no -Sdk. -Show prints it.

.EXAMPLE
.\tools\Configure.ps1

.EXAMPLE
.\tools\Configure.ps1 -Sdk D:\src\Silverlight360 -DonorApp 'D:\apps\Plex v0.0.0.7'

Uses the SDK there and has it make its runtime profile from an extracted
Plex v0.0.0.7.
#>
param(
    # The Silverlight360 SDK's folder, when it isn't found.
    [string]$Sdk = '',
    # Clones Silverlight360 next to this repository without asking when it isn't found.
    [switch]$CloneSdk,
    # For Silverlight360's Configure.ps1: see its help.
    [string]$MsBuild = '',
    [string]$Xenia = '',
    [string]$DonorApp = '',
    [string]$Tag = '',
    [switch]$DownloadXenia,
    [switch]$SkipXboxNGen,
    # Asks nothing: no clone or xenia-edge download unless -CloneSdk or -DownloadXenia.
    [switch]$NonInteractive,
    [switch]$Show
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$parent = Split-Path -Parent $root
$jsonFile = Join-Path $root 'config.json'

if ($Show) {
    if (-not (Test-Path -LiteralPath $jsonFile)) {
        throw 'Not configured yet: run .\tools\Configure.ps1.'
    }
    (Get-Content -LiteralPath $jsonFile -Raw | ConvertFrom-Json).PSObject.Properties |
        ForEach-Object { '{0,-16} {1}' -f $_.Name, $(if ($_.Value) { $_.Value } else { '(not set)' }) }
    return
}

$previous = if (Test-Path -LiteralPath $jsonFile) { Get-Content -LiteralPath $jsonFile -Raw | ConvertFrom-Json } else { $null }
$config = [ordered]@{}

# Prints one line of the report, as Silverlight360's Configure.ps1 does.
function Report([string]$name, [string]$value, [string]$fix, [switch]$Required) {
    if ($value) {
        Write-Host ('{0,-18} {1}' -f $name, $value)
    } else {
        Write-Host ('{0,-18} {1}' -f $name, "missing: $fix") -ForegroundColor $(if ($Required) { 'Red' } else { 'Yellow' })
    }
}

function Test-Sdk([string]$folder) {
    return $folder -and (Test-Path -LiteralPath (Join-Path $folder 'tools\Configure.ps1')) -and
        (Test-Path -LiteralPath (Join-Path $folder 'tools\Build-XboxApp.ps1'))
}

# 1. Silverlight360.
$beside = Join-Path $parent 'Silverlight360'
$found = @($Sdk, $env:SILVERLIGHT360, $previous.Sdk, $beside) | Where-Object { Test-Sdk $_ } | Select-Object -First 1
if ($Sdk -and -not (Test-Sdk $Sdk)) {
    throw "Silverlight360 isn't at ${Sdk}: it has no tools\Configure.ps1 and tools\Build-XboxApp.ps1."
}
if (-not $found -and -not (Test-Path -LiteralPath $beside)) {
    if ($CloneSdk -or (-not $NonInteractive -and
            (Read-Host "Silverlight360 not found. Clone https://github.com/Digiex/Silverlight360 to ${beside}? (Y/n)") -notmatch '^n')) {
        if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
            throw 'Cloning Silverlight360 needs git: install it, or clone it yourself and pass -Sdk.'
        }
        & git clone https://github.com/Digiex/Silverlight360.git $beside
        if ($LASTEXITCODE -ne 0) {
            throw 'git clone failed.'
        }
        $found = $beside
    }
}
if (-not $found) {
    throw ('Silverlight360 not found: pass -Sdk <folder>, set SILVERLIGHT360, or clone ' +
           "https://github.com/Digiex/Silverlight360 to $beside.")
}
$config.Sdk = (Resolve-Path -LiteralPath $found).Path

# 2. Its Configure.ps1, with the options meant for it.
$sdkArgs = @{}
foreach ($name in 'MsBuild', 'Xenia', 'DonorApp', 'Tag') {
    $value = Get-Variable -Name $name -ValueOnly
    if ($value) {
        $sdkArgs[$name] = $value
    }
}
foreach ($name in 'DownloadXenia', 'SkipXboxNGen', 'NonInteractive') {
    if ((Get-Variable -Name $name -ValueOnly).IsPresent) {
        $sdkArgs[$name] = $true
    }
}
Write-Host "Silverlight360 at $($config.Sdk). Its Configure.ps1:"
& (Join-Path $config.Sdk 'tools\Configure.ps1') @sdkArgs
$sdkConfig = Join-Path $config.Sdk 'config.json'
$sdkSettings = if (Test-Path -LiteralPath $sdkConfig) { Get-Content -LiteralPath $sdkConfig -Raw | ConvertFrom-Json } else { $null }

# 3. Silverdash360's own tools.
Write-Host ''
Write-Host 'Silverdash360:'
Report 'Silverlight360' $config.Sdk '' -Required
$config.Python = if ($sdkSettings) { "$($sdkSettings.Python)" } else { '' }
Report 'Python 3.7+' $config.Python ('install Python 3.7 or later and put python on PATH, for tools\deploy.py, ' +
    'tools\screenshot.py and the tests') -Required
$gh = Get-Command gh -ErrorAction SilentlyContinue
$config.Gh = if ($gh) { $gh.Source } else { '' }
Report 'GitHub CLI' $config.Gh 'optional: install gh (https://cli.github.com) and sign in, for tools\Release.ps1'
$mkdocs = ''
if ($config.Python) {
    $answer = try {
        $ErrorActionPreference = 'Continue'
        & $config.Python -m mkdocs --version 2>$null
    } catch { '' }
    $ErrorActionPreference = 'Stop'
    $mkdocs = if ("$answer" -match 'version (\S+)') { "MkDocs $($Matches[1])" } else { '' }
}
Report 'MkDocs' $mkdocs 'optional: pip install mkdocs==1.6.1, to preview the docs site'
$xenia = if ($sdkSettings) { "$($sdkSettings.Xenia)" } else { '' }
Report 'xenia-edge' $xenia 'optional for Silverdash360, but builds make the native image with it: see above'

$config | ConvertTo-Json | Set-Content -LiteralPath $jsonFile -Encoding utf8
Write-Host ''
if (-not $config.Python) {
    Write-Warning 'Python is missing: get it as said above, then run .\tools\Configure.ps1 again.'
} elseif ($sdkSettings -and -not $sdkSettings.Xenia -and -not $SkipXboxNGen) {
    Write-Warning ('XboxNGen or xenia-edge is missing, so builds need -NoNGen until it''s set up: ' +
                   'run .\tools\Configure.ps1 again once it is.')
}
Write-Host "Saved in $jsonFile."
