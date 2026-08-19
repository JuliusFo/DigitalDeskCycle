# How it works

[← back to the README](../README.md)

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

## Why USB comes first

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

## What gets stored

**Revolutions and timestamps**, never kilometres. Distance, speed, calories and
later watts are derived values.

The reason: for a long time the conversion factor was a rough estimate, and it
has since been replaced by a measured one. Every ride ever recorded became more
accurate at that moment, without anything being recalculated. Had kilometres
been stored, they would sit at the old estimate forever. The same goes for the
calories: change the body weight and every figure the application has ever shown
changes with it, because none of them was ever written down.

Samples are taken once per second, but **only while moving**. Seconds spent
standing still made up two thirds of the volume and added nothing that the
session itself does not already record; pauses fall out of the gaps between
timestamps when reading. Measured, a sample costs about 44 bytes, which is
roughly 39 MiB per year at an hour of movement per day.

The **resistance level** is invisible in the signal — the adjustment on the bike
is purely mechanical. It can be filled in per session (double-click a row in the
history). Nothing calculates with it yet: the calorie estimate gets it passed
along and ignores it, because there is no calibration to turn a level into
watts. That measurement is what a power estimate is still waiting for.

## Sessions

A session starts with the first revolution and ends after 90 seconds of
standstill. Sessions below 10 revolutions are discarded, so that a pedal nudged
in passing does not become a training session.

The time axis of the live chart is **active** time: interruptions of 30 seconds
or more are taken out and marked with a red tick. The curve therefore answers
"how did I ride", not "when". On a desk bike used in bursts throughout the day,
a real time axis would consist mostly of idle.

That the wall-clock duration in the history exceeds the active time in the live
view is not a contradiction — they answer two different questions.

A reset deletes nothing. It only moves a timestamp in `settings.json`, which
makes it harmless to undo.
