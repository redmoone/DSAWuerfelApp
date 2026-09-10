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

- Commit: `f563f95`

## M2 – Boss: SessionTree und seine Wächter

Status: GEPRÜFT

Änderungen:

- `DsaWuerfelApp/DsaWuerfelApp.Client/Components/SessionTree.razor`: Einträge werden nach Session-ID gekeyt, erhalten eindeutige Details-/Editor-IDs, sichtbare Spieler-/Online-Zahlen, `Öffnen`/`Weiterwürfeln`, Details-Toggle, Mitgliederstatus und getrennte Verwaltungsbereiche. Join-Code und Sessionaktionen liegen im aufgeklappten Body.
- `DsaWuerfelApp/DsaWuerfelApp.Client/Components/SessionTree.razor.cs`: einmaliges Autoexpand pro aktiver Session, Busy-Guard, lokale Feedback- und Bestätigungszustände, Ziel-/Rechteprüfung vor Aktionen, editierte Entwürfe bei gleichem Sessionupdate erhalten, direkte Navigation für die aktive Session und Clipboard-Fehler ohne Browserdialog.
- `DsaWuerfelApp/DsaWuerfelApp.Client/Components/SessionTree.razor.css`: dunkle Panels, 1px-Goldrahmen, kompakte Wrapping-Regeln, sichtbare Status-/Fehlertexte, 44px-Aktionsziele sowie Fokus ohne Schatten/Glow/Translation.
- `DsaWuerfelApp/DsaWuerfelApp.Tests/Browser/app-workflows.cjs`: Session-Strip-Selektor auf den Sessioneintrag und Verlassen auf die Inline-Bestätigung umgestellt.
- `DsaWuerfelApp/DsaWuerfelApp.Tests/Browser/management-ui.cjs`: neuer isolierter Playwright-Workflow für SessionTree-Interaktionen.

### Besiegte Mobs

- `dotnet build DsaWuerfelApp.sln -c Release -v minimal`: PASS, 0 Warnungen, 0 Fehler.
- `dotnet test DsaWuerfelApp/DsaWuerfelApp.Tests/DsaWuerfelApp.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~ClientStateTests`: PASS, 18/18.
- `dotnet publish DsaWuerfelApp/DsaWuerfelApp/DsaWuerfelApp.csproj -c Release --output <temporärer M2-Pfad> -v minimal`: PASS.
- `node --check` für `app-workflows.cjs` und `management-ui.cjs`: PASS.
- `node management-ui.cjs <M2-Publish>`: PASS; aktiver Sessioneintrag, einmaliges Autoexpand, manuelles Zuklappen trotz Fremdupdate, Session-/Spielerumbenennung sowie Abbruch von Löschen/Verlassen geprüft.
- `node app-workflows.cjs <M2-Publish>`: BASELINE ROT an derselben offenen Würfel-Detailfläche wie M0; SessionTree-spezifische Schritte wurden bis dahin nicht erreicht.
- `git diff --check`: PASS.

### Offene Mobs

- Die vollständige Würfel-/Workflowabnahme bleibt wegen des bekannten, vorbestehenden Playwright-Timeouts offen.
- Die neue Managementprüfung deckt die lokalen SessionTree-Szenarien ab; Fehlerfälle mit realem Server-Rechteverlust während eines laufenden Requests bleiben eine spätere integrierte Abnahme.

### Loot / Commit

- Commit: `a087130`

## M3 – Boss: Die Lobby ohne Labyrinth

Status: TEILWEISE

Änderungen:

- `DsaWuerfelApp/DsaWuerfelApp.Client/Pages/Lobby.razor`: Marketingübersicht, Statistikduplikate und aktiver Rundenstreifen entfernt. Kopf, Konto/Abmelden, `Meine Sessions (N)` und das gemeinsame Beitreten-/Erstellen-Panel bilden jetzt die direkte DOM-Reihenfolge. Auth bleibt im selben File; Join/Create verwenden je einen Submitpfad.
- `DsaWuerfelApp/DsaWuerfelApp.Client/Pages/Lobby.razor.cs`: Submit-Guard für Join/Create, Magic-Link- und Logout-Guards, Nutzer-/Session-Kontext für Namensentwürfe, Dirty-Schutz und stale-response-Schutz ergänzt. Nicht mehr gerenderte Präsentationsproperties entfernt.
- `DsaWuerfelApp/DsaWuerfelApp.Client/Pages/Lobby.razor.css`: dunkle Würfelpalette, 1600px-Shell, 3fr/2fr-Grid, Reflow bei 900px, kompakte anonyme Anmeldung, Fokusumrisse und natürliche Dokumenthöhe umgesetzt.
- `DsaWuerfelApp/DsaWuerfelApp.Tests/Browser/browser-fixture.cjs`: Loginbereitschaft auf `Abmelden` synchronisiert.

### Besiegte Mobs

