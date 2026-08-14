# Cadence firmware

The Pico 2 WH (RP2350) counts the pulses of the desk bike's reed switch and
reports them line by line over USB serial, and in parallel over BLE.

## Wiring

| Jack | Pico | Note |
|------|------|------|
| Ring | GPIO 15 (pin 20) | INPUT_PULLUP, active low |
| Sleeve | GND (pin 18 or any) | |
| Tip | unused | unconnected, as measured |

GPIO 15 sits on **physical pin 20** of the header, GND next to it on pin 18.
GPIO numbers and pin numbers are not the same thing — when in doubt, consult the
Raspberry Pi pinout diagram.

The reed switch is a passive contact with no polarity, so it does not matter
which of the two wires goes to GPIO 15 and which to GND.

On the jack-to-screw-terminal breakout used here the live terminals are **1 and
3**; terminal 2 is the unused tip. The terminal numbering therefore does not
follow tip/ring/sleeve order — rather than measuring, try the combinations and
let the Pico tell you (see below); it is more reliable than a multimeter probe
held against a screw head while turning the crank.

The RP2350 has erratum E9, which affects the internal **pull-downs**. This
firmware uses a pull-**up** and is not affected.

## Flashing

1. Download MicroPython for the board `RPI_PICO2_W` from
   <https://micropython.org/download/> (a `.uf2` file).
2. Hold the BOOTSEL button, plug in USB, release the button. A drive named
   `RP2350` appears.
3. Copy the `.uf2` onto that drive. The Pico reboots and shows up as a COM port.
4. Install [Thonny](https://thonny.org/) and pick *MicroPython (Raspberry Pi
   Pico)* as the interpreter in the bottom right.
5. Put two files on the Pico, each via **File > Save as > Raspberry Pi Pico**:

   | local | on the Pico | |
   |-------|-------------|---|
   | `cadence.py` | **`main.py`** | runs automatically on every power-up |
   | `ble_csc.py` | `ble_csc.py` | keep the name — it is imported |

   Without `ble_csc.py` the firmware still runs, just without BLE. The header
   line then reads `# ble off (ImportError: ...)`.

To stop it: Ctrl+C in Thonny, or the stop button.

## Output format

```
# cadence-fw 1.1 pin=15 debounce_ms=200 report_ms=1000 suspect_gap_ms=250
# ble on name=DeskCycle mac=28:CD:C1:...
PULSE,<uptime_ms>,<count>,<gap_ms>
CAD,<uptime_ms>,<count>,<rpm>,<bounce>,<suspect>
BLE,<uptime_ms>,<connected>
```

Lines starting with `#` are comments and are dropped by the parser. The decimal
separator is always a point — parse with `CultureInfo.InvariantCulture` in C#.

| Field | Meaning |
|-------|---------|
| `uptime_ms` | milliseconds since firmware start |
| `count` | accepted pulses in total |
| `gap_ms` | distance to the previous pulse (0 for the first one) |
| `rpm` | cadence, one decimal place |
| `bounce` | edges rejected inside the debounce window |
| `suspect` | accepted pulses less than 250 ms apart |
| `connected` | 1 = a BLE receiver is connected, 0 = disconnected (on change only) |

`bounce` and `suspect` are the two diagnostic counters. `bounce` is allowed to
climb — that is debouncing doing its job. `suspect` should stay at 0.

## Verification

### a) Bench test without the bike

Tap a jumper wire from GPIO 15 against GND. Each tap must produce exactly one
`PULSE` line and increment `count` by 1. The onboard LED blinks along.
`bounce` typically rises too — a wire held by hand bounces just like a reed
switch.

### b) Counting test on the bike

The real test. Pedal slowly and evenly while counting out loud.

After **50 revolutions**, `count` must have risen by exactly 50.

Watch two things at once:

- **`gap_ms` in the PULSE lines.** With even pedalling the values must cluster
  (e.g. 780, 795, 810). A single much smaller value in between — say 800, 800,
  **90**, 710 — is a double count.
- **`suspect` in the CAD lines.** If it stays at 0, not a single implausibly
  short gap occurred.

`count` = 50 and `suspect` = 0 means one revolution is counted once.

### Reading along without Thonny

`..\tools\Read-Cadence.ps1` taps the port using `System.IO.Ports` — the same
class the application uses. The test therefore doubles as a check of the .NET
side.

```powershell
.\tools\Read-Cadence.ps1
.\tools\Read-Cadence.ps1 -Port COM5 -LogFile .\capture.csv
```

