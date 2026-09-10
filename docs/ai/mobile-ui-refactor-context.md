# Mobile UI Refactoring Context

## 1. Executive Summary

- Die Anwendung ist eine gehostete .NET-10-Blazor-WebAssembly-Anwendung mit ASP.NET-Core-Server und MudBlazor 9.0.0-rc.1.
- Die UI besteht aus Razor-Komponenten, CSS-Isolation (*.razor.css), MudBlazor-Utility-Klassen, eigenem CSS und etwas JavaScript.
- App rendert Routes; RouteAccessView prüft den Authentifizierungszustand; die Seiten verwenden standardmäßig MainLayout.
- MainLayout enthält eine seitliche NavMenu, MudBlazor-Provider und einen flexiblen Content-Bereich. Ein Footer ist nicht vorhanden.
- Routing erfolgt über @page-Direktiven, [Authorize]/[AllowAnonymous] und RouteView; die Navigation ist eine eigene, zustandsabhängige Komponente.
- Die Anwendung besitzt bereits mehrere CSS-Media-Queries, vor allem bei 641px, 640px, 760px, 900px, 980px, 1080px, 1200px und 1280px.
- Responsive Verhalten ist daher seitenweise vorhanden, aber es gibt keine zentrale Breakpoint-, Spacing- oder Layout-Abstraktion.
- Die wichtigsten Layout-Flächen sind Wuerfel, Kampf, Lobby, HeldenVerwaltung, MainLayout, NavMenu und SessionTree.
- Der ursprüngliche Stand arbeitete in vielen Bereichen mit verschachteltem overflow-y, festen Mindestgrößen und lokalen Grid-Spalten; die aktuellen Änderungen reduzieren diese Muster gezielt, ohne eine globale overflow-x-Strategie einzuführen.
- Navigation und Würfelkontext hängen an handgeschriebenen, scoped State-Services und Event-Abonnements (AuthState, ActiveHeroState, SessionState, WuerfelState).
- Für die 3D-Würfelansicht wird Three.js über JavaScript eingebunden; die Canvas-Größe wird aus dem Viewport des Elements berechnet.
- Die Browserprüfungen decken zentrale Abläufe sowie eine Responsive-Matrix mit 12 Viewports und vier Routen ab; physische Geräte und visuelle Pixelvergleiche bleiben davon getrennt.

## 2. Repository Structure Relevant to UI

~~~text
DsaWuerfelApp/
  DsaWuerfelApp/
    DsaWuerfelApp.Client/
      App.razor
      Routes.razor
      RouteAccessView.razor
      Layout/
        MainLayout.razor
        MainLayout.razor.css
        NavMenu.razor
        NavMenu.razor.css
      Pages/
        Lobby.razor(.cs/.css)
        Wuerfel.razor(.cs/.css)
        Kampf.razor(.cs/.css)
        HeldenVerwaltung.razor(.cs/.css)
        NotFound.razor(.cs)
      Components/
        SessionTree.razor(.cs/.css)
        WuerfelActionBar.razor(.cs/.css)
        WuerfelInformationPanel.razor(.css)
        WuerfelProbePanel.razor(.cs/.css)
        ProbenSearch.razor(.cs/.css)
        AttributePanel.razor(.cs/.css)
        AttributePill.razor(.cs/.css)
        ModifierPill.razor(.cs/.css)
        TextPill.razor(.css)
        DiceControls.razor(.css)
        DiceViewport.razor(.cs)
        Dice3D.razor(.cs)
        RollHistory.razor(.cs/.css)
        RollEquation.razor(.cs/.css)
        SchlechteEigenschaftPanel.razor(.cs/.css)
      Services/
        State/
        Wuerfel/
        Transport/
      wwwroot/
        index.html
        app.css
        js/
    DsaWuerfelApp/
      Program.cs
    DsaWuerfelApp.Shared/
    DsaWuerfelApp.Tests/
~~~

Die für die UI relevanten Projektdateien sind DsaWuerfelApp/DsaWuerfelApp.Client/DsaWuerfelApp.Client.csproj, DsaWuerfelApp/DsaWuerfelApp/DsaWuerfelApp.csproj und DsaWuerfelApp/DsaWuerfelApp.Tests/DsaWuerfelApp.Tests.csproj. Es gibt kein package.json, kein Tailwind- oder SCSS-Setup und keinen separaten Frontend-Bundler.

## 3. Application Shell and Layout

### Root und Routing

DsaWuerfelApp/DsaWuerfelApp.Client/App.razor

- App rendert ausschließlich Routes.

DsaWuerfelApp/DsaWuerfelApp.Client/Routes.razor

- Router verwendet typeof(Program).Assembly.
- Gefundene Routen werden über RouteAccessView gerendert.
- Nicht gefundene Routen verwenden MainLayout und NotFound.

DsaWuerfelApp/DsaWuerfelApp.Client/RouteAccessView.razor

- RouteAccessView implementiert IDisposable, injiziert AuthState und NavigationManager.
- Während des Ladens wird MainLayout mit einem MudProgressCircular verwendet.
- Für authentifizierte Seiten wird RouteView mit DefaultLayout="@typeof(MainLayout)" gerendert.
- [AllowAnonymous] wird per Reflection erkannt; andere Seiten werden bei fehlender Authentifizierung nach / umgeleitet.
- Änderungen von AuthState lösen eine erneute Prüfung bzw. ein erneutes Rendern aus.

Aktive Routen:

- DsaWuerfelApp/DsaWuerfelApp.Client/Pages/Lobby.razor: /, anonym erlaubt.
- DsaWuerfelApp/DsaWuerfelApp.Client/Pages/Wuerfel.razor: /wuerfel, [Authorize].
- DsaWuerfelApp/DsaWuerfelApp.Client/Pages/Kampf.razor: /kampf, [Authorize].
- DsaWuerfelApp/DsaWuerfelApp.Client/Pages/HeldenVerwaltung.razor: /helden-verwaltung, [Authorize].
- DsaWuerfelApp/DsaWuerfelApp.Client/Pages/NotFound.razor: Fallback mit Navigation nach /.

### Main Layout

DsaWuerfelApp/DsaWuerfelApp.Client/Layout/MainLayout.razor

- MainLayout erbt von LayoutComponentBase.
- Es rendert MudPopoverProvider, MudDialogProvider und MudSnackbarProvider.
- Die Struktur ist div.page mit div.sidebar/NavMenu und main > article.content/Body.
- Eine Footer-Komponente oder ein Footer-Bereich ist nicht vorhanden.
- Das Layout besitzt keine eigene MudThemeProvider-Instanz und keinen zentralen Content-Maximalcontainer.

DsaWuerfelApp/DsaWuerfelApp.Client/Layout/MainLayout.razor.css

- .page: display:flex, zunächst vertikale Richtung, min-height:100vh.
- main: flexibler Bereich mit min-height:0; .content ist ebenfalls flexibel und hat kein zusätzliches Padding.
- Ab @media (min-width:641px) wird .page horizontal; .sidebar erhält width:5.5rem, height:100vh, position:sticky, top:0 und flex:0 0 auto.
- Bei .nav-toggle:checked wird die Desktop-Sidebar auf 17rem verbreitert.
- Die Blazor-Fehlerleiste ist unabhängig davon position:fixed, bottom:0, left:0, width:100%, z-index:1000.

### Navigation

DsaWuerfelApp/DsaWuerfelApp.Client/Layout/NavMenu.razor

