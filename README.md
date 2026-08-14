# DigitalDeskCycle

Tracks cadence, distance and training sessions of a desk bike. A Raspberry Pi
Pico 2 WH reads the reed switch the bike already has and reports the revolutions
to a Windows application.

> The user interface is in German — this is a personal tool for a specific bike.
> Everything else, including the reasoning in the code, is in English.

| Part | What it does |
|------|--------------|
| [`firmware/`](firmware/) | MicroPython on the Pico: counts pulses, debounces them, reports over USB **and** BLE |
| [`src/DeskCycle.Core/`](src/DeskCycle.Core/) | Capture, session detection, storage. Platform-neutral, no UI |
| [`src/DeskCycle.Desktop/`](src/DeskCycle.Desktop/) | WPF application: live view, history, tray icon |
| [`tools/`](tools/) | `Read-Cadence.ps1` — taps the COM port for troubleshooting |

## Requirements

- .NET 10 SDK
- Windows 10 version 2004 or newer
- For the firmware, see [`firmware/README.md`](firmware/README.md): wiring,
  flashing and the verification steps

## Running it

```bash
dotnet build DigitalDeskCycle2.slnx
dotnet run --project src/DeskCycle.Desktop
```

The application lives in the tray. **Closing the window does not quit it** —
recording is meant to continue throughout the working day. Quit through the
tray icon's context menu.

A session starts automatically with the first revolution and ends after 90
seconds of standstill. Sessions below 10 revolutions are discarded, so that a
pedal nudged in passing does not become a training session.

## The two views

**Live** (*Live*) summarises the period **since the last reset** — think trip
meter. Two ways to set it:

- The button beside the tab strip resets **to now**, for a deliberate fresh start.
- When the first revolution of a new day arrives while the statistics still run
  from yesterday, a bar appears and asks. Its button resets **to today at
  00:00**, so half an hour already ridden is not lost.

A reset deletes nothing. It only moves a timestamp in `settings.json`, which
makes it harmless to undo.

The time axis of the history chart is **active** time: interruptions of 30
seconds or more are taken out and marked with a red tick. The curve therefore
answers "how did I ride", not "when". On a desk bike used in bursts throughout
the day, a real time axis would consist mostly of idle.

**Verlauf** (*History*) shows individual sessions with date and wall-clock
duration. That this duration exceeds the active time in the live view is not a
contradiction — they answer two different questions.

## Where the data comes from

There are two sources. The application takes the first one that works:

1. **USB** (`SerialCadenceSource`) — reads the line-based output from the COM port
2. **Bluetooth** (`BluetoothCadenceSource`) — reads the standard
   *Cycling Speed and Cadence* profile

A running connection is never taken away **mid-session**: a change of source
resets the reference count — the two count different counters — and the
revolutions in between would be lost.

Between sessions it does switch, but only **towards** USB: while Bluetooth is
counting and nothing is being ridden, the application checks every five seconds
whether the serial port is back and then hands over. Without that, plugging the
cable back in would leave the radio link in charge until it drops of its own
accord. The other direction stays closed — whether the serial port exists costs
a look at a list, whether the bike is within radio reach would cost a scan next
to the running connection.

Which source is counting is shown by the two icons beside the tab strip, one for
USB and one for Bluetooth. The one delivering the samples is green and framed;
the other stays grey, because whether it *could* be used is unknown while
another connection is running. If nothing is connected, both turn red. The
tooltip names the source, e.g. `USB verbunden · COM3`.

### Why USB comes first

The CSC profile carries cumulative crank revolutions and a timestamp, nothing
else. The firmware's diagnostic counters — `bounce` and `suspect` — do not
exist there.

`suspect` counts pulses arriving implausibly close together and is therefore the
early warning for a bouncing or loose reed switch. When it rises, the sensor is
double-counting revolutions and **every** derived number is too high. Over USB
the interface warns about it; over Bluetooth it cannot.

That is why the counters are `null` rather than `0` when a source does not
provide them: a zero would look like a clean measurement that nobody took.

**Practical consequence:** as long as the Pico draws power from the computer,
the COM port exists too and USB always wins. Bluetooth only comes into play once
the Pico hangs off a charger or a power bank.

## Where the data lives

Everything under `%LOCALAPPDATA%\DeskCycle` — deliberately not next to the
executable, so that a rebuild or a moved program folder does not take the
history with it.

| File | Contents |
|------|----------|
| `deskcycle.db` | SQLite database with sessions and samples |
| `settings.json` | settings toggled at runtime |
| `logs\deskcycle-YYYY-MM-DD.log` | one file per day, older than 14 days are deleted |

