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
| P5 | in Arbeit | Regressionsläufe laufen; die vollständige visuelle und fachliche Abnahme ist noch offen, solange die unten genannten Grenzen nicht geprüft sind. |

## Umgesetzter Umfang

- `CombatRollRules` und `RollCombatHandler` lösen Kampfwerte serverseitig aus Besitzer, Held, Set, Waffe und Aktion auf. Der Client liefert keine verbindlichen finalen AT-/PA-/FK-Werte.
- Attacke, Waffen-/Schildparade, Ausweichen, Fernkampf, TP, Trefferzone und Hilfswürfe nutzen den echten `DiceService`. Erfolgreiche Attacken ändern keine fremden LeP automatisch; Abwehr, Zone und Schaden bleiben getrennte Schritte.
- 1/20, Fernkampf-Patzerkontrolle, Schadensformeln und nachvollziehbare Ergebnisdetails sind im gemeinsamen Regelkern abgebildet. Alte Verlaufseinträge behalten ihre damaligen Werte.
- `CombatState` speichert den laufenden Solozustand getrennt vom Importprofil. Negative LeP bleiben erhalten, Balken werden nur optisch auf null begrenzt, Wunden bleiben zonengebunden und Runden-/Setwechsel heilen nichts.
- `CombatSessionStateService` ist die autoritative Quelle für Sessionrevision, Teilnehmeridentität, INI, Reihenfolge, Aktionen, Reaktionen, gehaltene Handlungen, Runde, Gegner, Ansagen, Undo und idempotente RequestIds. Reconnect lädt den aktuellen Serverstand.
- Orientieren ist eine echte Sessionhandlung. Vor dem ersten INI-Wurf ist sie gesperrt; Aufmerksamkeit verwendet eine Aktion, sonst wird eine IN-Probe mit importierter Kriegskunst-Erleichterung angeboten. Ein Erfolg setzt den Orientierungsanteil, ein Misserfolg lässt die INI unverändert.
- `CombatStatusPanel` wird auf beiden Seiten verwendet. Ressourcen und Wundstand öffnen denselben `CombatDetailsDrawer`-Baustein; `/wuerfel` und `/kampf` teilen sich `CombatState`.

## Nachweise

- `dotnet build DsaWuerfelApp.sln -c Release --no-restore -v minimal`: erfolgreich, 0 Warnungen, 0 Fehler.
- `dotnet test DsaWuerfelApp.sln --no-restore -v minimal`: erfolgreich, 113 von 113 Tests.
- `combat-revision-p5.cjs`: zuvor mit Darian, Ardor, Cordula, fehlendem Profil und den responsiven Breiten 320, 390, 640, 900, 901, 1280 und 1600 ohne horizontalen Überlauf bestanden.
- `combat-attribute-mode.cjs`: Kampf-Eigenschaftsmodus, Mehrfachauswahl, Ergebnis und Entfernen der Auswahl bestanden.
- `combat-orientation.cjs`: Sessionstart, Sperre vor INI, Aufmerksamkeit, Orientieren und INI-Anpassung bestanden.
- `combat-shared-status.cjs`: Status auf `/wuerfel`, negative LeP, Navigation zu `/kampf` und zurück sowie horizontaler Überlauf bestanden.

## Noch offene Abnahme

- Die automatische Zuordnung aller laufenden Wund-, niedrige-LeP-/AuP- und strukturierten Effektfolgen zu AT/PA/FK/INI ist noch nicht vollständig modelliert. Die direkten manuellen Ressourcen- und Wundpfade funktionieren; unbekannte Folgen dürfen nicht als null erfunden werden.
- Für Orientieren fehlt noch ein eigener Browserfall ohne Aufmerksamkeit mit tatsächlichem IN-Probenpfad sowie ein expliziter Misserfolgsfall.
- Die Sessionübergänge brauchen noch fokussierte Tests für parallele veraltete Revisionen, doppelte Aktionen, gehaltene Handlungen über die Runde und Undo nach fremder Änderung.
- Die visuelle P5-Abnahme muss noch gezielt mit 1440×900, 1024×768, 390×844 und 200 % Zoom einschließlich sichtbarer Hauptaktion und Fokus-/Drawerverhalten dokumentiert werden.

## Commitfolge dieses Arbeitsstands

Die großen Änderungen wurden in kleine, einzeln gepushte Commits zerlegt. Die jüngsten Commits sind:

- `c0f65ae feat(combat): share status controls on dice page`
- `f03f483 test(ui): verify shared combat status navigation`

Weitere Änderungen werden nach einem geprüften Teilblock separat committed und nach `origin/master` gepusht.
