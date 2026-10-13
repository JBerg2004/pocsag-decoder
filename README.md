# POCSAG Decoder für Windows

Empfängt POCSAG-Funkmeldungen (z. B. BOS-Alarmierung) mit einem RTL-SDR-Stick,
zeigt **RIC** und **SubRIC** (A–D, Funktionsbits 0–3) an, filtert nach Telegrammereignissen
und leitet passende Meldungen per **REST-API / Webaufruf / Webhook** weiter.

```
RTL-SDR-Stick → rtl_fm.exe → (Audio 22050 Hz) → multimon-ng.exe → POCSAG Decoder → Telegrammereignisse → HTTP (POST/PUT/PATCH/GET)
```

## Was das Programm kann

- Empfang über `rtl_fm` + `multimon-ng`, POCSAG 512 / 1200 / 2400 Baud
- Anzeige: Zeit, RIC (7-stellig), SubRIC (A–D), Funktion (0–3), Baud, Typ, Text, Ergebnis der Verarbeitung
- Deutsche Umlaute (`[ \ ] { | } ~` → `Ä Ö Ü ä ö ü ß`)
- Empfang in mehrere Fenster aufteilbar, je Fenster eigene Filter (Telegrammereignis, RIC, SubRIC, Bezeichnung/Ort, Text) und Spalten
- **Telegrammereignisse** (von oben nach unten geprüft):
  - RIC: einzeln `1234567`, Liste `111,222`, Bereich `1000000-1000999`, Platzhalter `12345*` / `12?4567`, leer = alle. Button „Nach Bezeichnung auswählen…“ öffnet die Liste der RIC-Beschreibungen mit Suche und Haken; darunter steht, welche bekannten RICs das Ereignis erfasst
  - SubRIC: A, B, C, D einzeln an-/abwählbar (beschriftet mit der allgemeinen SubRIC-Bedeutung)
  - Text: enthält / enthält nicht / regulärer Ausdruck
  - Beschreibung: prüft Bezeichnung der RIC und SubRIC-Bedeutung, z. B. enthält `FF Musterstadt` oder `Vollalarm`; mehrere Begriffe mit `|` trennen
  - Ziele: ein oder mehrere Webhook-Ziele (anhaken), es wird an alle gesendet; optional „Danach stoppen“ und „Wiederholungen trotzdem senden“ (Wiederholungs-Sperre gilt für dieses Ereignis nicht)
  - Rechtsklick auf eine empfangene Meldung → „Telegrammereignis aus dieser Nachricht erstellen“
- **Verarbeitung** (Webhook-Ziele): URL, Methode, Content-Type, eigene Header (z. B. `Authorization: Bearer …`), Body-Vorlage mit Platzhaltern, Timeout, Wiederholungen. Button „Testnachricht senden“.
- SubRIC-Buchstaben wahlweise groß (A–D) oder klein (a–d), einstellbar im Reiter „Verarbeitung“; gilt für die Anzeige und `{subric}`. Fest: `{subric_upper}`, `{subric_lower}`
- Wiederholungs-Sperre: dieselbe Meldung (RIC + SubRIC + Text) wird innerhalb eines Zeitfensters (Standard 60 s) nur einmal weitergeleitet
- **RIC-Beschreibungen**: zu jeder RIC eine Bezeichnung, eine kurze Bezeichnung (Spalte „Kurz“, Platzhalter `{ric_short}`), einen Ort (Spalte „Ort“, Platzhalter `{ric_location}`) und für SubRIC A, B, C, D je ein eigener Klartext („FF Musterstadt · Vollalarm“), der in der Liste angezeigt und mit `{description}` weitergeleitet wird. Import aus CSV/TXT (z. B. aus Excel), JSON oder direkt aus einer BOSWatch-3-Konfiguration (YAML, descriptor-Modul), Export und Vorlage im Reiter „RIC-Beschreibungen“
- Reiter **Test**: Testnachricht einspeisen ohne SDR-Stick (zum Ausprobieren von Beschreibungen, Telegrammereignissen und Webhooks)
- Fehlerkorrektur einstellbar (Reiter Empfänger, multimon-ng `-b`, Standard 1): weniger Phantom-RICs als die multimon-Voreinstellung 2
- **Hintergrundbetrieb**: Symbol im Infobereich (unten rechts), Schließen/Minimieren blendet nur aus, Hinweis bei Treffern eines Telegrammereignisses, optional Start mit Windows (`--minimized`), nur eine Instanz gleichzeitig
- CSV-Tagesprotokoll unter `%APPDATA%\PocsagDecoder\logs`
- Einstellungen unter `%APPDATA%\PocsagDecoder\settings.json`

