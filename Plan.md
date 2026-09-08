# Ausführungsplan für GPT Luna

> **Aktuelle Uebergabe vom 08.09.2026:** Alte ERLEDIGT-Eintraege sind historische Meldungen. Massgeblich fuer den aktuellen Stand und die begrenzten Restarbeiten ist Abschnitt 11. P06/P08 sind implementiert und getestet; die komplette Browserabnahme P20 bleibt offen. Nicht alle Pakete erneut bearbeiten.

Stand: 07.09.2026. Grundlage: Code-Review und erneute Prüfung der aktuellen Arbeitskopie.
Dieses Dokument ist ein Implementierungsauftrag, kein Nachweis bereits erledigter Korrekturen.
Bei seiner Erstellung wurde ausschließlich diese Datei hinzugefügt.

## 1. Ziel und Ausführungsmodus

Zuerst den WIP sichern, danach konkrete Fehler mit Regressionstests korrigieren und erst anschließend die Kampfseite weiterentwickeln. Kein umfassendes Vorab-Refactoring.

Luna führt die unten festgelegten technischen Entscheidungen aus. Es soll nicht bei jedem Schritt erneut eine Architektur entwerfen. Fachlich offene Fragen sind ausdrücklich ausgespart oder durch eine sichere Zwischenlösung ersetzt.

### Startauftrag zum Kopieren

> Lies Plan.md vollständig. Bearbeite anschließend ausschließlich das von mir benannte Paket, beginnend mit P00. Beachte dessen Voraussetzungen, Dateigrenzen, Implementierungsschritte und Abnahmetests. Lies nur den dafür nötigen weiteren Code. Verändere keine fachlichen Regeln außerhalb des Pakets. Aktualisiere nach Abschluss den Fortschritt und berichte über Änderungen, tatsächlich ausgeführte Tests und offene Punkte. Fahre nur mit weiteren Paketen fort, wenn ich diese ebenfalls beauftragt habe. Ein unbestätigter Review-Befund ist zu dokumentieren, nicht vorsorglich umzubauen.

### Regeln für jedes Paket

1. Aktuellen Git-Status und den Abschnitt des Pakets lesen. Vorhandene Nutzeränderungen erhalten.
2. Genannte Dateien und ihre direkten Aufrufer mit `rg` prüfen. Zeilen aus dem Review sind historische Orientierung; Symbole sind maßgeblich.
3. Den beschriebenen Fehler möglichst mit einem gezielten Regressionstest reproduzieren. Keine roten Tests auf dem gemeinsamen Integrationsstand hinterlassen.
4. Nur die kleinste hier beschriebene Änderung umsetzen. Compile-Fixes direkter Aufrufer gehören dazu.
5. Pakettests und Release-Build ausführen. Nach gemeinsam genutzten Verträgen zusätzlich alle Tests ausführen.
6. Diff prüfen: keine Geheimnisse, Artefakte, zufälligen Formatierungen oder Änderungen der Kampfregeln.
7. Fortschritt in Abschnitt 10 ergänzen. Bei beauftragten Commits einen Commit pro abgeschlossenem Paket erstellen; Commit-Hash im Abschlussbericht nennen, nicht selbstreferenziell in demselben Commit speichern.
8. Fehlschläge ehrlich angeben. Ein Exitcode 0 ohne entdeckte Tests ist kein bestandener Verhaltenstest.

**Keine automatische Veröffentlichung:** Diese Datei allein autorisiert weder Push noch Merge, Deployment oder Änderungen an produktiven Daten. Lokale Branches und reversible Änderungen sind Teil der beauftragten Implementierung. Eine spätere ausdrückliche Push-Freigabe gilt weiter; nicht erneut fragen. Niemals Force-Push oder Merge nach main aus diesem Plan ableiten.

**Bei Abweichungen:** Kleine Anpassungen an umbenannte Dateien oder Signaturen selbst vornehmen. Bei widersprechenden Produktregeln, unklarer Datenzuordnung oder nötigem Architekturwechsel das betroffene Paket mit konkretem Entscheidungspunkt zurückgeben. Nicht ganze unabhängige Pakete deshalb blockieren.

## 2. Projektkontext

Repository: `C:\Users\Bosko\RiderProjects\DsaWuerfelApp`; Shell: PowerShell.

Pfadkürzel in diesem Dokument, jeweils relativ zum Repository:

| Kürzel | Pfad |
| --- | --- |
| S | `DsaWuerfelApp/DsaWuerfelApp` |
| C | `DsaWuerfelApp/DsaWuerfelApp.Client` |
| H | `DsaWuerfelApp/DsaWuerfelApp.Shared` |
| T | neues `DsaWuerfelApp/DsaWuerfelApp.Tests` |

- Solution: `DsaWuerfelApp.sln`; alle drei bestehenden Projekte verwenden `net10.0`.
- ASP.NET Core Server mit Controllern, Cookie-Authentifizierung und SignalR; Blazor-WASM-Client; EF Core/SQLite.
- Projektgraph: Server → Client und Shared; Client → Shared. Keine neuen Produktionsprojekte vorsehen.
- Serverregistrierungen und Schema-/Reimportstart: `S/Program.cs`.
- Scoped: `HeroDbContext`, `HeroContextReader`, Handler, Workflow, MagicLinkService.
- Singleton: SessionService, SessionRuntimeState, SessionRecordStore und zahlreiche fachliche Berechnungsdienste.
- SessionService schützt seinen Laufzeitzustand mit `_syncRoot`; keine asynchronen Arbeiten innerhalb eines `lock` einführen und keine scoped DbContexts in Singletons speichern.
- Benutzer-ID: `ClaimTypes.NameIdentifier`; AuthController erzeugt den GUID-String im Format `N`. Eigentümer und Sitzungsteilnehmer verwenden Strings.
- DTOs: `H/WuerfelContracts.cs`, `H/SessionContracts.cs`, `H/AuthContracts.cs`.
- DiceController und GameHub sind `[Authorize]`; das ersetzt keine Objekt- oder Meisterberechtigung.
- `HeroContextReader` nutzt bisher `IHttpContextAccessor` nur beim Laden des eigenen aktiven Helden; explizite IDs umgehen die Eigentümerprüfung.
- Master-Request-DTOs enthalten bislang Targets mit UserId, Namen und HeroId, aber keine SessionId.
- Aktive Sitzungshelden liegen im Laufzeitzustand. `SessionParticipantRecord` speichert diese Felder nicht.
- JS: Three.js in `C/wwwroot/js/dice3d.js`, Initialisierung in `dice-scene.js`; C#-Interop in `C/Components/Dice3D.razor.cs`.
- Laufzeitadressen aus launchSettings: HTTP `http://localhost:5206`, HTTPS `https://localhost:7283`.

### Wichtige Ausführungspfade

1. Würfelseite → WuerfelFacade → WuerfelRollCommandDispatcher → HTTP-API oder GameClient/SignalR.
2. DiceController → DiceWorkflowService → einzelner Handler → HeroContextReader/Probenauflösung/Berechnung.
3. GameHub → GameSessionRollPipeline → Sitzungsprüfung → Würfeln → Historie → Gruppenversand.
4. SessionsChanged → SessionState lädt Details → ActiveSessionChanged → Würfelseite lädt Kontext → WuerfelState.ApplyContext setzt Eingaben zurück.
5. Login-Anfrage → MagicLinkService speichert Token-Hash → Mailversand; Verifikation → Tokenverbrauch + Benutzeranlage → SignInAsync → Redirect.

### Review-Baseline und Grenzen

- Release-Build erfolgreich; NU1903 für transitives `SQLitePCLRaw.lib.e_sqlite3 2.1.11`.
- `dotnet test` erfolgreich beendet, aber keine Tests/Testprojekte vorhanden.
- Formatprüfung meldete WHITESPACE/FINALNEWLINE. Keine fachlichen Änderungen daraus ableiten.
- Publish scheiterte lokal mit NETSDK1152 an vorhandenen, ignorierten `artifacts/verify-build`-Verzeichnissen in Server/Client. Kein bewiesener Clean-Checkout-Fehler.
- Anwendung nicht gegen bestehende DB gestartet: Startup führt EnsureCreated, manuelle Schemaänderungen und Helden-Reimport aus.
- Reviewfehler ausdrücklich verworfen: `XDocument.Descendants("daten")` findet beim XDocument auch das Wurzelelement. Hier keinen XML-Fix vornehmen.
- Katalog-JSONs wurden in der MSBuild-Inhaltsauswahl gefunden. Aus `_ContentIncludedByDefault Remove` allein keinen fehlenden Publish-Inhalt ableiten.
- Die Kampfseite ist WIP. Mockdaten oder noch fehlende Kampfregeln sind kein Auftrag, neue Regeln zu erfinden.

## 3. Festgelegte Entscheidungen

### D01 – Umfang

Produktionsstruktur erhalten. Keine Mediator-/CQRS-Bibliothek, kein allgemeines Policy-Framework, kein Eventbus, keine generische Repository-Basisklasse und keine verteilte Sitzungsarchitektur hinzufügen. Schema-Migrationen sind ein separates späteres Paket.

### D02 – Rechte und vertrauenswürdige Identität

- Identität ausschließlich aus authentifiziertem HTTP-Principal beziehungsweise `GameHub.Context.User` gewinnen.
- Den ermittelten `string userId` explizit vom Transport durch Workflow/Handler zum HeroContextReader reichen. Kein UserId-Feld aus einem Request als Aufrufer verwenden; kein neues Ambient-Context-System.
- Eigenen Helden darf sein Eigentümer lesen und für normale Würfe verwenden.
- Meister darf im explizit benannten Sitzungskontext die aktiven Helden der Teilnehmer lesen und Meisterwürfe für diese Teilnehmer ausführen.
- Normale Spielerwürfe bleiben Eigentümeraktionen, auch wenn der Benutzer in einer Sitzung Meister ist. Fremde Helden nur über ausdrücklich geprüfte Meisteroperationen.
- Der Server prüft zusätzlich, dass die aktive HeroId wirklich dem Zielspieler gehört. Laufzeitzuordnung allein ist kein Eigentumsnachweis.
- Fehlender Login: 401 durch Authentifizierung; verbotene Meisteroperation: 403; fremder oder fehlender Held: einheitlich 404, um keine Existenzinformation preiszugeben; ungültige Form: 400.

Diese Rechte sind die konkrete Implementierungsauslegung der vorhandenen Meisteroberfläche. Falls der Nutzer ausdrücklich andere Rechte nennt, das Rechtepaket anpassen; nicht ohne Anlass weitere Rollen ergänzen.

### D03 – Verdeckte Würfe

Sichere Zwischenlösung: Funktion sichtbar deaktivieren und serverseitig Requests mit `IsHidden == true` vor Berechnung, Speicherung und Versand zurückweisen. Meldung: `Verdeckte Würfe sind derzeit nicht verfügbar.`

Kein stilles Umschalten auf öffentlich. Kein neues Historienmodell implementieren, solange nicht entschieden ist, ob nur der Würfelnde, der Meister oder beide lesen dürfen. Die spätere vollständige Funktion ist außerhalb dieses Reparaturplans.

### D04 – Würfelgrenzen

Technische Planvorgabe, keine DSA-Regel: maximal 100 Würfel insgesamt pro freiem Wurf, 1–100 pro Gruppe, Seitenzahl 2–1.000.000. Damit bleibt auch die Summe einschließlich des vorhandenen ±999-Modifikators im Int32-Bereich. Nullgruppen und leere Listen sind ungültig. Konstanten einmal in DiceService definieren; keine allgemeine konfigurierbare Limit-Infrastruktur.

Die Gesamtgrenze ist eine bewusst vorgeschlagene neue technische Beschränkung. Im Abschlussbericht ausdrücklich nennen. Wenn bestehende dokumentierte Nutzung mehr verlangt, mit dieser konkreten Abweichung zurückkommen; nicht eigenständig neue Werte wählen.

### D05 – Daten und Schema

- Niemals bestehende Nutzer-/Helden-Datenbanken löschen, überschreiben oder für Tests starten.
- Verwaiste Helden bleiben ohne Eigentümer. Kein automatisches Erraten, keine Adminoberfläche in diesem Plan.
- Bei mehreren aktiven Helden eines Eigentümers keine zufällige Auswahl als Datenbereinigung. Konflikt vor Schemaänderung erkennen und mit klarer Diagnose abbrechen; eine explizite Datenentscheidung ist ein separater Auftrag.
- Aktuelle manuelle Schema-Helfer zunächst erhalten. Jede neue DB-Invariante sowohl im EF-Modell für neue DBs als auch im Upgradepfad für vorhandene DBs abbilden und testen.

### D06 – Fehler und Transport

- Erwartete Ablehnungen serverseitig durch eine kleine `RequestRejectedException` mit Reason `Validation`, `Forbidden` oder `NotFound` ausdrücken. Keine ASP.NET-Typen in fachlichen Berechnungen.
- Bestehende reine Evaluatoren dürfen ArgumentException für Programmier-/Eingabeverträge behalten. Nur an fachlichen Requestgrenzen in erwartete Ablehnungen übersetzen, nicht jeden ArgumentException global als Benutzerschuld behandeln.
- HTTP bildet die drei Reasons auf 400/403/404 und die bisher vom Client gelesene Fehlerform ab; Payload-Kompatibilität am API-Client prüfen.
- Hub übersetzt nur erwartete Ablehnungen in HubException mit freigegebenem Text. Unerwartete Fehler zentral protokollieren; keine internen Exception-Texte an Clients.
- Keine gewählte Sitzung → API. Gewählte Sitzung ohne Verbindung → verständliche Ablehnung, kein API-Fallback und keine automatische Wiederholung eines Würfelwurfs.

### D07 – Testwerkzeuge

Ein neues xUnit-Testprojekt T mit TargetFramework net10.0; stabile Paketversionen aus dem installierten Template beziehungsweise passend zum bestehenden ASP.NET 10 verwenden, keine Preview-Pakete. Server- und Clientreferenz im Testprojekt sind erlaubt; kein Produktionsprojekt darf T referenzieren.