- NavMenu ist eine zustandsabhängige Navigation und implementiert IDisposable.
- Es injiziert ActiveHeroState, AuthState, SessionState und SessionHeroSyncService.
- Es zeigt abhängig vom Authentifizierungszustand Account, aktiven Helden, Session-Liste und Links für Würfeln, Kampf und Heldenverwaltung.
- Die Session-Liste wird über SessionTree gerendert.
- Ein verstecktes Checkbox-Element #nav-expanded und das Label .nav-expand-button bilden den Expand/Collapse-Mechanismus der Desktop-Sidebar; ein separates Drawer- oder Menu-Objekt wird nicht verwendet.
- Beim Initialisieren werden Auth-, Helden- und Sessionzustand geladen und SessionHeroSyncService.AttachAsync aufgerufen.
- AuthState.Changed, ActiveHeroState.Changed und SessionState.Changed werden abonniert und bei Dispose wieder entfernt.

DsaWuerfelApp/DsaWuerfelApp.Client/Layout/NavMenu.razor.css

- .nav-panel ist ein vertikaler Flex-Container mit Gradient, rechter Border und Shadow.
- Die Standardnavigation zeigt im kompakten Zustand nur Icons; .nav-text ist zunächst display:none.
- Ab 641px sind aktive Heldenanzeige und Desktop-Expand-Regeln vorhanden. Im expandierten Zustand werden Texte, Account-Mail und Session-Panel sichtbar.
- Unter 640px wird .nav-panel nur auf min-height:auto umgestellt; .nav-text wird dort nicht sichtbar geschaltet. Das Session-Panel wird unter 640px explizit angezeigt.
- Account-E-Mail, Header-Elemente und Linkgrößen besitzen feste bzw. minimale Höhen von etwa 3.25rem; die E-Mail verwendet Ellipsis.

### Host und Server-Routing

DsaWuerfelApp/DsaWuerfelApp/Program.cs registriert Controller, statische Dateien, Blazor Framework Files, MapControllers, MapHub<GameHub>("/gamehub") und MapFallbackToFile("index.html"). Das beeinflusst die Client-Shell und den Reconnect-/Sessionkontext, ist aber kein weiteres UI-Layout.

DsaWuerfelApp/DsaWuerfelApp.Client/wwwroot/index.html

- Der Viewport-Meta-Tag ist width=device-width, initial-scale=1.0.
- Geladen werden MudBlazor-CSS, app.css, die generierte CSS-Isolationsdatei DsaWuerfelApp.styles.css, Blazor WASM und MudBlazor-JavaScript.
- Three.js wird über eine Importmap von unpkg.com eingebunden.

## 4. Responsive Behaviour Already Present

Die Media Queries sind lokal in den CSS-Isolationsdateien verteilt. Zentrale Breakpoint-Konstanten oder eine gemeinsame Responsive Utility wurden nicht gefunden.

| Datei | vorhandene Schwellen und Verhalten |
|---|---|
| Layout/MainLayout.razor.css | min-width:641px: Desktop-Row, sticky Sidebar, 5.5/17rem Sidebar |
| Layout/NavMenu.razor.css | max-width:640px: Panel-Höhe und sichtbares Session-Panel; min-width:641px: kompakte/expandierte Desktop-Navigation |
| Pages/Lobby.razor.css | max-width:1080px: einspaltige Reihenfolge; max-width:760px: kleinere Abstände, einspaltige Statistiken, Form- und Button-Anpassungen |
| Pages/HeldenVerwaltung.razor.css | max-width:640px: Padding, Dropzone und Hero-Karten werden schmaler bzw. einspaltig |
| Pages/Wuerfel.razor.css | min-width:901px und min-width:1200px: Desktop-Grids; max-width:900px und max-width:640px: kleinere Dice-Fläche und Probe-Suchzeile; container-Query für .wuerfel-top |
| Components/WuerfelActionBar.razor.css | min-width:901px, max-width:1250px, max-width:640px; Aktionen und Containerbreiten werden unterschiedlich gewrappt |
| Components/WuerfelInformationPanel.razor.css | max-width:640px: Info-Felder von Grid auf eine Spalte |
| Components/WuerfelProbePanel.razor.css | max-width:640px: Suchzeile/Panel werden vertikal angeordnet |
| Components/ProbenSearch.razor.css | max-width:640px: Das Dropdown steht als eigener Bereich im normalen Fluss; die Suchpille bleibt kompakt |
| Pages/Kampf.razor.css | max-width:1280px: Shell einspaltig; max-width:980px: weitere Grids einspaltig; max-width:760px: kompakte Abstände und Header |

Es gibt keine gefundenen C#- oder JavaScript-Mechanismen für matchMedia, innerWidth, Desktop-/Mobile-Erkennung oder einen zentralen Viewport-Service. wwwroot/js/dice-scene.js reagiert nur auf die Größe des Three.js-Canvas und nutzt eine eigene Resize-Berechnung. wwwroot/js/hero-dropzone.js behandelt Drag-and-Drop-Dateien, enthält aber keine Responsive-Logik.

## 5. Key UI Components

### SessionTree

Path: DsaWuerfelApp/DsaWuerfelApp.Client/Components/SessionTree.razor sowie .razor.cs und .razor.css

Responsibility: Wiederverwendbare Session-Liste mit Auf-/Zuklappen, Öffnen, Beitreten/Verlassen, Umbenennen, Löschen, Join-Code-Kopie und optionaler Mitglieder-/Spielerbearbeitung.

Relevant symbols:

- SessionTree
- Parameter Sessions, ActiveSessionId, ShowJoinCode, ShowCopyButton, ShowManagement, ShowPlayerEditing
- OpenAsync, LeaveAsync, RenameAsync, DeleteAsync, CopyCodeAsync

Dependencies / used by:

- Injiziert AuthState, SessionState, IJSRuntime, NavigationManager.
- Wird von Layout/NavMenu.razor und Pages/Lobby.razor verwendet.

Responsive concerns: Der Session-Header nutzt einen flexiblen Toggle-Bereich, aber .session-header-meta bleibt flex:0 0 auto und flex-wrap:nowrap. Mehrere Aktionen, Code-Button, Status und lange Session-Namen liegen damit auf einer schmalen Zeile; der Name hat Ellipsis. Verschachtelte Member- und Edit-Bereiche können die nutzbare Breite zusätzlich reduzieren. Es existiert keine eigene Media Query.

### WuerfelActionBar

Path: DsaWuerfelApp/DsaWuerfelApp.Client/Components/WuerfelActionBar.razor sowie .razor.cs und .razor.css

Responsibility: Ergebnis-/Rollaktionsleiste mit Text-Pill, Modifikator, Refresh, Wurfaktion und Hidden-Roll-Hinweis.

Relevant symbols: WuerfelActionBar, Parameter RollText, Modifier, DisplayModifier, CanRoll, IsBusy, IsHiddenRoll, ResetRequested, RollRequested, ToggleHiddenRollRequested.

Dependencies / used by: Pages/Wuerfel.razor; verwendet TextPill, ModifierPill und MudBlazor-Buttons.

Responsive concerns: .results-bar nutzt width:100%, min-width:0, min-height:175px und eine Container-Query bei 560px. Unter kompakter Breite werden Text-, Modifier- und Icon-Aktionen innerhalb der vorhandenen Leiste gewrappt; die bestehenden 48px-Icon- und 132px-Buttongrößen bleiben als Bedienziele erhalten.

### WuerfelProbePanel und ProbenSearch

