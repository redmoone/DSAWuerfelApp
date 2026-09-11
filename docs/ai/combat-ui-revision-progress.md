# Kampfseite Revision 5 – Fortschritt

Stand: 11.09.2026

Der Arbeitsstand folgt `DSA-Kampfseite-Luna-Max-Plan.md`, Revision 5. Die alte Einschränkung auf eine reine UI ohne Kampfwürfe gilt nicht mehr. `master` enthält den laufenden Rev5-Stand; die frühere Branch `luna-max/combat-page` wurde nicht als fertige Lösung übernommen.

## Aktueller Paketstand

| Paket | Status | Nachweis |
|---|---|---|
| P0 | erledigt | Ausgangsstand, Branchvergleich und Dateigrenzen geprüft. |
| P1 | erledigt | Autoritative AT/PA/AW/FK-/TP-/Hilfswürfe, Ergebnis-Snapshots, Animation, Verlauf und Session-Hub angebunden. |
| P2 | erledigt | Gemeinsame Zahlencontrols, Eigenschaftsmodus und stabile Auswahlchips umgesetzt. |
| P3 | erledigt | Gemeinsamer Heldenstatus, Ressourcen-/Wunddrawer und Zonenansicht auf `/kampf` und `/wuerfel` verfügbar. |
| P4 | erledigt | Session-INI, Reihenfolge, Aktionen/Reaktionen, Halten, neue Runde, Gegner, Ansage, Undo und Orientieren umgesetzt. |
| P5 | abgenommen | Release-Build, 126 .NET-Tests und die vollständigen sequenziellen Browserläufe auf dem aktuellen Publish sind grün; fachliche Grenzen sind unten dokumentiert. |

## Umgesetzter Umfang

- `CombatRollRules` und `RollCombatHandler` lösen Kampfwerte serverseitig aus Besitzer, Held, Set, Waffe und Aktion auf. Der Client liefert keine verbindlichen finalen AT-/PA-/FK-Werte.
- `CombatRuntimeModifierRules` verrechnet bekannte laufende Wunden sowie niedrige LeP-/AuP-Stände für AT, PA, FK und INI regelbezogen. Unbekannte Werte bleiben ausdrücklich unbekannt und werden weder als null noch als 0 verrechnet.
- Attacke, Waffen-/Schildparade, Ausweichen, Fernkampf, TP, Trefferzone und Hilfswürfe nutzen den echten `DiceService`. Erfolgreiche Attacken ändern keine fremden LeP automatisch; Abwehr, Zone und Schaden bleiben getrennte Schritte.
- 1/20, Fernkampf-Patzerkontrolle, Schadensformeln und nachvollziehbare Ergebnisdetails sind im gemeinsamen Regelkern abgebildet. Alte Verlaufseinträge behalten ihre damaligen Werte.
- `CombatState` speichert den laufenden Solozustand getrennt vom Importprofil. Negative LeP bleiben erhalten, Balken werden nur optisch auf null begrenzt, Wunden bleiben zonengebunden und Runden-/Setwechsel heilen nichts.
- Der initiale Zustand zeigt unbekannte Ressourcen als `— / Maximum` und unbekannte Wunden als `—`. Erst `Mit Maximalwerten beginnen` setzt unbekannte Ressourcen auf bekannte Maxima und unbekannte Wunden auf 0; bereits erfasste Werte bleiben erhalten.
- `CombatSessionStateService` ist die autoritative Quelle für Sessionrevision, Teilnehmeridentität, INI, Reihenfolge, Aktionen, Reaktionen, gehaltene Handlungen, Runde, Gegner, Ansagen, Undo und idempotente RequestIds. Reconnect lädt den aktuellen Serverstand.
- Orientieren ist eine echte Sessionhandlung. Vor dem ersten INI-Wurf ist sie gesperrt; Aufmerksamkeit verwendet eine Aktion, sonst wird eine IN-Probe mit importierter Kriegskunst-Erleichterung angeboten. Ein Erfolg setzt den Orientierungsanteil, ein Misserfolg lässt die INI unverändert.
- `CombatStatusPanel` wird auf beiden Seiten verwendet. Ressourcen und Wundstand öffnen denselben `CombatDetailsDrawer`-Baustein; `/wuerfel` und `/kampf` teilen sich `CombatState`.
- `CombatDetailsDrawer` setzt beim Öffnen den Fokus in den Dialog, schließt mit Escape und gibt den Fokus nach Abbrechen, Übernehmen oder Schließen an den auslösenden Control zurück.

## Nachweise

