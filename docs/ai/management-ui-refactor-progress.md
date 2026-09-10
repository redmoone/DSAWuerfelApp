# Raidlog: Helden- und Gamesessionverwaltung

Stand: 10.09.2026

Plan: `C:\Users\Bosko\Downloads\DSA-Helden-Sessions-Luna-Max-Plan.md`

Referenz-HEAD zu Beginn: `fcdca1607a1c31d4d5bb50545cda893ec63ddf87`

Branch: `master`

Mana: Das Arbeitsbudget ist begrenzt; jeder Build-, Test- und Reviewlauf wird deshalb gezielt pro Raidabschnitt dokumentiert.

## Raidregeln

- Jeder Boss entspricht einer Planphase M0 bis M5.
- Eigene Änderungen werden nach jedem Boss separat geprüft und lokal committed.
- Die bestehenden ungetrackten Nutzerartefakte `DsaWuerfelApp/DsaWuerfelApp.Tests/TestResults/` und `docs/ai/mobile-ui-refactor-plan.md` bleiben unangetastet.
- Vollständige Abnahme wird nur für tatsächlich ausgeführte Prüfungen behauptet. Fehlende Geräte, Importfixtures oder Baselinefehler bleiben als offene Mobs im Log.

## M0 – Boss: Der Baselinewächter

Status: IMPLEMENTIERT
Änderungen: Diese Fortschrittsdatei neu angelegt. Keine Produktivdatei geändert.

### Besiegte Mobs

- Repository-Anweisungen: Keine `AGENTS.md` und keine `copilot-instructions.md` im Checkout gefunden.
- Mobile-Kontext und Fortschrittsstand gelesen; abgeschlossene Würfel-/Shell-Refactorings wurden nicht erneut geplant.
- Node `v24.13.0`, npm `11.6.2`, .NET SDK `10.0.103` und Microsoft Edge unter `C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe` verfügbar.
- Playwright und `three@0.160.0` für Browserprüfungen temporär unter `%TEMP%\dsa-browser-tests` installiert; keine Produktivabhängigkeit geändert.

### Würfel des Bosses

- `dotnet build DsaWuerfelApp.sln -c Release -v minimal`: PASS, 0 Warnungen, 0 Fehler.
- `dotnet test DsaWuerfelApp.sln -c Release --no-restore -v minimal`: BASELINE ROT, 91/99 bestanden, 8 fehlgeschlagen.
- `dotnet test DsaWuerfelApp/DsaWuerfelApp.Tests/DsaWuerfelApp.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~ClientStateTests`: PASS, 18/18.
- `dotnet publish DsaWuerfelApp/DsaWuerfelApp/DsaWuerfelApp.csproj -c Release --output <temporärer M0-Pfad> -v minimal`: PASS.
- `node DsaWuerfelApp/DsaWuerfelApp.Tests/Browser/app-workflows.cjs <M0-Publish>`: BASELINE ROT; bestehender Würfelablauf bricht beim zweiten Zauberwurf mit Playwright-Timeout ab, weil ein offenes Detail-`summary` den Wurfbutton überdeckt.
- `node DsaWuerfelApp/DsaWuerfelApp.Tests/Browser/responsive-ui.cjs <M0-Publish>`: BASELINE ROT; vier History-Prüfungen melden bei Tablet/Desktop nur vier sichtbare Einträge.
- `node DsaWuerfelApp/DsaWuerfelApp.Tests/Browser/dice-lifecycle.cjs`: PASS, zehn Navigationszyklen, zwei unabhängige Instanzen, keine offenen Frames/Listener.
- `node --check DsaWuerfelApp/DsaWuerfelApp.Tests/Browser/*.cjs`: PASS.
- `git diff --check`: PASS.

### Bekannte Baseline-Mobs

Die acht roten .NET-Tests wurden ohne eigene Änderungen reproduziert:

- `TalentProbeEvaluatorTests.Spell_calculation_keeps_zfw_and_applies_pre_roll_zfp_separately`
- `TestApplicationSmokeTests.Spell_option_modifier_is_resolved_from_catalog_and_keeps_original_zfw`
- sechs `RollHistoryContextTests` zu Talent-, Zauber-, Attribut- und Schlechte-Eigenschaft-Historien.

Sie liegen außerhalb dieses UI-Auftrags und werden nicht abgeschwächt oder übersprungen. Eine gültige anonymisierte Importdatei sowie native iOS-/Android-Dateiauswahl sind nicht verfügbar; diese Nachweise bleiben für die spätere Abnahme offen.

### Loot / Commit

- Geänderte Datei: `docs/ai/management-ui-refactor-progress.md`
- Commit: `05fa236`

## M1 – Boss: Goldene Navigation

Status: TEILWEISE

Änderungen:

- `DsaWuerfelApp/DsaWuerfelApp.Client/wwwroot/app.css`: sechs gemeinsame Verwaltungs-/Navigations-Tokens ergänzt; vorhandene Dice-Tokens unverändert gelassen.
- `DsaWuerfelApp/DsaWuerfelApp.Client/Layout/NavMenu.razor.css`: dunkle Panel-Fläche ohne Schatten, Goldrahmen, Gold-Hover, sichtbare Gold-Fokusumrisse und weiße aktive Links/Icons umgesetzt. Sidebar-Breiten und 640/641px-Vertrag unverändert.

### Besiegte Mobs

- Desktop-/Mobile-Navigationsmarkup blieb unverändert; die bestehende eine NavMenu-/SessionTree-Instanz wurde weiterverwendet.
- Aktive Links behalten die Goldfläche, während Text und Icons weiß bleiben. Die frühere blaue aktive Icon-/Textregel ist entfernt.
- `dotnet build DsaWuerfelApp.sln -c Release -v minimal`: PASS, 0 Warnungen, 0 Fehler.
- `dotnet test DsaWuerfelApp/DsaWuerfelApp.Tests/DsaWuerfelApp.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~ClientStateTests`: PASS, 18/18.
- Frischer Release-Publish nach temporärem M1-Pfad: PASS.
- `node DsaWuerfelApp/DsaWuerfelApp.Tests/Browser/responsive-ui.cjs <M1-Publish>`: vier Routen wurden durchlaufen; Ergebnis BASELINE ROT mit denselben vier History-Prüfungen wie M0 (Tablet/Desktop nur vier sichtbare Einträge).

### Offene Mobs

- Eine vollständig grüne Responsive-Matrix ist wegen des vorbestehenden History-Fehlers nicht erreicht.
- Die breitere Desktop-/Mobileabnahme der Verwaltungsseiten folgt mit den jeweiligen Markupänderungen in M2 bis M5.

### Loot / Commit

- Commit: wird nach dem lokalen M1-Commit im nächsten Raidabschnitt oder im Abschlussbericht ergänzt.

## M2 – Boss: SessionTree und seine Wächter

Status: AUSSTEHEND

## M3 – Boss: Die Lobby ohne Labyrinth

Status: AUSSTEHEND

## M4 – Boss: Heldenverwaltung und Importtor

Status: AUSSTEHEND

## M5 – Endboss: Responsive und integrierte Abnahme

Status: AUSSTEHEND

## Offene Abnahme nach M0

- Die vier bestehenden Browser-Baselinefehler und acht .NET-Baselinefehler sind vor Produktänderungen reproduziert.
- Gültiger echter Heldenimport mit anonymisierter Datei fehlt.
- Native iOS-/Android-Dateiauswahl und physische Touchprüfung fehlen.
- Kein Push, PR oder Deployment ausgeführt.