Paths: DsaWuerfelApp/DsaWuerfelApp.Client/Components/WuerfelProbePanel.razor (.cs/.css) und DsaWuerfelApp/DsaWuerfelApp.Client/Components/ProbenSearch.razor (.cs/.css)

Responsibility: Suche und Auswahl von Proben, optionale Zauberoptionen sowie erzwungene Würfe; ProbenSearch stellt das Suchfeld mit Dropdown bereit.

Relevant symbols: WuerfelProbePanel, ProbenSearch, Parameter für Probes, SelectedProbe, ProbeInfo, SpellOptions, IsBusy und die Callback-Events.

Dependencies / used by: Pages/Wuerfel.razor; verwendet MudIconButton, native <details>/<summary> und den lokalen .search-control-Container mit Suchfeld und Dropdown.

Responsive concerns: Das Suchfeld verwendet flex:1/min-width:0 und bleibt als .search-wrapper 45px hoch. Die Probe-Optionen nutzen repeat(auto-fit,minmax(min(190px,100%),1fr)). Unter 640px wird die Suchzeile vertikal und das Dropdown als Geschwister der Suchpille im normalen Fluss angeordnet; auf größeren Breiten bleibt es absolut unter dem Feld. ProbenSearch begrenzt die Liste auf 280px, verwendet .dsa-scroll-region und hält auswählbare Treffer mit mindestens 44px erreichbar.

### WuerfelInformationPanel

Path: DsaWuerfelApp/DsaWuerfelApp.Client/Components/WuerfelInformationPanel.razor und .razor.css

Responsibility: Zeigt aktuelle Probe-/Talent-/Attribut-/schlechte-Eigenschaft-Informationen sowie Ergebnislisten für den Meister.

Relevant symbols: WuerfelInformationPanel, ProbeInfo, ShowProbeInfoDetails, TalentResult, AttributeResult, BadTraitResult, MasterTalentResults, MasterAttributeResults.

Used by: Pages/Wuerfel.razor, als zusätzlicher Info-Bereich neben der Würfelansicht.

Responsive concerns: Der innere Inhalt nutzt auf größeren Hosts .dsa-scroll-region; unter 640px wird er in den normalen Dokumentfluss entlassen. Info-Felder wechseln bei 640px von minmax(96px,max-content) minmax(0,1fr) auf eine Spalte; Ergebnis-Chips verwenden repeat(auto-fit,minmax(92px,1fr)).

### AttributePanel, AttributePill, ModifierPill, TextPill

Paths: Components/AttributePanel.razor(.cs/.css), Components/AttributePill.razor(.cs/.css), Components/ModifierPill.razor(.cs/.css), Components/TextPill.razor(.css)

Responsibility: Wiederverwendbare Auswahl-/Eingabe-Pills für Attribute, numerische Modifikatoren und Rolltext.

Relevant symbols: AttributePanel mit acht konkreten AttributePill-Instanzen, AttributePill, ModifierPill, TextPill.

Used by: AttributePanel und Pages/Kampf.razor verwenden Attribute-/Modifier-Pills; WuerfelActionBar verwendet Modifier-/Text-Pills.

Responsive concerns: AttributePanel ist ein flex-wrap-Container, aber einzelne Elemente sind etwa 80px × 45px. ModifierPill hat min-width:140px, TextPill min-width:220px und max-width:280px; Kampf überschreibt einzelne Mindestgrößen lokal. Mehrere Pillenreihen können bei kleinen Breiten dicht oder mehrzeilig werden. Attribute werden als klickbare div-Elemente mit Contextmenu behandelt.

### DiceControls, DiceViewport und Dice3D

Paths: Components/DiceControls.razor(.css), Components/DiceViewport.razor(.cs), Components/Dice3D.razor(.cs), wwwroot/js/dice3d.js, wwwroot/js/dice-scene.js

Responsibility: Würfelauswahl und Three.js-Canvas für Vorschau/Ergebnis.

Relevant symbols: DiceControls, DiceViewport, Dice3D; JavaScript createDiceScene, resizeScene, dispose.

Used by: Pages/Wuerfel.razor; DiceViewport übergibt Selektions- und Ergebnisdaten an die JS-Instanz.

Responsive concerns: Die Controls nutzen MudBlazor-Flex-Utilities und sechs Würfelgrößen von etwa 60px. Der Canvas nimmt width:100%; height:100% ein; die JS-Szene berechnet clientWidth/clientHeight, setzt die Kamera-Aspect-Ratio und passt bei schmaler Breite die Kamera an. Die Szene registriert globale Resize- und Canvas-Click-Listener und räumt diese beim Dispose auf.

### RollHistory und SchlechteEigenschaftPanel

Paths: Components/RollHistory.razor(.cs/.css), Components/SchlechteEigenschaftPanel.razor(.cs/.css)

Responsibility: Verlauf der Würfe sowie Auswahl/Anzeige schlechter Eigenschaften.

Dependencies / used by: Beide werden von Pages/Wuerfel.razor verwendet.

Responsive concerns: RollHistory scrollt vertikal; Würfel im Verlauf werden mit etwa 32px-Containern und flex-wrap dargestellt. SchlechteEigenschaftPanel nutzt Wrap- und Flex-Layouts, aber keinen eigenen Breakpoint. Beide sind Teile der bereits verschachtelten Würfelansicht.

## 6. Pages / Screens

### Lobby / Session-Workbench

DsaWuerfelApp/DsaWuerfelApp.Client/Pages/Lobby.razor, .razor.cs, .razor.css

- Lobby kombiniert Login/Magic-Link, Account-Zusammenfassung, Session-Beitritt, Session-Erstellung und SessionTree.
- Lobby injiziert AuthState, IAuthApiClient, GameClient, SessionState und NavigationManager; Änderungen am Auth-/Sessionzustand beeinflussen die Anzeige und die Navigation nach /wuerfel.
- .lobby-shell ist ein zweispaltiges Grid mit max-width:1320px, Spalten minmax(340px,.92fr) und minmax(0,1.28fr).
- Bei 1080px wird die Reihenfolge einspaltig; bei 760px werden Statistiken, Account-Zusammenfassung, Modi und Aktionen weiter reduziert.
- Das Board verwendet min-height:24rem und overflow:auto; Session-Aktionen haben min-width:13.5rem und werden erst im kleineren Breakpoint vollbreit.

### Würfelseite

DsaWuerfelApp/DsaWuerfelApp.Client/Pages/Wuerfel.razor, .razor.cs, .razor.css

- Wuerfel ist die komplexeste aktive UI-Seite und verwendet RollHistory, WuerfelInformationPanel, WuerfelActionBar, DiceViewport, AttributePanel, WuerfelProbePanel, SchlechteEigenschaftPanel und DiceControls.
- Der Codebehind delegiert Benutzeraktionen an WuerfelFacade; WuerfelState, SessionState, AuthState und die Context-Orchestrierung liefern den View-Zustand.
- Die Seite besitzt oben History/Info/Masterauswahl und darunter Aktion/3D-Würfel/Probe-/Attribut-Blöcke.
- Ab 901px gibt es ein mehrspaltiges Desktop-Grid; ab 1200px nochmals größere Mindestspalten. Unter 900px und 640px werden nur einzelne Bereiche umgestellt.
- Die Seite nutzt min-height:100vh und ab 901px height:100dvh mit min-width:0 in den relevanten Grid-Ketten. Die History behält height:40vh ohne die frühere min-height:320px-Untergrenze; nur der Verlauf selbst besitzt den vorgesehenen Scrollbesitzer. Action-Blöcke für Attribute und Proben lassen Dropdowns bzw. Inhalte sichtbar wachsen.

