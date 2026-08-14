"""
Desk bike cadence sensor -- Raspberry Pi Pico 2 WH (RP2350), MicroPython

Hardware:
  Reed switch (passive, 1 pulse per crank revolution) on a 3.5 mm jack cable.
  Ring   -> GPIO 15  (INPUT_PULLUP, active low)
  Sleeve -> GND
  Tip    -> unused

Output over USB serial: line based, ASCII, LF line endings.
The decimal separator is always a point (C#: CultureInfo.InvariantCulture).

  # ...                                           comment/header, to be ignored by the parser
  PULSE,<uptime_ms>,<count>,<gap_ms>              per detected pulse (diagnostic, see PULSE_LINES)
  CAD,<uptime_ms>,<count>,<rpm>,<bounce>,<suspect>   periodic, see REPORT_MS
  BLE,<uptime_ms>,<connected>                     on change only; 1 = connected, 0 = disconnected

In parallel the Pico reports the same revolutions over BLE as a Cycling Speed
and Cadence sensor (see ble_csc.py). Both channels run at the same time and
independently of each other; if BLE fails, the serial side keeps counting and
reporting unchanged.

Fields:
  uptime_ms  milliseconds since firmware start (wraps at 2^30, see ticks_ms)
  count      total accepted pulses since start
  gap_ms     distance to the previous accepted pulse (0 for the very first)
  rpm        current cadence, one decimal place
  bounce     edges rejected inside the debounce window (debouncing at work)
  suspect    accepted pulses with an implausibly small gap (< SUSPECT_GAP_MS)
             -> suspected double count, see README
"""

import time
from machine import Pin

# ---------------------------------------------------------------- configuration

PULSE_PIN = 15          # GPIO number (not the header pin number!)
DEBOUNCE_MS = 200       # blocking time after an accepted pulse; see README "Debouncing"
REPORT_MS = 1000        # interval of the CAD lines
IDLE_TIMEOUT_MS = 4000  # no pulse for longer than this -> rpm 0.0
SUSPECT_GAP_MS = 250    # a gap below this is implausible (= 240 rpm)
PULSE_LINES = True      # also send PULSE lines (True while verifying)
LED_BLINK_MS = 60       # onboard LED flash per pulse, 0 = LED off

BLE_ENABLED = True      # start the BLE sensor; False = USB only
BLE_NAME = "DeskCycle"  # name in the device list; at most 9 chars (packet size)

FW_VERSION = "1.1"

# ---------------------------------------------------------------- state
# Everything the IRQ handler writes. The main loop only reads.

_count = 0
_bounce = 0
_suspect = 0
_last_pulse_ms = 0
_last_gap_ms = 0
_have_pulse = False


def _on_falling_edge(pin):
    """Interrupt handler. Deliberately minimal: no allocation, no print, no floats.

    Non-retriggering debounce -- measured against the last *accepted* pulse, not
    against the last edge seen. That keeps the blocking window a deterministic
    DEBOUNCE_MS long, even while the contact bounces continuously.
    """
    global _count, _bounce, _suspect, _last_pulse_ms, _last_gap_ms, _have_pulse

    now = time.ticks_ms()
    gap = time.ticks_diff(now, _last_pulse_ms)

    if _have_pulse:
        if gap < DEBOUNCE_MS:
            _bounce += 1
            return
        if gap < SUSPECT_GAP_MS:
            _suspect += 1
        _last_gap_ms = gap
    else:
        _last_gap_ms = 0

    _last_pulse_ms = now
    _count += 1
    _have_pulse = True


def _current_rpm(now):
    """Cadence derived from the last gap between pulses.

    While no new pulse arrives the effective gap grows with time, so the reading
    decays smoothly instead of freezing at the last value.
    """
    if not _have_pulse or _last_gap_ms <= 0:
        return 0.0
    since = time.ticks_diff(now, _last_pulse_ms)
    if since > IDLE_TIMEOUT_MS:
        return 0.0
    return 60000.0 / max(_last_gap_ms, since)


def main():
    pin = Pin(PULSE_PIN, Pin.IN, Pin.PULL_UP)

    led = None
    if LED_BLINK_MS > 0:
        try:
            led = Pin("LED", Pin.OUT)   # on the Pico W / 2 W the LED sits on the radio chip
            led.off()
        except Exception:
            led = None                  # without an LED everything else runs unchanged

    print("# cadence-fw %s pin=%d debounce_ms=%d report_ms=%d suspect_gap_ms=%d"
          % (FW_VERSION, PULSE_PIN, DEBOUNCE_MS, REPORT_MS, SUSPECT_GAP_MS))
    print("# CAD,uptime_ms,count,rpm,bounce,suspect")
    if PULSE_LINES:
        print("# PULSE,uptime_ms,count,gap_ms")
    print("# BLE,uptime_ms,connected")

    # BLE is an addition, not a prerequisite: if it fails to start, the verified
    # counting over USB carries on unchanged.
    ble = None
    if BLE_ENABLED:
        try:
            from ble_csc import CscPeripheral
            ble = CscPeripheral(BLE_NAME)
            print("# ble on name=%s mac=%s" % (BLE_NAME, ble.mac()))
        except Exception as exc:
            print("# ble off (%s: %s)" % (type(exc).__name__, exc))
    else:
        print("# ble off (BLE_ENABLED=False)")

    pin.irq(trigger=Pin.IRQ_FALLING, handler=_on_falling_edge)

    reported_count = 0
    next_report_ms = time.ticks_add(time.ticks_ms(), REPORT_MS)
    led_off_ms = 0
    ble_was_connected = False

    while True:
        now = time.ticks_ms()

        # Emit pulse events. The IRQ only increments; printing and sending happen
        # here so that nothing allocates inside the handler.
        if _count != reported_count:
            if PULSE_LINES:
                print("PULSE,%d,%d,%d" % (now, _count, _last_gap_ms))
            reported_count = _count
            if led is not None:
                led.on()
                led_off_ms = time.ticks_add(now, LED_BLINK_MS)
            if ble is not None:
                ble.notify(_count, _last_pulse_ms)

        if led is not None and led_off_ms and time.ticks_diff(now, led_off_ms) >= 0:
            led.off()
            led_off_ms = 0

        if time.ticks_diff(now, next_report_ms) >= 0:
            print("CAD,%d,%d,%.1f,%d,%d"
                  % (now, _count, _current_rpm(now), _bounce, _suspect))
            next_report_ms = time.ticks_add(next_report_ms, REPORT_MS)
            # Send while standing still too: unchanged values are how a receiver
            # learns that cadence is zero, and the connection stays alive.
            if ble is not None:
                ble.notify(_count, _last_pulse_ms)

        if ble is not None and ble.connected != ble_was_connected:
            ble_was_connected = ble.connected
            print("BLE,%d,%d" % (now, 1 if ble_was_connected else 0))

        time.sleep_ms(2)


if __name__ == "__main__":
    main()