SQLite-Tests nutzen eigene temporäre Dateidatenbanken und getrennte DbContexts pro konkurrierendem Aufruf. EF-InMemory ist kein Ersatz für Transaktionen/Constraints. Clientdienste mit vorhandenen Interfaces und kleinen handgeschriebenen Fakes testen. Keine Mocking-Bibliothek oder bUnit einführen, solange Diensttests ausreichen.

HTTP-Integrationstests verwenden WebApplicationFactory mit Testauthentifizierung ausschließlich im Testprojekt, Fake-Mailversand, temporären DB- und DataProtection-Pfaden und korrektem ContentRoot. Für SignalR eine echte Testclient-Verbindung über TestServer/LongPolling verwenden, sobald benötigt; keine WebSockets-Unterstützung von TestServer voraussetzen.

## 4. Prüfkommandos

Vom Repository-Root ausführen, jedes Kommando separat. Paketfilter an tatsächlich implementierte Testklassennamen anpassen.

```powershell
git status --short
git diff --stat
git diff --cached --stat
dotnet build DsaWuerfelApp.sln -c Release -v minimal
dotnet test DsaWuerfelApp.sln -c Release --no-restore -v normal
dotnet test DsaWuerfelApp/DsaWuerfelApp.Tests/DsaWuerfelApp.Tests.csproj -c Release --filter FullyQualifiedName~HeroAccessTests
dotnet list DsaWuerfelApp/DsaWuerfelApp/DsaWuerfelApp.csproj package --vulnerable --include-transitive
git diff --check
```

Publish in einen neuen temporären Pfad außerhalb des Repository, nicht in ein Projekt-Unterverzeichnis. PowerShell-Beispiel:

```powershell
$reviewPublishPath = Join-Path ([System.IO.Path]::GetTempPath()) ('dsa-publish-' + [guid]::NewGuid().ToString('N'))
dotnet publish DsaWuerfelApp/DsaWuerfelApp/DsaWuerfelApp.csproj -c Release --output $reviewPublishPath -v minimal
```

Keine umfassende Formatierung ausführen. Die bekannte Formatprüfung ist zunächst eine dokumentierte Baseline und darf funktionale Pakete nicht in Formatierungs-Rewrites verwandeln.

## 5. Reihenfolge und feste Paketgrenzen

Linear ausführen: P00 → P01 → P02 → P03 → P04 → P05 → P06 → P07 → P08 → P09 → P10 → P11 → P12 → P13 → P14 → P15 → P16 → P17 → P18 → P19 → P20.

Die meisten Pakete ergeben einen Commit. P06 und P07 dürfen wegen atomarer Vertragsumstellung zusammen abgeschlossen werden, wenn sonst die Meisteroberfläche vorübergehend unbenutzbar wäre. Keine sicherheitsrelevanten halbfertigen Zwischenstände veröffentlichen.

### P00 – Vorhandenen WIP sichern

**Dateien:** vorhandener Git-Diff, keine Funktionsänderungen.

1. Branch, Remote und gesamte gestagte/ungestagte Änderungsliste lesen.
2. Die zugleich gestagt gelöschten und unversionierten `C/Pages/Kampf.razor.cs` und `.razor.css` besonders prüfen. Der aktuelle Dateiinhalt muss im Checkpoint landen, nicht versehentlich nur die Löschung.
3. Weitere WIP-Änderungen betreffen AttributePill, Wuerfel-Seite/State/Orchestrierung, Shared-Verträge, Program, Handler und Fachservices. Nicht pauschal als unabhängige Kampfänderung etikettieren.
4. `.editorconfig`, `nuget.config`, `structure.txt` und weitere unversionierte Dateien einzeln auf Zweck und vertrauliche Inhalte prüfen. Keine `git add .`-Aktion ohne diese Prüfung.
5. Sicherungsbranch `wip/pre-review-fixes-YYYYMMDD` mit freiem Suffix erstellen. Zusammenhängenden WIP vollständig committen; bei untrennbaren Änderungen ein ehrlicher gemeinsamer WIP-Commit.
6. Buildstatus festhalten. Nur bei entsprechender Nutzerbeauftragung Branch pushen. Vor Änderungen an Sicherheitskonfiguration keine Veröffentlichung mit einem Deployment verwechseln.

**Abnahme:** WIP wiederherstellbar; keine Geheimnisse/DBs/Artefakte im Commit; Reststatus erklärt. Bestehende unbekannte Dateien nicht löschen.
**Commit:** `chore: checkpoint current combat and dice work`

### P01 – Testprojekt und reine fachliche Baseline

**Dateien:** T, Solution; keine fachlichen Produktionsänderungen.

1. xUnit-Projekt gemäß D07 erstellen, Server referenzieren und Solution ergänzen.
2. `TalentProbeEvaluatorTests` mit expliziten Erwartungen anlegen. Attribute jeweils `[10,10,10]`:
   - Talent 5, Modifikator 0, Würfe `[10,10,10]` → Bestanden, Rest 5.
   - Talent 5, Modifikator 0, `[12,13,10]` → Bestanden, Rest 0.
   - Talent 5, Modifikator 0, `[12,14,10]` → NichtBestanden, Rest -1.
   - Talent 0, Modifikator 2, `[8,8,8]` → Bestanden, effektiver Wert -2.
   - Talent 0, Modifikator 2, `[9,8,8]` → NichtBestanden.
   - `[1,1,20]` → GluecklicherWurf; `[20,20,1]` → Patzer.
   - falsche Anzahl Attribute/Würfe → ArgumentException.
3. Keine DSA-Regelauslegung korrigieren: Dies sind Baseline-Tests des geprüften aktuellen Verhaltens.

**Abnahme:** Mindestens diese acht Fälle werden ausgeführt; Build grün.
**Commit:** `test: add deterministic probe evaluation coverage`

### P02 – Isolierte Integrations-Testumgebung

**Dateien:** T/Infrastructure; S/Program.cs nur für Testzugang (`public partial class Program`) falls nötig.

1. `TestDatabase` mit eigenem temporärem Verzeichnis, SQLite-Verbindungsstring und Cleanup implementieren. Pro Test/Fabrik isolieren; DbContexts vor Cleanup entsorgen.
2. `TestApplicationFactory` hinzufügen. Konfiguration muss VOR Host-Build eigene `ConnectionStrings:HeroesDb`, DataProtection-Pfade und später PublicBaseUrl setzen. Hosting-Hooks sorgfältig prüfen: die Startup-Initialisierung darf niemals zuerst die Entwickler-DB öffnen.
3. Einen Testauth-Handler nur im Testprojekt registrieren. Benutzer-IDs aus kontrollierten Testanfragen; keine Testauth im ausgelieferten Server.
4. Fake für IMagicLinkEmailSender zeichnet Mails im Speicher auf. Keine echten Mails und keine externen Java-Aufrufe.
5. Smoke-Tests: anonymer Dice-Aufruf abgewiesen; authentifizierter Katalogaufruf funktioniert; Datenbankpfad liegt nachweislich im Testverzeichnis.
6. Die Testfabrik muss auch fehlende/fehlerhafte Produktionskonfiguration in späteren Tests gezielt setzen können.

**Abnahme:** Wiederholte Tests verändern weder Entwicklungs-DB noch Schlüsselverzeichnis; keine Netzwerkabhängigkeit des Mailversands.
**Commit:** `test: isolate authenticated integration tests`

### P03 – Erwartete Fehler explizit modellieren

**Dateien:** neu S/Services/Application/Support/RequestRejectedException.cs; S/Controller/DiceController.cs; S/Hubs/GameHub.cs; S/Program.cs; betroffene Master-Handler; T.

1. Exception/Reason nach D06 implementieren. HTTP-Abbildung einmal zentral für Dice-Endpunkte anordnen, nicht pro Methode kopieren. Bestehende allgemeine ASP.NET-Fehlerbehandlung berücksichtigen.
2. Vorhandene `catch (Exception)` in DiceController entfernen/ersetzen, sodass unerwartete Fehler den zentralen 500-Pfad erreichen. Bestehendes Client-Fehlerparsing in `C/Services/Transport/Api` lesen und kompatibel halten.
3. Master-Handler dürfen pro Ziel nur explizit erwartete fachliche Fehler in Target-Ergebnisse umwandeln. Autorisierung der ganzen Anfrage findet künftig VOR der Zielschleife statt. OperationCanceledException darf nicht als Targetfehler enden.
4. Hub nur erwartete Ablehnungen übersetzen; Standardverhalten für interne Fehler ohne detaillierte Fehlermeldungen erhalten.
5. Eingabefehler, die bisher durch InvalidOperationException aus fachlichen Requestprüfungen signalisiert wurden, an diesen konkreten Stellen umstellen. Nicht sämtliche InvalidOperationException global als 400 behandeln.

**Tests:** explizite Validation → 400, Forbidden → 403, NotFound → 404; absichtlich ausgelöster Infrastrukturfehler → 500 ohne internen Text; Abbruch beendet Sammelverarbeitung. Erfolgreiche DTOs unverändert.
**Commit:** `fix: distinguish request rejection from server failures`

### P04 – Verdeckte Würfe sicher deaktivieren

**Dateien:** C/Components/WuerfelActionBar.razor und zugehöriger Zustand; S/Services/Application/Handlers/Roll*Handler.cs; T.

1. Den Toggle deaktivieren und die Meldung aus D03 anzeigen; vorhandenen IsHidden-Zustand nicht einfach still auf öffentlich uminterpretieren.
2. In allen vier betroffenen Requesthandlern (Free/Talent/Attribute/BadTrait) IsHidden vor RNG, Heroabfrage mit Nebenwirkung oder Ergebnisbildung prüfen. Kleine lokale identische Guards sind hier akzeptabel; kein Featureflag-System.
3. Manipulierte HTTP- und Hubanfragen müssen dieselbe Ablehnung erhalten. Master-DTOs enthalten derzeit kein IsHidden; keine neue Meister-Sichtbarkeitsfunktion erfinden.

**Tests:** jede der vier Wurfarten mit true abgewiesen; in Sitzungsfall keine Historienzeile und kein Ergebnisereignis; false funktioniert weiter. Wenn echte Hubtests noch fehlen, jetzt den LongPolling-Testclient aus D07 ergänzen.
**Commit:** `fix: reject unsupported hidden rolls before execution`

### P05 – Freie Würfe vor Allokation validieren

**Dateien:** S/Services/Domain/Rolls/DiceService.cs; betroffener Free-Handler; T/DiceServiceTests.cs.

1. Ein erster Durchlauf validiert alle Gruppen gemäß D04 und summiert mit sicherer Arithmetik. Bei Überschreiten sofort ablehnen; erst danach Listenkapazität anlegen.
2. Count/Sides-Fehler und Nullgruppen sauber an Requestgrenze abbilden. `Sides + 1` muss nach Validierung sicher sein.
3. Erst im zweiten Durchlauf RNG verwenden. Bestehenden kryptografischen Zufall beibehalten.
4. Summe/Modifier im Ergebnispfad prüfen; keine gültige Kombination darf überlaufen.

**Tests:** null/leer; Nullgruppe; Count 0/101/int.MaxValue; Sides 1/1.000.001/int.MaxValue; 2×50 akzeptiert, 51+50 abgewiesen; Ergebnisanzahl und Wertebereich für 100 Würfel. Nicht auf statistische Verteilung testen.
**Commit:** `fix: bound dice requests before allocation`

### P06 – Serverseitige Heldenberechtigung und expliziter Aufrufer

**Dateien:** HeroContextReader, HeroReadRepository/IHeroReadRepository, DiceWorkflowService, zugehörige Handler, DiceController, GameHub, SessionService; T/HeroAccessTests.cs.

1. `userId` als erforderlichen serverinternen Parameter in Workflow/Handler/Reader weiterreichen. HTTP nimmt ihn aus Claims, Hub aus Context.User. Keine optionale Default-ID anbieten. IHttpContextAccessor-Abhängigkeit aus HeroContextReader entfernen.
2. `GetOwnedByIdAsync(heroId, userId, cancellationToken)` zum Repository hinzufügen; SQL filtert Id UND OwnerUserId. Unbeschränkte GetById-Aufrufe dürfen nicht aus Transportpfaden erreichbar bleiben.
3. Reader trennt eigene Zugriffe von explizit sitzungsgebundenen Meister-Lesezugriffen durch benannte Methoden; keinen schwer lesbaren Boolean `skipAuthorization` einführen.
4. SessionService bekommt eine kleine Methode, die unter `_syncRoot` Mitgliedschaft und MasterUserId prüft und einen Snapshot der zulässigen Zielspieler zurückgibt. Keine mutable GameSession als neue Berechtigungs-API herausreichen; keine DB-Abfrage/await im lock.
5. Für Meisterlesezugriff HeroId im Snapshot suchen und anschließend Eigentümer mit Ziel-UserId im Repository prüfen. Normale Rollhandler verwenden ausschließlich Eigentümerzugriff.
6. Eigener aktiver Held ohne ID funktioniert wie bisher. Für fehlenden eigenen aktiven Helden das bisherige Katalog-/Leer-Verhalten erhalten; keine neue NotFound-Pflicht für diesen optionalen Fall.

**Tests:** Rechte-Matrix aus Abschnitt 7. Jede API-/Hub-Route mindestens einem passenden Test zuordnen. Tests müssen bekannten fremden GUID verwenden, nicht nur zufälligen nicht vorhandenen GUID.
**Abhängigkeit:** P07 muss mit abgeschlossen werden, bevor dieser Stand als funktionsfähige Meisteroberfläche freigegeben wird.
**Commit:** `fix: enforce owned and session-scoped hero access`

### P07 – Meisterverträge und ActiveHero mit Serverzustand verbinden

**Dateien:** H/WuerfelContracts.cs; IWuerfelApiClient/WuerfelApiClient; WuerfelContextService/WuerfelFacade; DiceController/Workflow/Handler; GameHub.UpdateActiveHero; T.