## Configuration

### `src/DeskCycle.Desktop/appsettings.json`

Belongs to the program, read at startup.

| Key | Default | Purpose |
|-----|---------|---------|
| `Tracking:MetersPerRevolution` | `6.67` | distance per crank revolution — an **estimate**, see below |
| `Tracking:SerialPort` | empty | COM port; empty = automatic, as long as there is exactly one |
| `Tracking:BaudRate` | `115200` | must match the firmware |
| `Tracking:BluetoothDeviceName` | `DeskCycle` | must match `BLE_NAME` in the firmware |
| `Tracking:SessionIdleTimeoutSeconds` | `90` | no revolution for longer than this ends the session |
| `Tracking:MinimumSessionRevolutions` | `10` | shorter sessions are discarded |
| `Tracking:PauseThresholdSeconds` | `30` | a gap this long counts as a pause rather than a hiccup |
| `Tracking:SpeedGaugeMaxKmh` | `40` | upper end of the speed bar |

### `%LOCALAPPDATA%\DeskCycle\settings.json`

Belongs to the user, survives an update.

| Key | Default | Purpose |
|-----|---------|---------|
| `ApiEnabled` | `false` | web server for external consumers |
| `ApiPort` | `5056` | port of the web server |
| `ApiAllowRemote` | `false` | `false` = localhost only; `true` binds to the network and triggers the firewall prompt |
| `ResetAt` | today 00:00 | start of the period the live view summarises |

## Sharing data

Off by default. Switch it on with *Daten freigeben* (share data) in the tray
menu — while it is off, no socket and no listener exist.

| Endpoint | Returns |
|----------|---------|
| `GET /api/live` | current state |
| `GET /api/sessions?from=&to=&take=` | list of sessions |
| `GET /api/sessions/{id}` | one session including its samples |
| `GET /api/stats/daily?days=` | daily aggregates |
| SignalR `/hubs/cadence` | live push, message `Live` carrying a `LiveStatus` |

## Data model

What gets stored is **revolutions and timestamps**, never kilometres. Distance,
speed and later watts are derived values.

The reason: the conversion factor is still an estimate. Correct it and every
past ride is correct too. Had kilometres been stored, they would stay frozen at
the estimate forever.

Samples are taken once per second, but **only while moving**. Seconds spent
standing still made up two thirds of the volume and added nothing that the
session itself does not already record; pauses fall out of the gaps between
timestamps when reading. Measured, a sample costs about 44 bytes, which is
roughly 39 MiB per year at an hour of movement per day.

The **resistance level** is invisible in the signal — the adjustment on the bike
is purely mechanical. It can be filled in per session (double-click a row in the
history) and is the basis for a future power estimate.

## Changing the database schema

Migrations live in `src/DeskCycle.Core/Data/Migrations`. The EF tool is pinned
as a local tool in the repository (`.config/dotnet-tools.json`) rather than
installed globally:

```bash
dotnet tool restore
```

```bash
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
Get-Content "$env:LOCALAPPDATA\DeskCycle\logs\deskcycle-2026-08-13.log" -Wait
```

**If the counting looks wrong**, ask the sensor directly. Quit the application
first — it holds the port — then run

```bash
.\tools\Read-Cadence.ps1
```

That shows `bounce` and `suspect` raw. What the values mean, and how the
debounce time follows from them, is in
[`firmware/README.md`](firmware/README.md).

**Only one program can hold the COM port.** Thonny, the capture script and the
application are mutually exclusive.

## Open point

`Tracking:MetersPerRevolution` is set to `6.67`. The value comes from a rough
reading off the bike's original display (0.3 km over 45 revolutions) and is
accurate to roughly ±20 %.

To measure it properly:

1. Set a metronome to 60 bpm and pedal in time — with the Pico connected, check
   that the display really shows about 60 rpm
2. Plug the jack back into the original display unit, reset its trip counter
3. Pedal in time for **exactly 10 minutes** — that is **600 revolutions**
4. Read the distance, then put `km_read × 1000 ÷ 600` into `appsettings.json`

Over 600 revolutions the 0.1 km reading error collapses to about ±1.5 %, instead
of ±20 % over 45 revolutions.

## License

[MIT](LICENSE) — use it, change it, do what you like with it.

The firmware is measured against one particular reed switch on one particular
desk bike. The 200 ms debounce time and the conversion factor hold for that
setup; with different hardware the measurements change, while the reasoning
behind them still applies.
