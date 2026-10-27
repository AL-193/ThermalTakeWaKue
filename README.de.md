# ThermalTakeWaKue

**Open-Source-Treiber für das Segment-LCD auf Thermaltake-Wasserkühlern der LA-Serie — für Windows und Linux.**

🇬🇧 [English version](README.md)

Die CPU-Wasserkühler der Thermaltake-LA-Serie haben ein kleines Segment-LCD, das CPU-/GPU-Temperatur,
Lüfter- und Pumpendrehzahl und mehr anzeigt. Das offizielle Programm *TT SEGMENT LCD LA SERIES*
läuft nur unter Windows, ist Closed Source und startet eine komplette Oberfläche, nur um ein paar
Zahlen per USB zu verschicken.

`ttlcd` ist ein schlanker Ersatz:

- 🖥️ **Windows und Linux** — eine eigenständige Programmdatei pro Plattform, keine Runtime nötig
- 🌡️ **Dieselben Werte wie das Original** — CPU- & GPU-Temperatur, Auslastung, Leistung und Takt,
  CPU-Spannung, Lüfterdrehzahl, Pumpendrehzahl, RAM-Auslastung, Datum und Uhrzeit
- 🔧 **Freie Sensorwahl** für jedes Feld — z. B. einen bestimmten Gehäuselüfter-Anschluss als „Pumpe“
- 🔌 **Hot-Plug** — verbindet sich automatisch neu, wenn das USB-Gerät zurückkommt
- 📖 **Dokumentiertes Protokoll** — siehe [docs/PROTOCOL.md](docs/PROTOCOL.md)

> **Status:** frühe Entwicklung. Das Display-Protokoll ist vollständig umgesetzt und auf echter
> Hardware bestätigt (Windows 11, Intel Core i9-14900KF, ASUS ROG Maximus Z790 Formula, RTX 3090).
> Das LCD der LA-Serie zeigt CPU-Temperatur, CPU-Auslastung und Lüfterdrehzahl.
> Das Linux-Sensor-Backend ist implementiert, muss aber noch auf echten Systemen getestet werden —
> Rückmeldungen sind willkommen!

## Installation

Aktuelle Builds gibt es unter [Actions](https://github.com/AL-193/ThermalTakeWaKue/actions) bzw.
Releases — oder [selbst bauen](README.md#building).

> ⚠️ Das Original-Programm *TT SEGMENT LCD LA SERIES* beenden und aus dem Autostart entfernen —
> zwei Programme, die gleichzeitig ans Display senden, kommen sich in die Quere.

### Windows

Für CPU-Temperaturen und Mainboard-Lüfteranschlüsse sind **Administratorrechte** und der Treiber
**[PawnIO](https://pawnio.eu/)** nötig (wird von [LibreHardwareMonitor](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor)
verwendet und ersetzt den alten WinRing0-Treiber, den Windows Defender inzwischen blockiert).
GPU-Werte, CPU-Auslastung und RAM funktionieren auch ohne.

> ℹ️ Die Builds sind **noch nicht code-signiert**. Ist in Windows 11 *Smart App Control* aktiv,
> wird `ttlcd.exe` blockiert. Bis es signierte Releases gibt, aus dem Quellcode mit dem .NET SDK
> starten (`dotnet run --project src/TtLcd -- run`). Smart App Control dafür **nicht** abschalten —
> es lässt sich ohne Windows-Neuinstallation nicht wieder einschalten.

1. PawnIO von <https://pawnio.eu/> installieren.
2. `ttlcd.exe` in einen Ordner entpacken, z. B. `C:\Tools\ttlcd`.
3. In einem **Administrator**-Terminal ausprobieren:
   ```powershell
   .\ttlcd.exe list     # zeigt alle Sensoren und was das LCD anzeigen wird
   .\ttlcd.exe run      # aktualisiert das LCD bis Strg+C
   ```
4. Automatisch beim Systemstart ausführen (unsichtbar, läuft als SYSTEM):
   ```powershell
   .\ttlcd.exe init-config --config C:\ProgramData\ttlcd\config.json
   .\install-autostart.ps1 -ExePath C:\Tools\ttlcd\ttlcd.exe
   ```
   Entfernen mit `uninstall-autostart.ps1`. Beide Skripte liegen in [packaging/windows](packaging/windows).

### Linux

Die Sensoren kommen aus der `hwmon`-Schnittstelle des Kernels (`coretemp`/`k10temp`, Super-I/O-Chips
wie `nct6775` oder `it87`, `amdgpu`, …) und aus NVML für NVIDIA-Grafikkarten.

```bash
sudo install -m 755 ttlcd /usr/local/bin/ttlcd
# optional: Zugriff aufs LCD ohne root
sudo cp packaging/linux/99-ttlcd.rules /etc/udev/rules.d/
sudo udevadm control --reload && sudo udevadm trigger

ttlcd list
```

Als systemd-Dienst:

```bash
sudo ttlcd init-config --config /etc/ttlcd/config.json
sudo cp packaging/linux/ttlcd.service /etc/systemd/system/
sudo systemctl enable --now ttlcd
```

## Konfiguration

Ohne Konfigurationsdatei wählt `ttlcd` passende Sensoren automatisch. Für eine eigene Auswahl
`ttlcd init-config` ausführen und die Datei anpassen — die Sensor-IDs zeigt `ttlcd list`.
Details und Beispiel: [README.md#configuration](README.md#configuration).

## Mitmachen

Beiträge sind sehr willkommen — Fehlerberichte, Tests auf anderer Hardware, Linux-Feedback, Code.
Siehe [CONTRIBUTING.md](CONTRIBUTING.md).

## Autoren

- **[@AL-193](https://github.com/AL-193)** — Idee, Hardware, Tests, Maintainer
- **Claude** (Anthropic) — Co-Autor: Protokollanalyse, Architektur und Implementierung

## Lizenz

[GNU General Public License v3.0](LICENSE).

## Hinweis

Unabhängiges Community-Projekt, nicht verbunden mit Thermaltake Technology Co., Ltd.
„Thermaltake“ ist eine Marke des jeweiligen Inhabers. Das USB-Protokoll wurde ausschließlich zur
Herstellung der Interoperabilität analysiert; Code der Original-Software ist nicht enthalten.
Nutzung auf eigene Gefahr.
