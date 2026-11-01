# Installing

Silverdash360 comes in two parts: a base package with everything it needs to run, from the [Silverdash360 thread on Digiex](https://digiex.net/threads/silverdash360-homebrew-xbox-360-dashboard.20103/), and the latest update's files from [GitHub](https://github.com/Digiex/Silverdash360/releases/latest), which go over it.

## What you need

- **An Xbox 360 that runs homebrew** (RGH or JTAG). Silverdash360 isn't signed for a retail console, so it won't start on one.
- **A way to copy files to the console**, such as an FTP client or a USB drive.

## 1. Download the base package

Download the base package, such as `Silverdash360-0.1.2-console.zip`, from the first post of the [Silverdash360 thread on Digiex](https://digiex.net/threads/silverdash360-homebrew-xbox-360-dashboard.20103/), and extract it. It has a `Silverdash360` folder with everything Silverdash360 needs.

## 2. Add the latest update

The base package isn't updated with every version, so add the newest one's files to it:

1. Download the zip of the [latest release](https://github.com/Digiex/Silverdash360/releases/latest) on GitHub, such as `Silverdash360-0.1.2.zip`.
2. Extract everything in it into the base package's `Silverdash360` folder, replacing the files that are there. `LICENSE.md` and `INSTALL.txt` can be left out.

If the latest release has the same version as the base package, there's nothing to add.

## 3. Copy it to the console

Copy the `Silverdash360` folder to the console's hard drive, such as to `Hdd1:\Apps\Silverdash360`, over FTP or from a USB drive.

## 4. Start it

Start `default.xex` in that folder with your file manager or launcher. The first start welcomes you with a few basic settings, such as starting the FTP server with Silverdash360 (see [Welcome screen](settings.md#welcome-screen)), and reads every package on the console, which takes a minute or two with a large collection; the games and apps fill in as it goes, and later starts are quick.

Silverdash360 keeps its settings, the library it read and the box art it downloaded in a `Data` folder next to `default.xex`. Deleting `Data` starts it afresh.

## Next

- New versions install themselves when you say so: see [Updating](updating.md).
- To find your way around, see [Getting around](getting-around.md).
- To build Silverdash360 yourself, or make the base package from the app it runs on, see [Development](development.md).