- `dotnet build DsaWuerfelApp.sln -c Release --no-restore -v minimal`: erfolgreich, 0 Warnungen, 0 Fehler.
- `dotnet test DsaWuerfelApp.sln --no-restore -v minimal`: erfolgreich, 126 von 126 Tests.
- Das aktuelle Release-Publish wurde ebenfalls erfolgreich erzeugt und für alle Browserläufe verwendet.
- `combat-revision-p1.cjs`: Darian, responsive Breiten 320, 390, 640, 900, 901, 1280 und 1600, Zonen-/Orientierungsansicht, Aktionsleiste, Verlauf und Seitenbereich ohne Überlauf bestanden.
- `combat-revision-p2.cjs` und `combat-revision-p3.cjs`: Setauswahl, Importdarstellung und Desktop-Geometrie ohne Überlauf bestanden.
- `combat-revision-p4.cjs`: unbekannter Startzustand, direkte Ressourcen-/Wundenerfassung, gemeinsame Brust/Rücken-Zone, Undo, Reload, Escape-/Fokus-Rückgabe und mobiler Überlauf bestanden.
- `combat-revision-p5.cjs`: mit Darian, Ardor, Cordula und den responsiven Breiten 320, 390, 640, 900, 901, 1280 und 1600 ohne horizontalen Überlauf bestanden.
- `combat-attribute-mode.cjs`: Kampf-Eigenschaftsmodus, Mehrfachauswahl, Ergebnis, Entfernen der Auswahl sowie ein fehlender KO-Wert als "—" und deaktivierte Auswahl bestanden.
- `combat-orientation.cjs`: Sessionstart, Sperre vor INI, Aufmerksamkeit, Orientieren und INI-Anpassung bestanden.
- `combat-orientation-probe.cjs`: Orientieren ohne Aufmerksamkeit mit dem tatsächlichen IN-Probenpfad, einem deterministischen Misserfolg bei IN 0 und unveränderter INI bestanden.
- `combat-shared-status.cjs`: Status auf `/wuerfel`, negative LeP, Navigation zu `/kampf` und zurück sowie horizontaler Überlauf bestanden.
- `combat-visual-acceptance.cjs`: 1440x900, 1024x768, 390x844 und das 200-%-Viewport-Äquivalent 720x450, sichtbare Desktop-Hauptaktion, keine horizontal abgeschnittenen Controls, mobiler Detaildrawer und Fokus-Rückgabe bestanden; Screenshots wurden erzeugt.
- `CombatSessionStateTests`: Revision/Stale/Idempotenz, Eigentümerschutz/Reconnect, fremde Undo-Konflikte, gehaltene Handlungen über die Runde und aktuelle Laufzeitwerte beim Abschluss mit 7 fokussierten Tests bestanden.

## Präzise fachliche Grenzen

- `CombatRuntimeModifierRules` deckt die numerisch eindeutig ableitbaren allgemeinen und zonalen Wundabzüge sowie niedrige LeP/AuP für AT/PA/FK/INI ab. Armwunden können mangels importierter Waffenhand-Zuordnung nicht automatisch einer konkreten AT/PA zugeordnet werden; der Kopfverlust `2W6 INI` und dritte Wunden bleiben als benannte Tischfolge sichtbar.
- Die 1/20-Kontrolle wird im selben Ergebnislauf als zweiter Kontrollwurf gespeichert. Ein eigener wiederaufnehmbarer „Prüfwurf würfeln“-Schritt nach Reconnect ist noch nicht als separater Sessionzustand modelliert.
- Der Browserlauf für Orientieren deckt jetzt Erfolg und deterministischen Misserfolg ab. Die Session-Service-Tests decken veraltete Revisionen, doppelte RequestIds und Undo nach einer unabhängigen fremden Änderung ab; ein zusätzlicher Browser-E2E-Lauf über zwei gleichzeitig verbundene Fenster bleibt als Integrationsnachweis offen.
- Die drei Zielansichten, der mobile Drawer, die Hauptaktion im ersten Desktop-Viewport, das 200-%-Viewport-Äquivalent und die Rückführung des Fokus nach dem Schließen sind abgenommen.

## Commitfolge dieses Arbeitsstands

Die großen Änderungen wurden in kleine, einzeln gepushte Commits zerlegt. Die jüngsten Commits sind:

- `b0ddb1d fix(ui): keep combat roll in initial viewport`
- `71f4a66 fix(ui): restore focus from combat drawers`
- `7feaa7b test(combat): cover zoom and undo conflict paths`
- `e849250 fix(combat): apply runtime state when completing actions`
- `f021638 feat(combat): persist selected initiative set`
- `11bea11 feat(combat): keep solo initiative in sync with state`
- `95437d1 test(combat): cover explicit runtime initialization`
- `306cbf8 feat(combat): initialize unknown runtime state explicitly`
- `f4289f0 fix(combat): apply runtime state when orienting`
- `261fa4d fix(combat): keep legacy dice route in sync`

Diese Commits sind nach `origin/master` gepusht. Weitere fachliche Nachbesserungen werden weiterhin pro geprüftem Teilblock committed und gepusht.
