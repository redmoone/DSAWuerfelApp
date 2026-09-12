# Kampfseite – Tabs und direkte Bedienung

Ausgangsrevision: `4d1dee37263b83994bd4ce8a3e0d4b3696e39af6` auf `master`.

## Arbeitsstand

- Phasen A–H: vier Haupttabs, kompakter Statusbereich, direkte Ressourcen-/Wund-/INI-Änderung, Kampfwurf, Zonen, Runde, Eigenschaften und Responsive-Regeln umgesetzt.
- Phasen I–J: Build und statischer Strukturcheck abgeschlossen; die tatsächlichen Regelgrenzen sind unten dokumentiert.

## Bewusste Grenzen

Die vorhandenen Regel- und Transportpfade bleiben unverändert. Manöver bleiben eine Notiz, Treffer-/TP-Würfe wenden keinen Treffer automatisch an, und die bekannte Solo-Initiative-Abweichung bei Klingentänzer wird nicht im UI-Paket neu geregelt.

## Prüfung

- `dotnet build DsaWuerfelApp/DsaWuerfelApp/DsaWuerfelApp.csproj --no-restore`: erfolgreich, 0 Warnungen, 0 Fehler.
- Alte Ressourcen-/Wund-/INI-Drawer-Referenzen sind aus `Kampf.razor` und `Kampf.razor.cs` entfernt.
- Ein manueller Desktop-/Mobil-Rundgang war in dieser Umgebung nicht möglich, da keine CUA-Browseroberfläche verfügbar war.
