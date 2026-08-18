# Changelog

Notable changes per release. Dates are in ISO format.

## Unreleased

### Added

- **Einstellungen** (settings) as a third view: web server with status, switch
  and port; body weight; conversion factor; end of the speed bar; COM port and
  Bluetooth device name; autostart. What used to require an editor now has a
  place in the application
- Changes take effect immediately where possible — a corrected weight or
  conversion factor recalculates the live view at once. Port and device name
  are picked up on the next reconnect, and the page says so rather than
  pretending otherwise

### Changed

- The tray menu is down to *Fenster anzeigen*, *Protokoll öffnen* and
  *Beenden*. Autostart and data sharing moved to the settings page, where their
  state is visible — two places for one switch would only be two places to keep
  in step
- Values changed on the page land in `settings.json` and take precedence over
  `appsettings.json`. Untouched values stay absent, so a new default from an
  update still reaches whoever never had an opinion on it

## 1.0.0 — 2026-08-14

First release. Records the cadence of a desk bike and turns it into sessions,
distance and a history.

### Capture

- Reads the reed switch through a Raspberry Pi Pico 2 WH; the MicroPython
  firmware debounces the pulses and reports them over USB **and** BLE
- Two sources, USB preferred: only there do the firmware's diagnostic counters
  come along, which the Bluetooth profile cannot carry
- Sessions start with the first revolution and end after 90 seconds of
  standstill; sessions below 10 revolutions are discarded
- Revolutions are what gets stored, never kilometres — correcting the conversion
  factor applies retroactively to the whole history

### Application

- Live view: current speed with a zoned bar, cadence, distance, active time and
  the curve over active time, pauses marked rather than drawn
- History: sessions with date, duration and distance, plus distance per day
- The period since the last reset is a trip meter; at a change of day the
  application asks rather than deciding on its own
- Estimates calories from body weight and cadence, shown with a `≈` and only
  once `BodyWeightKg` is set in `settings.json`
- Warns when the sensor reports implausible pulses — a bouncing reed switch
  makes every derived number too high
- Lives in the tray, runs on beyond a closed window, optionally starts with
  Windows
- Optional read-only web interface: `GET /api/live`, `/api/sessions`,
  `/api/stats/daily` and live push over SignalR

### Known limitations

- `Tracking:MetersPerRevolution` is measured for this one bike and good to
  roughly ±1 %. Another bike, or a different mounting of the magnet, needs its
  own measurement — the procedure is in the README
- The calorie figure is an estimate from cadence and weight and can be off by
  up to half: the resistance of the bike is set mechanically and looks the same
  in the signal at every setting
- Windows only, and only for one bike at a time
- The downloads are not code-signed: Windows shows a SmartScreen warning on
  first start
