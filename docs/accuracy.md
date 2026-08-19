# How accurate are the figures?

[← back to the README](../README.md)

Two numbers turn revolutions into everything else. One is measured, the other
is an estimate — and the difference matters when you read the display.

| Figure | Rests on | Good to |
|--------|----------|---------|
| Distance, speed | measured conversion factor | about **±1 %** |
| Calories | cadence and body weight | up to **half** off |

## The conversion factor

`MetersPerRevolution` is `6.18` — measured on 2026-08-18, not estimated. Seven
readings a minute apart while pedalling to a metronome at 60 bpm, each one the
distance covered in that minute divided by its 60 revolutions:

| Minute | 4 | 5 | 6 | 7 | 8 | 9 | 10 |
|--------|---|---|---|---|---|---|----|
| m per revolution | 6.111 | 6.333 | 6.111 | 6.190 | 6.250 | 6.111 | 6.160 |

Mean 6.181, median 6.160, standard deviation 0.085. At roughly 0.37 km a minute
and a display resolving 10 metres, each single value carries about 2.7 % of
reading error; averaged over seven of them some **±1 %** remains.

The value before it was 6.67 from a single reading over 45 revolutions, good to
about ±20 %. The measurement therefore moved every distance and speed in the
whole history down by 7.3 % — nothing was recalculated for that, because only
revolutions are ever stored.

<details>
<summary>Measuring it again, for another bike or another sensor</summary>

1. Set a metronome to 60 bpm and pedal in time — with the Pico connected, check
   that the display really shows about 60 rpm
2. Plug the jack back into the original display unit, reset its trip counter
3. Read the distance once a minute and divide each minute's gain by 60
4. Average the readings and enter the result on the settings page

Reading once a minute beats one reading at the end: it gives several
independent values whose scatter shows what the measurement is worth, instead
of a single number that could be off by a tick without anyone noticing.

</details>

## Calories

The figure carries a `≈` because that is what it is. It needs a body weight,
which the settings page asks for; without one the tile stays empty rather than
showing a figure for an invented default person.

Behind it sits the usual MET formula — kilocalories per minute = MET × 3.5 × kg
÷ 200 — with the MET value interpolated from the cadence and the resting
metabolism subtracted: what is meant is what the riding costs on top of sitting
there.

**Where it falls down:** calories follow from mechanical power, and power is
exactly what this application cannot see. Sixty revolutions a minute against the
lightest resistance and against the heaviest are the same signal, and
energetically a multiple apart. The figure is good for comparing one day against
the next, not for a nutrition plan.

Making it better means calibrating watts per resistance level. The model sits
behind [`IEnergyModel`](../src/DeskCycle.Core/Statistics/IEnergyModel.cs) and
every call already passes the level along, so a measured model can take its
place without touching anything else.
