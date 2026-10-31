<#
.SYNOPSIS
Builds Silverdash360 for release and publishes it on GitHub Releases.

.DESCRIPTION
Makes a Release build of the dashboard's own files, zips them and
publishes the zip as a GitHub release, with the commits since the last
release as its notes.

The zip has only what this repository makes: Silverdash360.dll, the app
manifests and the splash screens, from the SDK's app-only build
(Build.ps1 -Release -AppOnly), with no debug server. The package's other
files, default.xex and the Silverlight platform, are the runtime profile's:
Microsoft's and the donor app's binaries, which aren't ours to hand out.
Whoever installs it lays the zip over the package folder of a runtime
profile they made themselves (see INSTALL.txt in the zip).

Each release is tagged build-<number>, the number being the commits up to
it, the build number settings shows. The commit must be clean and pushed:
the release is tagged on GitHub at that commit, and nothing is pushed.

Needs the GitHub CLI (gh), signed in, unless -DryRun is given.

.EXAMPLE
.\tools\Release.ps1 -DryRun

Builds and zips, and shows the release notes, without publishing.

.EXAMPLE
.\tools\Release.ps1 -Sdk ..\Silverlight360
#>
param(
    [string]$Sdk = $env:SILVERLIGHT360,
    # Builds the zip and shows the notes, without tagging or publishing.
    [switch]$DryRun,
    # Publishes the release as a prerelease.
    [switch]$Prerelease
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'src\Silverdash360'

function Git {
    $output = & git -C $root @args
    if ($LASTEXITCODE -ne 0) {
        throw "git $args failed"
    }
    $output
}

# A release is made from a commit as it is, which GitHub has.
if (Git status --porcelain --untracked-files=no) {
    throw 'There are changes not committed. Commit them, or stash them, first.'
}
$commit = Git rev-parse HEAD
if (-not $DryRun) {
    if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
        throw 'The GitHub CLI (gh) is needed to publish: https://cli.github.com'
    }
    Git fetch --quiet --tags origin
    if (-not (Git branch -r --contains $commit)) {
        throw "Commit $commit isn't on GitHub yet. Push it first."
    }
}

# A Release build, without the debug server, which listens without a
# password: the dashboard's own files alone.
& (Join-Path $PSScriptRoot 'Build.ps1') -Release -AppOnly -Sdk $Sdk
if ($LASTEXITCODE -ne 0) {
    throw 'The build failed.'
}

# The version and build number the build wrote into BuildInfo.
$buildInfo = Get-Content -LiteralPath (Join-Path $project 'obj\Release\BuildInfo.g.cs') -Raw
$version = [regex]::Match($buildInfo, 'Version = "([^"]*)"').Groups[1].Value
$number = [regex]::Match($buildInfo, 'Number = (\d+)').Groups[1].Value
if ([regex]::Match($buildInfo, 'Changed = (\w+)').Groups[1].Value -ne 'false') {
    throw 'The build has changes not committed.'
}
$tag = "build-$number"
$name = "Silverdash360 $version (build $number)"
if (Git tag --list $tag) {
    throw "$tag is released already. Commit something new to release."
}

# Nothing of the runtime's: the app-only build leaves it out.
$appFiles = Join-Path $project 'bin\Release\app'
$ours = @(Get-ChildItem -LiteralPath $appFiles -Recurse -File | ForEach-Object { $_.FullName.Substring($appFiles.Length + 1) })
foreach ($relative in $ours) {
    if ($relative -eq 'default.xex' -or $relative -like '*.ni.dll' -or $relative -like 'Microsoft.*' -or
        $relative -like 'System.*' -or $relative -like 'mscorlib*') {
        throw "$relative is the platform's, which isn't released."
    }
}
if ($ours -notcontains 'Silverdash360.dll') {
    throw "Silverdash360.dll isn't in the app files at $appFiles."
}

$out = Join-Path $project 'bin\Release\release'
$staging = Join-Path $out 'Silverdash360'
if (Test-Path -LiteralPath $out) {
    Remove-Item -LiteralPath $out -Recurse -Force
}
New-Item -ItemType Directory -Path $staging | Out-Null
Copy-Item -Path (Join-Path $appFiles '*') -Destination $staging -Recurse
Copy-Item -LiteralPath (Join-Path $root 'LICENSE.md') -Destination $staging
@"
$name
https://github.com/Digiex/Silverdash360

This zip has Silverdash360's own files only. The runtime it runs on,
default.xex and the Silverlight platform, comes from an Xbox 360 Silverlight
app you have, and isn't included.

1. Make a runtime profile from an extracted Xbox 360 Silverlight app with
   the Silverlight360 SDK's tools\New-RuntimeProfile.ps1 (see its getting
   started guide: https://github.com/Digiex/Silverlight360).
2. Copy the profile's package folder to a folder on the console, such as
   Hdd1:\Apps\Silverdash.
3. Copy everything in this zip into that folder, replacing what's there.
4. Start default.xex in that folder.

Silverdash360 keeps its settings and cache in the Data folder next to it.
"@ | Set-Content -LiteralPath (Join-Path $staging 'INSTALL.txt') -Encoding UTF8
$zip = Join-Path $out "Silverdash360-$version-build$number.zip"
Compress-Archive -Path (Join-Path $staging '*') -DestinationPath $zip

# The notes: the commits since the last release before this commit.
$previous = $null
foreach ($candidate in (Git tag --list 'build-*' --sort=-creatordate)) {
    & git -C $root merge-base --is-ancestor $candidate $commit
    if ($LASTEXITCODE -eq 0) {
        $previous = $candidate
        break
    }
}
$range = if ($previous) { "$previous..$commit" } else { $commit }
$notes = @()
$notes += if ($previous) { "## Changes since $previous" } else { '## Changes' }
$notes += ''
$notes += Git log $range --no-merges --format='- %s (%h)'
$notes += ''
$notes += "Built from $($commit.Substring(0, 7)). The zip has Silverdash360's own files; see INSTALL.txt in it for putting it on a runtime profile you made."
$notesFile = Join-Path $out 'notes.md'
$notes | Set-Content -LiteralPath $notesFile -Encoding UTF8

Write-Host "$name ($tag)"
Write-Host "Files: $($ours -join ', '), LICENSE.md, INSTALL.txt"
Write-Host "Zip: $zip"
Write-Host ''
Get-Content -LiteralPath $notesFile | Write-Host
if ($DryRun) {
    Write-Host ''
    Write-Host 'Dry run: nothing published.'
    return
}

$arguments = @('release', 'create', $tag, $zip, '--repo', 'Digiex/Silverdash360', '--target', $commit,
               '--title', $name, '--notes-file', $notesFile)
if ($Prerelease) {
    $arguments += '--prerelease'
}
& gh @arguments
if ($LASTEXITCODE -ne 0) {
    throw 'gh release create failed.'
}
Git fetch --quiet --tags origin