### Kampfseite

DsaWuerfelApp/DsaWuerfelApp.Client/Pages/Kampf.razor, .razor.cs, .razor.css

- Kampf ist derzeit eine eigenständige WIP-/Mock-Ansicht ohne Services und mit lokalen Listen/Properties.
- Die Oberfläche enthält Status-/Zustandsübersicht, Wund-/Waffeninformationen, eine Such-Workbench, Attribute/Modifier sowie Initiative- und Verlaufsseitenleisten.
- .combat-shell hat max-width:1420px, height:100dvh, Desktop-Spalten minmax(0,1.66fr)/minmax(300px,.84fr) und overflow:hidden.
- Bei 1280px wird die Shell einspaltig und overflow:visible; bei 980px und 760px folgen weitere einspaltige Grids und kompakte Abstände.
- Listen scrollen innerhalb von Panels. Das Such-Dropdown ist absolut positioniert und hat in dieser Seite keine eigene maximale Höhe/Overflow-Regel.

### Heldenverwaltung

DsaWuerfelApp/DsaWuerfelApp.Client/Pages/HeldenVerwaltung.razor, .razor.cs, .razor.css

- HeldenVerwaltung verwendet InputFile, Drag-and-Drop über wwwroot/js/hero-dropzone.js, Upload/Fehleranzeige und Hero-Karten mit Aktivieren/Löschen.
- ActiveHeroState und IHeroApiClient verbinden die Seite mit Navigation und Würfelkontext.
- Der Container hat max-width:1100px; Hero-Karten verwenden repeat(auto-fit,minmax(260px,1fr)).
- Unter 640px werden Container/Dropzone reduziert, Karten und Aktionen einspaltig bzw. vollbreit.

### NotFound

DsaWuerfelApp/DsaWuerfelApp.Client/Pages/NotFound.razor nutzt ein MudPaper mit pa-6 ma-6 und Navigation zurück zur Lobby; es gibt keine eigene CSS-Datei.

## 7. Styling Architecture

- Lokale Styles liegen überwiegend als CSS-isolierte *.razor.css neben der Razor-Komponente. Die generierte Zusammenführung DsaWuerfelApp.styles.css wird in wwwroot/index.html geladen.
- DsaWuerfelApp/DsaWuerfelApp.Client/wwwroot/app.css enthält globale Farben, --ui-touch-target und die wiederverwendbare Scrollbar-Klasse .dsa-scroll-region. Definiert sind --dsa-blue, --dsa-gold, --dsa-gold-glow, --dsa-white und --pill-bg.
- app.css enthält außerdem die globale Blazor-Fehlerleiste und die Würfel-Shape-Klassen (.die-shape, .d4, .d6, .d8, .d10, .d12, .d20).
- Es gibt keinen ermittelten zentralen MudTheme, keine eigene MudThemeProvider-Konfiguration, keine zentrale Breakpoint-Konstante und keinen zentralen Spacing-/Sizing-Token-Satz.
- MudBlazor-Utility-Klassen werden direkt im Markup genutzt, zum Beispiel pa-*, ma-*, d-flex, flex-wrap, gap-*, justify-* und align-*. Daneben existieren viele lokale Klassen wie .dsa-btn, .dsa-btn-icon, .panel-surface, .search-input und .pill.
- Wiederkehrende lokale CSS-Muster sind display:flex, min-width:0, natürliche Höhen im kompakten Fluss, begrenzte overflow-y:auto-Regionen, Grid-Spalten mit minmax, große border-radius-Werte und wiederholte Farbverläufe.
- Es gibt keine SCSS-Dateien, kein Tailwind und keine zentrale CSS-Datei mit allen Media Queries.
- Verwendete Größen mischen rem, px, %, vh, vw, dvh und clamp(). Mehrere Komponenten definieren ihre eigenen Mindestbreiten und Höhen.
- Eine globale overflow-x-Strategie wurde nicht eingeführt; das Proben-Dropdown begrenzt horizontalen Überlauf gezielt mit overflow-x:hidden. Weitere Bereiche verwenden vor allem overflow-y:auto, overflow:auto und overflow:hidden an nachgewiesenen Grenzen.

## 8. Potential Mobile Problem Areas

Die folgenden Punkte sind technische Befunde aus dem ursprünglichen Codebestand. Sie beschreiben keine Umsetzungsvorgaben; realisierte Korrekturen und der aktuelle Stand stehen in Abschnitt 16.

- Layout/MainLayout.razor.css, .page, .sidebar: Die Desktop-Navigation ist ab 641px sticky und 5.5rem bzw. expandiert 17rem breit. Unterhalb davon wird die Layout-Richtung vertikal, während NavMenu als regulärer hoher Bereich weitergerendert wird. Die verfügbare Höhe für Seiteninhalt kann auf kleinen Viewports dadurch stark sinken.
- Layout/NavMenu.razor.css, .nav-text und .nav-panel: Unter 640px gibt es keine Regel, die die ausgeblendeten Linktexte sichtbar macht. Gleichzeitig bleibt das Session-Panel sichtbar und die Navigation enthält feste 3.25rem-Elementhöhen. Account, SessionTree und Links können dadurch viel vertikalen Raum beanspruchen, während die Textführung der Links fehlt.
- Layout/NavMenu.razor plus SessionTree: Die Navigation lädt und synchronisiert Auth-, Helden- und Sessionzustand bereits aus dem Layout heraus. Änderungen am Layout müssen daher die Event-Abonnements und die Initialisierung von SessionHeroSyncService berücksichtigen.
- Pages/Wuerfel.razor.css, .top-section, .middle-section, .history-area: Im Ausgangsstand kombinierten diese Bereiche height:40vh, min-height:320px, Desktop-Spalten mit Mindestwerten und eine Desktop-Höhe von 100dvh. Diese Befunde wurden nach Messung durch min-width:0, natürliche kompakte Höhen und getrennte Scrollbesitzer reduziert.
- Pages/Wuerfel.razor.css, .action-blocks-container, .results-bar: Action-Blöcke haben minmax(320px,1fr), der Ergebnisbereich min-width:320px; zusätzlich gibt es TextPill/ModifierPill mit Mindestbreiten. Das ist eine konkrete Quelle für dichte oder mehrzeilige Bereiche auf schmalen Breiten.
- Pages/Wuerfel.razor.css, .dice-3d-box: Die 3D-Fläche ist auf Desktop 175px, auf schmaleren Breiten 150px hoch und abgeschnitten (overflow:hidden). Die nutzbare Canvasgröße hängt zugleich von der umgebenden Grid-/Flexgröße ab.
- Pages/Wuerfel.razor.css und Components/WuerfelInformationPanel.razor.css: Im Ausgangsstand besaßen History, Info, Action-Block und Dropdowns mehrere Scrollbereiche. Aktuell bleiben der Verlauf, die große Infoansicht und die begrenzte Suchliste als jeweils begründete Besitzer; kompakte Action- und Info-Panels wachsen im Dokumentfluss.
- Components/SessionTree.razor.css, .session-header-meta: Das Aktionscluster darf nicht umbrechen (flex-wrap:nowrap) und bleibt eigenständig breit. Zusammen mit Code-/Status-/Management-Aktionen ist die Session-Zeile für schmale Breiten besonders relevant.
- Components/WuerfelActionBar.razor.css: min-height, min-width, Mindestbreiten der Pills und der 48px-Icon-Button werden in mehreren Breakpoint-Regeln kombiniert. Die Anordnung von Text-, Modifikator- und Rollaktionen hängt stark vom lokalen Wrap-Verhalten ab.
- Pages/Kampf.razor.css, .combat-shell: Die Basisansicht nutzt height:100dvh, overflow:hidden, eine Mindest-Seitenleistenspalte von 300px und mehrere weitere Mindestspalten. Die Breakpoint-Regeln lösen dies erst ab 1280px, 980px und 760px schrittweise auf.
- Pages/Kampf.razor.css, .search-dropdown: Das Kampf-Suchdropdown ist position:absolute mit left/right:0, aber ohne eigene max-height- oder overflow-Regel. Die Anzahl der lokalen Mock-Einträge ist aktuell begrenzt, das Layoutmuster bleibt dennoch relevant.
- Pages/Lobby.razor.css, .lobby-shell und .session-btn: Die Lobby startet mit max-width:1320px, einer 340px-Mindestspalte und Buttons mit min-width:13.5rem; Anpassungen für 760px sind vorhanden, aber davor ist die Struktur relativ breit.
- Pages/HeldenVerwaltung.razor.css, .hero-grid und .drop-zone: minmax(260px,1fr), große Dropzone-Paddings und ein max-width:1100px sind auf großen Breiten unkritisch; der 640px-Breakpoint enthält bereits explizite Anpassungen. Touch-Verhalten des Drag-and-Drop-Skripts wurde nicht durch einen mobilen Browserlauf verifiziert.
- Globale Architektur: Es wurde keine overflow-x-Strategie, kein zentraler Mobile-/Desktop-Modus und kein gemeinsamer Breakpoint-Satz gefunden. Die vorhandenen Medienregeln können bei Änderungen an Shell oder gemeinsamen Komponenten mehrere Seiten gleichzeitig beeinflussen.
- Tabellen/Dialogs/Drawers: Es wurden keine HTML-table, MudTable, MudDrawer oder eigene feste Mud-Dialoggrößen gefunden. Dialog-/Popover-Provider sind im MainLayout registriert, aber die untersuchten UI-Flows verwenden überwiegend Panels, <details> sowie browser-native alert/confirm aus SessionTree.

