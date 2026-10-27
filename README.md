# ThermalTakeWaKue

**Open-source driver for the segment LCD on Thermaltake LA series water blocks — for Windows and Linux.**

[![Build](https://github.com/AL-193/ThermalTakeWaKue/actions/workflows/build.yml/badge.svg)](https://github.com/AL-193/ThermalTakeWaKue/actions/workflows/build.yml)
&nbsp;🇩🇪 [Deutsche Version](README.de.md)

Thermaltake's CPU water blocks of the LA series carry a small segment LCD that shows CPU/GPU
temperatures, fan and pump speed and more. The official tool, *TT SEGMENT LCD LA SERIES*, is
Windows-only, closed source and starts a full GUI just to push a few numbers over USB.

`ttlcd` is a small, lightweight replacement:

- 🖥️ **Windows and Linux** — one self-contained executable per platform, no runtime to install
- 🌡️ **Same values as the original** — CPU & GPU temperature, usage, power and clock, CPU voltage,
  fan speed, pump speed, RAM usage, date and time
- 🔧 **Pick any sensor** for any field — e.g. use a specific chassis fan header as "pump"
- 🔌 **Hot-plug aware** — reconnects when the USB device comes back
- 📖 **Documented protocol** — see [docs/PROTOCOL.md](docs/PROTOCOL.md)

> **Status:** early development. The display protocol is fully implemented and verified on real
> hardware (Windows 11, Intel Core i9-14900KF, ASUS ROG Maximus Z790 Formula, RTX 3090).
> The Linux sensor backend is implemented but still needs testing on real machines — reports welcome!

## Supported hardware

| Device | USB ID | Status |
|--------|--------|--------|
| Thermaltake LA series water block with segment LCD | `5131:2007` | ✅ works — shows CPU temperature, CPU usage and fan speed |

Do you have a different Thermaltake LCD? Run `ttlcd list` and open an issue with the output of
`lsusb` (Linux) or the device ID from Device Manager (Windows).

## Installation

Download the latest build for your platform from the
[Actions](https://github.com/AL-193/ThermalTakeWaKue/actions) / Releases page, or [build it yourself](#building).

> ⚠️ Close the original *TT SEGMENT LCD LA SERIES* software (and remove it from autostart) —
> two programs writing to the display at once will fight over it.

### Windows

Reading CPU temperatures and mainboard fan headers requires **administrator rights** and the
**[PawnIO](https://pawnio.eu/)** driver (used by [LibreHardwareMonitor](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor),
which replaces the old WinRing0 driver that Windows Defender now blocks). GPU values, CPU usage
and RAM work without it.

> ℹ️ Builds are **not code-signed yet**. If Windows 11 *Smart App Control* is on, it blocks
> `ttlcd.exe`. Until signed releases exist, run it from source with the .NET SDK
> (`dotnet run --project src/TtLcd -- run`). Do **not** turn Smart App Control off for this —
> it cannot be turned back on without reinstalling Windows.

1. Install PawnIO from <https://pawnio.eu/>.
2. Unpack `ttlcd.exe` to a folder, e.g. `C:\Tools\ttlcd`.
3. In an **administrator** terminal, try it:
   ```powershell
   .\ttlcd.exe list     # shows all sensors and what the LCD will display
   .\ttlcd.exe run      # updates the LCD until Ctrl+C
   ```
4. Start it automatically at boot (hidden, runs as SYSTEM):
   ```powershell
   .\ttlcd.exe init-config --config C:\ProgramData\ttlcd\config.json
   .\install-autostart.ps1 -ExePath C:\Tools\ttlcd\ttlcd.exe
   ```
   Remove it again with `uninstall-autostart.ps1`. Both scripts are in [packaging/windows](packaging/windows).

### Linux

Sensors come from the kernel's `hwmon` interface (`coretemp`/`k10temp`, Super I/O chips such as
`nct6775` or `it87`, `amdgpu`, …) and NVML for NVIDIA GPUs. Make sure the right kernel modules
are loaded (`sensors-detect` from lm-sensors helps).

```bash
sudo install -m 755 ttlcd /usr/local/bin/ttlcd
# optional: allow access to the LCD without root
sudo cp packaging/linux/99-ttlcd.rules /etc/udev/rules.d/
sudo udevadm control --reload && sudo udevadm trigger

ttlcd list
```

Run it as a systemd service (as root, so CPU power via RAPL is available):

```bash
sudo ttlcd init-config --config /etc/ttlcd/config.json
sudo cp packaging/linux/ttlcd.service /etc/systemd/system/
sudo systemctl enable --now ttlcd
```

## Usage

```
ttlcd [command] [--config <path>]

  run           Keep the LCD updated with sensor values (default)
  list          Show all sensors and which one each LCD field uses
  init-config   Write a config file with the current sensor choices
  demo          Show fixed test values on the LCD
  off           Switch the LCD off
```

## Configuration

Without a config file, `ttlcd` picks sensible sensors automatically (CPU package temperature,
fastest core clock, first spinning fan, a fan header named "pump"/"AIO" for the pump, …).
To choose yourself, run `ttlcd init-config`, then edit the file. Sensor IDs come from `ttlcd list`.

Default location: `%APPDATA%\ttlcd\config.json` (Windows), `~/.config/ttlcd/config.json` (Linux).

```jsonc
{
  "TemperatureUnit": "Celsius",      // or "Fahrenheit"
  "SensorIntervalMs": 1000,          // how often sensors are read
  "SendIntervalMs": 250,             // how often the LCD is updated
  "Sensors": {
    "CpuTemperature": "/intelcpu/0/temperature/26",
    "FanSpeed": "/lpc/nct6798d/0/fan/1",
    "PumpSpeed": "/lpc/nct6798d/0/fan/5"
    // fields left out are chosen automatically
  }
}
```

Fields: `CpuTemperature`, `CpuUsage`, `CpuPower`, `CpuClock`, `CpuVoltage`, `GpuTemperature`,
`GpuUsage`, `GpuPower`, `GpuClock`, `FanSpeed`, `PumpSpeed`, `RamUsage`.

## Building

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```bash
dotnet test
dotnet publish src/TtLcd -c Release -r win-x64   -p:PublishSingleFile=true --self-contained
dotnet publish src/TtLcd -c Release -r linux-x64 -p:PublishSingleFile=true --self-contained
```

### Project layout

```
src/TtLcd/
  Protocol/   LcdPacket – encodes the 64-byte HID reports (pure, unit-tested)
  Device/     LcdDevice – finds and writes to the USB HID device (HidSharp)
  Sensors/    LhmSensorSource (Windows, LibreHardwareMonitor)
              LinuxSensorSource (hwmon, /proc, RAPL) + NvmlGpuReader (NVIDIA)
              SensorSelector – maps sensors to LCD fields
  LcdService  main loop: read sensors → send packet, reconnect on unplug
tests/TtLcd.Tests/
docs/PROTOCOL.md
packaging/    autostart scripts (Windows), udev rule + systemd unit (Linux)
```

## Roadmap

- [x] Restore the LCD functionality of the original software (CLI / background service)
- [ ] Tray app / GUI for choosing sensors
- [ ] Show coolant temperature (e.g. ASUS "Water In/Out" headers) on a free field
- [ ] Code-signed Windows releases (e.g. via SignPath Foundation for open-source projects)
- [ ] Release builds and packages (winget, AUR, .deb)
- [ ] Support for further Thermaltake LCD models

## Contributing

Contributions are very welcome — bug reports, tests on other hardware, Linux feedback, code.
See [CONTRIBUTING.md](CONTRIBUTING.md).

## Authors

- **[@AL-193](https://github.com/AL-193)** — idea, hardware, testing, maintainer
- **Claude** (Anthropic) — co-author: protocol analysis, architecture and implementation

## License

[GNU General Public License v3.0](LICENSE). Uses
[LibreHardwareMonitorLib](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor) (MPL-2.0)
and [HidSharp](https://www.zer7.com/software/hidsharp) (Apache-2.0).

## Disclaimer

This is an independent community project. It is not affiliated with, endorsed by or supported by
Thermaltake Technology Co., Ltd. "Thermaltake" is a trademark of its respective owner.
The USB protocol was analysed solely to achieve interoperability; no code of the original
software is included. Use at your own risk.
