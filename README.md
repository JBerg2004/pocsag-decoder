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
  - Schnittstellen: eine oder mehrere (anhaken), es wird an alle gesendet; optional „Danach stoppen“ und „Wiederholungen trotzdem senden“ (Wiederholungs-Sperre gilt für dieses Ereignis nicht)
  - Rechtsklick auf eine empfangene Meldung → „Telegrammereignis aus dieser Nachricht erstellen“
- **Schnittstellen**: Webhook/REST, MQTT, Telegram, Signal, Pushover, E-Mail, Divera 24/7, FE2, Programm starten, Ton abspielen, Datenbank (SQLite), TCP-Ausgabe (siehe unten). Button „Testnachricht senden“.
- SubRIC-Buchstaben wahlweise groß (A–D) oder klein (a–d), einstellbar im Reiter „Schnittstellen“; gilt für die Anzeige und `{subric}`. Fest: `{subric_upper}`, `{subric_lower}`
- Wiederholungs-Sperre: dieselbe Meldung (RIC + SubRIC + Text) wird innerhalb eines Zeitfensters (Standard 60 s) nur einmal weitergeleitet
- **RIC-Beschreibungen**: zu jeder RIC eine Bezeichnung, eine kurze Bezeichnung (Spalte „Kurz“, Platzhalter `{ric_short}`), einen Ort (Spalte „Ort“, Platzhalter `{ric_location}`), den BosMon-Benutzer (Spalte „BosMon-Benutzer“, Platzhalter `{bosmon_user}`, z. B. als Benutzer der Schnittstelle BosMon-Netzwerk) und für SubRIC A, B, C, D je ein eigener Klartext („FF Musterstadt · Vollalarm“), der in der Liste angezeigt und mit `{description}` weitergeleitet wird. Import aus CSV/TXT (z. B. aus Excel), JSON oder direkt aus einer BOSWatch-3-Konfiguration (YAML, descriptor-Modul) oder einer BosMon-Konfiguration (.ini mit Ereignissen: Adresse, Ereignisname und Benutzer der Netzwerk-Aktion; vorhandene Bezeichnungen bleiben erhalten), Export und Vorlage im Reiter „RIC-Beschreibungen“
- Reiter **Einstellungen**, Abschnitt Testnachricht: Meldung ohne SDR-Stick einspeisen (zum Ausprobieren von Beschreibungen, Telegrammereignissen und Webhooks); vor dem Senden kommt eine Sicherheitsabfrage
- Fehlerkorrektur einstellbar (Reiter Empfänger, multimon-ng `-b`, Standard 1): weniger Phantom-RICs als die multimon-Voreinstellung 2
- **Hintergrundbetrieb**: Symbol im Infobereich (unten rechts), Schließen/Minimieren blendet nur aus, Hinweis bei Treffern eines Telegrammereignisses, optional Start mit Windows (`--minimized`), nur eine Instanz gleichzeitig
- **Kein Energiesparmodus**: Solange das Programm läuft, gehen Energiesparmodus und Ruhezustand nicht an (`SetThreadExecutionState`), der Bildschirm darf trotzdem ausgehen. Abschaltbar unter Einstellungen → Programm.
- CSV-Tagesprotokoll unter `%APPDATA%\PocsagDecoder\logs`; abgeschlossene Tage werden auf Wunsch als `.csv.gz` komprimiert (etwa ein Achtel der Größe), sehr alte Tage optional nach X Tagen gelöscht (Einstellungen → Aufzeichnung)
- Reiter **Verlauf**: alle Tagesprotokolle nach Zeitraum, Suchbegriffen (RIC, Bezeichnung, Ort, Text …), SubRIC und „nur weitergeleitete“ durchsuchen; Treffer als Excel-Datei (.xlsx) speichern
- **Empfangsqualität** in der Statusleiste: vergleicht wiederholt gesendete Alarme; kommt die Wiederholung mit genau demselben Text an, war der Empfang fehlerfrei
- **Design** hell, dunkel oder wie Windows; SubRIC-Farben und Schriftgröße der Listen einstellbar (Strg + Mausrad)
- **Sicherung**: alle Einstellungen in eine Datei speichern und wiederherstellen (Einstellungen → Programm; enthält Webhook-Adressen, nicht weitergeben)
- **Master/Slave** (Einstellungen → Master/Slave): zwei PCs mit je einem Stick empfangen gleichzeitig, weitergeleitet wird nur vom Master. Der Slave fragt den Master alle 5 Sekunden über das Netzwerk ab (TCP, Standard-Port 8754, IPv4 und IPv6; mit Kennwort per AES-GCM verschlüsselt und gegen wiederholte Antworten geschützt, zwischen verschiedenen Netzen am einfachsten über Tailscale, alternativ Fritzbox-VPN oder Portfreigabe) und übernimmt, wenn der Master nicht erreichbar ist, sein Empfang steht oder er seit 10 Minuten nichts empfängt, während der Slave Meldungen bekommt. Alarme, die der Master nicht empfangen hat, sendet der Slave auf Wunsch selbst. Ausfälle werden wie Fehler gemeldet; die Windows-Firewall am Master muss den Port zulassen
- **Updates**: sucht beim Start und zweimal am Tag nach einem neuen Release auf GitHub, lädt es im Hintergrund herunter und prüft die Prüfsumme; ein Klick auf „Jetzt aktualisieren“ ersetzt die Exe und startet neu, die Einstellungen bleiben erhalten
- Einstellungen unter `%APPDATA%\PocsagDecoder\settings.json`

