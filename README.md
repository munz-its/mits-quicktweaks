# Munz Quick Tweaks

Kostenlose Windows-App von Munz IT Service. Macht Windows 10 22H2 und Windows 11 schneller und aufgeräumter. Jede Änderung ist erklärt und lässt sich zurücksetzen. Portabel, ohne Installation, ohne Werbung, ohne Konto und ohne Telemetrie.

**Aktuelle Version: 1.1.0**
Download: [GitHub Releases](https://github.com/munz-its/mits-quicktweaks/releases/latest)
Änderungen: [RELEASE_NOTES.md](RELEASE_NOTES.md)

## Funktionen

| Bereich | Inhalt |
| --- | --- |
| Dashboard | Überblick über den PC und den Stand deiner Tweaks |
| Voreinstellungen | Ein Klick für viele Einstellungen, mit Vorschau und Häkchen pro Änderung |
| Tweaks, Datenschutz, Debloat | Tweaks mit Erklärung und Risiko-Stufe, Telemetrie, Copilot und Recall abschalten, vorinstallierte Apps entfernen und zurückholen |
| Apps | Edge, Chrome, Discord, OneDrive, Steam und Epic Games gezielt einstellen |
| Programme | Mehrere Programme auf einmal per winget installieren |
| Autostart | Programme beim Windows-Start verwalten |
| Energie | Energiemodus und Energieplan |
| Prozessregeln | Feste Priorität pro Programm, Grafikkarte pro Programm |
| Game Mode | Ein Klick vor dem Spielen, ein zweiter stellt alles wieder her |
| Benchmark | Prozessor, Arbeitsspeicher, Datenträger und Grafikkarte vor und nach den Tweaks messen, Startzeit |
| System Care | Temporäre Dateien und Caches aufräumen, darunter der Windows-Update-Cache |
| Speicherplatz | Zeigt, welche Ordner und Dateien am meisten Platz belegen |
| Treiber | Wichtige Treiber im Überblick, alte Treiber markiert |
| Gesundheit | SMART-Werte der Datenträger, Akku-Zustand bei Laptops, Windows-Reparatur mit SFC und DISM |
| Netzwerk | DNS wechseln mit automatischer Rücknahme, DNS-Cache leeren, Ping-Test, sichere Netzwerk-Reparatur |
| Wiederherstellung | Wiederherstellungspunkt, Journal mit jedem alten Wert, Registry-Export, alles zurücksetzen |
| Hilfe | Anleitung, Support-Anfrage per eigenem Mailprogramm, Datenschutzhinweise |

Weitere Punkte:
- Zwei Designs: Munz und Liquid Glass (Glaseffekt unter Windows 11). Umschalten unter Einstellungen.
- Prüfung nach Windows-Updates: Kurz nach dem Anmelden prüft die App, ob ein Update Tweaks zurückgesetzt hat, und meldet sich nur dann.
- Kontextmenü aufräumen, z. B. "Copilot fragen" oder "Mit Clipchamp bearbeiten" ausblenden.
- Sprachen: Deutsch und Englisch.

## Sicherheit

- Vor jeder Änderung sichert die App den alten Stand. Jede Änderung lässt sich einzeln oder komplett zurücksetzen.
- Kein globaler Netzwerk-Reset per netsh. Die Reparatur greift nur bei Adaptern ohne Adresse vom Router (169.254.x.x). Ein Test verhindert, dass ein solcher Reset wieder in den Code kommt.
- Die exe ist nicht signiert. Windows SmartScreen kann beim ersten Start warnen: "Weitere Informationen" und dann "Trotzdem ausführen". Die Prüfsumme steht im Release in `MunzQuickTweaks.exe.sha256`.

## Datenschutz

Die App geht nur in diesen Fällen ins Internet:
- Update-Prüfung beim Start: Abruf von `munz-its.de/quicktweaks/version.json`. Abschaltbar in den Einstellungen.
- Funktionen, die du selbst startest: Ping-Test, DNS-Wechsel, Programme installieren.

Einstellungen und Protokolle bleiben auf dem PC. Details stehen in der App unter Hilfe, Datenschutz, und unter [munz-its.de/datenschutz](https://munz-its.de/datenschutz).

## Technik

- C# mit .NET 10 LTS und WPF, MVVM mit CommunityToolkit.Mvvm
- Portable Einzeldatei für win-x64, vorkompiliert (ReadyToRun), läuft ohne installiertes .NET
- Startet immer mit Administratorrechten

## Aufbau

| Ordner | Inhalt |
| --- | --- |
| src/MunzQuickTweaks.App | WPF-Oberfläche, Views, ViewModels, Designs (Themes), Sprachen |
| src/MunzQuickTweaks.Core | Plattformunabhängige Logik, Einstellungen, Tweak-Engine |
| src/MunzQuickTweaks.Windows | Zugriff auf Registry, WMI, Netzwerk, Dienste und Grafik |
| tests/MunzQuickTweaks.Tests | Unit-Tests |
| catalog | Tweak-Definitionen als JSON, eine Datei pro Kategorie |
| tools/selftest | Selbsttest mit Bericht auf dem Desktop |

## Bauen

```
dotnet test tests/MunzQuickTweaks.Tests
dotnet publish src/MunzQuickTweaks.App -c Release -o publish
```

Ergebnis: `publish/MunzQuickTweaks.exe`.

Selbsttest: `MunzQuickTweaks.exe --selftest` oder `tools/selftest/Selbsttest starten.bat`.

## Veröffentlichen

1. Version in `Directory.Build.props` erhöhen.
2. `RELEASE_NOTES.md` anpassen. Der Text wird automatisch der Release-Text.
3. Nach `main` pushen. GitHub Actions baut die exe und legt das Release an, sobald es für diese Version noch keins gibt.
4. `version.json` auf munz-its.de anpassen, damit bestehende Installationen das Update melden.

## Speicherorte

- Einstellungen: `%LocalAppData%\MunzQuickTweaks\settings.json`
- Protokolle: `%ProgramData%\MunzQuickTweaks\logs` (eine Datei pro Tag, 14 Tage)
- Journal, Verlauf und Änderungsprotokoll: `%ProgramData%\MunzQuickTweaks\state`
- Registry-Sicherungen (.reg): `%ProgramData%\MunzQuickTweaks\backups`

## Lizenzen

Schriften Archivo, IBM Plex und Inter unter der SIL Open Font License 1.1 (Lizenztexte in `src/MunzQuickTweaks.App/Assets/Fonts`). Die Plex-Schriften sind wegen des reservierten Namens "Plex" als "Munz Sans" eingebunden.

## Kontakt

Munz IT Service, support@munz-its.de

© 2026 Munz IT Service
