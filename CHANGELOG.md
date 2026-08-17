# Changelog

Notable changes per release. Dates are in ISO format.

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

- `Tracking:MetersPerRevolution` is an estimate, accurate to roughly ±20 %.
  Every distance and speed carries that error until it is measured; the
  procedure is in `firmware/README.md`
- The calorie figure is an estimate from cadence and weight and can be off by
  up to half: the resistance of the bike is set mechanically and looks the same
  in the signal at every setting
- Windows only, and only for one bike at a time
- The downloads are not code-signed: Windows shows a SmartScreen warning on
  first start