## Installation

**Am schnellsten:** Beim neuesten Release `PocsagDecoder-komplett.zip` herunterladen und entpacken. Darin sind Programm,
`rtl_fm.exe`, `multimon-ng.exe` und `Installieren.bat`; danach nur noch den Zadig-Schritt (1.) und `Installieren.bat` ausführen.

1. **Treiber für den Stick**: Stick einstecken, [Zadig](https://zadig.akeo.ie/) starten,
   „Options → List All Devices“, „Bulk-In, Interface (Interface 0)“ wählen und **WinUSB** installieren.
2. **rtl_fm.exe**: Windows-Release von [rtl-sdr-blog](https://github.com/rtlsdrblog/rtl-sdr-blog/releases)
   (Datei `Release.zip`, enthält `rtl_fm.exe` und `rtlsdr.dll`) oder osmocom-Build.
3. **multimon-ng.exe**: Windows-Build von [multimon-ng](https://github.com/EliasOenal/multimon-ng/releases).
4. `PocsagDecoder.exe` und die Tools in denselben Ordner legen (oder im Reiter „Einstellungen“ die Pfade auswählen).
5. `PocsagDecoder.exe` starten. Es wird keine .NET-Installation benötigt (die Exe enthält die Laufzeit).

**Einfacher:** `Installieren.bat` in den Ordner mit `PocsagDecoder.exe`, `rtl_fm.exe` und `multimon-ng.exe` legen und doppelklicken.
Das Skript kopiert alles nach `%LOCALAPPDATA%\Programs\PocsagDecoder`, installiert bei Bedarf die .NET 8 Desktop Runtime
(nur für die kleine Exe nötig), legt Verknüpfungen auf dem Desktop und im Startmenü an und erklärt den Zadig-Schritt.
Für einen Umzug auf einen anderen PC die eigene `%APPDATA%\PocsagDecoder\settings.json` in einen Unterordner
`Einstellungen` neben das Skript legen; sie wird dann mit übernommen (enthält Webhook-Adressen, nicht weitergeben).

Test, ob der Stick funktioniert: `rtl_test.exe` in einer Eingabeaufforderung.

## Bedienung

1. Reiter **Einstellungen**: Frequenz (z. B. 173,255 MHz), Gain (leer = automatisch), ppm-Korrektur.
2. Reiter **Schnittstellen**: Ziel anlegen, URL und Body einstellen, „Testnachricht senden“.
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

## Schnittstellen-Arten

Im Reiter „Schnittstellen“ mit **+ Neu** eine Art aus der Übersicht wählen; es werden nur die passenden Felder angezeigt (Verbindung, Anmeldung, Inhalt, Erweitert). Platzhalter fügt „{ } Platzhalter einfügen“ an der Cursor-Position ein. Jede Schnittstelle zeigt, ob der letzte Versand geklappt hat (grüner/roter Punkt) und welche Telegrammereignisse sie benutzen. Beim Umbenennen werden die Telegrammereignisse mitgezogen, Änderungen werden automatisch gespeichert. Die SubRIC-Schreibweise (groß/klein) steht jetzt im Reiter Einstellungen.

| Art | Was eintragen | Hinweis |
|---|---|---|
| Webhook / REST | Methode, URL, Header, Body | Home Assistant, Node-RED, eigene Server; für alarmiert.de o. Ä. mit HTTP-API ebenfalls diese Art |
| MQTT | Broker, Port (1883/8883), Benutzer, Passwort, Topic, Nutzlast | MQTT 3.1.1, QoS 1, optional Retain und TLS |
| Telegram | Bot-Token, Chat-ID, Text | Bot bei @BotFather anlegen |
| Signal | Adresse der signal-cli-rest-api, eigene Nummer, Empfänger, Text | braucht einen laufenden signal-cli-rest-api-Dienst |
| Pushover | App-Token, User-Key, Titel, Text, Priorität | Priorität 2 wiederholt sich bis zur Bestätigung |
| E-Mail (SMTP) | Server, Port, TLS (STARTTLS), Benutzer, Passwort, Absender, Empfänger, Betreff, Text | Port 465 (reines SSL) geht nicht, 587 nehmen |
| Divera 24/7 | API-Adresse, Accesskey, Stichwort, Text, Divera-RIC, Sonderrechte | |
| FE2 | Server-Adresse (`http://SERVER:83/rest/external/http/alarm/v2`), Autorisierung, Absender, Einheiten, Stichwort, Stichwort-Beschreibung, Alarmtext | Externe Schnittstelle, Datenformat v2. In FE2: Alarmeingang „Externe Schnittstelle“, HTTP POST an, Version v2, Kennwort unter „Gültige Absender“. Einheiten = Codes der Einheiten in FE2 (Standard `{ric}{subric_upper}`, leer = Standard-Einheiten des Eingangs) |
| BosMon-Netzwerk | Host (z. B. FE2-Rechner), Port (Standard 5566), TLS, Benutzer (bei FE2 der Einheiten-Code), Ereignisname, Nachricht | Sendet wie BosMons Netzwerkereignis im Format „Erweitert“ (XML über TCP, eine Verbindung je Telegramm), z. B. an den BosMon-Eingang von FE2. Nachgebaut nach Mitschnitten von BosMon (Nachricht mit ihren Leerzeichen am Anfang und Ende, reiner Tonruf = leere Nachricht, Text-Telegramm ohne Inhalt = ein Leerzeichen) |
| Programm starten | Programm/Skript, Argumente | `.ps1` wird mit PowerShell gestartet |
| Ton abspielen | wav- oder mp3-Datei | spielt am PC |
| Datenbank (SQLite) | Datei (Standard `%APPDATA%\PocsagDecoder\telegramme.db`) | Tabelle `telegramme` |
| TCP-Ausgabe | Adresse (127.0.0.1 = nur dieser PC, 0.0.0.0 = Netzwerk), Port, Zeile | jedes Telegramm als eine Textzeile an alle verbundenen Programme |

Passwörter und Tokens stehen im Klartext in der `settings.json`. Die Felder für Divera und FE2 sind nach deren öffentlicher Doku gebaut; vor dem Einsatz mit „Testnachricht senden“ prüfen.

## Allgemeine SubRIC-Zuordnung

Im Reiter „RIC-Beschreibungen“ rechts neben der Suche: **SubRICs zuordnen…** – dort einmalig festlegen, was A, B, C und D bedeuten (z. B. A = Vollalarm, B = Kleinalarm). Das gilt für alle RICs und erscheint im Reiter Telegramme in der Spalte „SubRIC-Bedeutung“. Weicht eine einzelne RIC ab, kann sie in der Tabelle eigene Texte für A–D bekommen. Daneben gibt es je SubRIC einen **Kurztext** (z. B. A = Alarm, B = ohne dring). Er geht beim BosMon-Netzwerk ins Feld `short`, genau wie die Kurzbeschreibung der Funktion in BosMon; leer bleibt es die Bedeutung.

## Fehlermeldungen und Speicherort

Im Reiter **Einstellungen**:
- **Aufzeichnung**: Speicherort der CSV-Tagesdateien frei wählbar (auch Netzlaufwerk), „Standard“ setzt ihn zurück.
- **Fehlermeldungen**: Benachrichtigung, wenn der Empfang abbricht oder nicht startet, das Senden an eine Schnittstelle fehlschlägt, die CSV-Datei nicht geschrieben werden kann oder seit X Minuten keine Meldung kam. Wege: Hinweis unten rechts und/oder eine Schnittstelle (z. B. Home Assistant; `{message}` enthält den Fehlertext, `{rule}` ist „Systemfehler“). Gleiche Fehler höchstens alle 10 Minuten.
- Optional: Empfang nach einem Abbruch automatisch neu starten (alle 30 Sekunden).
- **Heartbeat**: sendet alle X Minuten (Standard 60) ein Lebenszeichen an eine Schnittstelle, z. B. an Home Assistant, Uptime Kuma oder FE2. Bleibt es dort aus, ist der PC, das Programm oder der Empfang ausgefallen. Standardmäßig nur, solange der Empfang läuft. `{message}` enthält den Heartbeat-Text (eigene Platzhalter `{pc}`, `{state}`, `{last}`), `{rule}` ist „Heartbeat“; RIC und SubRIC sind einstellbar, falls die Gegenseite nach RIC zuordnet. Button „Heartbeat jetzt senden“ zum Ausprobieren.
- **Heartbeat an eine URL**: ruft alle X Minuten (Standard 5) eine frei wählbare URL per `POST` mit JSON auf, z. B. [healthchecks.io](https://healthchecks.io) oder einen Home-Assistant-Webhook. Optional gibt es eine **Fehler-URL**: Sie wird statt der URL aufgerufen, wenn der Empfang gestoppt oder abgebrochen ist (Stick abgezogen?), und zusätzlich, wenn die URL nicht antwortet. Button „Heartbeat-URL testen“ zum Ausprobieren. Inhalt:

  ```json
  {"status":"ok","pc":"LEITSTELLE","stick":"ok","lastMessage":"2026-10-19T14:18:00","lastMessageMinutesAgo":12,"queueLength":0,"time":"2026-10-19T14:30:00"}
  ```

  `status` ist `ok` oder `fehler`, `stick` ist `ok`, `gestoppt` oder `Fehler`, `queueLength` zählt Sendungen an Schnittstellen, die noch laufen oder auf einen neuen Versuch warten. Für healthchecks.io: URL `https://hc-ping.com/<uuid>`, Fehler-URL `https://hc-ping.com/<uuid>/fail`. Für Home Assistant: `http://homeassistant.local:8123/api/webhook/<webhook_id>`, in der Automation stehen die Werte dann unter `trigger.json`, z. B. `trigger.json.stick`.

## Fenster im Reiter Telegramme

Der Reiter Telegramme lässt sich in mehrere Fenster aufteilen. Jedes Fenster hat nur eine schmale Titelleiste mit seinem Namen. Ein Klick hinein wählt es aus (blau umrandet).

Neben **Liste leeren** steht das Menü **Fenster ▾**:
- **Hinzufügen** – neues Fenster
- **Autom. Anordnen** – Untereinander, Nebeneinander oder Raster (2 Spalten)
- **Konfigurieren…** – Name, Filter und Spalten des ausgewählten Fensters (auch per Doppelklick auf die Titelleiste)
- **Nach vorne / Nach hinten verschieben** – Reihenfolge ändern; alternativ die Titelleiste auf ein anderes Fenster ziehen
- **Leeren** – nur dieses Fenster leeren
- **Löschen** – Fenster entfernen
- darunter die Liste aller Fenster zum Auswählen

Filter je Fenster: Ereignis (alle Telegramme oder nur die, die zu einem Telegrammereignis passen), RIC (Teil oder Muster wie `12345*`), SubRIC, Bezeichnung/Ort (sucht in Bezeichnung, Kurzbezeichnung, Ort und SubRIC-Bedeutung; mehrere Begriffe mit `|`), Text, „nur Ereignistreffer“ und „Wiederholungen ausblenden“ (gleiche RIC, SubRIC und gleicher Text innerhalb der Wiederholungs-Sperre, sonst 30 Sekunden). Spalten lassen sich im Dialog oder per Rechtsklick auf einen Spaltenkopf ein- und ausblenden. Alles wird automatisch gespeichert.

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

Neue Version veröffentlichen: Version in `src/PocsagDecoder.App/PocsagDecoder.App.csproj` erhöhen und nach `main` pushen.
Die GitHub-Action `.github/workflows/release.yml` baut dann `PocsagDecoder.exe` (klein, braucht die .NET 8 Desktop Runtime) und `PocsagDecoder-komplett.exe` (mit Laufzeit)
sowie `PocsagDecoder-komplett.zip` (mit den Programmen aus `sdr-tools/`) und legt das Release `v<Version>` an; laufende Programme finden es über die Update-Suche.

Projektaufbau:

- `src/PocsagDecoder.Core` – Parser, Telegrammereignisse, Webhook-Versand, Prozess-Pipeline (plattformunabhängig, getestet)
- `src/PocsagDecoder.App` – WPF-Oberfläche
- `tests/PocsagDecoder.Tests` – Unit-Tests und ein Ende-zu-Ende-Test mit echtem multimon-ng (unter Linux)
- `tools/pocsag_gen.py` – erzeugt ein synthetisches POCSAG-Signal zum Testen ohne Funk

## Hinweis

Das Abhören und Weitergeben von Funkmeldungen, die nicht für dich bestimmt sind
(z. B. BOS-Funk), ist in Deutschland durch das Abhörverbot im Telekommunikationsrecht untersagt.