1. `SessionId` an ProbeInfoRequestDto und Meister-Request-DTOs ergänzen. Für GetContext expliziten optionalen sessionId-Queryparameter durch alle Schichten ergänzen. CancellationToken bei C#-Methoden zuletzt belassen; alle Konstruktorstellen über `rg` aktualisieren.
2. Meister-Requests ohne nichtleere SessionId ablehnen. Client liest SessionId aus dem aktuellen SessionState und erfasst sie zusammen mit den Zielen für die konkrete Anfrage.
3. MasterRollTargetDto zunächst behalten, um den Umbau zu begrenzen. Target.UserId ist Auswahl-ID; tatsächliche HeroId und Namen aus validiertem Serversnapshot bestimmen. Bei vom Client abweichender HeroId ablehnen und Aktualisierung verlangen, nicht mit einem unerwarteten neuen Helden würfeln.
4. Doppelte oder sitzungsfremde Target-IDs ablehnen. Alle Ziele VOR der ersten Berechnung autorisieren. Fachlich nicht verfügbare Proben dürfen weiterhin pro Ziel erklärt werden.
5. UpdateActiveHero: null löscht die Zuordnung; sonst Eigentümerabfrage für den Hub-Aufrufer und Name aus Hero.Name. `heroName`-Parameter vorerst für Aufruferkompatibilität behalten, aber nicht vertrauen. Löschung/ungültige ID darf keine alte Zuordnung überschreiben.
6. Client- und Serververträge gemeinsam deploybar halten. Keine Unterstützung für beliebige ältere Clients entwickeln; veraltete Requests sicher ablehnen.

**Tests:** Nichtmeister 403; fremde Sitzung abgewiesen; gefälschter Name ohne Einfluss; falsche HeroId abgewiesen; eigener gültiger Hero übernommen; null löscht; gemischte gültige/ungültige Masterziele führen zu keiner Teilberechnung.
**Commit:** `fix: resolve master targets and active heroes on the server`

### P08 – Vertrauenswürdige Login-Basisadresse und lokale Redirects

**Dateien:** MagicLinkAuthOptions, MagicLinkService, AuthController, Program, appsettings.Development.json beziehungsweise vorhandene Konfigurationsdatei; T/AuthRedirectTests.cs.

1. `MagicLinkAuth:PublicBaseUrl` ergänzen. Absolutes http(s), kein UserInfo/Query/Fragment; HTTP nur Development, Produktion HTTPS. Einen PathBase-Pfad bei Linkaufbau erhalten.
2. Produktion ohne gültigen Wert früh mit klarer Konfigurationsmeldung ablehnen. Entwicklungswert `https://localhost:7283`; HTTP-Profil muss den Wert ausdrücklich auf `http://localhost:5206` überschreiben können.
3. Basisadresse innerhalb des konfigurierten Dienstes verwenden; baseUrl-Parameter aus RequestMagicLinkAsync und BuildBaseUrl aus Controller entfernen. Request.Host darf den Link nicht mehr bestimmen.
4. Produktions-AllowedHosts als Deploymentanforderung dokumentieren; keine nicht bekannte Domain eintragen. Öffentliche Domain ist eine echte externe Konfigurationsinformation, kein Grund für Luna, eine zu erfinden.
5. Vor Speicherung Redirectziel mit Framework-Local-URL-Prüfung prüfen, sonst `/`; beim Abschluss nochmals LocalRedirect beziehungsweise validierten Fallback verwenden. Keine eigene URL-Normalisierung schreiben.

**Tests:** Host evil.example verändert Mail-URL nicht; `/`, `/kampf` und lokale Query funktionieren; `//evil.example`, `/\evil.example`, absolute URL abgewiesen; fehlende Produktionsbasisadresse schlägt definiert fehl; bestehender PathBase bleibt im Verifikationslink.
**Commit:** `fix: use trusted magic-link origin and local redirects`

### P09 – Tokenverbrauch atomar machen

**Dateien:** MagicLinkService.VerifyAsync; T/MagicLinkConcurrencyTests.cs.

1. Token hashen und einmal `now` über TimeProvider erfassen.
2. Transaktion beginnen. Als erste schreibende Operation bedingtes UPDATE mit TokenHash, ConsumedAtUtc IS NULL und ExpiresAtUtc > now ausführen; Anzahl geänderter Zeilen muss 1 sein. Keine vorgelagerte ungeschützte Leseentscheidung.
3. Bei 0 Zeilen ungültiges Token zurückgeben. Bei 1 Zeile den Datensatz/Email innerhalb derselben Transaktion laden, Benutzer lesen/anlegen, LastLoginAtUtc setzen und speichern; erst danach Commit und Erfolg.
4. SQLite-Schreibserialisierung nutzen. Keine Prozess-globalen Locks als Ersatz, keine unbegrenzten Retry-Schleifen. Bei nötiger SQLite-Busy-Behandlung nur den gesamten noch nicht committed Vorgang begrenzt wiederholen; bei unerwartetem Providerverhalten Befund dokumentieren statt Ausnahme zu verschlucken.
5. Fehler vor Commit müssen den Verbrauch zurückrollen. Keine Mail-Sendelogik in diesen Schritt ziehen.

**Tests mit getrennter Verbindung pro Aufruf:** gleicher Token parallel → genau ein Erfolg; zwei gültige Tokens derselben noch unbekannten Email parallel → genau ein Benutzer, konsistente Ergebnisse; abgelaufen und bereits verbraucht → null; Gültigkeit exakt bei now → ungültig; simulierter Speicherfehler → kein verbrauchtes Token durch Teilcommit.
**Commit:** `fix: consume magic links transactionally`

### P10 – Keine implizite Eigentumsübertragung beim Start

**Dateien:** Program.EnsureHeroSchema; T/StartupOwnershipTests.cs.

1. UPDATE entfernen, das OwnerUserId dem ersten AuthUser zuweist. Restliche Schemaergänzungen erhalten.
2. Nicht zugewiesene Helden bleiben erhalten und sind über Eigentümer-APIs nicht sichtbar.
3. Kurze Betriebsnotiz ergänzen: Altbestand benötigt explizite Zuordnung mit Backup und bekanntem Eigentümer. Kein selbsttätiges Migrationsskript mit geratenem Benutzer liefern.

**Tests:** temporäre DB mit zwei Benutzern, verwaistem und zugewiesenem Helden zweimal starten; Besitzer unverändert, Daten erhalten. Reimport darf keine Eigentumszuordnung erzeugen.
**Commit:** `fix: preserve unresolved hero ownership during startup`

### P11 – Heldenaktivierung mit DB-Invariante

**Dateien:** HeroesController.ActivateHero; HeroDbContext.OnModelCreating; Program.EnsureHeroSchema; T/HeroActivationTests.cs.

1. Gefilterten Unique-Index auf OwnerUserId für `IsActive = 1 AND OwnerUserId IS NOT NULL AND OwnerUserId <> ''` im EF-Modell und bestehenden Schema-Upgrade ergänzen. Gleicher Indexname und Filter in beiden Pfaden.
2. Vor Einführung bestehende Konflikte erkennen. Bei Konflikt klare Diagnose, keine automatische Auswahl und keine bereits teilweise ausgeführten Datenänderungen.
3. Aktionsablauf in Transaktion: Zielbesitz prüfen, alle aktiven eigenen Helden deaktivieren, Ziel mit eigentümergefiltertem UPDATE aktivieren, betroffene Zeilen prüfen, Commit. Zurückgegebenen Helden frisch laden, damit ChangeTracker keinen veralteten IsActive-Wert liefert.
4. Fehler/Abbruch rollt beide Änderungen zurück. Kein Catch, das DB-Konflikte als erfolgreiche Aktivierung ausgibt.

**Tests:** A→B, B→B idempotent, fremder Held verändert nichts, Fehler zwischen Updates erhält vorherigen Zustand, zwei parallele Ziele ergeben höchstens einen aktiven Helden, alte DB bekommt Index, Konfliktbestand bleibt unverändert mit Diagnose.
**Commit:** `fix: enforce atomic active-hero selection`

### P12 – SQLite-Paketkette korrigieren

**Dateien:** S/DsaWuerfelApp.csproj und nur unmittelbar nötige Paketdateien.

1. `dotnet list ... package --vulnerable --include-transitive` erneut ausführen. Advisory prüfen: https://github.com/advisories/GHSA-2m69-gcr7-jv3q.
2. Releaseinformationen der offiziellen Paketmaintainer heranziehen; keine Paketversion aus dem alten Review erfinden. Dort war <=2.1.11 betroffen, SQLite >=3.50.2 korrigiert, keine gepatchte Version des betroffenen Pakets selbst angegeben.
3. Kleinste offiziell kompatible stabile Kombination wählen. Diese Versionsentscheidung ist bewusst nicht eingefroren, weil Paketverfügbarkeit zeitabhängig ist. Im Bericht exakte gewählte Versionen und Quelle nennen.
4. Nicht nur Warnung unterdrücken oder natives Binary manuell kopieren. Keine unabhängigen MudBlazor-/Frontend-Updates.

**Tests:** alle SQLite-Tests, Release-Build, Paketprüfung, Publish nach P19; native Laufzeitversion über `SELECT sqlite_version()` aus tatsächlicher Verbindung prüfen. Wenn Paketlösung blockiert, konkrete verfügbare Alternativen melden, keine vollständige Erledigung markieren.
**Commit:** `fix: update vulnerable SQLite dependency chain`

### P13 – Verbleibende Sitzungshelden erhalten

**Dateien:** SessionService.LeaveSession; SessionRuntimeState.ReplacePlayers; T/SessionMembershipTests.cs.

1. Vor Ersetzen vorhandene Spieler nach UserId indizieren, unter bestehendem lock.
2. Persistierte Mitgliedschaft/Masterrolle übernehmen; für weiterhin vorhandene IDs ActiveHeroId und ActiveHeroName aus Laufzeitobjekten erhalten. Ausgeschiedene Spieler nicht zurückkopieren.
3. Keine Schemaänderung, keine Persistierung flüchtiger Felder in diesem Paket.

**Tests:** drei Spieler mit Helden, einer verlässt → übrige Zuordnungen gleich; Meister verlässt → neue Rolle korrekt und Hero erhalten; letzter verlässt → Sitzung entfernt; nicht vorhandener Benutzer darf nichts ändern.
**Commit:** `fix: preserve active heroes when session membership changes`

### P14 – Synchronisierung nach Wiederverbindung

**Dateien:** GameClient.HandleReconnected; SessionState.HandleSessionChangedAsync; SessionHeroSyncService; T.

1. Nach erfolgreichem Wiederöffnen derselben Sitzung Details und Historie ausdrücklich neu laden; Vergleich gleicher SessionId darf diesen Refresh nicht überspringen.
2. Im HeroSync nicht nur letzten gesendeten Wert vergleichen: Ist der lokale Held bereits im aktuellen bestätigten eigenen SessionPlayer eingetragen? Wenn nein, erneut senden, auch bei gleichem last-Wert.
3. Pro Sitzung/Held höchstens einen gleichartigen Sync gleichzeitig starten. Nach Abschluss prüfen, ob sich der gewünschte Zustand geändert hat; bei Bedarf den neuesten Zustand senden. Keine unendliche Retry-Schleife, Fehler nicht leer verschlucken.
4. Niemals eine verlassene Sitzung durch verspätetes Reconnect wieder aktivieren. Aktuelle Sitzung/Verbindungsphase vor Ergebnisübernahme prüfen.

**Tests:** reconnect gleiche Sitzung lädt zwischenzeitliche Historie; Serverzustand ohne Hero wird wieder synchronisiert; bestätigter gleicher Hero erzeugt keine Ereignisschleife; Sitzungswechsel während Reconnect bleibt erhalten. Echte Restart-/Zweitclient-Prüfung in P20.
**Commit:** `fix: reconcile session state after reconnect`

### P15 – Überholte Ladeantworten und Busy-Zustand

**Dateien:** SessionState.LoadActiveSessionAsync/ClearActiveSessionAsync; WuerfelContextService; WuerfelUiOperationRunner; WuerfelState; T/ClientStateTests.cs.

1. Pro Ladedienst monoton steigende Version und CancellationTokenSource verwenden; alte Anfrage abbrechen. Parameterwerte vor await erfassen, keine aktuellen veränderlichen MasterTargets erst nachher lesen.
2. Vor JEDEM Ergebnis, Fehler und Folgeeffekt Version und angefragte Sitzung prüfen. Sessionwechsel/Logout/Clear invalidieren laufende Versionen, auch wenn kein neuer Load gestartet wird.
3. Async lokale Speicherung der ausgewählten SessionId gegen Umkehrung absichern: Persistierungen innerhalb SessionState serialisieren und unmittelbar vor dem Schreiben aktuelle Version prüfen. Clear verwendet denselben Pfad. Damit darf ein langsames altes SetItem nicht nach einem neuen Clear wirksam bleiben.
4. ContextService analog zum vorhandenen ProbeInfo-Versionsmuster absichern; Probeinfo beim echten Kontextwechsel invalidieren, damit dessen alte Antwort nicht in den neuen Kontext gelangt.
5. Busy als Anzahl laufender Operationen behandeln; Begin erhöht, finally verringert, sichtbar busy solange >0. Erwartete Cancellation setzt keine Fehlermeldung. Keine einfachen bool-Toggles bei Überlappung.

**Tests mit TaskCompletionSource und RunContinuationsAsynchronously:** A startet/B startet/B endet/A endet → B bleibt; alter Fehler nach neuem Erfolg bleibt unsichtbar; Logout während Load bleibt ausgeloggt; zwei parallele Loads → busy bis beide beendet; alte lokale Persistierung überschreibt neue Auswahl nicht. Keine Sleep-basierten Timingtests.
**Commit:** `fix: discard stale session and dice context responses`

### P16 – Formularzustand nur bei relevantem Wechsel zurücksetzen

**Dateien:** Wuerfel.razor.cs.ApplyMasterTargetSelectionAsync; WuerfelFacade.SetMasterTargetsAsync; WuerfelContextSubscription; WuerfelState.ApplyContext; T.

