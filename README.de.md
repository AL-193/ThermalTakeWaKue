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

Für CPU-Temperaturen und Mainboard-Lüfteranschlüsse sind der Treiber **[PawnIO](https://pawnio.eu/)**
(wird von [LibreHardwareMonitor](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor) verwendet
und ersetzt den alten WinRing0-Treiber, den Windows Defender inzwischen blockiert) und erhöhte Rechte nötig.
Empfohlen ist deshalb ein **Windows-Dienst**: Er läuft als SYSTEM und startet mit Windows — Administratorrechte
braucht man nur einmal bei der Einrichtung. GPU-Werte, CPU-Auslastung und RAM gehen auch ohne PawnIO.

1. PawnIO von <https://pawnio.eu/> oder per `winget install --id namazso.PawnIO` installieren.
2. ttlcd in einen festen Ordner kopieren, den nur Administratoren ändern dürfen, z. B.
   `C:\Program Files\ttlcd` (der Dienst läuft von dort).
3. In einem **Administrator**-Terminal:
   ```powershell
   cd "C:\Program Files\ttlcd"
   .\ttlcd.exe list               # optional: zeigt alle Sensoren und was das LCD anzeigen wird
   .\ttlcd.exe install-service    # legt die Konfiguration an, registriert und startet den Dienst
   ```

Konfiguration: `C:\ProgramData\ttlcd\config.json` (nach Änderungen `Restart-Service ttlcd`),
Log: `C:\ProgramData\ttlcd\ttlcd.log`. Entfernen mit `.\ttlcd.exe uninstall-service`.
Solange der Dienst läuft, kein zweites `ttlcd run` starten.

> ℹ️ **Smart App Control:** Die Builds sind **noch nicht code-signiert**, deshalb blockiert Windows 11
> mit aktivem *Smart App Control* die `ttlcd.exe`. Stattdessen die framework-abhängige Variante
> `ttlcd-portable` nutzen, die über Microsofts signierten `dotnet`-Host läuft: die
> [.NET-10-Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) installieren und in den Befehlen
> oben `.\ttlcd.exe` durch `dotnet ttlcd.dll` ersetzen — der Dienst wird dann genauso eingerichtet.
> Smart App Control dafür **nicht** abschalten — es lässt sich ohne Windows-Neuinstallation nicht
> wieder einschalten.

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

## Einstellungen

Während ttlcd läuft (z. B. als Dienst), lassen sich die Einstellungen **ohne Administratorrechte**
ändern — sie werden geprüft, gespeichert und sofort übernommen:

- **`ttlcd-settings`** — kleines Fenster (Windows und Linux): CPU oder GPU auf dem Display, welcher
  Lüfter, °C/°F, Aktualisierungsintervall und eine Live-Vorschau dessen, was das LCD zeigt.
- **`ttlcd configure`** — dasselbe als Menü im Terminal (Pfeiltasten + Enter).

Im GPU-Modus zeigen die Temperatur- und Auslastungsfelder des LCDs die Grafikkarte (die Beschriftung
auf dem LCD bleibt „CPU“). Beide Programme sprechen über eine lokale Named Pipe mit ttlcd; unter
Windows dürfen sich nur am PC angemeldete Benutzer verbinden, Netzwerkzugriff ist gesperrt.

> ℹ️ Mit aktivem *Smart App Control* blockiert Windows das unsignierte Einstellungsfenster, bis es
> signierte Releases gibt. Bis dahin `ttlcd configure` verwenden.

## Konfiguration

Ohne Konfigurationsdatei wählt `ttlcd` passende Sensoren automatisch. Für eine eigene Auswahl
`ttlcd init-config` ausführen und die Datei anpassen — die Sensor-IDs zeigt `ttlcd list`.
Details und Beispiel: [README.md#configuration](README.md#configuration).

Gelesen wird nur die Hardware, zu der ein gewählter Sensor gehört. Das LCD der LA-Serie zeigt nur
CPU-Temperatur, CPU-Auslastung und Lüfterdrehzahl — wer die `Gpu*`-Felder auf `"none"` setzt, spart
das Auslesen der Grafikkarte und kann `SensorIntervalMs` verkürzen (z. B. `500`).

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
