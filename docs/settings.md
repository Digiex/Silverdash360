# Settings

| Setting | |
|---|---|
| **Theme** | The wallpapers and colours: see [Themes](#themes) |
| **Storage** | Everything stored, largest first |
| **Search** | Find a game or app |
| **File browser** | Every file on the console's drives |
| **FTP server** | The console's drives over the network, on or off now: see [FTP server](#ftp-server) |
| **Start the FTP server with Silverdash** | Starts it whenever Silverdash360 starts |
| **FTP user name**, **FTP password** | What FTP clients log in with |
| **Welcome screen** | The basic settings, as at the first start: see [Welcome screen](#welcome-screen) |
| **Refresh library** | Reads every package again, for when something is missing or out of date |
| **Sign in** | Signs a profile in |
| **System version**, **Language**, **Console time** | The console's |
| **Running as title**, **Running from** | The title ID Silverdash360 runs as, and its folder |
| **Version** | Silverdash360's version, build and commit |
| **Check for updates** | Looks for a new version: see [Updating](updating.md) |
| **Return to the Xbox Dashboard** | Leaves Silverdash360 |

## Welcome screen

The welcome screen opens at the first start with the basic settings: whether the FTP server starts with Silverdash360 and, once it does, its user name and password. It opens again after an update that adds basic settings, with those marked **new**. Continue or B goes on to Silverdash360. **Welcome screen** in settings opens it any time.

## Themes

The theme follows the console dashboard's own by default, so your wallpapers come with you. **Theme** lists the themes built into the console's dashboard and those installed on the console; A uses one. The built-in themes are read from the console's own dashboard files, so they're the ones your dashboard version has. A theme's wallpapers go to the sections, a wallpaper each, and its colours to Silverdash360's. **Silverdash** is Silverdash360's own colours and no wallpaper. **Blades (Default)** and **Blades (Carbon)** are colours after the two styles of the first Xbox 360 dashboard, with no wallpaper; as its blades did, each section has a colour of its own: me is Xbox Live's orange, games green, media blue, apps the Marketplace blade's and settings the System blade's. If the dashboard's theme for your profile is a theme package that's no longer installed, such as one from an older dashboard, Silverdash360 shows its own colours and settings names the missing theme.

## Storage

Everything stored on the console, largest first, by kind: games, apps, add-ons, title updates, saves and media. LB and RB filter it. A opens the title a package belongs to.

## File browser

Every folder and file on the console's drives. A opens a folder, plays a song, video or picture, or starts a `.xex`. A content package shows its name and picture.

## FTP server

The FTP server shares the console's drives while Silverdash360 runs. **FTP server** turns it on and off with A, and shows the address, user name and password while it's on. With **Start the FTP server with Silverdash** on, it starts by itself each time.

The user name is `xbox` and the password six digits made up at the first start; both stay the same until you change them. A on **FTP user name** or **FTP password** opens the console's keyboard to change it.

- Connect with plain FTP, such as FileZilla with **Only use plain FTP**; there's no TLS.
- Each drive is a folder: `Hdd1`, `Usb0` to `Usb2`, `Mu0`, `Mu1` and `Dvd` (read only).
- You can download, upload, rename and delete, and make and remove folders.
- FTP sends the password unencrypted, so use it on a network you trust.