Only **one** program can hold the COM port. Close Thonny, or disconnect it,
first.

## Debouncing: why 200 ms and not 20 ms

A reed switch bounces when it closes **and** when it opens. 20 ms only covers
the closing bounce. Opening happens once the magnet leaves the field, much
later, and therefore outside a short window. The falling edges it produces would
otherwise be counted as additional revolutions.

Measured on this setup (capture at ~55 rpm): the contact stays closed for about
**90 ms** at a revolution period of 1140 ms — roughly 8 % of the revolution.
With `DEBOUNCE_MS = 20` that produced 2 miscounts over 49 revolutions (~4 %),
visible as `gap_ms` values of 90 and 92 ms and a `suspect` counter rising by 2.

The closed time scales with cadence:

| Cadence | Revolution | Contact closed |
|---------|------------|----------------|
| 120 rpm | 500 ms | ~40 ms |
| 55 rpm | 1090 ms | ~90 ms (measured) |
| 30 rpm | 2000 ms | ~160 ms |

So the blocking time has to sit above the longest closed time and below the
shortest genuine gap between pulses:

```
max. closed time  <  DEBOUNCE_MS  <  60000 / max_rpm
```

`DEBOUNCE_MS = 200` covers cadences down to about 24 rpm and lets everything up
to 300 rpm through. On a different bike or with different magnet geometry the
formula still holds — only the measured closed time changes, and that is
readable straight off the `gap_ms` of the bad pulses in a capture.

## BLE: Cycling Speed and Cadence

In parallel with the serial output the Pico advertises as a BLE sensor using the
standard **Cycling Speed and Cadence** profile (service `0x1816`). Being a
standard, any fitness app recognises it without configuration.

What is transmitted is the *cumulative* number of crank revolutions plus the
time of the last revolution. The receiver derives cadence itself by comparing
two consecutive notifications. The advantage of the cumulative format: a lost
notification leaves no hole, the next one carries the correct total.

| | |
|---|---|
| Device name | `DeskCycle` (`BLE_NAME`) |
| Service | `0x1816` Cycling Speed and Cadence |
| Characteristic | `0x2A5B` CSC Measurement (notify) |
| Sent on | every revolution **and** every `REPORT_MS` tick |

Sending periodically while standing still is deliberate: unchanged values are
how a receiver learns that cadence is zero.

**Note:** BLE replaces the USB cable for data, not for power. The Pico still
needs a USB supply — but that may be any charger or power bank.

### Verifying without .NET code

With a BLE scanner app on a phone (nRF Connect, for example) or straight from a
fitness app:

1. The firmware is running and the header reads `# ble on name=DeskCycle mac=...`
2. Search for `DeskCycle` in the app and connect
3. `BLE,<ms>,1` appears on the serial side
4. Pedal — the revolution counter in the app must rise in step with `count`

Both channels run independently. As long as USB is plugged in, every BLE
notification can be checked directly against `PULSE` and `CAD` — the serial
output stays the diagnostic tool even after switching to BLE.

### 16-bit rollovers

The profile allows only 16 bits for either value:

- the revolution counter rolls over after 65535 revolutions
- the timestamp (1/1024 s) rolls over every 64 seconds

Both are expected by the standard; receivers compute modulo. Anyone consuming
the counter has to treat the rollover, and a restart of the Pico (counter drops
to 0), as a step back rather than a negative difference.

## Configuration

All parameters are constants at the top of `cadence.py`:

| Constant | Default | Purpose |
|----------|---------|---------|
| `PULSE_PIN` | 15 | GPIO number |
| `DEBOUNCE_MS` | 200 | blocking time after an accepted pulse (derived above) |
| `REPORT_MS` | 1000 | interval of the CAD lines |
| `IDLE_TIMEOUT_MS` | 4000 | after this, rpm reads 0.0 |
| `SUSPECT_GAP_MS` | 250 | threshold for the plausibility counter |
| `PULSE_LINES` | True | send PULSE lines; optionally False after verification |
| `LED_BLINK_MS` | 60 | onboard LED flash per pulse, 0 = LED off |
| `BLE_ENABLED` | True | start the BLE sensor; False = USB only |
| `BLE_NAME` | `DeskCycle` | device name; at most 9 characters because of the packet size |

The conversion factor from revolutions to distance is deliberately **not** in
the firmware. The firmware delivers raw values; the conversion belongs on the
.NET side as configuration. The same goes for the manually recorded resistance
level used for a future power estimate.
