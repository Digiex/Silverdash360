# Updating

Silverdash360 looks for a new version on GitHub a few seconds after it starts, once you've left the controller alone for a moment. It needs the console to be on the internet; it doesn't need Xbox LIVE.

When there's a new version, it shows what's new in it and asks:

| Choice | |
|---|---|
| **Update now** | Downloads the new version, installs it and starts it. It takes a few seconds. |
| **Later** (or B) | Asks again the next time Silverdash360 starts. |
| **Skip this version** | Doesn't ask about this version again. A newer one is offered as usual. |

**Check for updates** in settings looks any time, and offers a version you skipped too.

## How an update installs

The update is checked against the release on GitHub before anything is changed: its size and its SHA-256 must match. Silverdash360 then puts each new file next to the one it replaces, swaps them, and starts again. If a step fails, the old files are put back and it carries on as it was. Your settings and library in the `Data` folder stay as they are.

## Updating by hand

Download the zip of the [latest release](https://github.com/Digiex/Silverdash360/releases/latest) and copy what's in it over the files in Silverdash360's folder on the console, replacing them. The runtime files stay as they are.

Version 0.1.0 doesn't look for updates, so updating from it to 0.1.1 or later is done by hand once.

## Which version you have

**Version** in settings shows it, such as `0.1.1, build 50 (commit 80c3ccc)`.