## Installation

1. **Treiber für den Stick**: Stick einstecken, [Zadig](https://zadig.akeo.ie/) starten,
   „Options → List All Devices“, „Bulk-In, Interface (Interface 0)“ wählen und **WinUSB** installieren.
2. **rtl_fm.exe**: Windows-Release von [rtl-sdr-blog](https://github.com/rtlsdrblog/rtl-sdr-blog/releases)
   (Datei `Release.zip`, enthält `rtl_fm.exe` und `rtlsdr.dll`) oder osmocom-Build.
3. **multimon-ng.exe**: Windows-Build von [multimon-ng](https://github.com/EliasOenal/multimon-ng/releases).
4. `PocsagDecoder.exe` und die Tools in denselben Ordner legen (oder im Reiter „Empfänger (SDR)“ die Pfade auswählen).
5. `PocsagDecoder.exe` starten. Es wird keine .NET-Installation benötigt (die Exe enthält die Laufzeit).

Test, ob der Stick funktioniert: `rtl_test.exe` in einer Eingabeaufforderung.

## Bedienung

1. Reiter **Empfänger (SDR)**: Frequenz (z. B. 173,255 MHz), Gain (leer = automatisch), ppm-Korrektur.
2. Reiter **Verarbeitung**: Ziel anlegen, URL und Body einstellen, „Testnachricht senden“.
3. Reiter **Telegrammereignisse**: Ereignis anlegen, RIC/SubRIC eintragen, Ziel auswählen.
4. **▶ Start**. Treffer werden grün markiert, Fehler beim Senden rot.
5. **💾 Speichern** (wird beim Beenden auch automatisch gespeichert).

## Platzhalter

| Platzhalter | Inhalt |
|---|---|
| `{ric}` | RIC 7-stellig, z. B. `0123456` |
| `{ric_raw}` | RIC ohne führende Nullen |
| `{subric}` | SubRIC `A`–`D` |
| `{subric_num}` | SubRIC `1`–`4` |
| `{function}` | Funktionsbits `0`–`3` |
| `{message}` | Nachrichtentext |
| `{type}` | `Alpha`, `Numeric`, `Tone`, `Skyper` |
| `{baud}` | `512`, `1200`, `2400` |
| `{timestamp}` | ISO 8601, z. B. `2026-10-13T14:05:00+02:00` |
| `{unix}` | Unix-Zeit in Sekunden |
| `{date}` / `{time}` | `13.10.2026` / `14:05:00` |
| `{description}` | Beschreibung aus der RIC-Liste (RIC + SubRIC) |
| `{ric_description}` / `{subric_description}` | nur RIC- bzw. nur SubRIC-Beschreibung |
| `{rule}` | Name des auslösenden Telegrammereignisses |
| `{raw}` | Originalzeile von multimon-ng |

Bei `application/json` werden die Werte JSON-sicher eingesetzt, in der URL und bei
`application/x-www-form-urlencoded` URL-kodiert.

### Beispiele

**JSON-Webhook (Standard)** – `POST`, `application/json`:
```json
{
  "ric": "{ric}",
  "subric": "{subric}",
  "function": {function},
  "message": "{message}",
  "timestamp": "{timestamp}"
}
```

**Einfacher Webaufruf** – `GET`:
```
https://mein-server.de/alarm.php?ric={ric}&sub={subric}&text={message}
```

**Formular** – `POST`, `application/x-www-form-urlencoded`:
```
ric={ric}&subric={subric}&text={message}
```

**Home Assistant Webhook** – `POST https://homeassistant:8123/api/webhook/<id>` mit JSON-Body wie oben.

**Telegram-Bot** – `GET https://api.telegram.org/bot<TOKEN>/sendMessage?chat_id=<ID>&text={ric}{subric}%20{message}`

## Allgemeine SubRIC-Zuordnung

Im Reiter „RIC-Beschreibungen“ rechts neben der Suche: **SubRICs zuordnen…** – dort einmalig festlegen, was A, B, C und D bedeuten (z. B. A = Vollalarm, B = Kleinalarm). Das gilt für alle RICs und erscheint im Reiter Empfang in der Spalte „SubRIC-Bedeutung“. Weicht eine einzelne RIC ab, kann sie in der Tabelle eigene Texte für A–D bekommen.

## Fenster im Reiter Empfang

Der Reiter Empfang lässt sich in mehrere Fenster aufteilen. Jedes Fenster hat nur eine schmale Titelleiste mit seinem Namen. Ein Klick hinein wählt es aus (blau umrandet).

Neben **Liste leeren** steht das Menü **Fenster ▾**:
- **Hinzufügen** – neues Fenster
- **Autom. Anordnen** – Untereinander, Nebeneinander oder Raster (2 Spalten)
- **Konfigurieren…** – Name, Filter und Spalten des ausgewählten Fensters (auch per Doppelklick auf die Titelleiste)
- **Nach vorne / Nach hinten verschieben** – Reihenfolge ändern; alternativ die Titelleiste auf ein anderes Fenster ziehen
- **Leeren** – nur dieses Fenster leeren
- **Löschen** – Fenster entfernen
- darunter die Liste aller Fenster zum Auswählen

Filter je Fenster: Ereignis (alle Telegramme oder nur die, die zu einem Telegrammereignis passen), RIC (Teil oder Muster wie `12345*`), SubRIC, Bezeichnung/Ort (sucht in Bezeichnung, Kurzbezeichnung, Ort und SubRIC-Bedeutung; mehrere Begriffe mit `|`), Text und „nur Ereignistreffer“. Spalten lassen sich im Dialog oder per Rechtsklick auf einen Spaltenkopf ein- und ausblenden. Alles wird automatisch gespeichert.

## RIC-Beschreibungen importieren

Eine Zeile pro RIC, mit eigener Spalte für jede SubRIC. Am einfachsten in Excel anlegen und als „CSV (Trennzeichen-getrennt)“ speichern:

```
RIC;Bezeichnung;A;B;C;D
1234567;FF Musterstadt;Vollalarm;Kleinalarm;Probealarm;Info
1234568;FF Beispieldorf;Einsatz;Atemschutz;;Sirene
```

- Bezeichnung = allgemeiner Name der RIC, A–D = Bedeutung der jeweiligen SubRIC; leere Felder sind erlaubt
- Angezeigt wird „FF Musterstadt · Vollalarm“
- Trennzeichen `;`, Tab, `,` oder `|`; die Spalten werden über die Kopfzeile erkannt (`A`–`D` oder `1`–`4`, Reihenfolge egal)
- Ältere Listen mit einer Zeile pro SubRIC (`RIC;SubRIC;Beschreibung`) werden ebenfalls gelesen und pro RIC zusammengeführt
- Kopfzeile, leere Zeilen und Zeilen mit `#` werden übersprungen; fehlerhafte Zeilen werden nach dem Import aufgelistet
- Alternativ JSON: `[{"ric": 1234567, "name": "FF Musterstadt", "a": "Vollalarm", "b": "Kleinalarm"}]`
- Beim Import wählbar: vorhandene Liste ersetzen oder ergänzen
- Im Programm direkt bearbeitbar (Doppelklick auf ein Feld); Rechtsklick auf eine empfangene Meldung → „Beschreibung für diese RIC bearbeiten“

## Eigener Befehl

Statt rtl_fm kann jeder Befehl verwendet werden, dessen Ausgabe im multimon-ng-Format ist,
z. B. multimon-ng mit Audioeingang aus SDR# / SDR++ über ein virtuelles Audiokabel:

```
multimon-ng.exe -a POCSAG512 -a POCSAG1200 -a POCSAG2400 -f alpha
```

## Selbst bauen

Voraussetzung: .NET 8 SDK.

```
dotnet test
dotnet publish src/PocsagDecoder.App -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o publish
```

Projektaufbau:

- `src/PocsagDecoder.Core` – Parser, Telegrammereignisse, Webhook-Versand, Prozess-Pipeline (plattformunabhängig, getestet)
- `src/PocsagDecoder.App` – WPF-Oberfläche
- `tests/PocsagDecoder.Tests` – Unit-Tests und ein Ende-zu-Ende-Test mit echtem multimon-ng (unter Linux)
- `tools/pocsag_gen.py` – erzeugt ein synthetisches POCSAG-Signal zum Testen ohne Funk

## Hinweis

Das Abhören und Weitergeben von Funkmeldungen, die nicht für dich bestimmt sind
(z. B. BOS-Funk), ist in Deutschland durch das Abhörverbot im Telekommunikationsrecht untersagt.