- `dotnet build DsaWuerfelApp.sln -c Release -v minimal`: PASS, 0 Warnungen, 0 Fehler.
- `dotnet test DsaWuerfelApp/DsaWuerfelApp.Tests/DsaWuerfelApp.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~ClientStateTests`: PASS, 18/18.
- `dotnet publish DsaWuerfelApp/DsaWuerfelApp/DsaWuerfelApp.csproj -c Release --output <temporärer M3-Pfad> -v minimal`: PASS.
- `node management-ui.cjs <M3-Publish>`: PASS; Session erstellen/beitreten, Lobby-Sessionliste, Sessiondetails und lokale Verwaltungsabläufe geprüft.
- `node app-workflows.cjs <M3-Publish>`: BASELINE ROT erst beim bestehenden Würfel-Detail-Timeout; Login, Session erstellen, zwei Beitritte und drei Heldenzuordnungen bestanden davor.
- `node responsive-ui.cjs <M3-Publish>`: BASELINE ROT mit unveränderten vier History-Sichtbarkeitsfehlern; keine zusätzlichen Lobby-/Horizontalscrollfehler.
- `node --check` für geänderte Browser-Skripte: PASS.
- `git diff --check`: PASS.

### Offene Mobs

- Die vollständige Workflow-/Responsiveabnahme bleibt bis zur Behandlung der vorbestehenden Dice-Baselinefehler offen.
- Die neue 899/900/901px-Verwaltungsgeometrie und die gezielten Draft-/Doppelsubmit-Prüfungen werden in M5 weiter ausgebaut.

### Loot / Commit

- Commit: `eed9cc4`

## M4 – Boss: Heldenverwaltung und Importtor

Status: TEILWEISE

Änderungen:

- `DsaWuerfelApp/DsaWuerfelApp.Client/Pages/HeldenVerwaltung.razor`: kosmetischen `SelectedHero`-Zustand entfernt; kompakte Heldenzeilen, eindeutiger Aktivstatus, Inline-Bestätigung, Empty State und direkt erreichbare Importfläche umgesetzt.
- `DsaWuerfelApp/DsaWuerfelApp.Client/Pages/HeldenVerwaltung.razor.cs`: Lade-/Retry-Zustand, Busy-Guard, 15-Dateien-/5-MiB-Prüfung, Teilakzeptanz, sichere Aktivierung/Löschung, stale-response-Schutz und bestehende Dropzone-Lifecycle-Logik zusammengeführt.
- `DsaWuerfelApp/DsaWuerfelApp.Client/Pages/HeldenVerwaltung.razor.css`: Würfelpalette, 1600px-Shell mit 3fr/2fr-Desktopraster, Reflow bei 900px, kompakte Zeilen, sichtbare/fokussierbare native Dateiauswahl und schattenfreie 1px-Goldrahmen umgesetzt. Die Blazor-`InputFile`-Isolation wird gezielt über `::deep` adressiert.
- `DsaWuerfelApp/DsaWuerfelApp.Client/wwwroot/js/hero-dropzone.js`: programmgesteuerte Drops während eines Imports werden wie der native Dateischalter gesperrt.
- `DsaWuerfelApp/DsaWuerfelApp.Tests/Browser/browser-fixture.cjs` und `management-ui.cjs`: optionale zweite Testhelden sowie fokussierte Helden-/Importvalidierungsprüfungen ergänzt; Standardfixtures bleiben unverändert.

### Besiegte Mobs

- `dotnet build DsaWuerfelApp.sln -c Release -v minimal`: PASS, 0 Warnungen, 0 Fehler.
- `dotnet test DsaWuerfelApp/DsaWuerfelApp.Tests/DsaWuerfelApp.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~ClientStateTests`: PASS, 18/18.
- Frischer Release-Publish nach `C:\Users\Bosko\AppData\Local\Temp\dsawuerfel-management-m4b-411bbfaeeec94f0a9f5d40ca39b22cbf`: PASS.
- `node management-ui.cjs <M4b-Publish>`: PASS; SessionTree-Refreshschutz sowie Heldenaktivierung, Abbruch und Bestätigung des Entfernens, aktiver Held ohne Ersatz, ungültige Endung und 16-Dateien-Grenze geprüft.
- `node responsive-ui.cjs <M4b-Publish>`: BASELINE ROT mit ausschließlich den vier bekannten Dice-History-Fehlern (Tablet/Desktop nur vier sichtbare Einträge). Der neue 320px-Helden-Overflow wurde behoben und wird nicht mehr gemeldet.
- `node app-workflows.cjs <M4b-Publish>`: BASELINE ROT erst beim bestehenden offenen Würfel-Detail-Timeout; Login, Session erstellen, zwei Beitritte, drei Heldenzuordnungen und Bad-Trait-Workflow bestanden davor.
- `node --check` für `browser-fixture.cjs`, `management-ui.cjs`, `app-workflows.cjs`, `responsive-ui.cjs` und `hero-dropzone.js`: PASS.
- `git diff --check`: nach Abschluss dieses Pakets noch auszuführen.

### Offene Mobs

- Ein echter erfolgreicher Import mit anonymisierter gültiger HLD/XML/ZIP-Datei fehlt weiterhin; die UI-Fehlerpfade sind geprüft, der Parsernachweis bleibt offen.
- Native iOS-/Android-Dateiauswahl, physische Touchbedienung und Tastatur-/Dropnachweis auf echten Geräten fehlen.
- Die vier bekannten Dice-History-Baselinefehler und der bestehende offene Detail-Timeout bleiben unverändert.

## M5 – Endboss: Responsive und integrierte Abnahme

Status: AUSSTEHEND

## Offene Abnahme nach M0

- Die vier bestehenden Browser-Baselinefehler und acht .NET-Baselinefehler sind vor Produktänderungen reproduziert.
- Gültiger echter Heldenimport mit anonymisierter Datei fehlt.
- Native iOS-/Android-Dateiauswahl und physische Touchprüfung fehlen.
- Kein Push, PR oder Deployment ausgeführt.
