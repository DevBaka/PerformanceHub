# ProfileDeck

Windows-Desktop-Tool, das einen Display-Manager mit System-Profilen kombiniert. C# / .NET 8 / WPF, MVVM,
JSON-Persistenz. Kein Backend, keine Cloud, alles lokal.

## Projektstruktur

```
ProfileDeck.slnx
src/
  ProfileDeck.Core/   Klassenbibliothek: Display-Steuerung, Prozesse, Dienste, Registry-Settings,
                       Power-Plan, Audio, Persistenz, Profil-Orchestrator. UI-unabhängig.
  ProfileDeck.Wpf/     WPF-UI (MVVM): Hauptfenster, Profil-Editoren, Picker, Tray-Icon, Hotkeys.
  ProfileDeck.Cli/     Konsolen-Tool, nutzt dieselbe Core-Bibliothek. Für Stream Deck/Verknüpfungen.
```

Beide ausführbaren Projekte (`ProfileDeck.Wpf`, `ProfileDeck.Cli`) haben ein `app.manifest` mit
`requireAdministrator`, weil Dienste-Steuerung und HKLM-Registry-Änderungen Admin-Rechte brauchen.
Das bedeutet: `dotnet run` aus einer nicht-erhöhten Shell löst eine UAC-Anfrage aus bzw. schlägt in
nicht-interaktiven Umgebungen mit "requested operation requires elevation" fehl. Zum Entwickeln/Debuggen
entweder das Terminal/die IDE als Administrator starten, oder den `<ApplicationManifest>`-Eintrag in der
jeweiligen `.csproj` temporär auskommentieren.

## Build & Run

Voraussetzung: .NET 8 SDK (oder neuer, mit installiertem `net8.0-windows` Runtime-Pack).

```powershell
dotnet build ProfileDeck.slnx -c Debug

# WPF-UI (als Administrator starten):
dotnet run --project src/ProfileDeck.Wpf

# CLI:
dotnet run --project src/ProfileDeck.Cli -- display list
dotnet run --project src/ProfileDeck.Cli -- --profile "DJ"
```

Release-Build/Publish (Single-Folder, portable):

```powershell
dotnet publish src/ProfileDeck.Wpf -c Release -r win-x64 --self-contained false -o .\publish\wpf
dotnet publish src/ProfileDeck.Cli -c Release -r win-x64 --self-contained false -o .\publish\cli
```

## Wo Profile liegen

```
%APPDATA%\ProfileDeck\
  profiles\display\*.json    Display-Profile (lesbares JSON, manuell editierbar)
  profiles\system\*.json     System-Profile
  logs\log_YYYYMMDD.txt      Tageslogs
  last-snapshot.json         Letzter Snapshot für "Restore Previous"
```

## Display-Profile: wie die Zuordnung funktioniert

Jeder Monitor wird über seinen **CCD-Target-Device-Path** identifiziert (z. B.
`\\?\DISPLAY#AUS258C#...#{e6f07b5f-...}`), der die EDID-Herstellerkennung enthält - nicht über Adapter-
/Source-/Target-Indizes, die sich bei jedem Neustart oder Umstecken ändern können. Ausgelesen und gesetzt
wird über die CCD-API (`QueryDisplayConfig`/`SetDisplayConfig`), gekapselt über das NuGet-Paket
`WindowsDisplayAPI`.

Anordnungen mit gemeinsamer `Group`-Nummer > 0 werden gespiegelt (Clone) auf eine gemeinsame Quelle gelegt;
`Group = 0` (bzw. eine pro Eintrag eindeutige Zahl) heißt "eigene erweiterte Quelle". Das erlaubt gemischte
Topologien wie im "DJ"-Beispielprofil (zwei Displays gespiegelt, zwei normal erweitert) in einem einzigen
Profil.

Wird versucht, aktuell inaktive Displays wieder zu aktivieren und schlägt die direkte Zuweisung fehl (weil
die im System zwischengespeicherte Quelle/Ziel-Kombination nicht mehr zur restlichen Topologie passt),
erzwingt der Display-Manager automatisch eine Basis-Extend-Topologie (`ApplyTopology(Extend)`) und wendet
danach die gewünschte Anordnung erneut an. Das ist beim Testen auf einem 4-Monitor-System aufgetreten und
wurde gegen echte Hardware verifiziert (siehe unten).

## Was bereits gegen echte Hardware getestet wurde

- Monitor-Erkennung (4 reale Displays, stabile Hardware-IDs, Position/Auflösung/Refresh/Primary korrekt).
- Display-Profil anwenden: reiner Reshuffle unter bereits aktiven Displays, Deaktivieren/Reaktivieren
  einzelner Displays (inkl. automatischer Selbstheilung über `ApplyTopology`), gemischte Clone+Extend-
  Topologie (2 gespiegelt + 2 erweitert) in einem Aufruf.
- System-Profil anwenden: Windows-Settings-Toggles (Verifikation über `reg.exe`, nicht nur "kein Fehler"),
  Prozessor-Scheduling (HKLM, korrekt mit "Access denied" ohne Admin-Rechte), Programme starten inkl.
  "warten auf Fenster" und Beenden nach Namen, Audiogeräte-Enumeration, Dienste-Enumeration.
- WPF-App-Start (Mutex/Single-Instance, Tray-Icon, Logging) ohne Absturz.

Nicht interaktiv testbar (kein GUI-Zugriff in dieser Umgebung): das eigentliche Look&Feel der WPF-Fenster,
Drag&Drop-Reihenfolge im Editor, Picker-Dialoge. Bitte einmal durchklicken.

## Bekannte Einschränkungen / Best-Effort-Bereiche

- **FocusAssist** und **WindowsUpdateActiveHoursAuto**: Microsoft veröffentlicht dafür keine stabile
  Registry-/API-Schnittstelle. FocusAssist wird über die Toast-Benachrichtigungs-Einstellung angenähert,
  nicht über den echten "Nicht stören"-Zustand. Kann auf manchen Windows-Builds nicht wirken.
- **Auflösung/Refreshrate ändern**: Wird nur übernommen, wenn der gewünschte Wert exakt dem aktuell aktiven
  Modus entspricht (dann 1:1 übernommen) - andernfalls wird der EDID-bevorzugte Modus des Monitors genutzt
  und eine Warnung geloggt. Eigene, beliebige CVT/GTF-Timings zu erzeugen ist über die CCD-API ohne
  Treiberunterstützung nicht zuverlässig möglich.
- **Hardwarebeschleunigtes GPU-Scheduling**: Registry-Wert wird korrekt gesetzt, braucht aber einen Neustart
  von Windows, um zu wirken (im Editor entsprechend markiert).
- Programme wie DasLight/OBS/Streamer.bot/Chatty im Beispielprofil "DJ" haben Platzhalter-Pfade - über
  "Aus laufenden Prozessen / Datei..." im Editor anpassen.

## Absichtlich nicht übernommen

Aus dem Vorgänger-Projekt (WinForms → halbfertige Avalonia-Migration) wurden Digital-Vibrance/NVIDIA-
Steuerung, Winget/Chocolatey-Paketmanager, 1ms-Timer-Resolution und Defender/OneDrive/Telemetrie-Tweaks
bewusst nicht übernommen - sie waren nicht Teil der ProfileDeck-Spezifikation.