## 9. Dependency Map

~~~text
App
└── Routes
    ├── RouteAccessView
    │   └── RouteView / MainLayout
    └── MainLayout
        ├── MudPopoverProvider
        ├── MudDialogProvider
        ├── MudSnackbarProvider
        ├── NavMenu
        │   ├── AuthState
        │   ├── ActiveHeroState
        │   ├── SessionState
        │   ├── SessionHeroSyncService
        │   └── SessionTree
        └── Body
            ├── Lobby
            │   └── SessionTree
            ├── Wuerfel
            │   ├── WuerfelFacade
            │   ├── WuerfelState
            │   ├── WuerfelActionBar
            │   ├── WuerfelProbePanel ── ProbenSearch
            │   ├── WuerfelInformationPanel
            │   ├── DiceViewport ── Dice3D ── dice3d.js/dice-scene.js
            │   ├── AttributePanel ── AttributePill
            │   ├── ModifierPill / TextPill
            │   ├── RollHistory
            │   └── SchlechteEigenschaftPanel
            ├── Kampf
            │   ├── AttributePill
            │   ├── ModifierPill
            │   └── Proben-/Such-/Status-Markup
            └── HeldenVerwaltung
                └── ActiveHeroState ── hero-dropzone.js
~~~

State-Verbindungen:

- AuthState ist die gemeinsame Authentifizierungsquelle für RouteAccessView, NavMenu, Lobby, SessionState und Würfelkontext.
- ActiveHeroState wird von HeldenVerwaltung verändert und von NavMenu sowie Würfel-Orchestrierung gelesen.
- SessionState hält Sessionliste und aktive Session, persistiert den aktiven Session-Schlüssel lokal und arbeitet mit GameClient/Realtime-Ereignissen.
- WuerfelState hält den Würfel-View-Zustand; WuerfelFacade, WuerfelContextService, WuerfelSessionBridge und WuerfelSignalREventBridge aktualisieren ihn.
- Alle genannten State-/Orchestration-Services sind in Client/Program.cs als scoped Services registriert. Eine externe globale State-Management-Bibliothek wurde nicht gefunden.

## 10. High-Leverage Files

1. DsaWuerfelApp/DsaWuerfelApp.Client/Layout/MainLayout.razor — Root-Layout, Provider, Sidebar-/Content-Struktur.
2. DsaWuerfelApp/DsaWuerfelApp.Client/Layout/MainLayout.razor.css — Desktop-/Mobile-Layoutachse, Sticky-Sidebar, Fehlerleiste.
3. DsaWuerfelApp/DsaWuerfelApp.Client/Layout/NavMenu.razor — zustandsabhängige Navigation und Session-Synchronisierung.
4. DsaWuerfelApp/DsaWuerfelApp.Client/Layout/NavMenu.razor.css — Icon-/Textnavigation, Checkbox-Expand, mobile Regeln.
5. DsaWuerfelApp/DsaWuerfelApp.Client/Routes.razor — Router und Fallback-Layout.
6. DsaWuerfelApp/DsaWuerfelApp.Client/RouteAccessView.razor — Authentifizierungs-Gate und Default-Layout.
7. DsaWuerfelApp/DsaWuerfelApp.Client/Program.cs — MudBlazor, State- und Würfel-Service-Registrierungen.
8. DsaWuerfelApp/DsaWuerfelApp.Client/wwwroot/index.html — Viewport-Meta, globale CSS-/JS-Ladefolge.
9. DsaWuerfelApp/DsaWuerfelApp.Client/wwwroot/app.css — globale Farben und Würfel-Utilities.
10. DsaWuerfelApp/DsaWuerfelApp.Client/Pages/Wuerfel.razor — zentrale Würfel-Seitenkomposition.
11. DsaWuerfelApp/DsaWuerfelApp.Client/Pages/Wuerfel.razor.css — größtes responsives Grid und viele Mindestgrößen.
12. DsaWuerfelApp/DsaWuerfelApp.Client/Components/WuerfelActionBar.razor — zentrale Rollaktionen.
13. DsaWuerfelApp/DsaWuerfelApp.Client/Components/WuerfelActionBar.razor.css — Aktionsleisten-Wrap und Mindestgrößen.
14. DsaWuerfelApp/DsaWuerfelApp.Client/Components/WuerfelProbePanel.razor — Probe-/Spell-Eingaben.
15. DsaWuerfelApp/DsaWuerfelApp.Client/Components/WuerfelProbePanel.razor.css — schmale Eingabeanordnung.
16. DsaWuerfelApp/DsaWuerfelApp.Client/Components/ProbenSearch.razor.css — kompakte Suchpille, responsiver Dropdown-Fluss und lokale Ergebnis-Scrollbar.
17. DsaWuerfelApp/DsaWuerfelApp.Client/Components/WuerfelInformationPanel.razor.css — innerer Scrollbereich und Info-Grids.
18. DsaWuerfelApp/DsaWuerfelApp.Client/Components/SessionTree.razor — gemeinsamer Session-Inhalt in Lobby und Navigation.
19. DsaWuerfelApp/DsaWuerfelApp.Client/Components/SessionTree.razor.css — Session-Header und Aktionscluster.
20. DsaWuerfelApp/DsaWuerfelApp.Client/Pages/Lobby.razor.css — repräsentatives Workbench-/Board-Layout.
21. DsaWuerfelApp/DsaWuerfelApp.Client/Pages/HeldenVerwaltung.razor.css — repräsentatives Upload-/Karten-Grid mit Mobile-Regeln.
22. DsaWuerfelApp/DsaWuerfelApp.Client/Pages/Kampf.razor.css — dichtes Vollbild-/Sidebar-Layout und viele Breakpoints.
23. DsaWuerfelApp/DsaWuerfelApp.Client/Services/State/SessionState.cs — Sessionzustand, aktiver Kontext und Navigationseinfluss.
24. DsaWuerfelApp/DsaWuerfelApp.Client/Services/State/WuerfelState.cs — gemeinsamer Würfel-View-Zustand und Reset-Verhalten.
25. DsaWuerfelApp/DsaWuerfelApp.Client/Services/State/ActiveHeroState.cs — aktiver Held als Verknüpfung zwischen Verwaltung, Navigation und Würfelkontext.

