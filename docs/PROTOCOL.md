# USB protocol

This document describes how the PC talks to the segment LCD on Thermaltake LA series water blocks.
It was worked out for interoperability by observing the official *TT SEGMENT LCD LA SERIES*
software (v2025-03) talking to the device. Corrections and reports from other models are very welcome.

## Device

| Property        | Value                              |
|-----------------|------------------------------------|
| USB VID / PID   | `0x5131` / `0x2007`                |
| Product string  | `FBB`                              |
| Class           | HID, usage page `0xFF00` (vendor), usage `0x01` |
| Reports         | 64-byte input, 64-byte output, **no report IDs** |

HID report descriptor:

```
06 00 ff 09 01 a1 01 09 02 15 00 26 00 ff 75 08 95 40 81 06
09 02 15 00 26 00 ff 75 08 95 40 91 06 c0
```

Because there are no report IDs, HID APIs (hidapi, HidSharp, Windows `WriteFile`) expect a
leading `0x00` byte, i.e. 65 bytes per write. The 64 bytes below are what follows that byte.

The device does not need to be initialised. The official software sends one update packet
every **250 ms**; the display keeps showing the last values when updates stop.

## Update packet (`01 02`)

| Byte | Content | Encoding |
|-----:|---------|----------|
| 0 | `0x00` | |
| 1 | `0x01` | command |
| 2 | `0x02` | sub-command |
| 3 | CPU temperature | integer part |
| 4 | CPU temperature | 2 decimals (0–99) |
| 5 | CPU temperature unit | `0` = °C, `1` = °F |
| 6 | CPU usage | % |
| 7 | CPU power | W mod 100 |
| 8 | CPU power | 2 decimals |
| 9 | CPU clock | MHz / 100 |
| 10 | CPU clock | MHz mod 100 |
| 11 | CPU voltage | integer part |
| 12 | CPU voltage | 2 decimals ¹ |
| 13 | GPU temperature | integer part |
| 14 | GPU temperature | 2 decimals |
| 15 | GPU temperature unit | `0` = °C, `1` = °F |
| 16 | GPU usage | % |
| 17 | GPU power | W mod 100 |
| 18 | GPU power | 2 decimals |
| 19 | GPU clock | MHz / 100 |
| 20 | GPU clock | MHz mod 100 |
| 21 | Fan speed | RPM / 100 |
| 22 | Fan speed | RPM mod 100 |
| 23 | Pump speed | RPM / 100 |
| 24 | Pump speed | RPM mod 100 |
| 25 | Year | century (e.g. 20) |
| 26 | Year | year mod 100 (e.g. 26) |
| 27 | Month | 1–12 |
| 28 | Day | 1–31 |
| 29 | Hour | 0–23 |
| 30 | Minute | |
| 31 | Second | |
| 32 | Day of week | 0 = Sunday … 6 = Saturday |
| 33 | RAM usage | % |
| 34 | CPU power | W / 100 |
| 35 | GPU power | W / 100 |
| 36–63 | `0x00` | unused |

Values are unsigned bytes. Four-digit values are split into two base-100 "digits",
so 1234 RPM is sent as `12, 34`. Temperatures are converted to °F by the sender when byte 5/15 is `1`.

Example — CPU 42.5 °C, 37 %, 123.45 W, 4567 MHz; fan 1234 RPM; pump 2345 RPM:

```
00 01 02 2a 32 00 25 17 2d 2d 43 01 19 ...
```

¹ The official software sends `floor(V) * 100` here (always 100 for a 1.x V core voltage),
which looks like a bug. This project sends the real two decimals.

## Off packet (`0F`)

Sent by the official software when it exits; the display goes dark.

| Byte | Content |
|-----:|---------|
| 0 | `0x00` |
| 1 | `0x0F` |
| 2–63 | `0x00` |

## What the display shows

The packet carries more values than a given LCD can show. Confirmed so far:

| LCD | Shown | Not shown |
|-----|-------|-----------|
| LA series water block (`5131:2007`) | CPU temperature (large digits, integer part), CPU usage % with bar graph, fan speed (RPM) with bar graph | GPU values, pump speed, CPU power/clock/voltage, RAM, date/time |

At 1234 RPM the fan bar is about half full; the full-scale value is not known yet.

## Open questions

- Full scale of the fan bar graph.
- Whether other LCD models use the remaining fields — please report what yours shows.
- The input report is never read by the official software; its meaning is unknown.
