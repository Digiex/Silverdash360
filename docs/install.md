# Installing

Silverdash360 runs on the Silverlight platform the console's own apps shipped with. The platform (the runtime and its libraries) isn't Digiex's to hand out, so a release has only Silverdash360's own files, and you add the platform from an app you have.

## What you need

- **An Xbox 360 that runs homebrew** (RGH or JTAG). Silverdash360's `default.xex` is the platform's, changed to drop the donor app's title, so a retail console won't start it.
- **A PC with Windows**, [Python 3](https://www.python.org/downloads/) and PowerShell.
- **The Plex app for Xbox 360, version 0.0.0.7**, extracted. The [Silverlight360 getting started guide](https://github.com/Digiex/Silverlight360/blob/master/docs/getting-started.md) says where to get it and how to extract it.
- **[Silverlight360](https://github.com/Digiex/Silverlight360)**, cloned or downloaded, for its runtime profile script.
- **A way to copy files to the console**, such as an FTP client or a USB drive.

## 1. Make the runtime

Make a runtime profile from the extracted Plex app with Silverlight360's script, in PowerShell in the Silverlight360 folder:

```powershell
.\tools\New-RuntimeProfile.ps1 -DonorApp 'C:\path\to\Plex v0.0.0.7' -Output .\runtime\plex-2015
```

The runtime is the `runtime\plex-2015\package` folder it makes. You make it once; every version of Silverdash360 runs on it.

## 2. Download Silverdash360

Download the zip of the [latest release](https://github.com/Digiex/Silverdash360/releases/latest), such as `Silverdash360-0.1.1.zip`.

## 3. Put them together

1. Make a folder for Silverdash360 on your PC, such as `Silverdash360`.
2. Copy everything in `runtime\plex-2015\package` into it.
3. Copy everything in the release zip into it, replacing what's there. `LICENSE.md` and `INSTALL.txt` can be left out.

## 4. Copy it to the console

Copy the folder to the console's hard drive, such as to `Hdd1:\Apps\Silverdash360`, over FTP or from a USB drive.

## 5. Start it

Start `default.xex` in that folder with your file manager or launcher. The first start welcomes you with a few basic settings, such as starting the FTP server with Silverdash360 (see [Welcome screen](settings.md#welcome-screen)), and reads every package on the console, which takes a minute or two with a large collection; the games and apps fill in as it goes, and later starts are quick.

Silverdash360 keeps its settings, the library it read and the box art it downloaded in a `Data` folder next to `default.xex`. Deleting `Data` starts it afresh.

## Next

- New versions install themselves when you say so: see [Updating](updating.md).
- To find your way around, see [Getting around](getting-around.md).
