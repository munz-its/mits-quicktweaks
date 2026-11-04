# Munz Quick Tweaks

Kostenlose Windows-App von Munz IT Service. Macht Windows 10 22H2 und Windows 11 schneller und aufgeräumter. Jede Änderung ist erklärt und lässt sich zurücksetzen.

## Stand

Version 0.2.0, Phase 2 (Engine): Tweaks prüfen, sichern, anwenden, kontrollieren und rückgängig machen. 5 Test-Tweaks auf der Tweaks-Seite. Journal unter `%ProgramData%\MunzQuickTweaks\state\journal.json`.

## Technik

- C# mit .NET 10 LTS und WPF
- Portable Einzeldatei für win-x64, läuft ohne installiertes .NET
- Startet immer mit Administratorrechten

## Aufbau

| Ordner | Inhalt |
| --- | --- |
| src/MunzQuickTweaks.App | WPF-Oberfläche, Views, ViewModels, Theme, Sprachen |
| src/MunzQuickTweaks.Core | Plattformunabhängige Logik, Einstellungen |
| src/MunzQuickTweaks.Windows | Zugriff auf Registry und WMI |
| tests/MunzQuickTweaks.Tests | Unit-Tests |
| catalog | Tweak-Definitionen als JSON (ab Phase 2) |

## Bauen

```
dotnet test tests/MunzQuickTweaks.Tests
dotnet publish src/MunzQuickTweaks.App -c Release -o publish
```

Ergebnis: `publish/MunzQuickTweaks.exe`. Bei jedem Push baut GitHub Actions die .exe automatisch, bei einem Tag `v*` entsteht ein Release.

## Speicherorte

- Einstellungen: `%LocalAppData%\MunzQuickTweaks\settings.json`
- Protokolle: `%ProgramData%\MunzQuickTweaks\logs`

## Lizenzen

Schriften Archivo und IBM Plex unter der SIL Open Font License 1.1 (Lizenztexte in `src/MunzQuickTweaks.App/Assets/Fonts`). Die Plex-Schriften sind wegen des reservierten Namens "Plex" als "Munz Sans" eingebunden.

© 2026 Munz IT Service
