# Development

Silverdash360 is C# and XAML for the Xbox 360's Silverlight platform, built with the [Silverlight360](https://github.com/Digiex/Silverlight360) SDK.

## Building

You need what Silverlight360 needs (see its [getting started guide](https://github.com/Digiex/Silverlight360/blob/master/docs/getting-started.md)), including a runtime profile (see [The runtime](#the-runtime)). First, once after cloning, in PowerShell:

```powershell
.\tools\Configure.ps1
```

It finds Silverlight360: `-Sdk <folder>`, the `SILVERLIGHT360` environment variable, the last configuration, or a `Silverlight360` clone next to this repository; with none, it offers to clone it there (`-CloneSdk` without asking). It then runs Silverlight360's own `tools\Configure.ps1`, which checks MSBuild, the Silverlight 3 SDK and the runtime profile, gets XboxNGen and finds xenia-edge (or offers to download it); `-MsBuild`, `-Xenia`, `-DonorApp`, `-Tag`, `-DownloadXenia`, `-SkipXboxNGen` and `-NonInteractive` go to it. Last it checks what this repository's other tools need, all optional, as building needs none of it: Python 3.7 or later for `tools\deploy.py`, `tools\screenshot.py` and the tests, the GitHub CLI for `tools\Release.ps1` and MkDocs for the docs. What it found goes to `config.json` (git ignores it), so the other tools need no `-Sdk`; `.\tools\Configure.ps1 -Show` prints it. Run it again after installing something it reports missing, or to update XboxNGen.

Then:

```powershell
.\tools\Build.ps1 -Debug
```

Every build is one of two:

- `-Debug` starts with the debug server on (see below) and lands in `src\Silverdash360\bin\Debug\xbox\`. `tools\deploy.py`, `tools\Run.ps1` and the tests use it.
- `-Release` starts with the debug server off, until Settings > **Debug server** turns it on, and lands in `src\Silverdash360\bin\Release\xbox\`. Builds for others are these. `tools\Run.ps1 -Release` runs it.

Both compile `Silverdash360.dll` to a native image, `Silverdash360.ni.dll`, with [XboxNGen](https://github.com/Digiex/Silverlight360-XboxNGen) (as the SDK's `Build-XboxApp.ps1` does by default; see its [native images](https://github.com/Digiex/Silverlight360/blob/master/docs/native-images.md) page), so the dashboard's code runs compiled instead of through the runtime's IL interpreter: on a console its benchmarks run tens to hundreds of times faster. That runs xenia-edge from the `silverlight-build` branch of [jessenic/xenia-edge](https://github.com/jessenic/xenia-edge/tree/silverlight-build), the one `tools\Configure.ps1` set up, for a minute or so, and can't while another xenia-edge runs. `-NoNGen` builds without the image, quicker; that package runs interpreted. The image is unsigned, which the consoles Silverdash360 runs on allow.

An image is bound to the exact `Silverdash360.dll` it was made from: one left beside a newer `Silverdash360.dll` stops the runtime as it starts. A build always has a matching one or none, and `tools\deploy.py`, `tools\Run.ps1 -Install` and the in-app update rename or delete an installed image when what they put in has none. Settings lists **XboxNGen** while the image is loaded, as XboxNGen's license asks.

`-AppOnly` writes Silverdash360's own files alone, without the runtime but with the native image, into `bin\<Debug|Release>\app\`. The image is made in the whole package first, which stays in `bin\<Debug|Release>\xbox\`, and fits only a runtime made from the same donor app as the SDK's profile (Plex's, as the base package's is); `-NoNGen` leaves it out. `-Sdk` picks another SDK than the one `Configure.ps1` found, as the `SILVERLIGHT360` environment variable does.

The package runs as the title the SDK's runtime profile came from (Plex by default), so it's started like that app. The debug server listens on port 4601 without a password, which is why Release builds leave it off: turning it on lets anyone on the network control the dashboard.

## The runtime

Silverdash360 runs on the Silverlight platform the console's own apps shipped with: the runtime (`default.xex`, `xcoreclr.dll`, `agcore.dll` and the rest) and its framework assemblies. They're Microsoft's and the donor app's, so GitHub releases have only Silverdash360's own files, and users get the runtime with the base package from the [Digiex thread](https://digiex.net/threads/silverdash360-homebrew-xbox-360-dashboard.20103/) (see [Installing](install.md)). To build Silverdash360, or to make the base package, make the runtime yourself from the Plex app for Xbox 360, version 0.0.0.7, extracted; the [Silverlight360 getting started guide](https://github.com/Digiex/Silverlight360/blob/master/docs/getting-started.md) says where to get it and how to extract it. In PowerShell in the Silverlight360 folder:

```powershell
.\tools\New-RuntimeProfile.ps1 -DonorApp 'C:\path\to\Plex v0.0.0.7' -Output .\runtime\plex-2015
```

The runtime is the `runtime\plex-2015\package` folder it makes, once for every version of Silverdash360. A build puts it and Silverdash360's files together; so does copying a release zip's files over a copy of that folder. The package's `default.xex` is the platform's, changed to drop the donor app's title, so it starts only on a console that runs homebrew.

## Releases

```powershell
.\tools\Release.ps1 -DryRun
.\tools\Release.ps1
```

`Release.ps1` makes a Release build of Silverdash360's own files (`Build.ps1 -Release -AppOnly`, the ones the SDK's `XboxAppOnly` lists), zips them with an `INSTALL.txt`, and publishes the zip on GitHub Releases with the commits since the last release as its notes. The zip has no `default.xex`, runtime or framework assemblies, nor images of them: those are Microsoft's and the donor app's, and the script stops if one is there, so whoever installs it lays it over the base package or a runtime profile of their own.

The zip always has the native image of its `Silverdash360.dll` too, which the build makes in the whole Release package, and `NativeImage.txt`, the runtime the image was made against: each of the runtime's native images and `xcoreclr.dll`, with its size. The base package's runtime is that one (Plex's). The in-app update installs the image only when the files in the dashboard's folder match the record; otherwise it leaves the image out and the dashboard runs interpreted. `INSTALL.txt` says to leave it out by hand over another runtime.

Every release has a higher version than the last, tagged `v<version>`. That's `Silverdash360Version` in `Silverdash360.csproj` when it's higher than the last release's, so a minor or major version can be set by hand; otherwise the script raises the last release's patch number (or the part `-Bump Minor` or `-Bump Major` names). A new version is committed to the project file and that commit pushed, so the release is built and tagged from it; everything before it has to be committed and pushed already. The release's title has the build number settings shows. The script needs the GitHub CLI (`gh`) signed in. `-DryRun` shows the version, builds the zip and shows the notes, without committing, pushing or publishing. `-Highlight 'Huge performance improvements'` puts lines at the top of the notes, above the commits; the update screen shows the notes' first lines.

`.\tools\Release.ps1 -Console` makes the base package that the [Digiex thread](https://digiex.net/threads/silverdash360-homebrew-xbox-360-dashboard.20103/) offers, and releases nothing: the whole Release package, runtime included, in a `Silverdash360` folder (`src\Silverdash360\bin\Release\console\Silverdash360-<version>-console.zip`) to copy to the console. It has the donor app's and Microsoft's files, so it's posted on Digiex rather than on GitHub.

## The documentation

This site is the `docs` folder, built with [MkDocs](https://www.mkdocs.org/) (`mkdocs.yml`). A push to `master` that changes it publishes it to GitHub Pages (`.github/workflows/docs.yml`). To preview it:

```powershell
pip install mkdocs==1.6.1
python -m mkdocs serve
```

## Debug server and tests

While the debug server is on (from the start in a Debug build; in a Release build once Settings > **Debug server** turns it on), it takes commands on TCP port 4601, one line each, and answers with a line of JSON: what the library holds, the console's functions called directly, memory, the log, what's on screen, button presses, frame rates and benchmarks of the dashboard's own work. Settings shows its address. The setting is kept, so a build deployed over one with it on starts with it on; it has no password, so anyone on the network can control the dashboard while it's on.

```bash
python tests/run_tests.py 192.168.1.20
python tests/debugclient.py 192.168.1.20
```

The first runs the API, functional and performance tests against the console and can compare performance with an earlier run; the second is a shell for typing commands. See [tests/README.md](../tests/README.md) for the commands, the protocol and adding tests.

To put a new build on the console and start it, over the dashboard's own FTP server and the debug server:

```bash
python tools/deploy.py 192.168.1.20 -- --suite functional
```

It finds where the dashboard runs from (else `/Hdd1/Apps/Silverdash360`), uploads only the files that changed, starts the new build through a staging folder so no running file is overwritten, checks it came up, and runs the tests given after `--`. See [Deploying a build](../tests/README.md#deploying-a-build).

## Running in xenia-edge

```powershell
.\tools\Run.ps1 -Steps '16:shot' -Seconds 20
```

`-ContentRoot` gives xenia a content folder of its own, so the dashboard can be tried on a known set of packages without touching xenia's (see [Testing in xenia](https://github.com/Digiex/Silverlight360/blob/master/docs/testing.md)). The dashboard needs the XAM functions below, which xenia-edge doesn't have yet; they're on the `pr-dashboard-content` branch, to be proposed to xenia-edge.

| Function | What the dashboard uses it for |
|---|---|
| `XamContentAggregateCreateEnumerator` | Every package of every title, as the console's dashboard lists them |
| `XamContentGetMetaDataInternal` | A package header's names, description and art |
| `XamContentLaunchImageInternalEx` | Starting a package |

xenia's hard drive has only its `Content` folder, so a build run from `bin\Debug\xbox` can't save its library, art or logs. To have it save them, install it under the content folder: `-Install` copies the build to `<ContentRoot>\Silverdash360` and runs it from there, where the dashboard finds itself as `/Hdd1/Content/Silverdash360` and keeps its data in `Data` beside it. `-Fresh` deletes that `Data` first, as a new install.

```powershell
.\tools\Run.ps1 -ContentRoot $env:TEMP\testcontent -Install -Fresh
```

To sign a profile in at the start, pass `--logged_profile_slot_0_xuid=<offline XUID>` in `-ExtraArgs`; xenia keeps the flags it's given in its config, so clear that line there to start with nobody signed in again. Signing in and out while the dashboard runs is xenia's Profile menu.

The dashboard writes what it does to xenia's log as `(DbgPrint) [Silverdash] ...`. For the FTP server, run with `--systemlink_transport=off` (for example `.\tools\Run.ps1 -ExtraArgs '--systemlink_transport=off'`): otherwise xenia's System Link emulation carries the dashboard's sockets and nothing listens on the PC. xenia moves port 21 to 10021, and the hard drive has only its `Content` folder there.

## How it works

Everything is C#. The console's own functions are called with `DllImport` by ordinal, with structures passed as pinned big-endian buffers.

```
src/Silverdash360/
  Native/     DllImports into xam.xex and xboxkrnl.exe, and NativeBuffer for structures
  Model/      ContentItem (one package), ContentMetadata (its header), TitleEntry, Gamer, ...
  Core/       Logging, background work, UiIdle (work that waits for the user), a JSON reader
  Services/   Library, LibraryCache, ProfileService, ImageService, ThemeService, UpdateService, ...
    Files/    XboxFileSystem: the drives as one tree of paths
    Ftp/      FtpServer, FtpSession and NativeSocket
    Media/    MediaService, the drive, DLNA and downloaded video sources, the local media server, an HTTP client
  Controls/   TileGrid (the virtualized grid), tile templates, OnScreenKeyboard, Theme
  Modules/    The hub's sections and the registry they're listed in
  Pages/      Hub, title details, search, storage, media, the player, pictures, files, themes, updates
  Debugging/  The debug server and its commands, on by default in Debug builds
tests/        The tests that run over the debug server, and a client for it
```

The library is read the way the console's dashboard reads it:

1. **Content**: `XamContentAggregateCreateEnumerator` over every title, for common content and for the signed-in profile, returning 0x200-byte `XCONTENT_DATA_INTERNAL` records: of every type on a first start or a full refresh, otherwise of the types that start games and apps (see below).
2. **Metadata**: `XamContentGetMetaDataInternal` per package, for names, description, publisher, version, size and thumbnails.
3. **Profile**: `XamUserGetSigninInfo`, `XamUserReadProfileSettings` for the gamer card, `XamUserCreateTitlesPlayedEnumerator` and `XamUserCreateAchievementEnumerator`, and `XamReadTile` for game tiles and gamer pictures.
4. **Launching**: `XamContentLaunchImageInternalEx` with the package's record, after checking it isn't partly downloaded or damaged. An executable from an Apps or Games folder starts with `XamLoaderLaunchTitleEx` and flags 0, with its folder as the D: drive.
5. **FTP**: the console's Winsock (`NetDll_socket`, `bind`, `listen`, `accept`, ...) on threads of its own, and the kernel's file calls on the drives' device paths.

`LibraryCache` keeps the records and the header metadata in the `Data` folder of the folder the dashboard is installed in: the console's packages and the Apps and Games executables, the same whoever is signed in, in `library.bin`, and each profile's own packages (its saves and themes) in `Profiles\<offline XUID>.bin`. A package is the profile's when the profile's enumeration found it, whatever its record's XUID says. On a start the saved library is shown first, the console's packages with the signed-in profile's. Then only the types that start games and apps, with disc installs and themes, are listed again: on a console each type listed costs over 2 s however few packages it has, plus about 16 ms a package, and those are a few hundred of thousands. The nine types are listed at once, a thread each, as the console's listings overlap: 25 s one after another, 13 s at once (the debug server's `library.types` times both). Add-ons, saves, avatar items, videos and the rest stay as saved; a title's page lists that title's packages again (an add-on holding an app, as Air Band is, keeps what was found in it), and **Refresh library** in settings lists everything. Only packages the files don't know have their headers read. With nobody signed in, only the console's are shown. A profile signing in or out from the Guide changes only the profile's part: the new profile's file and enumeration are read, the titles it and the old profile touch are built again (or copied, when only their played stats and saves change), and the console's packages stay as they are. A sign-in during a refresh pauses the refresh until the profile is shown. The debug server's `library.profile` times that step by step and checks it against a whole build. Reading the library, the art and the Marketplace pause while the user moves (`UiIdle`), and a save is written to a new file before the old one is replaced, so starting a game part way through doesn't lose it.

**Themes**: the theme in use is kept per profile in `Data\CurrentTheme\<offline XUID>`: its wallpapers and the colours worked out from the first, with `theme.txt` naming the theme (`package:<title>/<file>` or `builtin:<n>`). A start reads only the profile's theme choice; when it names the kept theme, the wallpapers and colours come from there, without mounting the package or sampling a wallpaper again. Theme files don't change, so the name is all that's compared. The built-in themes' list is read from the DashPack (`\Device\Harddisk0\SystemExtPartition\Content\0000000000000000\FFFE07DF\00008000\FFFE07DF00000006` on a console) only when the theme picker wants it. A picture theme made in the Xbox Dashboard keeps its wallpaper as raw pixels rather than a JPEG: ` RGB`, the width and height as big-endian words, then red, green and blue bytes a row at a time; `WallpaperImage` turns those into a `WriteableBitmap`'s pixels in the background. The theme picked is each profile's too, and nobody signed in's, in `Data\ThemeChoice\<offline XUID or Nobody>.txt`; one with no pick follows the Xbox Dashboard's theme, which for nobody signed in is Silverdash's own look. Themes from the editor are kept per profile in `Data\CustomTheme` and, saved for everyone, in `Data\SavedThemes`.

**Media**: the platform's player plays only from a URI, so the dashboard serves a file on a drive to it over HTTP on the console itself (port 4602, with a key made up at each start in every URL). A media server's files go through the same server, as the player won't play from another machine. Media servers are found with an SSDP search and browsed through their ContentDirectory; `tests/dlna_server.py` is a small one for trying it.

**System settings** (`Services/ConsoleSettings.cs`): the Xbox Dashboard's Console Settings (dash 2.0.17559) read and written as it does: XConfig settings through `ExGetXConfigSetting`/`ExSetXConfigSetting` (user category 3, Xbox category 7), the video driver's options (`VdSetOption`), `XAudioSetSpeakerConfig`, `XamSetCountry`, `NtSetSystemTime`, `XamSetConsoleFriendlyName` and the system controller for the remote. Network Settings (`Services/NetworkSettings.cs`, `Native/XNet.cs`) goes through xam's network configuration (`XnpLoadConfigParams`/`XnpSaveConfigParams`, then `XnpConfig`; xam keeps it encrypted in XConfig category 5) and the wireless adapter's options (`XNetGetOpt`/`XNetSetOpt`); Network Statistics measures with the console's sockets, a UDP echo to `x1ds.xboxlive.com:3074`, an upload to `networktest.xboxlive.com` and an HTTP download from `speedtest.xboxlive.com`, as the Xbox Dashboard does. System Info (`Services/SystemInfo.cs`) reads the serial number and console ID with `XeKeysGetKey`/`XeKeysGetConsoleID`, the kernel's versions from its exports, and the support codes as the Xbox Dashboard builds them. The resolution list comes from `VdEnumerateVideoModes`, which calls back into managed code, and a resolution is recorded with `VdSetDisplayModeOverride` for the display's next start. Its lists, the 83 time zones, 42 locales and the language lists, are in `ConsoleSettingsTables.cs`. The Xbox Dashboard takes a video setting into use by resetting its own display; Silverdash360 starts again instead, and for a resolution keeps the old one in `Data\display-trial.txt` to put back unless the start in the new one is confirmed within 10 seconds. The debug server's `xconfig.get <category> <setting> <size>` and `xconfig.av` read them.

**Updates**: the latest release comes from GitHub's API over HTTPS through the platform's `HttpWebRequest` (`github.com` itself doesn't answer the console; `api.github.com` and its file storage do). The zip is checked against the release's size and SHA-256, its files written next to the dashboard's as `.new`, each running file renamed to `.old` (the system allows that for a file in use) and the new one put in its place; a failure puts the old ones back. A release's native image goes in only over the runtime its `NativeImage.txt` records; without it, the update renames an installed `Silverdash360.ni.dll` to `.old` as well, and the dashboard runs interpreted. The dashboard starts again, and the next start deletes the `.old` files. Debug builds don't look by themselves; the debug server's `update.check`, `update.verify` and `update.install` test it.

## Extending it

- **A new hub section**: implement `IDashboardModule` (or derive from `DashboardModule`) and add it to `ModuleRegistry`, or call `ModuleRegistry.Register` before the hub is shown. A section gives a view, a header, an order and a legend, and hands the D-pad's left and right edges to the hub with `ForwardEdges`.
- **A new basic setting**: add a `BasicSetting` to `BasicSettings.All` with `Since` one higher than the newest. Settings lists it, and the welcome screen opens once after the update that brings it, with it marked new.
- **Telling more apps from games**: add an `ITitleClassifier` to `Library.Classifiers`. Classifiers are asked in order and the first answer wins.
- **Another content source**: implement `IContentSource`, for example to list packages from a network share, and pass it to `Library`.
- **A new view of titles**: `TileGrid` takes any `ITileTemplate`. `RowTemplate` makes rows from a `RowData` description, and `CoverTileTemplate` makes covers.