1. Entscheidung zum Neuladen an EINER Stelle in der bestehenden Orchestrierung bündeln, nicht mehrere Eventfilter mit abweichender Logik ergänzen.
2. Relevanten Kontext als kleinen unveränderlichen Vergleichswert erfassen: SessionId, Meistermodus, eigener HeroId sowie sortierte Zielpaare (UserId, ActiveHeroId). Reihenfolge der Anzeige, Name und Anwesenheit sind kein fachlicher Kontextwechsel.
3. Gleichbleibender Vergleichswert → Ziele/Anzeigenamen dürfen aktualisiert werden, aber kein ApplyContext mit Formularreset.
4. Explizite Hero-Änderung/Neuimport muss Refresh erzwingen können, auch bei gleicher HeroId. Vorhandenen ActiveHeroState-Änderungspfad als solchen Refresh verwenden; nicht ausschließlich nach ID deduplizieren.
5. Bei echtem Kontextwechsel bisherige fachlich notwendige Rücksetzungen erhalten. Kein pauschales Entfernen der Resets aus ApplyContext.

**Tests:** Modifikator, Talentwahl, Würfel und Text bleiben bei Umbenennung/Anwesenheit erhalten; Hero-/Ziel-/Rollenwechsel aktualisiert Kontext; Zielreihenfolge allein löst keinen Reset aus; echter Hero-Refresh bei gleicher ID lädt neu.
**Commit:** `fix: preserve prepared rolls on unrelated session updates`

### P17 – Kein stiller API-Fallback bei Verbindungsverlust

**Dateien:** WuerfelRollCommandDispatcher; beide DispatchStrategies; GameClient; T/DispatchTests.cs.

1. Bestehende Strategien zunächst behalten. API.CanHandle nur wenn KEINE Sitzung ausgewählt ist; Sessionstrategie beansprucht ausgewählte Sitzung unabhängig von Verbindung.
2. Sessionstrategie prüft direkt vor Versand IsConnected. Bei false verständliche Fehlermeldung `Die Verbindung zur Sitzung wird wiederhergestellt. Bitte danach erneut würfeln.`
3. Verbindungsabbruch zwischen Prüfung und Versand führt ebenfalls zu Fehler, niemals API-Retry. Doppelte Würfe vermeiden: keine automatische Wiederholung nach unklarem Sendestatus.
4. Falls Tests einen schmalen Seam brauchen, Transportentscheidung als kleine reine Funktion mit (hasSession, isConnected) extrahieren und zusätzlich realen Requestpfad testen; nicht den gesamten GameClient nur für Mocks virtual machen.

**Tests:** keine Sitzung → API genau einmal; Sitzung+connected → Hub; Sitzung+disconnected → kein Transport, kein Ergebnis; Hubfehler → kein API-Fallback. Spätere Strategiezusammenlegung ist optional, nicht Teil dieses Fixes.
**Commit:** `fix: fail explicitly when session transport is unavailable`

### P18 – 3D-Lebenszyklus an Komponenteninstanz binden

**Dateien:** C/wwwroot/js/dice3d.js, dice-scene.js, C/Components/Dice3D.razor.cs; ggf. DiceViewport.razor.cs für Readiness.

1. Modul exportiert eine Factory, die pro Canvas einen eigenen Zustand und Methoden init/updateDice/rollDice/dispose erzeugt. Renderer, Szene, Modelle, Callback und Frames gehören dieser Instanz. Keine globale aktive Szene.
2. C# hält die JS-Instanzreferenz; erst deren dispose aufrufen, dann JS-Referenzen und DotNetObjectReference freigeben. Disposal idempotent machen.
3. RequestAnimationFrame-IDs für Dauerschleife und Rollanimationen verwalten und abbrechen. Benannte Listener referenzieren und entfernen. Renderer sowie eindeutig besessene Geometrien/Materialien/Texturen freigeben; geteilte Template-Ressourcen nicht beim Entfernen eines einzelnen Klons zerstören. Bei Szenen-Cleanup Sets zur einmaligen Freigabe gemeinsam genutzter Ressourcen verwenden.
4. init nach asynchronem Modellladen muss disposed prüfen; bei bereits entsorgter Instanz geladene Ressourcen freigeben, keinen Loop/Listener starten.
5. Update/Roll vor abgeschlossener Initialisierung darf nicht verloren gehen oder auf undefinierte Szene zugreifen. C# wartet auf ein Initialisierungstask oder JS hält genau die neueste Vorschau bis ready; kein unbeschränktes Queue-System. Navigation/Dispose darf dabei kein Deadlock erzeugen.
6. Parallel laufende alte Rollanimation vor neuer Animation abbrechen. Modellrotationen und optische Gestaltung nicht ändern.

**Abnahme im Browser:** zehnmal Würfelseite öffnen/verlassen; keine verbleibenden Loops pro entsorgter Instanz, keine wachsende Listeneranzahl, keine disposed-.NET-Fehler; Navigation vor Modell-Load-Ende; Vorschau direkt nach Öffnen; zwei Instanzen unabhängig, sofern Testseite verfügbar. Keine neue Testseite ausliefern und keine dauerhaften Debugglobals hinzufügen.
**Commit:** `fix: dispose per-instance dice rendering resources`

### P19 – Reproduzierbarer Publish

**Dateien:** Server-/Client-csproj; T oder dokumentierter Publish-Check.

1. MSBuild Content/None-Items prüfen, die artifacts/verify-build einsammeln. Sowohl Server als auch Client berücksichtigen.
2. `artifacts/**` früh über passende SDK-DefaultItemExcludes ausschließen beziehungsweise konkrete Content/None-Items entfernen, wenn SDK-Importreihenfolge es verlangt. Die wirksame Itemliste prüfen, nicht nur XML schreiben.
3. Echte Data-JSONs, statische Assets und GLB müssen weiterhin veröffentlicht werden. Bestehende DataProtection-Schlüssel/DBs dürfen nicht in der Ausgabe landen.
4. Vorhandene Nutzer-Artefakte nicht zur bloßen Herstellung eines grünen Builds löschen. Publish in neuen externen Temp-Pfad gemäß Abschnitt 4.

**Abnahme:** Publish trotz vorhandener artifacts-Verzeichnisse erfolgreich; keine obj/project.assets.json-Kollision; Kataloge und GLB vorhanden; keine Schlüssel, DBs oder rekursiven Altbuilds. Keine Ausgabe mit Geheimnissen protokollieren, nur Pfad-/Namensprüfung.
**Commit:** `fix: exclude local artifacts from publish inputs`

### P20 – Gesamtabnahme und Übergabe an Kampfentwicklung

**Dateien:** Plan-Fortschritt/Testdokumentation; Produktionscode nur bei neu reproduziertem Defekt in separatem Fix.

1. Release-Build, gesamte Tests, Paketprüfung und externen Publish durchführen. Testanzahl und Einschränkungen festhalten.
2. Zwei oder drei isolierte Testbenutzer verwenden. Login im Testsetup ohne echte fremde Postfächer.
3. Matrix aus Abschnitt 7 und Browserpfade aus Abschnitt 8 prüfen.
4. Review-Abdeckungstabelle mit jedem erledigten Paket abgleichen. Nicht gelöste Punkte ausdrücklich offen lassen.
5. Kampf-WIP mit dem Checkpoint vergleichen: keine unbeabsichtigten Design-/Regeländerungen.

**Abnahme:** Alle notwendigen Pakete bestanden oder ausdrücklich mit konkretem Blocker benannt. Nur bei bestandenem erforderlichem Sicherheits-/Zustandsumfang lautet Empfehlung „Kampfentwicklung fortsetzen“; optionale Formatierung und große Migrationsumstellung dürfen offen bleiben.
**Commit:** `docs: record review remediation verification`

## 6. Bewusst vertagte Arbeiten

- Vollständige verdeckte Würfe: Empfängerregel und spätere Historienberechtigung zuerst fachlich entscheiden; dann eigener Plan einschließlich bestehender Daten.
- Allgemeines EF-Migrationssystem: eigener Auftrag mit Baseline für existierende DBs. Nicht einfach EnsureCreated durch Migrate austauschen.
- Strategien zu einem Dispatcher zusammenführen: optional nach P17, ausschließlich bei kleinerer und klarerer Implementierung.
- Unbenutzte Modelle/Komponenten löschen: nur mit nachgewiesener Nichtverwendung und separatem Cleanupauftrag.
- Einheitliche Formatierung: separater mechanischer Commit, keine Vermischung mit Fehlerkorrekturen.
- Neue Kampfregeln, persistente Kämpfe, Initiative, Schaden, Animationserweiterungen: nicht Teil dieses Plans.
- Verteilte Sitzungen/mehrere Serverinstanzen und globale Performanceoptimierungen: keine Anforderung vorhanden.

## 7. Verbindliche Rechte- und Regressionstestmatrix

Fixture: Benutzer A ist Meister von S1, B Spieler von S1, C Meister von S2. HA gehört A, HB B, HC C. HB ist aktiver Sitzungsheld von B. Alle IDs sind den Tests bekannt.

| Aktion | Erwartung |
| --- | --- |
| Anonym liest Würfelkontext | 401/definierte Auth-Ablehnung, keine Heldendaten |
| B liest HB ohne Sitzung | Erfolg |
| B liest HA ohne Sitzung | 404 |
| B liest HA mit S1 | 404; Mitgliedschaft allein reicht nicht |
| A liest HB mit S1 | Erfolg |
| A liest HB ohne S1 | 404 |
| C liest HB mit S1 | Ablehnung, keine Heldendaten |
| B ruft Masterwurf in S1 auf | 403 |
| A ruft Masterwurf auf HB in S1 auf | Erfolg |
| A mischt Ziel B und sitzungsfremdes C | ganze Anfrage abgewiesen, keine Würfelberechnung |
| B normaler Talent-/Attribut-/BadTrait-Wurf mit HA | 404, keine Historie |
| B setzt HA als eigenen Sitzungshelden | abgewiesen, bisherige Zuordnung bleibt |
| B setzt HB mit gefälschtem Namen | Servername von HB maßgeblich |
| Beliebiger IsHidden=true-Wurf | abgewiesen, kein Versand/keine Historie |
| Zwei gleichzeitige Verifikationen desselben Tokens | genau ein Erfolg |
| Zwei Aktivierungen verschiedener eigener Helden | höchstens ein aktiver Held |
| Spieler verlässt Sitzung | übrige Heldenzuordnungen erhalten |
| Antwort A kommt nach neuer Antwort B | Zustand B bleibt |
| Metadatenereignis während vorbereitetem Wurf | Eingaben erhalten |
| Sitzungswurf während Verbindungsabbruch | kein API-Ersatzwurf |

HTTP- und SignalR-Einstiege getrennt absichern. Ein bestandener Reader-Unit-Test beweist nicht, dass jeder Endpunkt den Reader korrekt verwendet. Mindestens die kritischen Umgehungsfälle über echte Transport-Einstiege prüfen.

## 8. Manuelle Endabnahme

1. Eigene Helden importieren/auswählen und normale Würfe ausführen.
2. Sitzung erstellen, zweiter Spieler beitreten, eigene Helden zuordnen.
3. Meister liest und würfelt für erlaubte Ziele; Spieler kann das nicht.
4. Wurf vorbereiten, anderer Spieler wird umbenannt/getrennt: Auswahl bleibt.
5. Dritter Spieler verlässt: übrige Helden bleiben.
6. Verbindung unterbrechen, Wurf auslösen: sichtbarer Fehler, kein lokaler Ersatzwurf. Danach reconnect und frische Historie prüfen.
7. Testserver neu starten, bestehende Clients reconnecten lassen: Mitgliedschaft, Historie und aktive Helden werden konsistent wiederhergestellt.
8. Mehrfach zwischen Kampf-/Würfel-/Lobbyseite navigieren; keine 3D-Lebenszyklusfehler.
9. Kampfseite mit gesichertem WIP vergleichen; unfertige Funktionen weiterhin als WIP behandeln.

Browserprüfungen gelten nur als ausgeführt, wenn sie tatsächlich ausgeführt wurden. Fehlende Browserumgebung im Abschlussbericht nennen; keinen Erfolg aus reinem Build ableiten.

## 9. Zuordnung zum Review

| Reviewbefund | Umsetzung |
| --- | --- |
| 1 Objekt-/Meisterberechtigungen | P03, P06, P07 |
| 2 Verdeckte Würfe | P04; vollständige Funktion vertagt |
| 3 Speicher vor Validierung | P05 |
| 4 Login-Link aus Host | P08 |
| 5 Automatische Eigentumszuordnung | P10 |
| 6 SQLite-Schwachstelle | P12, P19 |
| 7 Token-Nebenläufigkeit | P09 |
| 8 Redirect | P08 |
| 9 Heldenverlust beim Austritt | P13, P14 |
| 10 Veraltete Antworten | P15 |
| 11 Formularreset durch Sitzungsereignisse | P16 |
| 12 Stiller Transportwechsel | P17 |
| 13 Heldenaktivierung | P11 |
| 14 JS-Ressourcen | P18 |
| 15 Fehlerbehandlung | P03 |
| 16 Tests fehlen | P01, P02, Tests in jedem Paket |
| 17 Publish-Artefakte | P19 |

## 10. Fortschritt und Übergabeprotokoll

Alle Pakete sind bei Erstellung dieser Datei OFFEN. Das vorangegangene Review ist keine Implementierung.

