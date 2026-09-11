# Kampfseite Revision 3 – Fortschritt

Stand: 11.09.2026

Dieser Arbeitsstand folgt `DSA-Kampfseite-Luna-Max-Plan.md`, Revision 3. Der lokale Branch `luna-max/combat-page` wurde gegen `master` geprüft; die fünf dortigen Combat-Commits enthalten zwar verwertbare Zonen-/Zustandsansätze, aber auch einen manuellen Profileditor und einen neuen Kampfwurf-Regelkern. Diese Teile gehören nicht in Revision 3 und werden nicht übernommen.

## Pakete

| Paket | Status | Nachweis |
|---|---|---|
| P0 | erledigt | Ausgangsbaum sauber auf `master`; Branchvergleich und Dateigrenzen geprüft. |
| P1 | offen | Sichtbare UI- und Komponentenüberarbeitung ausstehend. |
| P2 | offen | Importprojektion und geschützter Profilendpunkt ausstehend. |
| P3 | offen | Aktionen, Suche, Info, Verlauf und ehrlicher Kampfwurf-Leerzustand ausstehend. |
| P4 | offen | Laufender lokaler Kampfzustand und Treffererfassung ausstehend. |
| P5 | offen | Gesamtprüfung, Screenshots und Übergabedokumentation ausstehend. |

## Baseline vor der Umsetzung

- `dotnet build DsaWuerfelApp.sln -c Release -v minimal --no-restore`: erfolgreich, 0 Warnungen, 0 Fehler.
- `dotnet test DsaWuerfelApp.sln -c Release --no-restore -v minimal`: 92 erfolgreich, 8 bekannte fachliche Baselinefehler in Talent-/Zauber-/RollHistory-Tests. Diese Fehler sind unabhängig von der Kampfseite und werden nicht durch abgeschwächte Assertions verdeckt.
- `git diff --check`: erfolgreich für den sauberen Ausgangsbaum.
- Der erste parallele Testaufruf wurde wegen einer gleichzeitig gesperrten Build-DLL verworfen; der anschließend sequenzielle Lauf ist die maßgebliche Baseline.

## Entscheidungen für Revision 3

- Kampf wird aus `ModifierPill`, `ProbenSearch`, `AttributePill`, `TextPill`, `WuerfelActionBar`, `DiceViewport`, `RollHistory` und der erweiterbaren `WuerfelInformationPanel`-Hülle zusammengesetzt.
- Importierte Kampfwerte werden serverseitig aus `Hero.SourceXml` projiziert. Das XML wird nicht an den Browser ausgeliefert.
- AT/PA/FK, Waffen, Rüstung, Sonderfertigkeiten und Ressourcenmaxima erhalten keine Eingabeformulare.
- Der laufende Zustand enthält sieben Wundbestände; Brust und Rücken teilen einen Bestand. Ressourcen und Initiative bleiben vom Importprofil getrennt.
- Es wird kein Kampfwurf simuliert und kein allgemeiner Eigenschaftswurf als Ersatz aufgerufen. Solange kein separater Regel-/Würfeldienst existiert, bleibt die Kampfwurfaktion deaktiviert und erklärt den Grund.

## Nächster Schritt

P1: Kampfseite auf die bestehende Seitenpalette und die vorhandenen Controls umstellen, mit kompakter Statuszeile, Arbeitsfläche, Info-/Initiative-/Verlaufsbereichen sowie der neuen Körperzonen-Darstellung.
