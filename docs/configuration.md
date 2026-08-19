# Configuration

[← back to the README](../README.md)

Most of this is editable on the **Einstellungen** (settings) page. The tables
below are for looking things up, and for the handful of values that have no
field yet.

## Where the data lives

Everything under `%LOCALAPPDATA%\DeskCycle` — deliberately not next to the
executable, so that a rebuild or a moved program folder does not take the
history with it.

| File | Contents |
|------|----------|
| `deskcycle.db` | SQLite database with sessions and samples |
| `settings.json` | settings toggled at runtime |
| `logs\deskcycle-YYYY-MM-DD.log` | one file per day, older than 14 days are deleted |

## `settings.json` — belongs to you

Survives an update. Everything here is editable on the settings page; the four
values at the bottom override the program's defaults and stay absent until they
are changed, so that a new default from an update still reaches whoever never
had an opinion on it.

| Key | Default | Purpose |
|-----|---------|---------|
| `ApiEnabled` | `false` | web server for external consumers |
| `ApiPort` | `5056` | port of the web server |
| `ApiAllowRemote` | `false` | `false` = localhost only; `true` binds to the network and triggers the firewall prompt |
| `ResetAt` | today 00:00 | start of the period the live view summarises |
| `BodyWeightKg` | `0` | basis for the calorie estimate; `0` = not stated, then no figure is shown |
| `MetersPerRevolution` | absent | overrides the measured factor |
| `SerialPort` | absent | overrides the COM port |
| `BluetoothDeviceName` | absent | overrides the device name |
| `SpeedGaugeMaxKmh` | absent | overrides the end of the speed bar |

## `appsettings.json` — belongs to the program

Sits next to the executable and is read at startup. These are defaults; an
update overwrites the file, which is why nothing the settings page changes is
kept here.

| Key | Default | Purpose |
|-----|---------|---------|
| `Tracking:MetersPerRevolution` | `6.18` | distance per crank revolution — [measured](accuracy.md) |
| `Tracking:SerialPort` | empty | COM port; empty = automatic, as long as there is exactly one |
| `Tracking:BaudRate` | `115200` | must match the firmware |
| `Tracking:BluetoothDeviceName` | `DeskCycle` | must match `BLE_NAME` in the firmware |
| `Tracking:SessionIdleTimeoutSeconds` | `90` | no revolution for longer than this ends the session |
| `Tracking:MinimumSessionRevolutions` | `10` | shorter sessions are discarded |
| `Tracking:PauseThresholdSeconds` | `30` | a gap this long counts as a pause rather than a hiccup |
| `Tracking:SpeedGaugeMaxKmh` | `40` | upper end of the speed bar |

The last four have no field on the settings page. They are the ones you
understand once and then never touch again.

## Sharing data

Off by default. The switch is on the settings page, together with the port and
the status — while it is off, no socket and no listener exist.

| Endpoint | Returns |
|----------|---------|
| `GET /api/live` | current state |
| `GET /api/sessions?from=&to=&take=` | list of sessions |
| `GET /api/sessions/{id}` | one session including its samples |
| `GET /api/stats/daily?days=` | daily aggregates |
| SignalR `/hubs/cadence` | live push, message `Live` carrying a `LiveStatus` |

Read-only throughout: nothing out there can change anything in here.
