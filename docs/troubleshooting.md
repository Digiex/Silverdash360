# Troubleshooting

## It doesn't start, or goes back to the console's dashboard

- Check the folder has both the runtime (`default.xex`, `xcoreclr.dll`, `agcore.dll` and the rest) and Silverdash360's files (`Silverdash360.dll`, `AppManifest.xaml`, `XboxAppManifest.xml`). See [Installing](install.md).
- The runtime has to come from Plex v0.0.0.7, made with Silverlight360's `New-RuntimeProfile.ps1`.
- The console has to run homebrew.

## A game or app is missing

- The shelves show installed titles only. BACK on the shelf, **Show**, **Everything** shows the rest too.
- **Refresh library** in settings reads every package again.
- Homebrew is looked for in the `Apps` and `Games` folders of `Hdd1` and `Usb0` to `Usb2`, a folder each with a `default.xex` in it.

## The box art or the theme is missing

- Box art comes from the Xbox Marketplace the first time a title is shown, so the console needs the internet once. A title the Marketplace doesn't have gets a cover of its kind.
- Right after a start, the theme shows once the library has been read.

## A video doesn't play

See [Media](media.md#formats) for the formats the console plays. MKV and AVI files are listed but can't be played.

## A media server isn't listed

See [Media servers](media.md#media-servers).

## Starting afresh

Silverdash360 keeps its settings, the library it read, the box art and its logs in the `Data` folder next to its `default.xex`. Delete the folder, or a file in it, to start without it:

| In `Data` | |
|---|---|
| `settings.txt` | Your shelf options and other settings |
| `ThemeChoice` | The theme each profile picked, and the one for nobody signed in |
| `library.bin` | The library it read, so it starts quickly |
| `Art` | The box art and store details it downloaded |
| `Logs` | What it did: `silverdash.log` for this run, `silverdash.log.last` for the one before |

When you report a problem, `Logs\silverdash.log.last` from the run that went wrong helps: report it on [GitHub](https://github.com/Digiex/Silverdash360/issues).
