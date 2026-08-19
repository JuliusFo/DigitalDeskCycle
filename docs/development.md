# Development

[← back to the README](../README.md)

## Layout

| Part | What it does |
|------|--------------|
| [`firmware/`](../firmware/) | MicroPython on the Pico: counts pulses, debounces them, reports over USB **and** BLE |
| [`src/DeskCycle.Core/`](../src/DeskCycle.Core/) | Capture, session detection, storage. Platform-neutral, no UI |
| [`src/DeskCycle.Desktop/`](../src/DeskCycle.Desktop/) | WPF application: live view, history, settings, tray icon |
| [`tests/DeskCycle.Core.Tests/`](../tests/DeskCycle.Core.Tests/) | xUnit tests for the arithmetic |
| [`tools/`](../tools/) | `Read-Cadence.ps1` — taps the COM port for troubleshooting |

Needs the .NET 10 SDK. Windows 10 version 2004 or newer, because of the WPF
dependencies.

```bash
dotnet build DigitalDeskCycle2.slnx
dotnet run --project src/DeskCycle.Desktop
```

## Tests

```bash
dotnet test DigitalDeskCycle2.slnx
```

The tests cover the arithmetic that decides what ends up in the database: the
difference between two counter readings including the Pico's restart, where a
session begins and ends, which ones get discarded, the pause threshold on the
time axis, and the line format from the firmware. Those are the places where a
mistake stays invisible the longest — the numbers still look plausible, they
are simply wrong.

The calorie estimate is covered differently: an estimate cannot be pinned to a
correct value, so the tests hold it to behaving sensibly — more cadence and
more time cost more, a pause earns nothing, twice the weight costs twice as
much, and one figure is nailed down as a check on the order of magnitude.

The user interface is not covered; nor is anything that needs real hardware.

## Building a release

The [release workflow](../.github/workflows/release.yml) builds both packages
on a tag and attaches them to the GitHub release:

```bash
git tag v1.1.0 && git push origin v1.1.0
```

Running it by hand from the Actions tab builds the same packages and keeps them
as workflow artifacts, without touching a release — useful for a dry run.

The release text comes from the matching section of
[`CHANGELOG.md`](../CHANGELOG.md), so the version heading there has to match the
tag. GitHub's own generated notes are no use here: they are built from pull
requests, and everything goes straight onto `main`. A release that already
exists keeps whatever text it has — the workflow only attaches the packages
then.

<details>
<summary>Building the packages locally</summary>

```bash
dotnet publish src/DeskCycle.Desktop -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none -o publish/self-contained
```

</details>

## Changing the database schema

Migrations live in `src/DeskCycle.Core/Data/Migrations`. The EF tool is pinned
as a local tool in the repository rather than installed globally:

```bash
dotnet tool restore
dotnet ef migrations add YourChangeName --project src/DeskCycle.Core --output-dir Data/Migrations
```

There is nothing to apply by hand — the application runs pending migrations at
startup. Databases created before migrations existed are detected and stamped
instead of being recreated.

## Troubleshooting

**Start with the log.** The application has no console; without the log file
every message disappears without trace. *Protokoll öffnen* (open log) in the
tray menu opens the folder. To follow along live:

```bash
Get-Content "$env:LOCALAPPDATA\DeskCycle\logs\deskcycle-2026-08-18.log" -Wait
```

**If the counting looks wrong**, ask the sensor directly. Quit the application
first — it holds the port — then run

```bash
.\tools\Read-Cadence.ps1
```

That shows `bounce` and `suspect` raw. What the values mean, and how the
debounce time follows from them, is in
[`firmware/README.md`](../firmware/README.md).

**Only one program can hold the COM port.** Thonny, the capture script and the
application are mutually exclusive.