## 11. Secondary Files

- DsaWuerfelApp/DsaWuerfelApp.Client/Pages/Lobby.razor und .razor.cs — Formzustand, Sessionaktionen und Auth-Flows der Lobby.
- DsaWuerfelApp/DsaWuerfelApp.Client/Pages/Wuerfel.razor.cs — Delegation der Würfelinteraktionen und State-Abonnements.
- DsaWuerfelApp/DsaWuerfelApp.Client/Pages/Kampf.razor und .razor.cs — WIP-Markup und lokale Mock-Daten der Kampfseite.
- DsaWuerfelApp/DsaWuerfelApp.Client/Pages/HeldenVerwaltung.razor und .razor.cs — Upload-/Hero-State und JS-Registrierung.
- DsaWuerfelApp/DsaWuerfelApp.Client/Pages/NotFound.razor — einfacher Fallback-Screen.
- DsaWuerfelApp/DsaWuerfelApp.Client/Services/State/AuthState.cs — Auth-Ereignisse, die Route und Navigation steuern.
- DsaWuerfelApp/DsaWuerfelApp.Client/Services/Wuerfel/Orchestration/WuerfelFacade.cs — Fassade für UI-Aktionen.
- DsaWuerfelApp/DsaWuerfelApp.Client/Services/Wuerfel/Orchestration/WuerfelContextService.cs, WuerfelContextSubscription.cs, WuerfelSessionBridge.cs, WuerfelSignalREventBridge.cs — Ableitung und Synchronisierung des Würfelkontexts.
- DsaWuerfelApp/DsaWuerfelApp.Client/Services/Wuerfel/Orchestration/SessionHeroSyncService.cs — Synchronisierung aktiver Helden mit der Session.
- Components/AttributePanel.razor(.cs/.css), AttributePill.razor(.cs/.css), ModifierPill.razor(.cs/.css), TextPill.razor(.css) — wiederverwendete dichte Eingabepills.
- Components/DiceControls.razor(.css), DiceViewport.razor(.cs), Dice3D.razor(.cs) sowie wwwroot/js/dice3d.js und dice-scene.js — Canvas-/Würfelinteraktion.
- Components/RollHistory.razor(.cs/.css), SchlechteEigenschaftPanel.razor(.cs/.css), RollEquation.razor(.cs/.css) — weitere Würfelseiten-Teilflächen.
- Components/CheckForms/MeleeCheck.razor und SpellCheck.razor — vorhandene Formkomponenten mit MudGrid, deren aktive Verwendung im Client nicht gefunden wurde.
- Components/DiceActionPanel.razor(.cs/.css) — vorhandene, in den aktiven Seiten nicht referenzierte Komponente.
- wwwroot/js/hero-dropzone.js — Datei-Dropzone der Heldenverwaltung.
- DsaWuerfelApp/DsaWuerfelApp.Client/DsaWuerfelApp.Client.csproj — Blazor-WASM-/MudBlazor-/SignalR-Abhängigkeiten.
- DsaWuerfelApp/DsaWuerfelApp.Tests/Browser/app-workflows.cjs — Playwright-End-to-End-Workflows.
- DsaWuerfelApp/DsaWuerfelApp.Tests/Browser/dice-lifecycle.cjs — isolierter Three.js-Lifecycle-Test.
- DsaWuerfelApp/DsaWuerfelApp.Tests/ClientStateTests.cs — xUnit-Tests für Client-State/Orchestrierung.
- Plan.md — projektinterne Build-, Test- und Browser-Validierungshinweise sowie WIP-Status.
- .github/workflows/deploy.yml — vorhandener Release-Publish-Workflow.

## 12. Tests and Validation

- Testframework: xUnit 2.9.3 mit Microsoft.NET.Test.Sdk; Server- und Integrationsabdeckung liegt in DsaWuerfelApp.Tests.
- Es gibt keine gefundene bUnit-, Playwright- oder andere Komponententest-Abhängigkeit im Testprojekt. Playwright wird von separaten Node-Skripten verwendet.
- ClientStateTests.cs deckt State-/Orchestrierungsverhalten ab, nicht CSS-Layout oder Viewportdarstellung.
- Browser/app-workflows.cjs startet eine veröffentlichte Anwendung und prüft Login, Session erstellen/beitreten, Helden-Synchronisierung, Würfe, Navigation, Rename/Leave und Reconnect. Die Selektoren sind überwiegend funktional; die separate responsive-ui.cjs-Matrix ergänzt feste Viewports, Geometrie- und Überlaufprüfungen.
- Browser/dice-lifecycle.cjs prüft Three.js-Instanzen, Animationen, Listener und Dispose-Verhalten isoliert; es ist kein Blazor-Layouttest.
- Aus Plan.md ermittelte Befehle:
  - dotnet build DsaWuerfelApp.sln -c Release -v minimal
  - dotnet test DsaWuerfelApp.sln -c Release --no-restore -v normal
  - dotnet publish DsaWuerfelApp/DsaWuerfelApp/DsaWuerfelApp.csproj -c Release --output <temp> -v minimal
  - git diff --check
- Der Browser-Workflow benötigt zusätzlich Node-/Playwright-Abhängigkeiten und eine veröffentlichte Anwendung; ein eigenständiger Frontend-Test-/Build-Script-Eintrag wurde nicht gefunden.
- Ein dedizierter Lint- oder Formatierungsbefehl ist im Repository nicht ermittelbar. Plan.md beschreibt keine umfassende Formatierungsautomatisierung.
- Die ursprüngliche Context-Discovery führte die Tests nicht erneut aus. Der aktuelle Ausführungsstand und die bekannten Baseline-Fehler sind in Abschnitt 16 dokumentiert.

## 13. Architectural Constraints

- Das Frontend ist Blazor WebAssembly und wird vom ASP.NET-Core-Server gehostet. UI-Routen werden über @page-Direktiven und RouteView gefunden.
- RouteAccessView behandelt [AllowAnonymous] und alle anderen Routen über den vorhandenen AuthState; diese Routing-/Auth-Konvention ist in den aktiven Seiten eingebaut.
- CSS-Isolation und MudBlazor sind die vorhandenen UI-Konventionen. Ein Wechsel auf ein anderes Styling-System oder ein neues globales Theme ist aus dem Repository nicht ableitbar.
- MainLayout stellt MudPopover-, Dialog- und Snackbar-Provider bereit; Komponenten können deren globalen Kontext voraussetzen.
- Navigation und Session-/Heldendarstellung hängen an scoped State-Services und Event-Abonnements. NavMenu ist nicht nur statisches Markup.
- Würfelinteraktionen laufen über WuerfelState und WuerfelFacade sowie Context-/Realtime-Bridges. Die Seitenansicht darf deshalb nicht isoliert von diesen Zustandsübergängen betrachtet werden.
- Kampf ist laut aktuellem Projektstand eine WIP-/Mock-Ansicht mit lokalen Daten; aus ihr sind keine fertigen Domain- oder Navigationskonventionen abzuleiten.
- Die Three.js-Komponenten besitzen explizite JS-Instanzen, globale Resize-Listener und Dispose-Logik. Die Canvas-Größe hängt an der tatsächlichen Elementgröße.
- Die Lobby und Heldenverwaltung verwenden native HTML-Eingaben/InputFile neben MudBlazor. Diese bestehenden Interaktionsformen gehören zum aktuellen UI-Vertrag.
- Plan.md beschreibt Kampf als WIP/Mock und verbietet, daraus fehlende Kampfregeln zu erfinden; für das UI-Kontextverständnis ist deshalb nur die vorhandene Layoutstruktur maßgeblich.