| Paket | Status | Tatsächlich ausgeführte Prüfungen / Einschränkung |
| --- | --- | --- |
| P00 | ERLEDIGT | Branch/Commit gesichert; Release-Build erfolgreich, NU1903-Warnung blieb bestehen |
| P01 | ERLEDIGT | Testprojekt ergänzt; TalentProbeEvaluatorTests 9/9 erfolgreich, Release-Build erfolgreich |
| P02 | ERLEDIGT | Isolierte Testdatenbank, Testauth, Fake-Mailversand und Smoke-Tests; Gesamt-Testlauf 11/11 erfolgreich |
| P03 | ERLEDIGT | RequestRejectedException und zentrale HTTP-/Hub-Abbildung; 400/500-Transporttests und Gesamt-Testlauf erfolgreich |
| P04 | ERLEDIGT | Verdeckte Würfe UI-seitig deaktiviert und serverseitig in allen vier Handlern vor Ausführung abgewiesen; Tests 17/17 |
| P05 | ERLEDIGT | Zweistufige Dice-Validierung vor Allokation/RNG; Grenzfalltests und Gesamt-Testlauf erfolgreich |
| P06 | ERLEDIGT | Eigentuemer- und expliziter Session-Meisterzugriff; HTTP-/Hub-Tests in HeroAccessTests, siehe Abschnitt 11. |
| P07 | ERLEDIGT | Masterziele vor Berechnung aus Serverzustand aufgeloest; ActiveHero prueft Besitz und verwendet Servernamen. |
| P08 | ERLEDIGT | Validierte PublicBaseUrl, lokale Redirects und Startup-Ablehnung; 14 Testfaelle in AuthRedirectTests. |
| P09 | ERLEDIGT | Tokenverbrauch per bedingtem UPDATE innerhalb einer DB-Transaktion atomar; Gesamt-Testlauf 28 erfolgreich |
| P10 | ERLEDIGT | Startup weist verwaisten Helden keinen AuthUser mehr zu; Build und 28 Tests erfolgreich. Altbestand braucht explizite Zuordnung mit Backup und bekanntem Eigentümer. |
| P11 | ERLEDIGT | Gefilterter Unique-Index und transaktionale Eigentümer-Aktivierung umgesetzt; bestehende Konflikte werden beim Schema-Upgrade diagnostiziert; 28 Tests erfolgreich |
| P12 | ERLEDIGT | EF/ASP.NET-Paketkette auf 10.0.11 aktualisiert; SQLitePCLRaw 2.1.12 wird aufgelöst, Vulnerability-Scan ohne Treffer, 28 Tests erfolgreich |
| P13 | ERLEDIGT | ReplacePlayers übernimmt ActiveHeroId/ActiveHeroName für verbleibende UserId; 28 Tests erfolgreich |
| P14 | IMPLEMENTIERT | Reconnect und Hero-Abgleich umgesetzt; Neustart-Browserpfad bestanden, gesamte Browserabnahme noch offen. |
| P15 | IMPLEMENTIERT | Ladeversionen, Cancellation und serialisierte Speicherung; Clienttests bestanden. Zusaetzlicher Umfang, siehe Abschnitt 11. |
| P16 | IMPLEMENTIERT | Zentraler Kontextvergleich und expliziter Hero-Refresh; Metadaten erhalten vorbereitete Eingaben. |
| P17 | ERLEDIGT | API-Strategie nur ohne Session; ausgewählte Session beansprucht Hub-Transport und meldet Verbindungsverlust explizit; 28 Tests erfolgreich |
| P18 | TEILABNAHME | Instanzgebundene 3D-Ressourcen; isolierter Browser-Lebenszyklustest bestanden, finale Blazor-Navigation noch offen. |
| P19 | ERLEDIGT | artifacts/** aus Server- und Client-Default-Items ausgeschlossen; externer Release-Publish erfolgreich, Kataloge und GLB vorhanden, keine artifacts/DB/Keys im Output |
| P20 | OFFEN | 78 Release-Testfaelle bestanden; finale Browserfassung nicht komplett erfolgreich ausgefuehrt. Restauftrag in Abschnitt 11. |

Nach jedem Paket darunter einen kurzen Eintrag ergänzen:

```text
Paket / Datum:
Bestätigte Ursache:
Geänderte Dateien:
Verhaltensänderung:
Tests (Kommando + Ergebnis + Anzahl):
Build:
Manuell geprüft:
Nicht geprüft / Blocker:
Nächstes zulässiges Paket:
```

Bei Kontextwechsel liest Luna zuerst diesen Plan, den letzten Übergabeeintrag und den aktuellen Git-Status. Keine erledigten Pakete neu beginnen und keine bloß beabsichtigten Tests als bestanden übernehmen.

Paket / Datum: P00 / 07.09.2026
Bestätigte Ursache: Vorhandener WIP war teilweise gestagt, teilweise ungestagt und enthielt zusätzlich unversionierte Dateien; die Kampfdateien waren als Löschung gestagt, lagen aber mit aktuellem Inhalt unversioniert vor.
Geänderte Dateien: Keine fachlichen Änderungen durch P00. Der vorhandene WIP einschließlich Kampfdateien, `.editorconfig`, `nuget.config`, `structure.txt` und `Plan.md` wurde im Checkpoint erfasst.
Verhaltensänderung: Sicherungsbranch `wip/pre-review-fixes-20260907` erstellt; vollständiger WIP im Commit `6f779be` gesichert. Kein Push ausgeführt.
Tests (Kommando + Ergebnis + Anzahl): `dotnet build DsaWuerfelApp.sln -c Release -v minimal` erfolgreich, 0 Fehler, 2 NU1903-Warnungen. Keine Verhaltenstests vorhanden oder ausgeführt.
Build: Release-Build erfolgreich.
Manuell geprüft: Branch, Remote, gestagte/ungestagte/unversionierte Dateien, Kampfdateien, `.editorconfig`, `nuget.config`, `structure.txt`; keine Datenbank- oder Schlüsseldateien im Commit.
Nicht geprüft / Blocker: Kein Push, Publish und keine Browserprüfung; für P00 nicht erforderlich. Die bekannte SQLite-Sicherheitswarnung bleibt offen für spätere Planpakete.
Nächstes zulässiges Paket: P01, nur nach ausdrücklicher Beauftragung.

Paket / Datum: P01 / 07.09.2026
Bestätigte Ursache: Es fehlte ein Testprojekt und eine deterministische fachliche Baseline für den bestehenden TalentProbeEvaluator.
Geänderte Dateien: `DsaWuerfelApp/DsaWuerfelApp.Tests/DsaWuerfelApp.Tests.csproj`, `DsaWuerfelApp/DsaWuerfelApp.Tests/TalentProbeEvaluatorTests.cs`, `DsaWuerfelApp.sln`, `Plan.md`.
Verhaltensänderung: Neues xUnit-Testprojekt für `net10.0` mit Serverreferenz; Tests decken die sieben vorgegebenen Auswertungsfälle sowie falsche Attribut- und Würfelanzahl ab. Keine Produktionslogik geändert.
Tests (Kommando + Ergebnis + Anzahl): `dotnet test DsaWuerfelApp/DsaWuerfelApp.Tests/DsaWuerfelApp.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~TalentProbeEvaluatorTests -v normal` erfolgreich, 9/9. `dotnet test DsaWuerfelApp.sln -c Release --no-restore -v minimal` erfolgreich, 9/9.
Build: `dotnet build DsaWuerfelApp.sln -c Release --no-restore -v minimal` erfolgreich, 0 Fehler, 2 NU1903-Warnungen.
Manuell geprüft: Testprojekt referenziert das Serverprojekt; Testprojekt liegt ohne automatisch erzeugte Solution-Untergruppe in der Solution.
Nicht geprüft / Blocker: Keine zusätzlichen Produktions- oder Integrationstests; für P01 nicht erforderlich. NU1903 bleibt als bekannte Warnung bestehen.
Nächstes zulässiges Paket: P02, nur nach ausdrücklicher Beauftragung.

Paket / Datum: P02 / 07.09.2026
Bestätigte Ursache: Für Integrationsprüfungen fehlte eine isolierte Host-, Datenbank-, DataProtection-, Authentifizierungs- und Mailumgebung.
Geänderte Dateien: `DsaWuerfelApp/DsaWuerfelApp/Program.cs`, `DsaWuerfelApp/DsaWuerfelApp.Tests/DsaWuerfelApp.Tests.csproj`, `DsaWuerfelApp/DsaWuerfelApp.Tests/Infrastructure/TestDatabase.cs`, `TestAuthenticationHandler.cs`, `FakeMagicLinkEmailSender.cs`, `TestApplicationFactory.cs`, `TestApplicationSmokeTests.cs`, `Plan.md`.
Verhaltensänderung: Testfabrik setzt Konfiguration vor dem Host-Build auf eine temporäre SQLite-Datei und einen temporären DataProtection-Pfad. Authentifizierung erfolgt ausschließlich über Testheader; Magic-Link-Mails werden ausschließlich im Speicher aufgezeichnet. Anonyme Dice-Anfragen werden mit 401 abgewiesen, authentifizierte Kataloganfragen liefern 200.
Tests (Kommando + Ergebnis + Anzahl): `dotnet test DsaWuerfelApp/DsaWuerfelApp.Tests/DsaWuerfelApp.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~TestApplicationSmokeTests -v minimal` erfolgreich, 2/2. `dotnet test DsaWuerfelApp.sln -c Release --no-restore -v minimal` erfolgreich, 11/11.
Build: `dotnet build DsaWuerfelApp.sln -c Release --no-restore -v minimal` erfolgreich, 0 Fehler, 2 NU1903-Warnungen.
Manuell geprüft: Datenbankpfad liegt im temporären Testverzeichnis; Entwicklungsdatenbank und Schlüsselverzeichnis werden nicht verwendet; kein externer Mailversand.
Nicht geprüft / Blocker: Keine weiteren Integrations- oder SignalR-Tests; für P02 nicht erforderlich. NU1903 bleibt als bekannte Warnung bestehen.
Nächstes zulässiges Paket: P03, nur nach ausdrücklicher Beauftragung.

Paket / Datum: P03 / 07.09.2026
Bestätigte Ursache: DiceController wandelte sämtliche Exceptions in 400 um; erwartete Ablehnungen waren nicht typisiert und Master-Sammelhandler fingen auch Abbruch- und unerwartete Fehler je Ziel ab.
Geänderte Dateien: `DsaWuerfelApp/DsaWuerfelApp/Services/Application/Support/RequestRejectedException.cs`, `DiceController.cs`, `Program.cs`, `GameHub.cs`, `RollMasterTalentHandler.cs`, `RollMasterAttributeHandler.cs`, `RollTalentHandler.cs`, `DsaWuerfelApp.Tests/RequestRejectionTests.cs`, `Plan.md`.
Verhaltensänderung: Erwartete Ablehnungen tragen Validation/Forbidden/NotFound als Reason und werden zentral auf 400/403/404 mit `{ error }` abgebildet. Unerwartete HTTP-Fehler liefern 500 ohne interne Details. Der Hub übersetzt erwartete Ablehnungen in `HubException`; Sammelhandler reichen `OperationCanceledException` und unerwartete Exceptions weiter.
Tests (Kommando + Ergebnis + Anzahl): `dotnet test DsaWuerfelApp/DsaWuerfelApp.Tests/DsaWuerfelApp.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~RequestRejectionTests -v minimal` erfolgreich, 2/2. `dotnet test DsaWuerfelApp.sln -c Release --no-restore -v minimal` erfolgreich, 12/12.
Build: `dotnet build DsaWuerfelApp.sln -c Release --no-restore -v minimal` erfolgreich, 0 Fehler, 2 NU1903-Warnungen.
Manuell geprüft: DiceController enthält keine pauschalen `catch (Exception)`-Abbildungen mehr; Client-Fehlerpayload `{ error }` bleibt erhalten.
Nicht geprüft / Einschränkung: 403/404 können vor P06/P07 noch nicht über echte Besitz-/Meisterpfade ausgelöst werden; ihre zentrale Reason-Abbildung ist vorbereitet. Kein separater SignalR-Testclient und kein expliziter Abbruch-Sammeltest in P03.
Nächstes zulässiges Paket: P04, nur nach ausdrücklicher Beauftragung.

Paket / Datum: P04 / 07.09.2026
Bestätigte Ursache: `IsHidden` wurde aus dem Clientzustand in alle Requests übertragen, war aber als verfügbare UI-Funktion schaltbar und wurde serverseitig in den vier Wurfhandlern nicht geprüft.
Geänderte Dateien: `DsaWuerfelApp/DsaWuerfelApp.Client/Components/WuerfelActionBar.razor`, `RollFreeHandler.cs`, `RollTalentHandler.cs`, `RollAttributeHandler.cs`, `RollBadTraitHandler.cs`, `DsaWuerfelApp.Tests/HiddenRollTests.cs`, `Plan.md`.
Verhaltensänderung: Die UI zeigt den deaktivierten Hidden-Wurf-Schalter mit der Meldung „Verdeckte Würfe sind derzeit nicht verfügbar.“. HTTP-Requests mit `IsHidden == true` werden vor Heldenzugriff, RNG, Ergebnisbildung und Historie mit 400 und derselben Meldung abgewiesen.
Tests (Kommando + Ergebnis + Anzahl): `dotnet test DsaWuerfelApp/DsaWuerfelApp.Tests/DsaWuerfelApp.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~HiddenRollTests -v minimal` erfolgreich, 4/4. `dotnet test DsaWuerfelApp.sln -c Release --no-restore -v minimal` erfolgreich, 17/17.
Build: `dotnet build DsaWuerfelApp.sln -c Release --no-restore -v minimal` erfolgreich, 0 Fehler, 2 NU1903-Warnungen.
Manuell geprüft: Alle vier Handler prüfen `IsHidden` vor fachlicher Verarbeitung; UI-Schalter ist nicht anklickbar und die Meldung sichtbar.
Nicht geprüft / Einschränkung: Kein echter SignalR-Testclient in P04; der Hub verwendet dieselben Requests und Handlerguards. Vollständige Hidden-Funktion bleibt vertagt.
Nächstes zulässiges Paket: P05, nur nach ausdrücklicher Beauftragung.

Paket / Datum: P05 / 07.09.2026
Bestätigte Ursache: `DiceService.RollDice` summierte die angeforderten Counts vor der Gruppenvalidierung für die Listenkapazität und erlaubte unbegrenzte Gesamtmengen sowie Seitenzahlen oberhalb des vorgesehenen Bereichs.
Geänderte Dateien: `DsaWuerfelApp/DsaWuerfelApp/Services/Domain/Rolls/DiceService.cs`, `RollFreeHandler.cs`, `DsaWuerfelApp.Tests/DiceServiceTests.cs`, `Plan.md`.
Verhaltensänderung: Maximal 100 Würfel insgesamt, maximal 100 je Gruppe und 2 bis 1.000.000 Seiten. Alle Gruppen werden in einem ersten Durchlauf inklusive sicherer Summierung geprüft; erst danach werden Liste und kryptografischer RNG verwendet. Ungültige Free-Roll-Eingaben werden als erwartete 400-Ablehnung weitergegeben. Die neue Gesamtgrenze ist die technische Planvorgabe aus D04.
Tests (Kommando + Ergebnis + Anzahl): `dotnet test DsaWuerfelApp/DsaWuerfelApp.Tests/DsaWuerfelApp.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~DiceServiceTests -v minimal` erfolgreich, 11/11. `dotnet test DsaWuerfelApp.sln -c Release --no-restore -v minimal` erfolgreich, 28/28.
Build: `dotnet build DsaWuerfelApp.sln -c Release --no-restore -v minimal` erfolgreich, 0 Fehler, 2 NU1903-Warnungen.
Manuell geprüft: Null/Leer, Nullgruppe, Count- und Seitenzahlgrenzen, 2×50, 51+50 sowie Wertebereich bei 100 Würfeln.
Nicht geprüft / Einschränkung: Keine statistische Zufallsverteilung geprüft; keine weitergehende konfigurierbare Limit-Infrastruktur eingeführt.
Nächstes zulässiges Paket: P06, nur nach ausdrücklicher Beauftragung.

## 11. Begrenzte Uebergabe nach Umfangsreview / 08.09.2026

### Auftrag und Grenze

Der Nutzer wollte die offenen Punkte P06 und P08 abschliessen. Notwendige P07-Anpassungen gehoeren zu P06. Die darueber hinaus erfolgten Arbeiten an Persistenz, Clientzustand und 3D waren eine zu grosse Ausweitung. Sie sind jetzt in separaten lokalen Commits nachvollziehbar erhalten. Keine weiteren funktionalen Aenderungen beim Aufteilen der Commits vorgenommen; keine neuen Tests dafuer gestartet. Einzelne alte Windows-Umlautbytes in dieser Datei wurden fuer gueltiges UTF-8 normalisiert.

Das naechste Modell soll ausschliesslich die unten genannten Restpunkte bearbeiten. Kein neues Sicherheitsframework, keine neuen Kampfregeln, kein allgemeines Refactoring und keine Erweiterung der Testinfrastruktur. Insbesondere nicht aus der Zahl gruener Tests auf eine vollstaendige Abnahme schliessen.

### Lokale Commits

| Commit | Inhalt |
| --- | --- |
| a7d0655 | P08: Login-Basisadresse, lokale Redirects, Konfiguration, AuthRedirectTests und Deploymenthinweis |
| a136bfd | P06/P07: Eigentumspruefung, serverseitige Masterziele, ActiveHero-Validierung und HTTP-/Hub-Tests |
| 48d2739 | Zusatzumfang: atomare Aktivierung/Schema-Upgrades und Persistenz-/Mitgliedschaftsregressionstests |
| 6f7b347 | Zusatzumfang P14-P17: veraltete Antworten, Logout, Auswahlpersistenz, Kontextvergleich und Clienttests |
| 21d3a3d | Zusatzumfang P18: instanzgebundener 3D-Lebenszyklus und isolierter Browsertest |
| da33cf6 | Entwurf P20: isolierter Browser-Workflow; finale Navigationsfassung noch nicht komplett abgenommen |

Die Commits wurden inhaltlich aufgeteilt; Zwischencommits wurden nicht jeweils separat gebaut/getestet. Nachfolgende Ergebnisse beziehen sich auf den gemeinsamen Stand vor der rein dokumentarischen Uebergabe. Es wurde nicht gepusht.

### Tatsaechlich vorhandene Nachweise

- Letzter Gesamtlauf: `dotnet test DsaWuerfelApp.sln -c Release --no-restore -v quiet` -> 78 bestanden, 0 fehlgeschlagen, 0 uebersprungen. Davon 28 vorher vorhandene und 50 ergaenzte Testfaelle.
- Davon 26 neue Faelle fuer Heldenzugriff/Login, 12 fuer Clientzustand und 12 fuer Persistenz/Mitgliedschaft. Viele Faelle sind kurze Theory-Varianten.
- Release-Publish ausserhalb des Repository erfolgreich: zuletzt `%TEMP%/dsa-review-final-20260908b`. Katalogdateien und `wwwroot/models/dice_set.glb` enthalten; keine DBs, Schluessel oder artifacts/obj-Verzeichnisse enthalten. Temp-Ausgabe ist nur ein lokaler Nachweis, keine Voraussetzung fuer das naechste Modell.
- Paketpruefung ohne bekannte anfaellige Pakete. EF/Sqlite 10.0.11, SQLitePCLRaw 2.1.12; native Version ueber die .NET-Verbindung: `SELECT sqlite_version()` -> 3.53.3. Bezug: https://github.com/advisories/GHSA-2m69-gcr7-jv3q (native Korrektur ab 3.50.2).
- `Browser/dice-lifecycle.cjs` mit Edge bestanden: zehn Aufbau-/Abbauzyklen, zwei unabhaengige Szenen, Dispose waehrend Modellladen, neueste Vorschau, Abbruch alter Rollanimation, Ressourcenfreigabe; danach 0 Frames und 0 Listener. Dies war ein isolierter Szenentest, keine vollstaendige Blazor-Seitennavigation.
- In `Browser/app-workflows.cjs` bestanden in bisherigen Laeufen: drei echte Cookie-Logins mit isolierten Testkonten, Session erstellen/beitreten, Heldenzuordnungen, normaler Wurf, Umbenennung ohne Formularverlust, Austritt ohne Verlust verbleibender Helden, Reload der Auswahl sowie Serverneustart mit Hero-/Historienabgleich und Ablehnung eines getrennten Sessionwurfs.
- Einschraenkung des Master-Browserpfads: Der Test klickt den Meisterwurf, prueft dessen Ergebnis aber noch nicht ausdruecklich. Serverseitiger Erfolg und Nichtmeister-Ablehnung sind in HeroAccessTests geprueft.
- Kampf-WIP (`Kampf.razor.cs` / `.razor.css`) unveraendert gegen Checkpoint 6f779be.

### R01 - Letzte Browserabnahme gezielt abschliessen (offen)

Datei: `DsaWuerfelApp/DsaWuerfelApp.Tests/Browser/app-workflows.cjs`.

1. Die letzte Aenderung ersetzt beim Navigieren den unsichtbaren Linknamen durch `.nav-links a[href=...]` und nimmt Kampf in die Navigation auf. Diese Fassung wurde noch nicht komplett erfolgreich ausgefuehrt. Bei Selektorproblemen nur den betroffenen Schritt untersuchen; nicht erneut eine allgemeine Browser-Testplattform bauen.
2. Direkt beim Meisterwurf die Antwort von `/api/dice/master-attribute-roll` auf Erfolg und vorhandene Zielergebnisse pruefen. Keine weitere Rechte-Matrix im Browser duplizieren.
3. Den bestehenden Ablauf einmal komplett erfolgreich abschliessen, einschliesslich zehn Navigationszyklen und Fehlerpruefung am Ende. Vorherige Teilerfolge nicht als Gesamterfolg ausgeben.
4. Ausschliesslich frische temporaere DB/Keys verwenden. Der Script-Entwurf startet/stoppt seinen eigenen Testserver auf Port 5298. Bei belegtem Port keinen fremden Prozess beenden. Kein externer Mailversand. Ein echter Import ueber den Java-Konverter wurde nicht getestet; die Browserhelden sind Testfixtures. XML-Reimport/Eigentum sind separat in StartupOwnershipTests geprueft.

Vorhandene Ausfuehrung (vom Repository-Root, Edge und Node erforderlich):

```powershell
npm install --prefix "$env:TEMP/dsa-browser-tests" playwright three@0.160.0
$env:NODE_PATH = "$env:TEMP/dsa-browser-tests/node_modules"
# Vorhandenen aktuellen externen Publish verwenden oder einmal in einen frischen Temp-Pfad publishen.
node DsaWuerfelApp/DsaWuerfelApp.Tests/Browser/app-workflows.cjs <externer-publish-pfad>
```

### R02 - Abschluss dokumentieren (offen, nach R01)

P18/P20 nur gemaess tatsaechlichem Ergebnis aktualisieren. Bei weiterem Blocker den konkreten Schritt und Fehler festhalten. Wenn ausschliesslich Testselektoren/Dokumentation geaendert werden, keine weiteren umfassenden .NET-Test-/Publish-Wiederholungen. Bei einem reproduzierten Produktionsfehler: kleinster Fix und passende vorhandene Tests, danach noetige Abschlusspruefung.

### Ausdruecklich kein sofortiger Folgeauftrag

Die zusaetzlichen Client-/Persistenz-/3D-Aenderungen nicht pauschal zuruecksetzen: Sie sind bereits miteinander getestet und einzeln nachvollziehbar committed. Ob ihr Zusatzumfang behalten werden soll, ist beim Review getrennt von P06/P08 zu betrachten. Reflection auf private Methoden und DispatchProxy in ClientStateTests sind wartungsanfaellig; ihre Bereinigung ist optional und rechtfertigt jetzt keinen neuen Umbau.

Mindestens das Problem, dass ein Metadaten-Refresh das Speichern der Sessionauswahl unterbrechen konnte, entstand im Zuge der neuen Ladeversionen. Es wurde korrigiert und mit `Metadata_refresh_also_persists_current_selection` abgesichert. Solche Nacharbeiten nicht als ausschliesslich zuvor vorhandene Produktfehler darstellen.

## 12. UI-Review und Umsetzungsplan / 08.09.2026

### Auftrag

Die folgenden drei sichtbaren UI-Probleme sollen gezielt behoben werden:

1. Das Menü-Icon im linken Menü ist gegenüber den übrigen Icons anders ausgerichtet. Sein Abstand zum ersten Navigationseintrag unterscheidet sich ebenfalls.
2. Der Text `Verdeckte Würfe sind derzeit nicht verfügbar.` steht dauerhaft neben dem deaktivierten Icon. Sichtbar bleiben soll nur der ausgegraute deaktivierte Button; die Erklärung soll als kurze Information beim Hover und beim Tastaturfokus erscheinen.
3. Bei kleineren Fensterbreiten überlappen die Bereiche der Würfelseite. Die Seite soll sich responsiv verkleinern oder in sinnvolle Zeilen umbrechen, ohne dass Bedienelemente übereinander liegen oder am Viewportrand abgeschnitten werden.

### Analyse des aktuellen Stands

Beim Menü verwenden `.nav-expand-button` und `.nav-link` zwar ähnliche Maße, aber unterschiedliche Layoutpfade. Der Button liegt in einem eigenen `.nav-header`; zusätzlich erzeugt `.nav-panel` einen Abstand von `1rem`, während `.nav-links` nur `0.5rem` Abstand verwendet. Im aufgeklappten Zustand ändern Header und Links ihre Ausrichtung ebenfalls getrennt. Das erklärt die abweichende vertikale Position und den sichtbaren Abstand des Menü-Buttons. Die Korrektur soll gemeinsame Maße und einen gemeinsamen Abstand verwenden, ohne die Auf-/Zu-Funktion des Menüs zu ändern.

Der Hinweistext ist direkt als sichtbares `span` in `WuerfelActionBar.razor` neben dem deaktivierten Button eingebaut. Der Button besitzt bereits ein `title`, aber ein natives deaktiviertes Element ist für Tastaturfokus und zuverlässige Tooltip-Anzeige ungeeignet. Der Hinweis braucht deshalb einen umgebenden Tooltip- oder Hinweis-Wrapper mit zugänglicher Beschriftung. Der Serverguard für `IsHidden` und der deaktivierte Zustand bleiben unverändert.

Die Würfelseite kombiniert in `Wuerfel.razor.css` eine feste `height: 100vh` mit `overflow: hidden`, mehreren festen `min-height`-/`min-width`-Werten und breitenabhängigen Grid-Spalten. Zusätzlich erzwingen die Action-Bar und einzelne Pill-Komponenten Mindestbreiten. Bei kleinen Viewports kann die Summe der Mindestgrößen nicht in die verfügbare Fläche passen; das Abschneiden durch `overflow: hidden` verschärft den Effekt. Die responsive Lösung muss deshalb die tatsächliche Inhaltsgröße berücksichtigen, Mindestbreiten abbauen oder an Breakpoints neu anordnen und vertikales Scrollen zulassen, wenn die Bildschirmhöhe nicht reicht.

### Begrenzte Umsetzung

#### UI-01 – Menü-Icon und Navigation vereinheitlichen

Dateien: `DsaWuerfelApp/DsaWuerfelApp.Client/Layout/NavMenu.razor`, `DsaWuerfelApp/DsaWuerfelApp.Client/Layout/NavMenu.razor.css`, bei Bedarf `MainLayout.razor.css`.

- Button und Navigationseinträge auf eine gemeinsame Icon-Fläche, Höhe, Zentrierung und Außenabstände bringen.
- Den zusätzlichen Header-Abstand gegenüber `.nav-links` entfernen oder auf denselben definierten Abstand reduzieren.
- Im eingeklappten Zustand alle runden Einträge einschließlich Menü-Button optisch gleich ausrichten.
- Im aufgeklappten Zustand Textausrichtung und Icon-Abstand beibehalten; nur die unterschiedliche vertikale Position beseitigen.
- Keine neuen Icons, keine Änderung der Navigation und keine Änderung der Desktop-/Mobile-Funktionalität.

#### UI-01 - Ergebnis / Abnahme

Umgesetzt in `DsaWuerfelApp/DsaWuerfelApp.Client/Layout/NavMenu.razor.css`:

- Gemeinsame Variablen für Eintragsgröße und Abstand eingeführt.
- Menübutton und Navigationseinträge auf 3,25 rem Höhe/Breite vereinheitlicht.
- Headerabstand und Abstand der Navigationseinträge auf 0,5 rem vereinheitlicht.
- Eingeklappte Einträge horizontal zentriert; die aufgeklappte Textausrichtung bleibt erhalten.

Abgenommen am 08.09.2026 mit einem Release-Client-Build, `git diff --check` und einer Edge-Browserprüfung bei 1280x800. Dabei wurden eingeklappte Zentrierung, gleiche Maße, 8 px Abstand und die unveränderte Buttonhöhe beim Aufklappen geprüft.

#### UI-02 – Verdeckte Würfe kompakt und zugänglich darstellen

Dateien: `DsaWuerfelApp/DsaWuerfelApp.Client/Components/WuerfelActionBar.razor`, zugehörige `WuerfelActionBar.razor.css`.

- Das sichtbare Erklärung-`span` entfernen.
- Den Button weiterhin deaktiviert und sichtbar ausgegraut lassen.
- Die Erklärung `Verdeckte Würfe sind derzeit nicht verfügbar.` ausschließlich über einen Tooltip bzw. einen zugänglichen Hinweis am Icon bereitstellen.
- Hover und Tastaturfokus müssen denselben Hinweis zeigen; die Beschriftung darf nicht von einem nicht fokussierbaren deaktivierten Element abhängen.
- Keine Änderung an Requestverträgen, Hidden-Roll-Serverprüfung oder Fachlogik.

#### UI-02 - Ergebnis / Abnahme

Umgesetzt in `DsaWuerfelApp/DsaWuerfelApp.Client/Components/WuerfelActionBar.razor` und `WuerfelActionBar.razor.css`:

- Den dauerhaft sichtbaren Erklärungstext aus dem Layout entfernt.
- Den deaktivierten und ausgegrauten Button in einem fokussierbaren Hinweis-Wrapper belassen.
- Einen zugänglichen Hinweis mit `role="img"`, `tabindex="0"` und `aria-label` ergänzt.
- Hover und Fokus zeigen denselben Tooltiptext über dem Icon.
- Requestverträge, Hidden-Roll-Serverprüfung und Fachlogik unverändert gelassen.

Abgenommen am 08.09.2026 mit einem Release-Client-Build, `git diff --check` und einer Edge-Browserprüfung. Der Hinweistext erscheint nicht dauerhaft, der Button bleibt deaktiviert, der Fokuspfad funktioniert bei 1280x900 und der Hoverpfad bei 1920x900. Bei 1280x900 überlagert der bestehende Würfeln-Button den Iconbereich teilweise und fängt dort den Mauszeiger ab; die Behebung bleibt UI-03 vorbehalten.

#### UI-03 – Responsive Würfelseite stabilisieren

Dateien: `DsaWuerfelApp/DsaWuerfelApp.Client/Pages/Wuerfel.razor.css`, bei Bedarf die CSS-Dateien der direkt betroffenen Action-Bar-/Pill-Komponenten.

- Layoutgrenzen und Breakpoints anhand der real verfügbaren Inhaltsbreite festlegen, nicht nur anhand der Browserbreite.
- Bei kleinen und mittleren Breiten die Bereiche in klare Zeilen untereinander umbrechen: Historie/Information, Würfelaktionen/3D und die vier unteren Aktionskarten.
- `overflow: hidden` nur für einzelne Karteninhalte verwenden, wenn deren eigener Scrollbereich beabsichtigt ist. Die Seite selbst darf bei geringer Bildschirmhöhe vertikal scrollen.
- Grid-Kinder mit `min-width: 0` und flexiblen Spaltenbreiten versehen; feste Mindestbreiten der Action-Bar und ihrer Pillen an den Breakpoints reduzieren oder die Elemente gezielt untereinander anordnen.
- Keine Elemente durch negative Abstände, feste Überlagerungen oder ein bloßes Verkleinern der Schrift aus dem sichtbaren Bereich drücken.
- Desktopaufteilung mit Historie, 3D-Bereich und Zusatzinformationen erhalten, sofern die Mindestbreite ausreicht.

#### UI-03 - Ergebnis / Abnahme

Umgesetzt in `DsaWuerfelApp/DsaWuerfelApp.Client/Pages/Wuerfel.razor.css` und `WuerfelActionBar.razor.css`:

- Die feste Seitenhöhe und das globale Abschneiden des Würfelbereichs entfernt, damit notwendiger Inhalt vertikal scrollen kann.
- Grid-Kinder auf `min-width: 0` umgestellt und die Action-Bar bei geringer eigener Breite gezielt umgebrochen.
- Die Aufteilung von Aktionsbereich und 3D-Ansicht an die tatsächliche Containerbreite gekoppelt; bei kleinen und mittleren Breiten stehen sie untereinander.
- Die Desktop-Zweispaltenaufteilung bleibt bei ausreichender Inhaltsbreite erhalten.

Abgenommen am 08.09.2026 mit Release-Client-Build, `git diff --check` und Edge-Browserprüfung. Geprüfte Viewports: `1920x1080`, `1440x900`, `1280x800`, `1024x768`, `768x1024` und `390x844`; zusätzlich `1920x600` für den vertikalen Scroll. Alle Prüfungen liefen ohne horizontalen Overflow, Überschneidungen oder Seitenfehler. Auswahlwechsel, d6-Auswahl, normaler Wurf und Navigation blieben funktionsfähig.

### Abnahme

Die Umsetzung gilt erst als abgeschlossen, wenn alle drei Punkte in einem echten Browser geprüft sind:

- Menü eingeklappt und aufgeklappt: Menü-Icon, Account-/Seiten-Icons und Abstände sind zentriert und gleichmäßig.
- Hidden-Icon: kein dauerhafter Hinweistext; Tooltip erscheint bei Maus-Hover und Tastaturfokus; Button bleibt sichtbar deaktiviert.
- Würfelseite bei mindestens `1920x1080`, `1440x900`, `1280x800`, `1024x768`, `768x1024` und `390x844`: keine überlappenden Karten, Inputs, Buttons oder Überschriften; kein unerwarteter horizontaler Overflow; notwendiger vertikaler Scroll funktioniert.
- Ein normaler Wurf, eine Auswahländerung und die Navigation bleiben nach der CSS-Anpassung funktionsfähig.
- Screenshots vor/nach der Änderung oder ein kurzer Browser-Testbericht dokumentieren die geprüften Viewports. Keine neue allgemeine Testplattform und keine fachlichen Tests für reine CSS-Regeln hinzufügen.

### Commitgrenze

Die UI-Änderung kann in höchstens zwei fachlichen Commits umgesetzt werden:

1. `fix(ui): align navigation and compact hidden-roll control`
2. `fix(ui): make dice page responsive without overlap`

Plan- und Abnahmeeintrag erst nach tatsächlicher Browserprüfung ergänzen. Keine Aussage `UI erledigt`, solange mindestens ein genannter Viewport oder der Tastaturfokus-Hinweis nicht geprüft wurde.

## Datenbasis und Zauberinfo – Planentwurf vom 08.09.2026

Status: **ZT-02 umgesetzt; ZT-03 und ZT-04 bleiben offen.**

### Verbindliche Grundlage

- Zauber: `C:/Users/Bosko/Downloads/Zauber_wuerfelbot_v8.1_final_QA (1).json`, interne Version `v8.1_final_QA`.
- Talente: `C:/Users/Bosko/Downloads/Talente_Anzeige_v1.1_mit_Spezialisierungen.json`, interne Version `v1.1_talente_anzeige_mit_spezialisierungen`.
- Regelprüfung für Modifikationen und Varianten: `Y:/DSA4/01.Regelwerke/C01 - Liber Cantiones.pdf` und `Y:/DSA4/01.Regelwerke/DSA 4.1 - Wege der Zauberei.pdf`. Diese Werke dürfen zur Auslegung der bereits gelieferten Daten herangezogen werden; es werden daraus keine zusätzlichen Regeln vermutet, die in den freigegebenen Daten nicht belegt sind.
- Diese Dateien bestimmen Regeln und Anzeigetexte. Keine Ergänzungen oder Korrekturen aus allgemeinem DSA-Wissen. Bestehende abweichende Regeln nicht ungeprüft übernehmen.
- Geprüfter ausführlicher Text hat bei scheinbaren Widersprüchen Vorrang; unklare Fälle bleiben manuell. Originaldaten unverändert erhalten.
- Heldenwerte, erlernte Spezialisierungen und Repräsentationen kommen aus Charakterdaten. Ein Katalogeintrag verleiht dem Helden keine Fähigkeit.
- Sichtbarkeit (vom Nutzer bestätigt): Spieler sehen nur die Zauber ihres Helden. Der Meister sieht alle Zauber und benötigt diesen Zugriff auch für NPCs; ein eigener Held muss den Zauber dafür nicht beherrschen. Katalogzugriff und konkrete Probenwerte sind getrennt zu behandeln.

### Befunde aus Dateien und Code

Die JSON-Dateien wurden eingelesen und ihre Eintragsstrukturen untersucht. Die Angaben zur Prüfung gegen Regelwerke stammen aus dem Auftrag bzw. den Dateien; eine erneute Regelwerksprüfung ist nicht Teil dieser Analyse.

- Zauberkatalog: 268 Zauber, 450 Varianten und 802 spontane Modifikationen. Darüber hinaus 56 Folgeproben, 116 situative Zauberprobenmodifikatoren, 33 Schadensangaben, 18 ZfP*-Schwellen, 15 Wertänderungen und 49 Hinweise unter `Meisterentscheidungen`.
- Talentkatalog: 105 Talente sowie ein separater Bereich mit vier Sprach-/Schriftvorlagen. `Probe` enthält Listen von Eigenschaftskombinationen. Singen, Sinnenschärfe und Fährtensuchen haben jeweils zwei Kombinationen. Attributo benötigt eine dynamisch gewählte dritte Eigenschaft.
- `Voraussetzung`, `Probenmodifikator` und `VorabZfP` sind nicht einheitlich numerisch: Es gibt null, Zahlen, Texte und Objekte mit Unterfällen. Beispiel Zauberdauer: Verkürzen und Verlängern haben unterschiedliche Kosten und Modifikatoren. Ein einzelner Auswahlchip reicht dafür fachlich nicht aus.
- `AutomatischAuswertbar` ist bei Folgeproben, situativen Modifikatoren und Schaden vorhanden; bei Varianten, spontanen Modifikationen, Schwellen und Wertänderungen fehlt es. Seine Behandlung muss feldbezogen erfolgen, nicht als pauschale Freigabe aller Zahlen.
- Auch strukturierte Angaben können nur Teilinformationen enthalten: Accuratum, Variante „Sackleinen und Spinnenseide“, enthält strukturiert Mindest-ZfW 14 und +7; zusätzliche Erschwernis, Repräsentationsbeschränkung und Unvereinbarkeit mit „Haltbarkeit“ stehen im Text. Ein Zahlenwert allein bedeutet daher keine vollständig automatisierbare Variante.
- `SpellCatalogStore.cs` lädt bisher `Data/Zauber.json` (mit zusätzlichem Downloads-Fallback), nicht die neue Datei. Er liest alte `AnzeigenWenn`-Voraussetzungen; `Voraussetzung`, strukturierte Grunddaten, `Probenmodifikator`, `VorabZfP` und die neuen Regelbereiche werden nicht abgebildet. Fehlende Dateien und Ladefehler ergeben still einen leeren Katalog.
- `TalentCatalogStore.cs` erwartet bisher eine Liste unter `talente_mit_spezialisierungen.json` mit `Eigenschaften`, `Zweck`, `Kernregel` usw. Die neue Datei ist ein Wurzelobjekt mit `Talente`, `Probe`, `Kurzbeschreibung` und Regelbasis. Bloßes Ersetzen der Datei wäre inkompatibel.
- `SpellSelectionResolver.cs` bildet Optionsmodifikatoren derzeit nur aus einer passenden Zauberspezialisierung ab (0 oder -2). Die echten Zuschläge und Vorab-ZfP der neuen Datei werden dort nicht verwendet. Ein getrenntes Vorab-ZfP-Feld fehlt auch in `ResolvedSpellOption`.
- `SpellInfoSectionFactory.cs` gibt Grundtexte und gefilterte Varianten/Modifikationen aus. `WuerfelInformationPanel.razor` zeigt Detailabschnitte erst bei `ShowProbeInfoDetails`. Neue Regelbereiche erreichen diese Anzeige derzeit nicht über den Katalog.
- Talentspezialisierungen laufen derzeit als Modifikator -2 durch `TalentProbeService`. Ob dies auch bei Ergebnisgrenzen der geforderten Erhöhung des effektiven TaW entspricht, muss gezielt geprüft werden; gleiche Erfolgschance allein genügt nicht.

Das sind statische Integrationsbefunde, kein Nachweis eines konkreten Anzeigefehlers. Dass einem Spieler nicht erlernte Zauber fehlen, ist beabsichtigt. Die bestehende Trennung zwischen Heldenauswahl und vollständigem Meisterkatalog muss erhalten bleiben.

### Vorgeschlagene Umsetzung in begrenzten Schritten

Für Zauber werden Anzeige und Berechnung gemeinsam umgesetzt. ZT-01, ZT-02 und die zauberbezogenen Ergebnisregeln aus ZT-04 gehören deshalb zu einem fachlichen Zauberpaket. Es gibt keinen Zwischenstand, in dem Varianten zwar angezeigt, aber noch nicht korrekt für die Probe berücksichtigt werden.

#### ZT-01 – Neue Datenbasis und Zauberinfo integrieren

- Die beiden freigegebenen Dateien unverändert als versionierte Projektdaten aufnehmen und ihre Auslieferung beim Publish sicherstellen. Laufzeit nicht von einem persönlichen Downloads-Verzeichnis abhängig machen.
- Bestehende Katalogleser auf die tatsächlichen Wurzelobjekte und Feldtypen anpassen. Fehlende/ungültige Daten erkennbar melden, statt kommentarlos leere Info zu liefern. Keine zweite allgemeine Katalogplattform bauen.
- Neue Informationen durch Auswahl → Info-Anfrage → Namenszuordnung → Antwort → Detailanzeige integrieren. Spieler erhalten Informationen zu ihren erlernten Zaubern, der Meister zu allen Katalogzaubern, unabhängig vom eigenen Helden. Mehrdeutige Namen nicht still dem falschen Zauber zuordnen.
- Zaubergrunddaten und geprüfte Texte zuverlässig anzeigen. Sonderregeln, Folgeproben und Meisterhinweise getrennt von auswählbaren Varianten darstellen. Talent-Kurzbeschreibungen bevorzugen; ausführliche Texte über Detailansicht zugänglich halten.
- Vorhandene alte Ersatz-/Fallback-Regeln mit der neuen Datenbasis abgleichen. Nicht von der neuen Basis gedeckte Regeln als Klärungsbedarf behandeln, nicht still weiterverwenden.
- Abschluss dieses Schritts: verlässliche Information. Noch nicht unterstützte Berechnungen ausdrücklich als manuell kennzeichnen; keine rechnerisch wirkungslosen Optionen als vollständig unterstützt anbieten.

#### ZT-01 – Ergebnis / Abnahme

Umgesetzt:

- Die freigegebenen JSON-Dateien liegen unverändert unter `DsaWuerfelApp/DsaWuerfelApp/Data/` und werden über `CopyToOutputDirectory` sowie `CopyToPublishDirectory` ausgeliefert. Es gibt keinen Laufzeit-Fallback auf ein persönliches Downloads-Verzeichnis.
- Beide Loader lesen die tatsächlichen Wurzelobjekte. Fehlende Dateien, leere Wurzellisten und ungültiges JSON führen zu einer erkennbaren Ausnahme statt zu einem still leeren Katalog.
- Zaubergrunddaten, strukturierte Grundprobe, Varianten/Modifikationen und die getrennten Regelbereiche werden in die bestehende Infoantwort übernommen. Nicht auswertbare Regelteile werden mit einem manuellen Hinweis angezeigt.
- Der Katalogpfad funktioniert ohne Heldenwert. Spieler bleiben auf die Zauber ihres Helden begrenzt; der Meister kann Katalogzauber auch ohne eigenen Heldenwert anzeigen.
- Talente verwenden `Kurzbeschreibung`, mehrere Probe-Kombinationen, Behinderung, Voraussetzungen, Regelhinweise und Spezialisierungsdaten. Bei mehreren Probenkombinationen wird nicht still die erste gewählt.
- Neue Varianten und spontane Modifikationen sind in ZT-01 noch nicht auswählbar. Ihre Regeltexte und strukturierten Werte werden angezeigt; Auswahl und Berechnung folgen gemeinsam in ZT-02.

Prüfung:

- `dotnet build DsaWuerfelApp.sln -c Release --no-restore -v minimal` erfolgreich, 0 Fehler/0 Warnungen.
- `dotnet test DsaWuerfelApp.sln -c Release --no-restore -v minimal` erfolgreich, 79/79.
- Smoke-Test für den Katalogkontext und Katalogzauberinfo ohne Heldenwert ergänzt.
- Release-Publish erfolgreich; beide Katalogdateien liegen im veröffentlichten `Data/`-Ordner.

#### ZT-02 – Zauberauswahl und Probenwerte korrekt verbinden

- Bestehende Auswahl-/Request-/Ergebnismodelle gezielt um unveränderten ZfW, Vorab-ZfP, echte Probenmodifikatoren und erforderliche Eingaben erweitern. Dieselben Regeln in Einzel- und Sessionwürfen anwenden.
- Mindest-ZfW gegen den ursprünglichen Heldenwert prüfen. Vorab-ZfP reduzieren den verfügbaren Probenvorrat und die erreichbaren ZfP*, nicht den rohen ZfW. Ergebnisgrenzen und Sonderergebnisse anhand der freigegebenen Regeln klären, bevor gerechnet wird.
- Varianten von Sonderregeln trennen. Bei spontanen Modifikationen Unterfall und Stufe auswählen lassen; Einschränkungen, Kombinationen, Repräsentation und Anzahl der Modifikationsstufen aus der Datei beachten.
- MR, dynamische Eigenschaften und situative Werte nur mit gesicherter Herkunft oder expliziter Eingabe verwenden. Automatisierbare Bedingungen gelten nicht automatisch als erfüllt.
- Klar abgegrenzte unterstützte Fälle rechnen; bei komplexen oder unvollständigen Fällen darf der Spieler die Erschwernis oder Erleichterung der aktuellen Probe manuell eingeben. Diese Eingabe wird sichtbar als manuell gesetzt behandelt; der Bot leitet daraus keine eigene Regel und keine Änderung des rohen ZfW ab.

#### ZT-02 - Ergebnis / Abnahme

Umgesetzt:

- Varianten und spontane Modifikationen werden aus der freigegebenen Datei geladen. Objektwerte werden in auswählbare Unterfälle aufgeteilt, zum Beispiel `Zauberdauer: Verkürzen` und `Zauberdauer: Verlängern`.
- Echte numerische Probenmodifikatoren werden serverseitig aus dem Katalog übernommen und mit dem manuellen Modifikator addiert. Der Wert aus der Auswahl wird nicht aus der Clientangabe vertraut.
- Mindest-ZfW wird gegen den unveränderten Heldenwert geprüft. Vorab-ZfP bleiben getrennt vom ZfW und werden im Ergebnis als Roh-ZfP* und verfügbare ZfP* ausgewiesen.
- Nicht eindeutig numerische oder stufenabhängige Angaben werden auswählbar angezeigt, aber ausdrücklich als manuell zu ergänzen markiert. Der Bot ersetzt solche Angaben nicht durch eine Vermutung.
- Bekannte Zauber werden im Meister-Sammelwurf mit dem konkreten ZfW des Zielhelden gerechnet. Reine Katalogfälle behalten die bestehende Anforderungsdarstellung ohne erfundenen ZfW.

Prüfung:

- `dotnet build DsaWuerfelApp.sln --no-restore -p:BaseOutputPath=<Temp-Ausgabe>` erfolgreich, 0 Fehler/0 Warnungen.
- `dotnet test DsaWuerfelApp.sln --no-build --no-restore` erfolgreich, 81/81.
- Repräsentativer HTTP-Test für Abvenenum prüft den Unterfall `Verlängern`, den echten Modifikator `-3`, den unveränderten ZfW und die ZfP*-Ausgabe. Ein zusätzlicher Servicetest prüft die Trennung von Probenmodifikator, Roh-ZfP* und verfügbaren ZfP*.

#### ZT-03 – Talentinformationen und passende Spezialisierungen

- Neue Kurzbeschreibungen, BE-Angaben, Voraussetzungen und Regelhinweise in die bestehende Info integrieren. Mehrere Probenkombinationen erhalten; keine willkürliche Wahl der ersten Kombination.
- Erlernte Spezialisierungen mit Charakterdaten abgleichen und nur bei ausdrücklich passender Anwendung +2 effektiven TaW ansetzen. Grund-TaW unverändert lassen; Ergebnis und Anzeige konsistent halten.
- Katalogmöglichkeiten von tatsächlich erlernten Spezialisierungen unterscheiden. Offene Kategorien offen lassen; unbekannte importierte Namen nicht automatisch als ungültig löschen.
- Schwellen 7/14/21/28 und das Verbot identischer Mehrfachwahl bei der Auswertung berücksichtigen. Eine Erwerbs-, Änderungs- oder Heldenverwaltungsfunktion gehört nicht zu diesem Vorhaben.

#### ZT-04 – Ergebnisbezogene Zauberhinweise im Zauberpaket

- Erst nach erfolgreicher Probe die tatsächlichen ZfP* auf relevante Schwellen und eindeutig unterstützte Wirkungsformeln anwenden. Variantenbezogene Regeln nur bei gewählter Variante berücksichtigen.
- Folgeproben getrennt von der Zauberprobe anbieten bzw. anzeigen; nicht automatisch für Opfer oder Beobachter würfeln.
- Schaden, Dauer, Kosten und Wertänderungen nur bei eindeutig unterstützter Struktur berechnen. `AutomatischAuswertbar=false/null` bleibt bei entsprechend gekennzeichneten Bereichen manuell; fehlende Eingaben nicht durch 0 ersetzen.
- Meisterentscheidungen mit vorhandenem Text kennzeichnen. Keine automatische Änderung von Heldenwerten oder Ressourcen ohne gesondert geklärten Umfang.

### Offene Fragen zur Finalisierung

1. **Sichtbarkeit – geklärt:** Spieler sehen nur erlernte Zauber ihres Helden; der Meister sieht alle Zauber, insbesondere für NPCs. Daraus ergibt sich kein Infofehler. Diese Frage ist beantwortet.
2. **Umfang/Reihenfolge – geklärt:** Zauberanzeige und Zauberberechnung werden gemeinsam umgesetzt. ZT-01, ZT-02 und ZT-04 bilden ein zusammenhängendes Zauberpaket. Eine angezeigte Variante muss bei ihrer Auswahl auch mit ihren geprüften Voraussetzungen, Probenmodifikatoren und Vorab-ZfP korrekt in die Probe einfließen. ZT-03 für normale Talente folgt als eigener Bereich.
3. **Kurze Zaubertexte – geklärt:** Die Texte in den bereitgestellten JSON-Dateien sind bereits aufbereitet und gekürzt. Es werden keine zusätzlichen Kurztexte erstellt. Für die Anzeige werden die vorhandenen Felder verwendet, insbesondere `Wirkung` bei Zaubern sowie `Kurzbeschreibung` und `RegelhinweiseKurz` bei Talenten.
4. **Manuelle Fälle – geklärt:** Bei wirklich komplexen oder nicht eindeutig automatisierbaren Regeln gibt der Spieler die Erschwernis oder Erleichterung der aktuellen Probe selbst ein. Die Eingabe wird ausdrücklich als manuell angezeigt und nur für diese Probe verwendet. Der Bot ergänzt keine vermuteten Regeln und verändert den rohen ZfW nicht.
5. **Probenkontext – geklärt:** Die Heldendaten kommen aus dem XML. Beim Beispiel `Darian.xml` stehen aktuelle Eigenschaften und MR unter `eigenschaften`, Talentwert, Probe, Probenwerte, BE und Spezialisierungen unter `talentliste/talent` sowie ZfW, Probe, Probenwerte, Repräsentation, Merkmale und MR-Bezug unter `zauberliste/zauber`. Diese Werte werden für den jeweiligen Helden verwendet. Die Oberfläche fragt nur noch Kontext ab, der nicht im eigenen Held steckt: Ziel bzw. Ziel-MR, eine dynamische Eigenschaft wie bei Attributo, die konkrete alternative Talentprobe sowie die gewählten Modifikationsstufen und Varianten. Bei einem Meister kann der Zielheld bzw. NPC ausgewählt werden; fehlt ein benötigter Zielwert, bleibt die Eingabe manuell. Modifikations- und Variantenregeln werden anhand von Liber Cantiones und Wege der Zauberei geprüft und mit den freigegebenen JSON-Daten verbunden.
6. **Spezialisierungen und Heldenverwaltung – geklärt:** Es werden ausschließlich bereits vorhandene Heldendaten und importierte Spezialisierungen gelesen. Keine neue Heldenverwaltung, kein XML-Editor, kein Import- oder Erwerbsdialog und keine Änderung an der bestehenden Heldenverwaltung im Rahmen dieses Vorhabens. Offene Spezialisierungskategorien bleiben offen; die vier Sprach-/Schriftvorlagen dürfen als Katalogdaten für Proben verwendet werden.
7. **Automatisierungsgrenze – vorläufig bestätigt:** Explizite einfache Variantenwerte und Schwellen dürfen verwendet werden, wenn Text und Bedingungen eindeutig sind. Bei Folgeproben, situativen Modifikatoren und Schaden ist `AutomatischAuswertbar=true` erforderlich. Komplexe Fälle bleiben manuell. Für Zauberspezialisierungen und nicht eindeutig abgedeckte Ergebnis- oder Rundungsgrenzen gelten ausschließlich die freigegebenen Daten und Regelquellen; bei einem konkreten Widerspruch wird der Fall erneut geklärt.
8. **Ressourcen und Wirkungen – vorläufig bestätigt:** AsP, Schaden und Wertänderungen werden zunächst berechnet und angezeigt, aber nicht automatisch auf dem Helden verbucht. Reichweitenstufen, Rundungen und Grenzfälle werden nur umgesetzt, wenn sie in den freigegebenen Daten oder Regelquellen eindeutig festgelegt sind. Unklare Fälle bleiben manuell.
9. **Zusammenspiel manueller und automatischer Modifikatoren – geklärt:** Automatische und manuelle Modifikatoren werden getrennt angezeigt und addiert. Die manuelle Eingabe startet bei 0 und ersetzt nur dann einen nicht automatisierbaren Teil, wenn sie ausdrücklich als vollständiger Ersatzwert eingegeben wird.

### Schlanke Prüfung und Abschluss

- Die reine Planphase ist abgeschlossen. Umsetzung und Abnahme werden pro vereinbartem Schritt dokumentiert.
- Bei Umsetzung vorhandene Tests gezielt ergänzen: Laden beider Originalschemas, echte Variantenerschwernis versus Vorab-ZfP, Mindest-ZfW unverändert, manuelle Fälle ohne automatische Berechnung, passende Spezialisierung +2 effektiver TaW und Ergebnisgrenze.
- Kleine repräsentative Fälle aus den Dateien verwenden (z.B. Abvenenum/Accuratum, Attributo, alternative Talentprobe); keine hunderte textidentischen Tests und keine neue Testplattform.
- Visuelle Abnahme übernimmt auf Wunsch der Nutzer. Je Schritt kurzer Bericht über geändertes Verhalten, durchgeführte Prüfungen und verbleibende manuelle Fälle; keine umfangreichen Browserläufe ohne Bedarf.
- Nach ZT-01 folgt ZT-02 mit der gemeinsamen Auswahl- und Berechnungslogik für Varianten und spontane Modifikationen; dabei bleiben komplexe Fälle manuell.
