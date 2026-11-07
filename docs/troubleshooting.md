# Troubleshooting

## It doesn't start, or goes back to the console's dashboard

- Check the folder has everything from the base package (`default.xex`, `xcoreclr.dll`, `agcore.dll` and the rest) and the update's files over them (`Silverdash360.dll`, `AppManifest.xaml`, `XboxAppManifest.xml`). See [Installing](install.md).
- A runtime you made yourself has to come from Plex v0.0.0.7: see [The runtime](development.md#the-runtime).
- The console has to run homebrew.

## A game or app is missing

- The shelves show installed titles only. BACK on the shelf, **Show**, **Everything** shows the rest too.
- **Refresh library** in settings reads every package again.
- Homebrew is looked for in the `Apps` and `Games` folders of `Hdd1` and `Usb0` to `Usb2`, a folder each with a `default.xex` in it.

## An add-on, save or downloaded video is missing

A start looks only for new games, apps and themes. A title's add-ons, updates and saves are read again when you open its page; **Refresh library** in settings reads everything, downloaded videos too.

## The box art or the theme is missing

- Box art comes from x360db, or else the Xbox Marketplace, the first time a title is shown, so the console needs the internet once. A title neither has gets a cover of its kind.
- The theme in use is kept, so it shows straight away at a start. The first time, or after picking another, it shows once its package has been found.
- Each profile has its own theme, and so has nobody signed in: see [Themes](settings.md#themes).

## A video doesn't play

See [Media](media.md#formats) for the formats the console plays. MKV and AVI files are listed but can't be played.

## A media server isn't listed

See [Media servers](media.md#media-servers).

## Starting afresh

Silverdash360 keeps its settings, the library it read, the box art and its logs in the `Data` folder next to its `default.xex`. Delete the folder, or a file in it, to start without it:

| In `Data` | |
|---|---|
| `settings.txt` | Your shelf options and other settings |
| `library.bin` | The console's games, apps and other packages it read, the same for every profile, so it starts quickly |
| `Profiles` | Each profile's own packages, such as its saves and themes, a file per profile |
| `ThemeChoice` | The theme each profile picked, and the one for nobody signed in |
| `CurrentTheme` | The theme in use for each profile, so it shows straight away |
| `CustomTheme` | The themes made in the theme editor, each profile's and nobody signed in's |
| `SavedThemes` | The themes saved with **Save as...**, for everyone |
| `Art` | The box art and store details it downloaded |
| `Logs` | What it did: `silverdash.log` for this run, `silverdash.log.last` for the one before |

When something here doesn't help, [report the problem](issues.md#reporting-a-problem) on GitHub; `Logs\silverdash.log.last` from the run that went wrong helps.
