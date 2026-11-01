# Development

Silverdash360 is C# and XAML for the Xbox 360's Silverlight platform, built with the [Silverlight360](https://github.com/Digiex/Silverlight360) SDK.

## Building

You need what Silverlight360 needs (see its [getting started guide](https://github.com/Digiex/Silverlight360/blob/master/docs/getting-started.md)), including a runtime profile. Then:

```powershell
.\tools\Build.ps1 -Debug -Sdk path\to\Silverlight360
```

Every build is one of two:

- `-Debug` has the debug server (see below) and lands in `src\Silverdash360\bin\Debug\xbox\`. `tools\deploy.py`, `tools\Run.ps1` and the tests use it.
- `-Release` leaves the debug server out and lands in `src\Silverdash360\bin\Release\xbox\`. Builds for others are these.

`-AppOnly` writes Silverdash360's own files alone, without the runtime, into `bin\<Debug|Release>\app\`. `-Sdk` can be left out when the `SILVERLIGHT360` environment variable points to the SDK, or when it's cloned next to this repository as `Silverlight360`.

The package runs as the title the SDK's runtime profile came from (Plex by default), so it's started like that app. The debug server listens on port 4601 without a password, which is why it's only in Debug builds.

## Releases

```powershell
.\tools\Release.ps1 -DryRun
.\tools\Release.ps1
```

`Release.ps1` makes a Release build of Silverdash360's own files (the SDK's `XboxAppOnly`), zips them with an `INSTALL.txt`, and publishes the zip on GitHub Releases with the commits since the last release as its notes. The zip has no `default.xex`, runtime or framework assemblies: those are Microsoft's and the donor app's, so whoever installs it lays it over a runtime profile of their own.

Every release has a higher version than the last, tagged `v<version>`. That's `Silverdash360Version` in `Silverdash360.csproj` when it's higher than the last release's, so a minor or major version can be set by hand; otherwise the script raises the last release's patch number (or the part `-Bump Minor` or `-Bump Major` names). A new version is committed to the project file and that commit pushed, so the release is built and tagged from it; everything before it has to be committed and pushed already. The release's title has the build number settings shows. The script needs the GitHub CLI (`gh`) signed in. `-DryRun` shows the version, builds the zip and shows the notes, without committing, pushing or publishing.

`.\tools\Release.ps1 -Console` makes a zip for your own console instead and releases nothing: the whole Release package, runtime included, in a `Silverdash360` folder (`src\Silverdash360\bin\Release\console\Silverdash360-<version>-console.zip`) to copy to the console. It has the donor app's and Microsoft's files, so it isn't for handing out.

## The documentation

This site is the `docs` folder, built with [MkDocs](https://www.mkdocs.org/) (`mkdocs.yml`). A push to `master` that changes it publishes it to GitHub Pages (`.github/workflows/docs.yml`). To preview it:

```powershell
pip install mkdocs==1.6.1
python -m mkdocs serve
```

## Debug server and tests

While a Debug build runs, the debug server takes commands on TCP port 4601, one line each, and answers with a line of JSON: what the library holds, the console's functions called directly, memory, the log, what's on screen, button presses, frame rates and benchmarks of the dashboard's own work. Settings shows its address.

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
  Debugging/  The debug server and its commands, in Debug builds
tests/        The tests that run over the debug server, and a client for it
```

The library is read the way the console's dashboard reads it:

1. **Content**: `XamContentAggregateCreateEnumerator` over every title, for common content and for the signed-in profile, returning 0x200-byte `XCONTENT_DATA_INTERNAL` records: of every type on a first start or a full refresh, otherwise of the types that start games and apps (see below).
2. **Metadata**: `XamContentGetMetaDataInternal` per package, for names, description, publisher, version, size and thumbnails.
3. **Profile**: `XamUserGetSigninInfo`, `XamUserReadProfileSettings` for the gamer card, `XamUserCreateTitlesPlayedEnumerator` and `XamUserCreateAchievementEnumerator`, and `XamReadTile` for game tiles and gamer pictures.
4. **Launching**: `XamContentLaunchImageInternalEx` with the package's record, after checking it isn't partly downloaded or damaged. An executable from an Apps or Games folder starts with `XamLoaderLaunchTitleEx` and flags 0, with its folder as the D: drive.
5. **FTP**: the console's Winsock (`NetDll_socket`, `bind`, `listen`, `accept`, ...) on threads of its own, and the kernel's file calls on the drives' device paths.

`LibraryCache` keeps the records and the header metadata in the `Data` folder of the folder the dashboard is installed in: the console's packages and the Apps and Games executables, the same whoever is signed in, in `library.bin`, and each profile's own packages (its saves and themes) in `Profiles\<offline XUID>.bin`. A package is the profile's when the profile's enumeration found it, whatever its record's XUID says. On a start the saved library is shown first, the console's packages with the signed-in profile's. Then only the types that start games and apps, with disc installs and themes, are listed again: on a console each type listed costs about 3 s however few packages it has, plus about 16 ms a package, and those are a few hundred of thousands. Add-ons, saves, avatar items, videos and the rest stay as saved; a title's page lists that title's packages again (an add-on holding an app, as Air Band is, keeps what was found in it), and **Refresh library** in settings lists everything. Only packages the files don't know have their headers read. With nobody signed in, only the console's are shown. A profile signing in or out from the Guide changes only the profile's part: the new profile's file and enumeration are read, the titles it and the old profile touch are built again (or copied, when only their played stats and saves change), and the console's packages stay as they are. A sign-in during a refresh pauses the refresh until the profile is shown. The debug server's `library.profile` times that step by step and checks it against a whole build. Reading the library, the art and the Marketplace pause while the user moves (`UiIdle`), and a save is written to a new file before the old one is replaced, so starting a game part way through doesn't lose it.

**Themes**: the theme in use is kept per profile in `Data\CurrentTheme\<offline XUID>`: its wallpapers and the colours worked out from the first, with `theme.txt` naming the theme (`package:<title>/<file>` or `builtin:<n>`). A start reads only the profile's theme choice; when it names the kept theme, the wallpapers and colours come from there, without mounting the package or sampling a wallpaper again. Theme files don't change, so the name is all that's compared. The built-in themes' list is read from the DashPack (`\Device\Harddisk0\SystemExtPartition\Content\0000000000000000\FFFE07DF\00008000\FFFE07DF00000006` on a console) only when the theme picker wants it. A picture theme made in the Xbox Dashboard keeps its wallpaper as raw pixels rather than a JPEG: ` RGB`, the width and height as big-endian words, then red, green and blue bytes a row at a time; `WallpaperImage` turns those into a `WriteableBitmap`'s pixels in the background. Themes from the editor are kept per profile in `Data\CustomTheme` and, saved for everyone, in `Data\SavedThemes`.

**Media**: the platform's player plays only from a URI, so the dashboard serves a file on a drive to it over HTTP on the console itself (port 4602, with a key made up at each start in every URL). A media server's files go through the same server, as the player won't play from another machine. Media servers are found with an SSDP search and browsed through their ContentDirectory; `tests/dlna_server.py` is a small one for trying it.

**Updates**: the latest release comes from GitHub's API over HTTPS through the platform's `HttpWebRequest` (`github.com` itself doesn't answer the console; `api.github.com` and its file storage do). The zip is checked against the release's size and SHA-256, its files written next to the dashboard's as `.new`, each running file renamed to `.old` (the system allows that for a file in use) and the new one put in its place; a failure puts the old ones back. The dashboard starts again, and the next start deletes the `.old` files. Debug builds don't look by themselves; the debug server's `update.check`, `update.verify` and `update.install` test it.

## Extending it

- **A new hub section**: implement `IDashboardModule` (or derive from `DashboardModule`) and add it to `ModuleRegistry`, or call `ModuleRegistry.Register` before the hub is shown. A section gives a view, a header, an order and a legend, and hands the D-pad's left and right edges to the hub with `ForwardEdges`.
- **A new basic setting**: add a `BasicSetting` to `BasicSettings.All` with `Since` one higher than the newest. Settings lists it, and the welcome screen opens once after the update that brings it, with it marked new.
- **Telling more apps from games**: add an `ITitleClassifier` to `Library.Classifiers`. Classifiers are asked in order and the first answer wins.
- **Another content source**: implement `IContentSource`, for example to list packages from a network share, and pass it to `Library`.
- **A new view of titles**: `TileGrid` takes any `ITileTemplate`. `RowTemplate` makes rows from a `RowData` description, and `CoverTileTemplate` makes covers.