## 14. Unknowns / Gaps

- Es gibt kein verbindliches Design-Referenzbild und keine dokumentierten Zielgeräte, Orientierungen, Safe-Area- oder Touch-Anforderungen. Lokale Desktop-Baselines und temporäre kompakte Screenshots wurden für die Implementierungsprüfung erzeugt, sind aber keine Geräteabnahme.
- Die Browser-Matrix nutzt feste kompakte und breite Viewports und misst tatsächliche Layout-Überläufe; sie emuliert kein physisches Gerät und ersetzt keine iOS-/Android-Prüfung. Problemstellen ohne passenden Datensatz bleiben deshalb als offene Abnahmebefunde dokumentiert.
- Es gibt keine bUnit-/Komponententests für Shell, Navigation, CSS-Isolation oder responsive Zustände.
- Das tatsächliche Verhalten der MudBlazor-Standard-Breakpoints und des nicht explizit konfigurierten Themes wurde nicht durch eine separate MudBlazor-Konfigurationsdatei erklärt.
- Es ist nicht geklärt, ob die WIP-Kampfseite im gleichen Umfang wie Lobby, Würfel und Heldenverwaltung produktiv priorisiert werden soll.
- Die generierte Datei DsaWuerfelApp.styles.css wurde nicht als primäre Quelle analysiert; maßgeblich betrachtet wurden die zugehörigen *.razor.css-Quellen.
- Es gibt keine zentrale Dokumentation für Browser-Support, minimale Breite oder gewünschtes Verhalten bei Tastatur, Screenreader und Touch.
- Das Drag-and-Drop-Verhalten von hero-dropzone.js auf Touch-Geräten wurde nicht in einem mobilen Browser validiert.
- Der genaue Zustand beim Wechsel zwischen NavMenu, aktiver Session, aktivem Helden und Würfelseite ist aus den Event-/Orchestration-Services ableitbar, wurde aber nicht durch einen vollständigen Browserlauf jeder Kombination verifiziert.

## 15. Files Inspected

### Fully inspected

- DsaWuerfelApp/DsaWuerfelApp.Client/App.razor, Routes.razor, RouteAccessView.razor, _Imports.razor, Program.cs, wwwroot/index.html, wwwroot/app.css
- DsaWuerfelApp/DsaWuerfelApp.Client/Layout/MainLayout.razor, MainLayout.razor.css, NavMenu.razor, NavMenu.razor.css
- DsaWuerfelApp/DsaWuerfelApp.Client/Pages/Lobby.razor, Lobby.razor.cs, Lobby.razor.css
- DsaWuerfelApp/DsaWuerfelApp.Client/Pages/Wuerfel.razor, Wuerfel.razor.cs, Wuerfel.razor.css
- DsaWuerfelApp/DsaWuerfelApp.Client/Pages/Kampf.razor, Kampf.razor.cs, Kampf.razor.css
- DsaWuerfelApp/DsaWuerfelApp.Client/Pages/HeldenVerwaltung.razor, HeldenVerwaltung.razor.cs, HeldenVerwaltung.razor.css, NotFound.razor
- Components/SessionTree.razor, .razor.cs, .razor.css; WuerfelActionBar.razor, .razor.cs, .razor.css; WuerfelProbePanel.razor, .razor.cs, .razor.css; ProbenSearch.razor, .razor.cs, .razor.css
- Components/WuerfelInformationPanel.razor, .razor.css; AttributePanel.razor, .razor.cs, .razor.css; AttributePill.razor, .razor.cs, .razor.css; ModifierPill.razor, .razor.cs, .razor.css; TextPill.razor, .razor.css
- Components/DiceControls.razor, .razor.css; DiceViewport.razor, .razor.cs; Dice3D.razor, .razor.cs; RollHistory.razor, .razor.cs, .razor.css; SchlechteEigenschaftPanel.razor, .razor.cs, .razor.css; RollEquation.razor, .razor.cs, .razor.css
- DsaWuerfelApp/DsaWuerfelApp.Client/Components/CheckForms/MeleeCheck.razor, SpellCheck.razor, DiceActionPanel.razor, DiceActionPanel.razor.cs, DiceActionPanel.razor.css
- DsaWuerfelApp/DsaWuerfelApp.Client/Services/State/AuthState.cs, ActiveHeroState.cs; wwwroot/js/hero-dropzone.js, dice3d.js, dice-scene.js, dice-constants.js
- DsaWuerfelApp/DsaWuerfelApp.Tests/ClientStateTests.cs, Browser/app-workflows.cjs, Browser/dice-lifecycle.cjs

### Partially inspected / symbols searched

- DsaWuerfelApp/DsaWuerfelApp.Client/Services/State/SessionState.cs, WuerfelState.cs
- DsaWuerfelApp/DsaWuerfelApp.Client/Services/Wuerfel/Orchestration/WuerfelFacade.cs, WuerfelContextService.cs, WuerfelContextSubscription.cs, WuerfelSessionBridge.cs, WuerfelSignalREventBridge.cs, SessionHeroSyncService.cs, WuerfelUiOperationRunner.cs
- DsaWuerfelApp/DsaWuerfelApp.Client/Services/Wuerfel/Dispatching/ und Services/Transport/ relevante Typen
- DsaWuerfelApp/DsaWuerfelApp/Program.cs, DsaWuerfelApp.sln, die drei Projektdateien, Properties/launchSettings.json
- Plan.md, Deployment.md, .github/workflows/deploy.yml
- CSS-/Markup-Suche über alle Client-Quellen nach width, min-width, max-width, height, overflow, grid-template, flex, Positionierung, Media Queries, Tables, Drawers, Menus und Viewport-APIs

### Discovered but not inspected

- Weitere Client-Transport-/API-Dateien außerhalb der für State und UI referenzierten Typen
- DsaWuerfelApp/DsaWuerfelApp.Shared/-DTOs und Models, soweit sie keine direkt untersuchte UI-Signatur bilden
- Der überwiegende Server-Code unter DsaWuerfelApp/DsaWuerfelApp/Controllers, Services, Data und Auth-/Hub-Implementierungen
- Generierte obj/- und bin/-Artefakte, generierte CSS-Dateien, Bilder, Icons und das GLB-Modell
- Nicht referenzierte oder nicht layoutrelevante Client-Services und API-Clients

## 16. Aktueller Implementierungs- und Betriebsstand (2026-09-09)

### Mobile UI

