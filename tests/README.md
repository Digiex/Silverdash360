# Testing on a console

The dashboard has a debug server: a TCP port that takes commands while it runs, for testing and debugging on a console that has no development kit. The tests here drive it from a PC.

The server is built in by default for now. `.\tools\Build.ps1 -NoDebugServer` leaves it out (it sets the `DebugServer` build property, which defines `DEBUG_SERVER` and compiles `src\Silverdash360\Debugging\`).

**It takes no password.** Anyone on the network can read what the dashboard knows, read files on its drives and drive its UI. Use it on a network you trust, and leave it out of builds for others.

## Deploying a build

`tools/deploy.py` updates the dashboard on the console and starts the new build, so a change can go from the PC to the console and through the tests with one command:

```bash
.\tools\Build.ps1
python tools/deploy.py 192.168.1.20
python tools/deploy.py 192.168.1.20 -- --suite functional
```

With the dashboard running, it:

1. asks the dashboard where it runs from (`app.location`: the folder its D: drive links to). That's the target, unless it's on no drive the dashboard knows or one that can't be written, such as the disc; then the target is `/Hdd1/Apps/Silverdash360`. `--target` picks another.
2. starts the dashboard's FTP server (`ftp.start`) and uploads the files that changed since the last deploy. A `.deploy-manifest.json` in the folder keeps each file's hash, so after the first time it's usually only `Silverdash360.dll`.
3. starts the new build (`app.launch`), waits for its debug server and checks it's the build uploaded (`.deploy-id`).
4. runs `tests/run_tests.py` with what comes after `--`, if anything does.

A running dashboard's files aren't overwritten. When the target is where it runs from, the build goes to `<target>/_update` instead, with a `deploy-install.txt` naming the target and the changed files. The dashboard started from there copies those files into the target and starts it. If that fails, it starts the dashboard from `_update`, and deploy.py prints the error and the log. The first time, `_update` gets the whole package (about 107 MB); after that, only what changed. `--mode direct` uploads into the target anyway, and `--mode staging` stages even when the target isn't the running folder.

When the dashboard isn't running, give another FTP server with `--ftp host:port --user ... --password ...` (Aurora's, for one); the files go straight into the target, and you start it yourself. `--no-launch` uploads without starting anything, and `--full` uploads every file.

Staging needs a build with the debug server, since that build does the install.

In xenia, run with `--systemlink_transport=off` and `--ftp-port 2121`, since xenia moves ports below 1024, and with a `--target` under `/Hdd1/Content`, the only folder of its hard drive xenia has. xenia mounts a launched title's folder read-only and as `\Device\Package_0`, so `app.location` can't name it. Relaunching needs xenia-edge's `pr-socket-accept-relaunch` fix: before it, a title that relaunched while a thread waited in `accept` crashed the emulator at its next socket.

## Running the tests

You need Python 3, with nothing else installed. Start the dashboard, find the console's address under Settings > Debug server, and then:

```bash
python tests/run_tests.py 192.168.1.20
```

| Option | |
|---|---|
| `--suite api,functional` | Which suites to run: `api`, `functional`, `performance` (all by default). |
| `-k name`, `-x name` | Only the tests whose name contains it, or all but them. |
| `--quick` | Fewer iterations: for xenia, and for a quick check. |
| `--json results.json` | Writes the results, notes and metrics. |
| `--save-baseline perf.json` | Writes the performance metrics, to compare later runs with. |
| `--baseline perf.json` | Fails when a metric got worse than that file's by more than `--tolerance` (25% by default). |

The exit code is 0 when everything passed and nothing regressed.

The suites:

- **api**: the protocol, and the console's functions the dashboard uses: system version and memory, sign-in, content enumeration, package headers, tiles, played titles and achievements, the network address, and file system listings and reads.
- **functional**: the library loads and its lists agree, titles are found by key and ID, search finds what it should, a refresh keeps the library, the cache holds what the library has, the hub's sections and bumpers, the D-pad moving a selection, A opening a title and B coming back, the other pages, several clients, the log.
- **performance**: the benchmarks (interpreted code next to the framework's, and the dashboard's own work on the library on the console), searching, reading the cache, enumerating and reading headers, a full refresh, memory, the frame rate when idle and while scrolling the games and apps, and opening a title's page.

Performance numbers depend on the library, so compare runs on the same console with the same content. The console's clock counts whole milliseconds; each metric has a noise floor below which a difference doesn't count as a regression.

### In xenia

Run xenia-edge with `--systemlink_transport=off`, or its System Link emulation takes the socket and nothing listens on the PC. The port is above 1024, so xenia doesn't move it:

```powershell
.\tools\Run.ps1 -Seconds 30 -ExtraArgs '--systemlink_transport=off'
```

Then, while it runs, point the tests at `127.0.0.1` with `--quick`.

## Commands by hand

`tests/debugclient.py` is an interactive shell:

```bash
python tests/debugclient.py 192.168.1.20
```

```
> library
> library.search halo
> ui.press rb
> bench record.parse 500
> memory collect
> log 20
```

`help` lists every command with its arguments. The main ones:

| Command | |
|---|---|
| `info`, `memory [collect]`, `log [count]` | The console, memory (managed and the kernel's counts), the latest log lines. |
| `library`, `library.titles`, `library.title`, `library.content`, `library.search` | What the library holds. |
| `library.refresh [full]`, `library.wait`, `cache.load` | Refreshing, and reading the cache. |
| `xam.*`, `net.address`, `fs.*` | The console's functions, called directly. Only reads. |
| `ui.state`, `ui.section`, `ui.navigate`, `ui.back`, `ui.press` | What's on screen, and driving it. |
| `ui.fps`, `ui.scroll` | Frame rate when idle, and while moving a grid's selection a step per frame. |
| `bench.list`, `bench <name> [iterations]` | Timed runs of the dashboard's work. |
| `app.location`, `app.launch [path]`, `ftp.start [port]`, `ftp.stop` | Where the dashboard runs from, starting a build, and the FTP server, for deploy.py. |

A controller's button events can't be made from code, so `ui.press` does what the button does instead: it calls the focused grid's `TileGrid.Press`, then the hub's `HubPage.Press`, then the frame's back for B. Pages with their own button handling, such as the search page's keyboard, aren't reached that way.

## The protocol

A plain TCP connection to port 4601. The server sends a greeting, then answers each line with one line of JSON:

```
{"ok":true,"command":"hello","result":{"server":"Silverdash360 debug server","protocol":1,"commands":36}}
library.search "two words"
{"ok":true,"command":"library.search","ms":4,"result":{"query":"two words",...}}
no.such.command
{"ok":false,"command":"no.such.command","ms":0,"error":"Unknown command no.such.command. \"help\" lists them."}
```

A line is a command and its arguments, separated by spaces; an argument with spaces goes in double quotes. `ms` is how long the command took on the console. `quit` closes the connection. Several clients can connect at once.

## Adding to it

- **A command**: add it to one of the `IDebugCommands` classes in `src\Silverdash360\Debugging\`, or make a class of your own and list it in `DebugServer.StartDefault`. A command gets a `DebugRequest` and returns its result: a `JsonObject`, a list, a string or a number. Throw a `DebugException` for a failure the client should see. Work that touches the UI goes through `UiThread.Invoke`.
- **A benchmark**: `Add` it in `BenchCommands`' constructor, with a function that prepares what it needs and returns one iteration.
- **A test**: a function in one of `tests/suites/*.py` marked with `@test('suite')`. It gets a context: `t.call(command, args...)`, `t.check(condition, message)`, `t.equal(actual, expected, what)`, `t.skip(reason)`, `t.note(text)`, `t.metric(name, value)`, and `t.library()`, which waits until a library is shown (`settled=True` also waits for a refresh to end). See `tests/harness.py`.

Keep in mind that the dashboard's code is interpreted on the console (see the Silverlight360 performance guide): a command that loops over thousands of items takes far longer there than in xenia.
