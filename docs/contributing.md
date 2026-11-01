# Contributing

Contributions are welcome: fixes, features, documentation, testing on your console, and good bug reports. Pull requests go to [GitHub](https://github.com/Digiex/Silverdash360/pulls).

## Ways to help

- **Report problems and ideas** on [GitHub Issues](https://github.com/Digiex/Silverdash360/issues): see [Problems and ideas](issues.md).
- **Test on your console**, especially with a large collection, USB drives, media servers or themes that others might not have, and say what works and what doesn't.
- **Improve the docs**: every page here has an edit link to its file in the `docs` folder.
- **Fix something or add a feature** with a pull request.

## Before you start

For a small fix, a pull request on its own is fine. For a new feature or a bigger change, open an [issue](https://github.com/Digiex/Silverdash360/issues) first, or comment on one that's there, so we can agree on how it should work before you spend time on it. Issues labelled [good first issue](https://github.com/Digiex/Silverdash360/labels/good%20first%20issue) and [help wanted](https://github.com/Digiex/Silverdash360/labels/help%20wanted) are a good place to start.

## Making a change

1. Fork the repository and make a branch from `master`.
2. Set up the build: the [Silverlight360](https://github.com/Digiex/Silverlight360) SDK and a runtime you made yourself (see [Development](development.md#building) and [The runtime](development.md#the-runtime)).
3. Make the change, in the style of the code around it.
4. Try it on a console that runs homebrew, or in [xenia-edge](development.md#running-in-xenia-edge): a Debug build, `tools\deploy.py` to put it on the console, and the tests in `tests` (see [Debug server and tests](development.md#debug-server-and-tests)).
5. Update the docs in `docs` when you change what users see or do.
6. Open a pull request against `master`.

## Pull requests

- Keep each one to one change, so it's easy to review; a fix and an unrelated feature are two pull requests.
- Say what it changes and why, which issue it fixes (`Fixes #12`), and how you tested it: on which console or in xenia, and which test suites passed.
- Screenshots help for anything on screen: `tools\screenshot.py` takes them from a running Debug build (see the [tests README](https://github.com/Digiex/Silverdash360/blob/master/tests/README.md)).
- Commit messages say what the change does, in a short first line, such as "Keep a theme pick for each profile".
- It needs to build with `tools\Build.ps1 -Release` without new warnings.

## What can't go in

- **The runtime and the donor app's files**: `default.xex`, `xcoreclr.dll`, the framework assemblies and anything else from the Plex app or Microsoft. They aren't Digiex's to hand out, so they never go in the repository or a release on GitHub.
- **Code or files from the Xbox 360 SDK** or from other people's projects whose licenses don't allow it.
- **Anything taken from a console's firmware**, such as its dashboard's files: Silverdash360 reads them on the console instead.

## License

Silverdash360 is licensed under the [PolyForm Noncommercial License 1.0.0](https://github.com/Digiex/Silverdash360/blob/master/LICENSE.md) with an additional condition. By opening a pull request, you agree that your contribution is licensed under the same terms.

## Questions

Ask on the [Silverdash360 thread on Digiex](https://digiex.net/threads/silverdash360-homebrew-xbox-360-dashboard.20103/), or in the issue you're working on.