- P0 bis P7a sowie P7c sind umgesetzt; P7b bleibt wegen fehlender gültiger Importdatei und fehlender nativer Geräteprüfung partiell. P8 bleibt wegen bekannter Test-Baselinefehler und ausstehender Geräte-/Browserabnahme partiell. Die ausführliche Phasenübersicht steht in `docs/ai/mobile-ui-refactor-progress.md`.
- `ProbenSearch` verwendet aktuell einen `.search-control`-Wrapper für Fokus-in/Fokus-out. Die `.search-wrapper`-Pille bleibt 45px hoch. Das Dropdown ist bei größeren Breiten absolut unter dem Feld positioniert und steht bis 640px als eigener Bereich im normalen Fluss darunter. Die Auswahl- und Alternativbuttons bleiben tastaturbedienbar, haben mindestens 44px Trefferhöhe und verwenden `.dsa-scroll-region` mit 280px maximaler Höhe.
- Die kompakte Suchdarstellung wurde in Commit `b29012e` an die frühere visuelle Anordnung angenähert: keine aufgeblähte äußere Pille, keine Überdeckung des Infobuttons und kompakte Ergebniszeilen. Die Fokus-/Blur-Logik aus P4c und die Auswahlverträge bleiben erhalten.
- `wwwroot/app.css` stellt `.dsa-scroll-region` zentral für das Informationspanel und die Probenliste bereit. Das Proben- und Attributpanel besitzen auf der Würfelseite keine unnötige äußere Scrollfläche; der Verlauf und begrenzte lange Listen behalten jeweils ihren begründeten Scrollbesitzer.
- `WuerfelActionBar`, Würfel-Grid, History, Info- und Action-Panels wurden in den vorherigen Paketen auf `min-width:0`, verfügbare Inhaltsbreite und natürliche kompakte Höhen abgestimmt. Die 3D-Instanz und Wuerfel-State-/Lifecycle-Verträge wurden nicht dupliziert oder vom Viewport abhängig remountet.

### HTTP-Betrieb und Deployment

- `DsaWuerfelApp/DsaWuerfelApp/Program.cs` liest `Web:UseHttps`. Bei deaktiviertem HTTPS wird ohne gesetztes `ASPNETCORE_URLS` auf `http://0.0.0.0:5000` gebunden; HSTS und HTTPS-Weiterleitung werden nur bei aktiviertem HTTPS verwendet.
- `DsaWuerfelApp/DsaWuerfelApp/appsettings.json` setzt `Web:UseHttps` aktuell auf `false`. Die Magic-Link-URL darf in diesem Modus eine absolute HTTP-Adresse verwenden.
- `.github/workflows/deploy.yml` ersetzt nach dem Publish die inkompatible gebündelte SQLite-Nativbibliothek auf dem Zielserver durch den vorhandenen Systemlink und startet danach `dsawuerfelapp.service` neu. Damit wird der zuvor reproduzierte GLIBC-/SQLite-Startfehler bei weiteren Deployments vermieden.
- Commit `b718a8a` ist nach `origin/master` gepusht und erfolgreich deployed. Commit `b29012e` ist ebenfalls auf `master` und `origin/master`; Deployment-Lauf `34396096213` endete erfolgreich. `http://78.141.212.242/` lieferte danach HTTP 200.

### Aktuelle Validierung

- `dotnet build DsaWuerfelApp/DsaWuerfelApp/DsaWuerfelApp.csproj -c Release --no-restore`: PASS, 0 Warnungen und 0 Fehler.
- `dotnet publish ... -c Release --no-restore -o artifacts/probe-search-test-publish`: PASS.
- Temporärer Playwright-Check der Probenliste bei 320, 390, 640, 641 und 1280px: PASS; getrennte Pille/Liste, normaler kompakter Fluss, keine horizontale Überbreite, 44px-Ergebnistreffer und Tastaturauswahl geprüft.
- `node DsaWuerfelApp/DsaWuerfelApp.Tests/Browser/responsive-ui.cjs artifacts/probe-search-test-publish`: PASS, 12 Viewports und 4 Routen.
- `git diff --check`: PASS für den letzten Produktiv-Commit; nach Änderungen an dieser Dokumentdatei erneut ausführen.
- Der zuletzt ausgeführte vollständige Testlauf des Testprojekts blieb bei den bekannten fachlichen Baselinefehlern rot: `TalentProbeEvaluatorTests.Spell_calculation_keeps_zfw_and_applies_pre_roll_zfp_separately` sowie `TestApplicationSmokeTests.Spell_option_modifier_is_resolved_from_catalog_and_keeps_original_zfw`. Die CSS-/Markup-Änderungen dieses Updates betreffen diese Tests nicht; Assertions wurden nicht abgeschwächt.

### Offene Kontext- und Abnahmepunkte

- CN-1/CN-5: verbindliche Zielgeräte, Browser-/iOS-/Android-Abdeckung sowie physische Touch-, Tastatur- und Browserleistenprüfung fehlen weiterhin.
- CN-4: gültige anonymisierte Importdatei sowie spezielle Datensätze für Alternativproben, Zauberoptionen, Meisterziele und umfangreiche History fehlen für einzelne Szenarien.
- CN-6 wird nur benötigt, falls die bestehende Reihenfolge bei sehr geringer Höhe nach der natürlichen Reflow-Anordnung nicht ausreicht.
- Der Server läuft aktuell absichtlich über HTTP; eine spätere HTTPS-Aktivierung benötigt eine gültige Reverse-Proxy-/Zertifikatskonfiguration und eine bewusste Änderung von `Web:UseHttps`.

## 17. Management-UI-Abschlussstand (2026-09-10)

Die Verwaltungsseiten wurden nach dem bestehenden Würfelkontext weitergeführt; die Details und Commitzuordnung stehen in `docs/ai/management-ui-refactor-progress.md`.

- Lobby: Kopf, Sessionsboard und Session-Workbench folgen direkt aufeinander. Join/Create nutzt je ein Submitformular mit Busy-Sperre; der Name für Beitreten/Erstellen bleibt bei unabhängigen Sessionupdates als Entwurf erhalten.
- SessionTree: Die aktive Session wird einmalig geöffnet, manuelles Zuklappen bleibt bei Metadatenupdates erhalten. Öffnen/Weiterwürfeln, Mitglieder, Kopieren und Verwaltungsaktionen bleiben in derselben Komponente; destruktive Aktionen geben lokale Inline-Bestätigung und Rückmeldung.
- Heldenverwaltung: Die Seite verwendet eine dunkle 1600px-Shell mit 3fr/2fr-Raster und Reflow bei 900px. Helden erscheinen als kompakte Zeilen mit ausschließlich fachlichem Aktivstatus. Import nutzt sichtbares/fokussierbares `InputFile`, HLD/XML/ZIP-Prüfung, 15-Dateien-/5-MiB-Grenzen, Busy-Sperre und Dropzone-Lifecycle ohne zweite Registrierung.
- Navigation: Die vorhandene 640/641px-Shell und ihre Zustands-/Scrollverträge bleiben erhalten; Verwaltungsflächen verwenden die gemeinsamen dunklen Panel-/Goldwerte.
- Validierung: Die isolierten Playwright-Checks prüfen SessionTree, Draft-/Doppelsubmit, verzögerten Upload mit gesperrtem Drop, Heldenaktionen sowie 320px bis 1920px und 899/900/901px. `dice-lifecycle.cjs` bleibt grün. Der echte Parserimport mit gültiger anonymisierter Datei und native iOS-/Android-/physische Touchabnahme fehlen weiterhin. Die bestehende Würfel-History-/Zauber-Testbaseline bleibt fachlich rot und ist im Raidlog getrennt ausgewiesen.
